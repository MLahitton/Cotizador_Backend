namespace Application.Common.Abstractions.Operations;

public interface IOperationCancellationRegistry
{
    CancellationToken Register(
        string operationKey,
        CancellationToken callerCancellationToken);

    bool TryCancel(string operationKey);

    void Complete(string operationKey);
}

public static class RequirementOperationKeys
{
    public static string ProcessingAttempt(Guid processingAttemptId) =>
        $"requirement-processing:{processingAttemptId:D}";

    public static string Pricing(Guid requirementId) =>
        $"requirement-pricing:{requirementId:D}";
}
