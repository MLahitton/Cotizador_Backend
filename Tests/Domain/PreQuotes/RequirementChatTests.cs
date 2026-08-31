using Domain.PreQuotes;
using Xunit;

namespace Tests.Domain.PreQuotes;

public sealed class RequirementChatTests
{
    private static readonly Guid RequirementId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ItemId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Now = new(
        2026,
        8,
        31,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void CreateRequirementThread_WithoutItem_Succeeds()
    {
        var thread = RequirementChatThread.Create(
            RequirementId,
            RequirementChatScope.Requirement,
            null,
            UserId,
            Now);

        Assert.Equal(RequirementId, thread.RequirementId);
        Assert.Equal(RequirementChatScope.Requirement, thread.Scope);
        Assert.Null(thread.TechnicalProposalItemId);
        Assert.Equal(Now, thread.CreatedAtUtc);
        Assert.Equal(Now, thread.UpdatedAtUtc);
    }

    [Fact]
    public void CreateItemThread_WithItem_Succeeds()
    {
        var thread = RequirementChatThread.Create(
            RequirementId,
            RequirementChatScope.Item,
            ItemId,
            UserId,
            Now);

        Assert.Equal(RequirementChatScope.Item, thread.Scope);
        Assert.Equal(ItemId, thread.TechnicalProposalItemId);
    }

    [Fact]
    public void CreateRequirementThread_WithItem_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => RequirementChatThread.Create(
            RequirementId,
            RequirementChatScope.Requirement,
            ItemId,
            UserId,
            Now));
    }

    [Fact]
    public void CreateItemThread_WithoutItem_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => RequirementChatThread.Create(
            RequirementId,
            RequirementChatScope.Item,
            null,
            UserId,
            Now));
    }

    [Fact]
    public void CreateMessage_WithWhitespace_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => RequirementChatMessage.Create(
            Guid.NewGuid(),
            RequirementChatMessageRole.User,
            "   ",
            1,
            Now));
    }

    [Fact]
    public void CreateMessage_WithSequenceZero_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => RequirementChatMessage.Create(
            Guid.NewGuid(),
            RequirementChatMessageRole.User,
            "Hola",
            0,
            Now));
    }

    [Fact]
    public void CreateMessage_TrimsContentAndPreservesSequence()
    {
        var message = RequirementChatMessage.Create(
            Guid.NewGuid(),
            RequirementChatMessageRole.Assistant,
            "  Respuesta  ",
            2,
            Now);

        Assert.Equal("Respuesta", message.Content);
        Assert.Equal(2, message.Sequence);
        Assert.Equal(RequirementChatMessageRole.Assistant, message.Role);
    }
}
