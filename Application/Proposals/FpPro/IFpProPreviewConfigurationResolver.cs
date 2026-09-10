using Application.Common.Abstractions.Proposals;

namespace Application.Proposals.FpPro;

public interface IFpProPreviewConfigurationResolver
{
    Task<FpProReportPreviewData> ResolveAsync(
        FpProReportPreviewData preview,
        CancellationToken cancellationToken);
}
