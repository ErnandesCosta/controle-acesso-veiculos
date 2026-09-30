using ControleAcessoVeiculos.API.Security;
using ControleAcessoVeiculos.Application.InstitutionalVehicles;

namespace ControleAcessoVeiculos.API.Endpoints;

public static class InstitutionalVehicleCatalogEndpoints
{
    public static IEndpointRouteBuilder MapInstitutionalVehicleCatalogEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/institutional-vehicles")
            .RequireAuthorization(AuthorizationPolicies.ReadInstitutionalVehicleCatalog)
            .WithTags("Institutional vehicles");

        group.MapGet(string.Empty, ListActiveAsync)
            .WithName("ListActiveInstitutionalVehicles")
            .WithSummary("Lista veículos institucionais ativos");
        group.MapPost(string.Empty, CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.ManageInstitutionalVehicleCatalog)
            .WithName("CreateInstitutionalVehicle")
            .WithSummary("Cadastra um veículo institucional");
        group.MapPut("/{id:int}", UpdateAsync)
            .RequireAuthorization(AuthorizationPolicies.ManageInstitutionalVehicleCatalog)
            .WithName("UpdateInstitutionalVehicle")
            .WithSummary("Atualiza um veículo institucional");
        group.MapDelete("/{id:int}", DeactivateAsync)
            .RequireAuthorization(AuthorizationPolicies.ManageInstitutionalVehicleCatalog)
            .WithName("DeactivateInstitutionalVehicle")
            .WithSummary("Desativa um veículo institucional");
        group.MapPost("/{id:int}/reactivation", ReactivateAsync)
            .RequireAuthorization(AuthorizationPolicies.ManageInstitutionalVehicleCatalog)
            .WithName("ReactivateInstitutionalVehicle")
            .WithSummary("Reativa um veículo institucional");

        return endpoints;
    }

    private static async Task<IResult> ListActiveAsync(
        InstitutionalVehicleCatalogService service,
        CancellationToken cancellationToken)
    {
        var vehicles = await service.ListActiveAsync(cancellationToken);
        return Results.Ok(vehicles);
    }

    private static async Task<IResult> CreateAsync(
        CreateInstitutionalVehicleRequest request,
        HttpContext httpContext,
        InstitutionalVehicleCatalogService service,
        CancellationToken cancellationToken)
    {
        if (!AuthenticatedUser.TryGetId(httpContext.User, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateAsync(
            new CreateInstitutionalVehicleCommand(
                request.Plate,
                request.Identification,
                request.VehicleType,
                request.Brand,
                request.Model,
                request.Color,
                request.Year),
            actorUserId,
            cancellationToken);

        return result.Status switch
        {
            CreateInstitutionalVehicleStatus.Success => Results.Created(
                $"/institutional-vehicles/{result.Vehicle!.Id}",
                result.Vehicle),
            CreateInstitutionalVehicleStatus.Conflict => Results.Conflict(new
            {
                Message = "Não foi possível cadastrar o veículo institucional.",
                Errors = result.Errors
            }),
            _ => Results.ValidationProblem(result.Errors)
        };
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        UpdateInstitutionalVehicleRequest request,
        HttpContext httpContext,
        InstitutionalVehicleCatalogService service,
        CancellationToken cancellationToken)
    {
        if (!AuthenticatedUser.TryGetId(httpContext.User, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var result = await service.UpdateAsync(
            id,
            new UpdateInstitutionalVehicleCommand(
                request.Plate,
                request.Identification,
                request.VehicleType,
                request.Brand,
                request.Model,
                request.Color,
                request.Year),
            actorUserId,
            cancellationToken);

        return result.Status switch
        {
            UpdateInstitutionalVehicleStatus.Success => Results.Ok(result.Vehicle),
            UpdateInstitutionalVehicleStatus.NotFound => Results.NotFound(new
            {
                Message = "Veículo institucional não encontrado."
            }),
            UpdateInstitutionalVehicleStatus.Conflict => Results.Conflict(new
            {
                Message = "Não foi possível atualizar o veículo institucional.",
                Errors = result.Errors
            }),
            _ => Results.ValidationProblem(result.Errors)
        };
    }

    private static Task<IResult> DeactivateAsync(
        int id,
        HttpContext httpContext,
        InstitutionalVehicleCatalogService service,
        CancellationToken cancellationToken) =>
        ChangeStateAsync(id, reactivate: false, httpContext, service, cancellationToken);

    private static Task<IResult> ReactivateAsync(
        int id,
        HttpContext httpContext,
        InstitutionalVehicleCatalogService service,
        CancellationToken cancellationToken) =>
        ChangeStateAsync(id, reactivate: true, httpContext, service, cancellationToken);

    private static async Task<IResult> ChangeStateAsync(
        int id,
        bool reactivate,
        HttpContext httpContext,
        InstitutionalVehicleCatalogService service,
        CancellationToken cancellationToken)
    {
        if (!AuthenticatedUser.TryGetId(httpContext.User, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var result = reactivate
            ? await service.ReactivateAsync(id, actorUserId, cancellationToken)
            : await service.DeactivateAsync(id, actorUserId, cancellationToken);

        return result.Status switch
        {
            ChangeInstitutionalVehicleStateStatus.Success => Results.NoContent(),
            ChangeInstitutionalVehicleStateStatus.NotFound => Results.NotFound(new
            {
                Message = "Veículo institucional não encontrado."
            }),
            ChangeInstitutionalVehicleStateStatus.Conflict => Results.Conflict(new
            {
                Message = reactivate
                    ? "O veículo institucional já está ativo."
                    : "O veículo institucional já está inativo.",
                Errors = result.Errors
            }),
            _ => Results.ValidationProblem(result.Errors)
        };
    }
}

public sealed record CreateInstitutionalVehicleRequest(
    string? Plate,
    string? Identification,
    string? VehicleType = null,
    string? Brand = null,
    string? Model = null,
    string? Color = null,
    int? Year = null);

public sealed record UpdateInstitutionalVehicleRequest(
    string? Plate,
    string? Identification,
    string? VehicleType = null,
    string? Brand = null,
    string? Model = null,
    string? Color = null,
    int? Year = null);
