using ControleAcessoVeiculos.API.Security;
using ControleAcessoVeiculos.Application.AccessRecords;

namespace ControleAcessoVeiculos.API.Endpoints;

public static class VehicleAccessEndpoints
{
    public static IEndpointRouteBuilder MapVehicleAccessEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/access-records")
            .WithTags("Vehicle access");

        group.MapPost("/entries", RegisterEntryAsync)
            .RequireAuthorization(AuthorizationPolicies.OperateAccess)
            .WithName("RegisterVehicleEntry")
            .WithSummary("Registra a entrada de um veículo");
        group.MapGet("/open", ListOpenAsync)
            .RequireAuthorization(AuthorizationPolicies.OperateAccess)
            .WithName("ListOpenVehicleAccesses")
            .WithSummary("Lista acessos de veículos ainda abertos");
        group.MapGet("/entry-candidates", SearchEntryCandidatesAsync)
            .RequireAuthorization(AuthorizationPolicies.OperateAccess)
            .WithName("SearchAccessEntryCandidates")
            .WithSummary("Pesquisa pessoas e veículos para preencher uma entrada");
        group.MapPost("/{accessRecordId:int}/exit", RegisterExitAsync)
            .RequireAuthorization(AuthorizationPolicies.OperateAccess)
            .WithName("RegisterVehicleExit")
            .WithSummary("Registra a saída de um veículo");
        group.MapPost("/{accessRecordId:int}/exceptional-closure", CloseExceptionallyAsync)
            .RequireAuthorization(AuthorizationPolicies.ExceptionallyCloseAccessRecords)
            .WithName("ExceptionallyCloseVehicleAccess")
            .WithSummary("Encerra um acesso excepcionalmente sem inventar horário de saída");
        group.MapGet("/history", SearchHistoryAsync)
            .RequireAuthorization(AuthorizationPolicies.ReviewAccessRecords)
            .WithName("SearchVehicleAccessHistory")
            .WithSummary("Consulta o histórico de acessos de veículos");
        group.MapPut("/{accessRecordId:int}/correction", CorrectAsync)
            .RequireAuthorization(AuthorizationPolicies.CorrectAccessRecords)
            .WithName("CorrectVehicleAccess")
            .WithSummary("Corrige dados descritivos de um acesso com auditoria");

        return endpoints;
    }

    private static async Task<IResult> RegisterEntryAsync(
        RegisterVehicleEntryRequest request,
        HttpContext httpContext,
        VehicleAccessService vehicleAccessService,
        CancellationToken cancellationToken)
    {
        if (!AuthenticatedUser.TryGetId(httpContext.User, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var result = await vehicleAccessService.RegisterEntryAsync(
            new RegisterVehicleEntryCommand(
                request.DriverName,
                request.Plate,
                request.Objective,
                request.CategoryName,
                request.DocumentType,
                request.DocumentNumber,
                request.VehicleType,
                request.Brand,
                request.Model,
                request.Color,
                request.Year,
                request.Observation,
                request.EventAuthorizationId,
                request.VehicleId,
                request.PersonId),
            actorUserId,
            cancellationToken);

        return result.Status switch
        {
            RegisterVehicleEntryStatus.Success => Results.Created(
                $"/access-records/{result.AccessRecord!.Id}",
                result.AccessRecord),
            RegisterVehicleEntryStatus.Conflict => Results.Conflict(new
            {
                Message = "Não foi possível registrar a entrada.",
                Errors = result.Errors
            }),
            _ => Results.ValidationProblem(result.Errors)
        };
    }

    private static async Task<IResult> ListOpenAsync(
        VehicleAccessService vehicleAccessService,
        CancellationToken cancellationToken)
    {
        var records = await vehicleAccessService.ListOpenAsync(cancellationToken);
        return Results.Ok(records);
    }

    private static async Task<IResult> SearchEntryCandidatesAsync(
        [AsParameters] SearchAccessEntryCandidatesRequest request,
        VehicleAccessService vehicleAccessService,
        CancellationToken cancellationToken)
    {
        var result = await vehicleAccessService.SearchEntryCandidatesAsync(
            new SearchAccessEntryCandidatesCommand(request.Query),
            cancellationToken);

        return result.Status == SearchAccessEntryCandidatesStatus.Success
            ? Results.Ok(result.Items)
            : Results.ValidationProblem(result.Errors);
    }

    private static async Task<IResult> RegisterExitAsync(
        int accessRecordId,
        HttpContext httpContext,
        VehicleAccessService vehicleAccessService,
        CancellationToken cancellationToken)
    {
        if (!AuthenticatedUser.TryGetId(httpContext.User, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var result = await vehicleAccessService.CloseAsync(
            accessRecordId,
            actorUserId,
            cancellationToken);

        return result.Status switch
        {
            CloseVehicleAccessStatus.Success => Results.Ok(result.AccessRecord),
            CloseVehicleAccessStatus.NotFound => Results.NotFound(new
            {
                Message = "Registro de acesso não encontrado."
            }),
            _ => Results.Conflict(new
            {
                Message = "O registro de acesso já foi encerrado."
            })
        };
    }

    private static async Task<IResult> SearchHistoryAsync(
        [AsParameters] SearchVehicleAccessesRequest request,
        VehicleAccessService vehicleAccessService,
        CancellationToken cancellationToken)
    {
        var result = await vehicleAccessService.SearchHistoryAsync(
            new SearchVehicleAccessesCommand(
                request.Plate,
                request.DriverName,
                request.CategoryName,
                request.Status,
                request.From,
                request.To,
                request.Page,
                request.PageSize),
            cancellationToken);

        return result.Status == SearchVehicleAccessesStatus.Success
            ? Results.Ok(result.Result)
            : Results.ValidationProblem(result.Errors);
    }

    private static async Task<IResult> CloseExceptionallyAsync(
        int accessRecordId,
        ExceptionallyCloseVehicleAccessRequest request,
        HttpContext httpContext,
        VehicleAccessService vehicleAccessService,
        CancellationToken cancellationToken)
    {
        if (!AuthenticatedUser.TryGetId(httpContext.User, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var result = await vehicleAccessService.CloseExceptionallyAsync(
            accessRecordId,
            new ExceptionallyCloseVehicleAccessCommand(
                request.Reason,
                request.Observation,
                request.ObservedExitAtUtc),
            actorUserId,
            cancellationToken);

        return result.Status switch
        {
            CloseVehicleAccessStatus.Success => Results.Ok(result.AccessRecord),
            CloseVehicleAccessStatus.Invalid =>
                Results.ValidationProblem(result.Errors!),
            CloseVehicleAccessStatus.NotFound => Results.NotFound(new
            {
                Message = "Registro de acesso não encontrado."
            }),
            _ => Results.Conflict(new
            {
                Message = "O registro de acesso já foi encerrado."
            })
        };
    }

    private static async Task<IResult> CorrectAsync(
        int accessRecordId,
        CorrectVehicleAccessRequest request,
        HttpContext httpContext,
        VehicleAccessService vehicleAccessService,
        CancellationToken cancellationToken)
    {
        if (!AuthenticatedUser.TryGetId(httpContext.User, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var result = await vehicleAccessService.CorrectAsync(
            accessRecordId,
            new CorrectVehicleAccessCommand(
                request.Objective,
                request.CategoryName,
                request.Observation,
                request.Justification),
            actorUserId,
            cancellationToken);

        return result.Status switch
        {
            CorrectVehicleAccessStatus.Success => Results.Ok(result.AccessRecord),
            CorrectVehicleAccessStatus.NotFound => Results.NotFound(new
            {
                Message = "Registro de acesso não encontrado."
            }),
            CorrectVehicleAccessStatus.Conflict => Results.Conflict(new
            {
                Message = "Não foi possível corrigir o registro de acesso.",
                Errors = result.Errors
            }),
            _ => Results.ValidationProblem(result.Errors)
        };
    }

}

public sealed record RegisterVehicleEntryRequest(
    string DriverName,
    string Plate,
    string Objective,
    string CategoryName,
    string? DocumentType = null,
    string? DocumentNumber = null,
    string? VehicleType = null,
    string? Brand = null,
    string? Model = null,
    string? Color = null,
    int? Year = null,
    string? Observation = null,
    int? EventAuthorizationId = null,
    int? VehicleId = null,
    int? PersonId = null);

public sealed record SearchAccessEntryCandidatesRequest(string Query);

public sealed record SearchVehicleAccessesRequest(
    string? Plate = null,
    string? DriverName = null,
    string? CategoryName = null,
    string? Status = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 25);

public sealed record CorrectVehicleAccessRequest(
    string Objective,
    string CategoryName,
    string? Observation,
    string Justification);

public sealed record ExceptionallyCloseVehicleAccessRequest(
    string Reason,
    string Observation,
    DateTimeOffset? ObservedExitAtUtc = null);
