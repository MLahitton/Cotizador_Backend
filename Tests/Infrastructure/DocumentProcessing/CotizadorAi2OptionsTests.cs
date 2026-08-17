using Infrastructure.DocumentProcessing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.DocumentProcessing;

public sealed class CotizadorAi2OptionsTests
{
    [Fact]
    public void FromConfiguration_WithLongRunningExtractionTimeout_AcceptsValue()
    {
        var configuration = CreateConfiguration("600");

        var options = CotizadorAi2Options.FromConfiguration(configuration);

        Assert.Equal(600, options.TimeoutSeconds);
    }

    [Fact]
    public void FromConfiguration_WithTimeoutAboveMaximum_RejectsValue()
    {
        var configuration = CreateConfiguration("901");

        var exception = Assert.Throws<InvalidOperationException>(
            () => CotizadorAi2Options.FromConfiguration(configuration));

        Assert.Contains("entre 1 y 900", exception.Message);
    }

    private static IConfiguration CreateConfiguration(string timeoutSeconds) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CotizadorAi2:BaseUrl"] = "http://127.0.0.1:8000",
                ["CotizadorAi2:TimeoutSeconds"] = timeoutSeconds,
                ["CotizadorAi2:MaximumResponseBytes"] = "33554432",
                ["CotizadorAi2:Enabled"] = "true"
            })
            .Build();
}
