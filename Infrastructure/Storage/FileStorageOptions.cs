using Microsoft.Extensions.Configuration;

namespace Infrastructure.Storage;

public sealed record FileStorageOptions(string RootPath)
{
    public static FileStorageOptions FromConfiguration(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var rootPath = configuration["FileStorage:RootPath"];

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException(
                "La configuración 'FileStorage:RootPath' es obligatoria.");
        }

        return new FileStorageOptions(rootPath.Trim());
    }
}
