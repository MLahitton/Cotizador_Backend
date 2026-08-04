using Domain.PreQuotes;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class PreQuoteDraftConfigurationTests
{
    [Fact]
    public void ValuationSnapshot_UsesSourceValuationDecimalPrecision()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model;Username=model")
            .Options;

        using var context = new ApplicationDbContext(options);
        var entity = context.Model.FindEntityType(
            typeof(PreQuoteDraftItemValuationSnapshot));

        Assert.NotNull(entity);
        Assert.Equal((18, 6), Precision(entity, nameof(PreQuoteDraftItemValuationSnapshot.UnitPricePerSquareMeter)));
        Assert.Equal((18, 6), Precision(entity, nameof(PreQuoteDraftItemValuationSnapshot.UnitAmount)));
        Assert.Equal((18, 6), Precision(entity, nameof(PreQuoteDraftItemValuationSnapshot.TotalAmount)));
    }

    private static (int? Precision, int? Scale) Precision(
        IEntityType entity,
        string property)
    {
        var metadata = entity.FindProperty(property)!;
        return (metadata.GetPrecision(), metadata.GetScale());
    }
}

