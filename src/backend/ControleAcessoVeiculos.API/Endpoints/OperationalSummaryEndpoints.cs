using ControleAcessoVeiculos.API.Security;
using ControleAcessoVeiculos.Application.OperationalSummaries;

namespace ControleAcessoVeiculos.API.Endpoints;

public static class OperationalSummaryEndpoints
{
    public static IEndpointRouteBuilder MapOperationalSummaryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/daily-summary", GetDailyAsync)
            .RequireAuthorization(AuthorizationPolicies.ReviewOperationalSummary)
            .WithTags("Operations")
            .WithName("GetDailyOperationalSummary")
            .WithSummary("Obtém o resumo operacional diário");

        return endpoints;
    }

    private static async Task<IResult> GetDailyAsync(
        DateOnly? date,
        OperationalSummaryService service,
        CancellationToken cancellationToken)
    {
        var summary = await service.GetAsync(date, cancellationToken);
        return Results.Ok(summary);
    }
}
