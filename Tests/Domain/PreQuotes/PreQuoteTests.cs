using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class PreQuoteTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset At = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(2026, 1, "PC-2026-0001")]
    [InlineData(2026, 47, "PC-2026-0047")]
    [InlineData(2026, 10000, "PC-2026-10000")]
    public void FormatSerial_UsesYearAndPaddedSequence(
        int year,
        int sequence,
        string expected)
    {
        Assert.Equal(expected, PreQuote.FormatSerial(year, sequence));
    }

    [Fact]
    public void Create_AllowsNullNameAndStoresSerial()
    {
        var preQuote = PreQuote.Create(ProjectId, UserId, " PC-2026-0001 ", null, At);

        Assert.Equal("PC-2026-0001", preQuote.Serial);
        Assert.Null(preQuote.Name);
    }

    [Fact]
    public void UpdateName_TrimsNameWithoutChangingSerial()
    {
        var preQuote = PreQuote.Create(ProjectId, UserId, "PC-2026-0001", null, At);

        preQuote.UpdateName("  Cocina terraza - Apto 302  ", At.AddMinutes(1));

        Assert.Equal("PC-2026-0001", preQuote.Serial);
        Assert.Equal("Cocina terraza - Apto 302", preQuote.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateName_ClearsBlankName(string? name)
    {
        var preQuote = PreQuote.Create(ProjectId, UserId, "PC-2026-0001", "Initial", At);

        preQuote.UpdateName(name, At.AddMinutes(1));

        Assert.Null(preQuote.Name);
    }

    [Fact]
    public void UpdateName_RejectsTooLongName()
    {
        var preQuote = PreQuote.Create(ProjectId, UserId, "PC-2026-0001", null, At);
        var tooLong = new string('A', PreQuote.MaxNameLength + 1);

        Assert.Throws<ArgumentException>(() =>
            preQuote.UpdateName(tooLong, At.AddMinutes(1)));
    }
}