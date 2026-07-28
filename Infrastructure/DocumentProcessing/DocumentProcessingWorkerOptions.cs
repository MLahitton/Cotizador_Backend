using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.DocumentProcessing;

public sealed record DocumentProcessingWorkerOptions(
    bool Enabled,
    TimeSpan PollInterval)
{
    public static DocumentProcessingWorkerOptions FromConfiguration(
        IConfiguration configuration)
    {
        var enabledValue =
            configuration["DocumentProcessingWorker:Enabled"];
        var pollIntervalValue =
            configuration["DocumentProcessingWorker:PollInterval"];

        if (!bool.TryParse(enabledValue, out var enabled))
        {
            throw new InvalidOperationException(
                "La configuracion 'DocumentProcessingWorker:Enabled' debe ser booleana.");
        }

        if (!TimeSpan.TryParse(
                pollIntervalValue,
                CultureInfo.InvariantCulture,
                out var pollInterval)
            || pollInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "La configuracion 'DocumentProcessingWorker:PollInterval' debe ser mayor que cero.");
        }

        return new DocumentProcessingWorkerOptions(enabled, pollInterval);
    }
}
