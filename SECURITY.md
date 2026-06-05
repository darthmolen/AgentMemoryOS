# Security Policy

## Supported versions

This project is pre-1.0-stable in spirit; security fixes are applied to the latest released
minor version.

| Version | Supported |
| --- | --- |
| latest `1.x` | ✅ |
| older | ❌ |

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report privately via GitHub's [private vulnerability reporting](https://github.com/darthmolen/AgentMemoryOS/security/advisories/new)
(Security → Report a vulnerability), or email **darthmolen@gmail.com** with:

- a description of the issue and its impact,
- steps to reproduce or a proof of concept,
- affected version(s).

You can expect an acknowledgement within a few business days. Once a fix is available, a patched
release will be published to NuGet and the advisory disclosed.

## Scope notes

- Memory captured by this library can contain user/PII content. Consumers are responsible for
  encryption at rest and access control on their chosen store (Postgres/Redis).
- AI context providers inject retrieved data into model requests; a compromised store could be a
  vector for indirect prompt injection. Validate/trust your data sources accordingly.
