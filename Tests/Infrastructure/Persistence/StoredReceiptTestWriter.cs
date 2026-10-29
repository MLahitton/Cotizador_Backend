using Application.Common.Abstractions.Storage;
using Infrastructure.Storage;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

internal static class StoredReceiptTestWriter
{
    internal static async Task<StoredContentReceipt> Write(Stream source, long length, long limit, CancellationToken token)
    {
        var root = Directory.CreateTempSubdirectory("sng-receipt-unit-").FullName;
        try
        {
            var storage = new LocalFileStorage(new FileStorageOptions(root), Substitute.For<IHostEnvironment>());
            return await storage.SaveVerifiedAsync("content", source, length, limit, token);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
