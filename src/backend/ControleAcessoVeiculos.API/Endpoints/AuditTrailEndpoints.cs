using System.Text.Json;
using ControleAcessoVeiculos.API.Security;
using ControleAcessoVeiculos.Application.Auditing;

namespace ControleAcessoVeiculos.API.Endpoints;

public static class AuditTrailEndpoints
{
    public static IEndpointRouteBuilder MapAuditTrailEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/audits", SearchAsync)
            .RequireAuthorization(AuthorizationPolicies.ReviewAuditTrail)
            .WithTags("Auditing")
            .WithName("SearchAuditTrail")
            .WithSummary("Consulta a trilha de auditoria com filtros e paginação");

        return endpoints;
    }

    private static async Task<IResult> SearchAsync(
        [AsParameters] SearchAuditTrailRequest request,
        AuditTrailService service,
        CancellationToken cancellationToken)
    {
        var result = await service.SearchAsync(
            new SearchAuditTrailCommand(
                request.FromUtc,
                request.ToUtc,
                request.Action,
                request.Entity,
                request.RecordId,
                request.ActorUserId,
                request.SystemOnly,
                request.Page,
                request.PageSize),
            cancellationToken);

        if (result.Status == SearchAuditTrailStatus.Invalid)
        {
            return Results.ValidationProblem(result.Errors);
        }

        var page = result.Result!;
        return Results.Ok(new AuditTrailPageResponse(
            page.Items.Select(ToResponse).ToArray(),
            page.Page,
            page.PageSize,
            page.TotalCount,
            page.TotalPages));
    }

    private static AuditTrailResponse ToResponse(AuditTrailRecord audit) =>
        new(
            audit.Id,
            audit.OccurredAtUtc,
            audit.Action,
            audit.Entity,
            audit.RecordId,
            audit.ActorUserId,
            audit.ActorUserId.HasValue ? "Human" : "System",
            audit.Details,
            ParseJson(audit.PreviousStateJson),
            ParseJson(audit.NewStateJson));

    private static JsonElement? ParseJson(string? value)
    {
        if (value is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}

public sealed record SearchAuditTrailRequest(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? Action = null,
    string? Entity = null,
    int? RecordId = null,
    int? ActorUserId = null,
    bool? SystemOnly = null,
    int Page = 1,
    int PageSize = 25);

public sealed record AuditTrailResponse(
    int Id,
    DateTime OccurredAtUtc,
    string Action,
    string Entity,
    int RecordId,
    int? ActorUserId,
    string ActorType,
    string? Details,
    JsonElement? PreviousState,
    JsonElement? NewState);

public sealed record AuditTrailPageResponse(
    IReadOnlyList<AuditTrailResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
