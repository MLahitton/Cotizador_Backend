using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class StructuredItemGlassDetectionTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithIdentifiedGlass_PersistsDeterministicChildren()
    {
        var glassTypeId = Guid.NewGuid();
        var extraction = Create(new StructuredItemGlassInput(
            glassTypeId, "Laminado 4+4", "LAM_4_4",
            GlassAssignmentScope.Item, false, [], [1],
            [new(1, 1, EvidenceSourceType.Native, "Vidrio laminado 4+4")]));

        var glass = Assert.Single(extraction.Items).GlassDetection;
        Assert.NotNull(glass);
        Assert.Equal(glassTypeId, glass.GlassTypeId);
        Assert.Equal("LAM_4_4", glass.NormalizedCodeSnapshot);
        Assert.Equal(1, Assert.Single(glass.Evidence).Sequence);
    }

    [Fact]
    public void Create_WithValidUnassignedGlass_Succeeds()
    {
        var extraction = Create(new StructuredItemGlassInput(
            null, null, null, GlassAssignmentScope.Unassigned, true,
            [GlassReviewReason.GlassTypeNotIdentified], [], []));

        Assert.Null(Assert.Single(extraction.Items).GlassDetection!.GlassTypeId);
    }

    [Fact]
    public void Create_WithXlsxEvidenceDifferentCellRanges_IsAllowed()
    {
        var extraction = Create(new StructuredItemGlassInput(
            Guid.NewGuid(), "Vidrio templado", "TEMP_8",
            GlassAssignmentScope.Item, false, [], [],
            [
                new StructuredItemGlassEvidenceInput(
                    1, null, EvidenceSourceType.Xlsx, "TEXTO",
                    "Sheet1", "A1:A10"),
                new StructuredItemGlassEvidenceInput(
                    2, null, EvidenceSourceType.Xlsx, "TEXTO",
                    "Sheet1", "A11:A20")
            ]));

        var glass = Assert.Single(extraction.Items).GlassDetection;
        Assert.NotNull(glass);
        Assert.Equal(2, glass.Evidence.Count);
    }

    [Fact]
    public void Create_WithXlsxEvidenceSameRangeDifferentSheets_IsAllowed()
    {
        var extraction = Create(new StructuredItemGlassInput(
            Guid.NewGuid(), "Vidrio templado", "TEMP_8",
            GlassAssignmentScope.Item, false, [], [],
            [
                new StructuredItemGlassEvidenceInput(
                    1, null, EvidenceSourceType.Xlsx, "TEXTO",
                    "Sheet1", "A1:A10"),
                new StructuredItemGlassEvidenceInput(
                    2, null, EvidenceSourceType.Xlsx, "TEXTO",
                    "Sheet2", "A1:A10")
            ]));

        var glass = Assert.Single(extraction.Items).GlassDetection;
        Assert.NotNull(glass);
        Assert.Equal(2, glass.Evidence.Count);
    }

    [Fact]
    public void Create_WithIdenticalXlsxEvidenceDuplicates_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            Create(new StructuredItemGlassInput(
                Guid.NewGuid(), "Vidrio templado", "TEMP_8",
                GlassAssignmentScope.Item, false, [], [],
                [
                    new StructuredItemGlassEvidenceInput(
                        1, null, EvidenceSourceType.Xlsx, "TEXTO",
                        "Sheet1", "A1:A10"),
                    new StructuredItemGlassEvidenceInput(
                        2, null, EvidenceSourceType.Xlsx, "TEXTO",
                        "Sheet1", "A1:A10")
                ])));
    }

    [Fact]
    public void Create_WithNonConsecutiveEvidenceSequence_MustReject()
    {
        Assert.Throws<ArgumentException>(() =>
            Create(new StructuredItemGlassInput(
                Guid.NewGuid(), "Vidrio templado", "TEMP_8",
                GlassAssignmentScope.Item, false, [], [],
                [
                    new StructuredItemGlassEvidenceInput(
                        1, null, EvidenceSourceType.Xlsx, "TEXTO",
                        "Sheet1", "A1:A10"),
                    new StructuredItemGlassEvidenceInput(
                        3, null, EvidenceSourceType.Xlsx, "TEXTO",
                        "Sheet1", "A11:A20")
                ])));
    }

    [Fact]
    public void Create_WithIdenticalPdfEvidenceDuplicates_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            Create(new StructuredItemGlassInput(
                Guid.NewGuid(), "Vidrio laminado", "LAM_4_4",
                GlassAssignmentScope.Item, false, [], [],
                [
                    new StructuredItemGlassEvidenceInput(
                        1, 1, EvidenceSourceType.Native, "PDF LAM 4-4",
                        null, null),
                    new StructuredItemGlassEvidenceInput(
                        2, 1, EvidenceSourceType.Native, "PDF LAM 4-4",
                        null, null)
                ])));
    }

    [Theory]
    [InlineData("code_without_id")]
    [InlineData("id_without_code")]
    [InlineData("blank_raw")]
    [InlineData("review_mismatch")]
    [InlineData("duplicate_reason")]
    [InlineData("duplicate_page")]
    [InlineData("invalid_page")]
    public void Create_WithInvalidGlass_Throws(string scenario)
    {
        var id = Guid.NewGuid();
        var input = scenario switch
        {
            "code_without_id" => new StructuredItemGlassInput(null, null,
                "LAM_4_4", GlassAssignmentScope.Item, false, [], [1], []),
            "id_without_code" => new StructuredItemGlassInput(id, null,
                null, GlassAssignmentScope.Item, false, [], [1], []),
            "blank_raw" => new StructuredItemGlassInput(id, " ",
                "LAM_4_4", GlassAssignmentScope.Item, false, [], [1], []),
            "review_mismatch" => new StructuredItemGlassInput(id, null,
                "LAM_4_4", GlassAssignmentScope.Item, true, [], [1], []),
            "duplicate_reason" => new StructuredItemGlassInput(null, null,
                null, GlassAssignmentScope.Unassigned, true,
                [GlassReviewReason.GlassTypeNotIdentified,
                 GlassReviewReason.GlassTypeNotIdentified], [1], []),
            "duplicate_page" => new StructuredItemGlassInput(null, null,
                null, GlassAssignmentScope.Unassigned, true,
                [GlassReviewReason.GlassTypeNotIdentified], [1, 1], []),
            _ => new StructuredItemGlassInput(null, null, null,
                GlassAssignmentScope.Unassigned, true,
                [GlassReviewReason.GlassTypeNotIdentified], [0], [])
        };

        Assert.Throws<ArgumentException>(() => Create(input));
    }

    private static StructuredDocumentExtraction Create(
        StructuredItemGlassInput glass) =>
        StructuredDocumentExtraction.Create(
            Guid.NewGuid(), StructuredExtractionStatus.Completed,
            "Project", null, null, 1, 0, 0, 1,
            "rule_based_v1", 1,
            [new StructuredItemInput(1, "V-01", "Window",
                StructuredElementType.Window, null, 1000, 1000, 1,
                false, glass)], [], [], [], [], CreatedAt,
            glass.NormalizedCodeSnapshot is null ? 0 : 1,
            glass.RequiresReview ? 1 : 0);
}
