using Domain.PreQuotes;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class PreQuoteDraftModelTests
{
    [Fact]
    public void Model_ConfiguresSixTablesAndConcurrency()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model;Username=model;Password=model")
            .Options;
        using var context = new ApplicationDbContext(options);
        var model = context.Model;

        Assert.Equal(
            "pre_quote_drafts",
            model.FindEntityType(typeof(PreQuoteDraft))!.GetTableName());
        Assert.True(model.FindEntityType(typeof(PreQuoteDraft))!
            .FindProperty(nameof(PreQuoteDraft.Version))!.IsConcurrencyToken);
        Assert.Equal("pre_quote_draft_items",
            model.FindEntityType(typeof(PreQuoteDraftItem))!.GetTableName());
        Assert.Equal("pre_quote_draft_requirements",
            model.FindEntityType(typeof(PreQuoteDraftRequirement))!.GetTableName());
        Assert.Equal("pre_quote_draft_document_references",
            model.FindEntityType(typeof(PreQuoteDraftDocumentReference))!.GetTableName());
        Assert.Equal("integer[]",
            model.FindEntityType(typeof(PreQuoteDraftIssue))!
                .FindProperty(nameof(PreQuoteDraftIssue.PageNumbers))!
                .GetColumnType());
        Assert.Equal("integer[]",
            model.FindEntityType(typeof(PreQuoteDraftConflict))!
                .FindProperty(nameof(PreQuoteDraftConflict.ItemSequences))!
                .GetColumnType());
    }
}
