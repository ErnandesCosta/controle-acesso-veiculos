using ControleAcessoVeiculos.API.Security;
using ControleAcessoVeiculos.Application.InstitutionalVehicleUsages;

namespace ControleAcessoVeiculos.API.Endpoints;

public static class InstitutionalVehicleUsageEndpoints
{
    public static IEndpointRouteBuilder MapInstitutionalVehicleUsageEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/institutional-vehicle-usages")
            .WithTags("Institutional vehicle usages");

        group.MapPost("/departures", RegisterDepartureAsync)
            .RequireAuthorization(AuthorizationPolicies.OperateAccess)
            .WithName("RegisterInstitutionalVehicleDeparture")
            .WithSummary("Registra a saída de um veículo institucional");
        group.MapGet("/open", ListOpenAsync)
            .RequireAuthorization(AuthorizationPolicies.OperateAccess)
            .WithName("ListOpenInstitutionalVehicleUsages")
            .WithSummary("Lista utilizações institucionais ainda abertas");
        group.MapPost("/{usageId:int}/returns", RegisterReturnAsync)
            .RequireAuthorization(AuthorizationPolicies.OperateAccess)
            .WithName("RegisterInstitutionalVehicleReturn")
            .WithSummary("Registra o retorno de um veículo institucional");
        group.MapGet("/history", SearchHistoryAsync)
            .RequireAuthorization(AuthorizationPolicies.ReviewTransportationRecords)
            .WithName("SearchInstitutionalVehicleUsageHistory")
            .WithSummary("Consulta o histórico de utilizações institucionais");

        return endpoints;
    }

    private static async Task<IResult> RegisterDepartureAsync(
        RegisterInstitutionalVehicleDepartureRequest request,
        HttpContext httpContext,
        InstitutionalVehicleUsageService service,
        CancellationToken cancellationToken)
    {
        if (!AuthenticatedUser.TryGetId(httpContext.User, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var result = await service.RegisterDepartureAsync(
            new RegisterInstitutionalVehicleDepartureCommand(
                request.VehicleId,
                request.DriverId,
                request.DepartureMileage,
                request.Itinerary),
            actorUserId,
            cancellationToken);

        return result.Status switch
        {
            RegisterInstitutionalVehicleDepartureStatus.Success => Results.Created(
                $"/institutional-vehicle-usages/{result.Usage!.Id}",
                result.Usage),
            RegisterInstitutionalVehicleDepartureStatus.NotFound => Results.NotFound(new
            {
                Message = "Veículo institucional ou motorista autorizado e ativo não encontrado."
            }),
            RegisterInstitutionalVehicleDepartureStatus.Conflict => Results.Conflict(new
            {
                Message = "Não foi possível registrar a saída institucional.",
                Errors = result.Errors
            }),
            _ => Results.ValidationProblem(result.Errors)
        };
    }

    private static async Task<IResult> ListOpenAsync(
        InstitutionalVehicleUsageService service,
        CancellationToken cancellationToken)
    {
        var usages = await service.ListOpenAsync(cancellationToken);
        return Results.Ok(usages);
    }

    private static async Task<IResult> SearchHistoryAsync(
        [AsParameters] SearchInstitutionalVehicleUsagesRequest request,
        InstitutionalVehicleUsageService service,
        CancellationToken cancellationToken)
    {
        var result = await service.SearchHistoryAsync(
            new SearchInstitutionalVehicleUsagesCommand(
                request.VehicleId,
                request.DriverId,
                request.Plate,
                request.VehicleIdentification,
                request.From,
                request.To,
                request.Page,
                request.PageSize),
            cancellationToken);

        return result.Status == SearchInstitutionalVehicleUsagesStatus.Success
            ? Results.Ok(result.Result)
            : Results.ValidationProblem(result.Errors);
    }

    private static async Task<IResult> RegisterReturnAsync(
        int usageId,
        RegisterInstitutionalVehicleReturnRequest request,
        HttpContext httpContext,
        InstitutionalVehicleUsageService service,
        CancellationToken cancellationToken)
    {
        if (!AuthenticatedUser.TryGetId(httpContext.User, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var result = await service.RegisterReturnAsync(
            usageId,
            new RegisterInstitutionalVehicleReturnCommand(request.ReturnMileage),
            actorUserId,
            cancellationToken);

        return result.Status switch
        {
            RegisterInstitutionalVehicleReturnStatus.Success => Results.Ok(result.Usage),
            RegisterInstitutionalVehicleReturnStatus.NotFound => Results.NotFound(new
            {
                Message = "Uso institucional não encontrado."
            }),
            RegisterInstitutionalVehicleReturnStatus.Conflict => Results.Conflict(new
            {
                Message = "O uso institucional já foi concluído."
            }),
            _ => Results.ValidationProblem(result.Errors)
        };
    }
}

public sealed record RegisterInstitutionalVehicleDepartureRequest(
    int VehicleId,
    int DriverId,
    int DepartureMileage,
    string? Itinerary);

public sealed record RegisterInstitutionalVehicleReturnRequest(int ReturnMileage);

public sealed record SearchInstitutionalVehicleUsagesRequest(
    int? VehicleId = null,
    int? DriverId = null,
    string? Plate = null,
    string? VehicleIdentification = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 25);
