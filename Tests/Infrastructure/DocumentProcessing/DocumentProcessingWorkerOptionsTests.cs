using Infrastructure.DocumentProcessing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.DocumentProcessing;

public sealed class DocumentProcessingWorkerOptionsTests
{
    [Fact]
    public void FromConfiguration_WithValidValues_ReturnsOptions()
    {
        var configuration = Build("true", "00:00:02");

        var options =
            DocumentProcessingWorkerOptions.FromConfiguration(configuration);

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(2), options.PollInterval);
    }

    [Theory]
    [InlineData(null, "00:00:02")]
    [InlineData("invalid", "00:00:02")]
    [InlineData("true", null)]
    [InlineData("true", "00:00:00")]
    [InlineData("true", "-00:00:01")]
    public void FromConfiguration_WithInvalidValues_Throws(
        string? enabled,
        string? interval)
    {
        var configuration = Build(enabled, interval);

        Assert.Throws<InvalidOperationException>(() =>
            DocumentProcessingWorkerOptions.FromConfiguration(configuration));
    }

    [Theory]
    [InlineData("false", "00:00:02", false, 2)]
    [InlineData("TRUE", "00:00:00.001", true, 0.001)]
    [InlineData("False", "1.00:00:00", false, 86400)]
    [InlineData("true", "00:00:30", true, 30)]
    [InlineData("false", "00:05:00", false, 300)]
    public void FromConfiguration_WithDistinctValidFormats_ParsesExactly(
        string enabled,
        string interval,
        bool expectedEnabled,
        double expectedSeconds)
    {
        var options = DocumentProcessingWorkerOptions.FromConfiguration(
            Build(enabled, interval));

        Assert.Equal(expectedEnabled, options.Enabled);
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            options.PollInterval);
    }

    private static IConfiguration Build(string? enabled, string? interval)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentProcessingWorker:Enabled"] = enabled,
                ["DocumentProcessingWorker:PollInterval"] = interval
            })
            .Build();
    }
}
