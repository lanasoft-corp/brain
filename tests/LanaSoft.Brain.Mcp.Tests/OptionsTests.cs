namespace LanaSoft.Brain.Mcp.Tests;

/// <summary>Réglages obligatoires de la passerelle : aucune valeur par défaut ne pointe vers un vrai déploiement.</summary>
public sealed class OptionsTests
{
    [Fact]
    public void Missing_settings_stop_the_startup()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new BrainOptions().Validate());

        Assert.Contains("Brain:Resource", error.Message);
        Assert.Contains("Brain:Authority", error.Message);
        Assert.Contains("Brain:AgentOwner", error.Message);
    }

    [Fact]
    public void Complete_settings_are_accepted()
    {
        var options = new BrainOptions
        {
            Resource = "https://brain.example.test/mcp",
            Authority = "https://auth.example.test/realms/demo",
            AgentOwner = "alice",
        };

        options.Validate();
    }
}
