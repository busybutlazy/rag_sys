using BeServer.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BeServer.Tests;

public class ModelRegistryTests
{
    private static ModelRegistry Create(Dictionary<string, string?> values) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

    [Fact]
    public void Resolve_ReturnsDefault_WhenRequestedIsNull()
    {
        var registry = Create(new() { ["Models:ChatDefault"] = "gpt-4o-mini", ["Models:Allowed"] = "gpt-4o-mini,gpt-4o" });
        Assert.Equal("gpt-4o-mini", registry.Resolve(null, registry.ChatDefault));
    }

    [Fact]
    public void Resolve_ReturnsDefault_WhenRequestedIsEmpty()
    {
        var registry = Create(new() { ["Models:ChatDefault"] = "gpt-4o-mini", ["Models:Allowed"] = "gpt-4o-mini,gpt-4o" });
        Assert.Equal("gpt-4o-mini", registry.Resolve("  ", registry.ChatDefault));
    }

    [Fact]
    public void Resolve_ReturnsDefault_WhenModelNotInAllowlist()
    {
        var registry = Create(new() { ["Models:ChatDefault"] = "gpt-4o-mini", ["Models:Allowed"] = "gpt-4o-mini" });
        Assert.Equal("gpt-4o-mini", registry.Resolve("gpt-4-turbo", registry.ChatDefault));
    }

    [Fact]
    public void Resolve_ReturnsRequested_WhenModelIsInAllowlist()
    {
        var registry = Create(new() { ["Models:ChatDefault"] = "gpt-4o-mini", ["Models:Allowed"] = "gpt-4o-mini,gpt-4o" });
        Assert.Equal("gpt-4o", registry.Resolve("gpt-4o", registry.ChatDefault));
    }

    [Fact]
    public void AllowedModels_UsesConfiguredList()
    {
        var registry = Create(new() { ["Models:Allowed"] = "gpt-4o-mini,gpt-4o" });
        Assert.Equal(["gpt-4o-mini", "gpt-4o"], registry.AllowedModels);
    }

    [Fact]
    public void AllowedModels_FallsBackToDefault_WhenNotConfigured()
    {
        var registry = Create(new());
        Assert.Contains("gpt-4o-mini", registry.AllowedModels);
    }

    [Fact]
    public void ChatDefault_UsesConfiguredValue()
    {
        var registry = Create(new() { ["Models:ChatDefault"] = "gpt-4o" });
        Assert.Equal("gpt-4o", registry.ChatDefault);
    }

    [Fact]
    public void AgentDefault_FallsBackToHardcoded_WhenNotConfigured()
    {
        var registry = Create(new());
        Assert.Equal("gpt-4o-mini", registry.AgentDefault);
    }
}
