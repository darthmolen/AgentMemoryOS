namespace AgentMemoryOS.Tests;

using AgentMemoryOS.Abstractions;

[TestClass]
public sealed class ToolchainSmokeTests
{
    [TestMethod]
    public void DefaultScopeHasDefaultTemplateId()
    {
        Assert.AreEqual("default", MemoryScope.Default.TemplateId);
    }
}
