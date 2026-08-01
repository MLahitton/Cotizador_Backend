using Domain.PreQuotes;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class StructuredItemGlassModelTests
{
    [Fact]
    public void Model_ConfiguresGlassDetectionRelationshipsAndConstraints()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model;Username=model")
            .Options;
        using var context = new ApplicationDbContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var detection = model.FindEntityType(
            typeof(StructuredExtractionItemGlassDetection))!;
        var itemForeignKey = Assert.Single(detection.GetForeignKeys(),
            value => value.PrincipalEntityType.ClrType
                == typeof(StructuredExtractionItem));
        var glassForeignKey = Assert.Single(detection.GetForeignKeys(),
            value => value.PrincipalEntityType.ClrType
                == typeof(global::Domain.Catalogs.GlassType));

        Assert.True(itemForeignKey.IsUnique);
        Assert.Equal(DeleteBehavior.Cascade, itemForeignKey.DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict, glassForeignKey.DeleteBehavior);
        Assert.True(detection.FindProperty(nameof(
            StructuredExtractionItemGlassDetection.GlassTypeId))!.IsNullable);
        Assert.Equal(500, detection.FindProperty(nameof(
            StructuredExtractionItemGlassDetection.RawSpecification))!
            .GetMaxLength());
    }
}
