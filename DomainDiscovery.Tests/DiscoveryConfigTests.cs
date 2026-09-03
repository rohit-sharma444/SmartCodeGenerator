using DomainDiscovery;
using Xunit;

namespace DomainDiscovery.Tests;

public class DiscoveryConfigTests
{
    [Fact]
    public void DefaultsToOllamaWhenThereIsNoFile()
    {
        var config = DiscoveryConfig.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));

        Assert.Equal(DiscoveryConfig.OllamaSource, config.Source);
        Assert.Equal(DiscoveryConfig.DefaultModel, config.Model);
        Assert.Equal(DiscoveryConfig.DefaultEndpoint, config.Endpoint);
        Assert.Equal(DiscoveryConfig.DefaultTimeoutMinutes, config.TimeoutMinutes);
    }

    [Fact]
    public void ReadsTheFileWhenItIsThere()
    {
        var path = Path.Combine(Path.GetTempPath(), $"discovery-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"Source":"Wikipedia","Model":"llama3.2:3b","TimeoutMinutes":3}""");

        var config = DiscoveryConfig.Load(path);

        Assert.Equal("Wikipedia", config.Source);
        Assert.Equal("llama3.2:3b", config.Model);
        Assert.Equal(3, config.TimeoutMinutes);
        Assert.Equal(DiscoveryConfig.DefaultEndpoint, config.Endpoint);

        File.Delete(path);
    }

    [Fact]
    public void YieldsDefaultsRatherThanThrowingOnBrokenJson()
    {
        var path = Path.Combine(Path.GetTempPath(), $"broken-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ this is not json");

        var config = DiscoveryConfig.Load(path);

        Assert.Equal(DiscoveryConfig.OllamaSource, config.Source);

        File.Delete(path);
    }
}
