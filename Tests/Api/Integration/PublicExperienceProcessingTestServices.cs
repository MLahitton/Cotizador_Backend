using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.Operations;
using Application.Common.Abstractions.PreQuotes;
using CotizadorBackend.Tests.Application.PreQuotes;
using Domain.PreQuotes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace CotizadorBackend.Tests.Api.Integration;

internal sealed class PublicExperienceAiStub : IAi2DocumentProcessingClient
{
    public string Scenario { get; set; } = "success";
    public Func<Task>? BeforeResponse { get; set; }
    public int Calls;

    public async Task<DocumentProcessingClientResult> ProcessAsync(
        DocumentProcessingClientRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        if (BeforeResponse is not null) await BeforeResponse();
        return Scenario switch
        {
            "unavailable" => DocumentProcessingClientResult.Failed(DocumentProcessingClientFailure.ServiceUnavailable),
            "timeout" => DocumentProcessingClientResult.Failed(DocumentProcessingClientFailure.Timeout),
            "invalid" => DocumentProcessingClientResult.Failed(DocumentProcessingClientFailure.InvalidResponse),
            "service-error" => DocumentProcessingClientResult.RemoteFailure(DocumentProcessingClientFailure.ServiceError,
                new DocumentProcessingRemoteError(500, "AI2-1.0", "AI2_SERVICE_ERROR", "Simulated service error")),
            "rejected" => DocumentProcessingClientResult.RemoteFailure(DocumentProcessingClientFailure.RemoteRejection,
                new DocumentProcessingRemoteError(422, "AI2-1.0", "AI2_REQUEST_REJECTED", "Simulated rejection")),
            _ => DocumentProcessingClientResult.Success(ProcessRequirementServiceTests.CreateResponse(request,
                Scenario == "review" ? DocumentProcessingOutcome.RequiresReview : DocumentProcessingOutcome.Completed,
                Scenario == "review" ? 1 : 0))
        };
    }
}

internal static class PublicExperienceProcessingTestServices
{
    public static void Add(IServiceCollection services, PublicExperienceAiStub ai)
    {
        global::Application.DependencyInjection.AddApplication(services);
        services.AddSingleton<IAi2DocumentProcessingClient>(ai);
        services.AddSingleton(Substitute.For<IOperationCancellationRegistry>());
        var systems = Substitute.For<IProductSystemCatalogRepository>();
        systems.ListActiveSelectableAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ProductSystemCatalogReadModel>());
        var glasses = Substitute.For<IGlassTypeCatalogRepository>();
        glasses.GetActiveWithCurrentPriceRangesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<GlassTypeCatalogReadModel>());
        var finishes = Substitute.For<IFinishTypeCatalogRepository>();
        finishes.ListActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<FinishTypeCatalogReadModel>());
        services.AddSingleton(systems);
        services.AddSingleton(glasses);
        services.AddSingleton(finishes);
        var historical = Substitute.For<IHistoricalSimilarityEvaluationService>();
        historical.EvaluateBatchAsync(Arg.Any<IReadOnlyList<HistoricalSimilarityBatchQuery>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyDictionary<string, HistoricalSimilarityEvaluationResult>>(
                call.Arg<IReadOnlyList<HistoricalSimilarityBatchQuery>>().ToDictionary(x => x.RequestId,
                    _ => new HistoricalSimilarityEvaluationResult(HistoricalSimilarityStatus.Completed, [], null))));
        services.AddSingleton(historical);
    }
}
