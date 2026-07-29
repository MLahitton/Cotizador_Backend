using Domain.PreQuotes;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class StructuredExtractionModelTests
{
    [Fact]
    public void DocumentReference_HasPositiveQuantityConstraint()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=metadata_only;Username=metadata")
            .Options;
        using var context = new ApplicationDbContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = Assert.IsAssignableFrom<IReadOnlyEntityType>(
            model.FindEntityType(
                typeof(StructuredExtractionDocumentReference)));
        var constraint = Assert.Single(
            entity.GetCheckConstraints(),
            value => value.Name ==
                "ck_structured_extraction_document_references_quantity_positive");

        Assert.Contains(
            "\"quantity\" IS NULL OR \"quantity\" > 0",
            constraint.Sql,
            StringComparison.Ordinal);
    }
}
