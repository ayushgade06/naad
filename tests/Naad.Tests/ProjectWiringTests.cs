namespace Naad.Tests;

public sealed class ProjectWiringTests
{
    // Production types are internal (docs/STYLE.md), so every test depends on
    // InternalsVisibleTo. Program is internal: this fails to compile without it.
    [Fact]
    public void Program_FromTestProject_IsVisibleInNaadAssembly()
    {
        Assert.Equal("Naad", typeof(Program).Assembly.GetName().Name);
    }
}
