# Securing the Example.WebHost with Microsoft Entra ID

The [Example.WebHost](tests/AgentMemoryOS.Example.WebHost) ships with **open endpoints** so the
demo runs with zero friction. That is deliberately *not* production-ready. This guide is the
missing half: how to put **Microsoft Entra ID** (Azure AD) authentication **and** authorization
in front of the API — both interactive (delegated / on behalf of a user) and server-to-server
(app-only / daemon) — using [`Microsoft.Identity.Web`](https://learn.microsoft.com/entra/msal/dotnet/microsoft-identity-web/).

It is a reference you apply yourself; nothing here is wired into the example by default.

---

## a) Prerequisites

- An Azure subscription and a Microsoft Entra tenant.
- **Permission to register an app and grant consent.** Creating the app registration needs the
  **Application Developer** role (the tenant default for members) or higher. Granting
  **admin consent** for app roles / API permissions (sections d–e) needs **Cloud Application
  Administrator**, **Application Administrator**, or **Privileged Role Administrator**. Global
  Administrator covers all of it but is overkill.
- **Azure CLI** 2.x: `az --version`. Sign in to the right tenant:
  ```bash
  az login --tenant <tenant-id>
  ```
- The `Microsoft.Identity.Web` package added to the WebHost (it is **not** referenced by default):
  ```bash
  dotnet add tests/AgentMemoryOS.Example.WebHost package Microsoft.Identity.Web
  ```
  > This repo uses Centralized Package Management — add `Microsoft.Identity.Web` to
  > `Directory.Packages.props` (with a version) and a versionless `<PackageReference>` to
  > `tests/AgentMemoryOS.Example.WebHost/AgentMemoryOS.Example.WebHost.csproj`.

---

## b) Register the API (az CLI)

```bash
APP_NAME="AgentMemoryOS-Example-WebHost"
TENANT_ID=$(az account show --query tenantId -o tsv)

# 1. Create the app registration (single tenant).
APP_ID=$(az ad app create \
  --display-name "$APP_NAME" \
  --sign-in-audience AzureADMyOrg \
  --query appId -o tsv)
echo "Client (application) id: $APP_ID"

# 2. Set the Application ID URI. The default form is api://<app-id> — this is the API's audience.
az ad app update --id "$APP_ID" --identifier-uris "api://$APP_ID"

# 3. Expose a delegated scope named access_as_user (this is the "default scope" clients request).
#    The az ad flags for scopes are limited, so PATCH the Graph application object directly.
SCOPE_ID=$(python3 -c "import uuid; print(uuid.uuid4())")
az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications(appId='$APP_ID')" \
  --headers 'Content-Type=application/json' \
  --body "{\"api\":{\"oauth2PermissionScopes\":[{\"id\":\"$SCOPE_ID\",\"value\":\"access_as_user\",\"type\":\"User\",\"isEnabled\":true,\"adminConsentDisplayName\":\"Access the AgentMemoryOS example API\",\"adminConsentDescription\":\"Allows the app to access the API as the signed-in user.\",\"userConsentDisplayName\":\"Access the API on your behalf\",\"userConsentDescription\":\"Allows the app to access the API as you.\"}]}}"
```

After this, clients request the scope **`api://<app-id>/access_as_user`**, and the API's expected
audience is **`api://<app-id>`**.

---

## c) Configuration — what goes in appsettings.json, what is a secret

These values are **public identifiers, not secrets** — put them in
[appsettings.json](tests/AgentMemoryOS.Example.WebHost/appsettings.json):

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<your-tenant-id>",
    "ClientId": "<your-app-id>",
    "Audience": "api://<your-app-id>"
  }
}
```

**A protected *resource* API needs no client secret at all** — it validates incoming tokens
against Entra's published signing keys (keyless). You only need a secret in two cases:

- the WebHost calls a **downstream API on the user's behalf** (on-behalf-of flow), or
- you are configuring the **server-to-server caller** (section d) — the *daemon* holds the secret.

When a secret *is* required, never put it in `appsettings.json`:

| Environment | Where the secret lives |
| --- | --- |
| Local dev | **User secrets** — `dotnet user-secrets set "AzureAd:ClientSecret" "<value>"` |
| Server / CI | **Environment variable** — `AzureAd__ClientSecret=<value>` (the `__` maps to the nested key) |

Prefer a **certificate** or **managed identity** over a client secret in production; a secret is
the simplest to demonstrate, not the most secure.

---

## d) Server-to-server (app-only) authentication

A daemon with no signed-in user authenticates with the **client-credentials** flow and is
authorized by **app roles** (not delegated scopes).

```bash
# 1. Define an app role on the API (allowedMemberTypes: Application).
ROLE_ID=$(python3 -c "import uuid; print(uuid.uuid4())")
az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications(appId='$APP_ID')" \
  --headers 'Content-Type=application/json' \
  --body "{\"appRoles\":[{\"id\":\"$ROLE_ID\",\"allowedMemberTypes\":[\"Application\"],\"value\":\"App.Access\",\"displayName\":\"Access as application\",\"description\":\"Daemon services may call the API.\",\"isEnabled\":true}]}"

# 2. Register the calling daemon and give it a secret.
CLIENT_ID=$(az ad app create --display-name "AgentMemoryOS-Daemon-Client" \
  --sign-in-audience AzureADMyOrg --query appId -o tsv)
az ad sp create --id "$CLIENT_ID"
CLIENT_SECRET=$(az ad app credential reset --id "$CLIENT_ID" --query password -o tsv)

# 3. Grant the daemon the API's app role, then admin-consent it (needs an admin role).
az ad app permission add --id "$CLIENT_ID" --api "$APP_ID" --api-permissions "$ROLE_ID=Role"
az ad app permission admin-consent --id "$CLIENT_ID"
```

The daemon requests a token for the API's **`.default`** and calls the API with it:

```bash
TOKEN=$(curl -s -X POST "https://login.microsoftonline.com/$TENANT_ID/oauth2/v2.0/token" \
  -d "client_id=$CLIENT_ID" \
  -d "client_secret=$CLIENT_SECRET" \
  -d "scope=api://$APP_ID/.default" \
  -d "grant_type=client_credentials" | python3 -c "import sys,json; print(json.load(sys.stdin)['access_token'])")

curl -s localhost:5000/chat -H "Authorization: Bearer $TOKEN" \
  -H 'content-type: application/json' -d '{"message":"hello"}'
```

App-only tokens carry a **`roles`** claim (e.g. `App.Access`) instead of the delegated **`scp`**
claim. Store `CLIENT_SECRET` as a secret on the *caller* (env var / vault), never in source.

---

## e) The `AddCustomAuthentication` extension (drop-in)

Add a file `EntraAuthenticationExtensions.cs` to the WebHost:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

namespace AgentMemoryOS.Example.WebHost;

public static class EntraAuthenticationExtensions
{
    /// <summary>
    /// Protects the whole API with Microsoft Entra ID bearer tokens. A fallback authorization
    /// policy makes every endpoint require an authenticated caller unless it opts out with
    /// .AllowAnonymous(), so a single call locks the API.
    /// </summary>
    public static void AddCustomAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
    }
}
```

Wire it in [Program.cs](tests/AgentMemoryOS.Example.WebHost/Program.cs) (the commented signpost is
already there) and exempt the health probe:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddExampleMemory();

// Uncomment to require Microsoft Entra ID auth on every endpoint:
builder.AddCustomAuthentication();

var app = builder.Build();
// ...
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
```

`WebApplication` **auto-adds** the authentication/authorization middleware once those services are
registered, so there is nothing else to wire — no `UseAuthentication()` / `UseAuthorization()`
calls needed.

### Tightening: require a specific scope or app role

`RequireAuthenticatedUser()` accepts any valid token from your tenant. To require the delegated
scope and/or the app role, replace the fallback policy:

```csharp
options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .RequireAssertion(context =>
        context.User.HasClaim("scp", "access_as_user") ||  // delegated (user) token
        context.User.HasClaim("roles", "App.Access"))      // app-only (daemon) token
    .Build();
```

---

## Verify

| Request | Expected |
| --- | --- |
| `curl -i localhost:5000/chat -d '{"message":"hi"}' -H 'content-type: application/json'` (no token) | **401 Unauthorized** |
| `curl -i localhost:5000/healthz` | **200 OK** (AllowAnonymous) |
| `/chat` with a valid `Authorization: Bearer <token>` | **200 OK** |

Once `/chat` returns 401 without a token and 200 with one, the API is protected.
