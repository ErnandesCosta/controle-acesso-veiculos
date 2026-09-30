using ControleAcessoVeiculos.API.Security;
using ControleAcessoVeiculos.Application.InstitutionalDrivers;

namespace ControleAcessoVeiculos.API.Endpoints;

public static class InstitutionalDriverEndpoints
{
    public static IEndpointRouteBuilder MapInstitutionalDriverEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/institutional-drivers")
            .RequireAuthorization(AuthorizationPolicies.ReadInstitutionalDrivers)
            .WithTags("Institutional drivers");

        group.MapGet(string.Empty, ListActiveAsync)
            .WithName("ListActiveInstitutionalDrivers")
            .WithSummary("Lista motoristas institucionais autorizados e ativos");
        group.MapPost(string.Empty, AuthorizeAsync)
            .RequireAuthorization(AuthorizationPolicies.ManageInstitutionalDrivers)
            .WithName("AuthorizeInstitutionalDriver")
            .WithSummary("Autoriza um motorista institucional");
        group.MapDelete("/{id:int}", DeactivateAsync)
            .RequireAuthorization(AuthorizationPolicies.ManageInstitutionalDrivers)
            .WithName("DeactivateInstitutionalDriver")
            .WithSummary("Desativa uma autorização de motorista institucional");

        return endpoints;
    }

    private static async Task<IResult> ListActiveAsync(
        InstitutionalDriverService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListActiveAsync(cancellationToken));

    private static async Task<IResult> AuthorizeAsync(
        AuthorizeInstitutionalDriverRequest request,
        HttpContext httpContext,
        InstitutionalDriverService service,
        CancellationToken cancellationToken)
    {
        if (!AuthenticatedUser.TryGetId(httpContext.User, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var result = await service.AuthorizeAsync(
            new AuthorizeInstitutionalDriverCommand(
                request.Name,
                request.DocumentType,
                request.DocumentNumber),
            actorUserId,
            cancellationToken);

        return result.Status switch
        {
            AuthorizeInstitutionalDriverStatus.Success => Results.Created(
                $"/institutional-drivers/{result.Driver!.Id}",
                result.Driver),
            AuthorizeInstitutionalDriverStatus.Conflict => Results.Conflict(new
            {
                Message = "Não foi possível autorizar o motorista institucional.",
                Errors = result.Errors
            }),
            _ => Results.ValidationProblem(result.Errors)
        };
    }

    private static async Task<IResult> DeactivateAsync(
        int id,
        HttpContext httpContext,
        InstitutionalDriverService service,
        CancellationToken cancellationToken)
    {
        if (!AuthenticatedUser.TryGetId(httpContext.User, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        if (id <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["id"] = ["O identificador deve ser maior que zero."]
            });
        }

        var result = await service.DeactivateAsync(
            id,
            actorUserId,
            cancellationToken);

        return result.Status switch
        {
            DeactivateInstitutionalDriverStatus.Success => Results.NoContent(),
            DeactivateInstitutionalDriverStatus.NotFound => Results.NotFound(new
            {
                Message = "Autorização de motorista institucional não encontrada."
            }),
            _ => Results.Conflict(new
            {
                Message = "A autorização de motorista institucional já está inativa."
            })
        };
    }
}

public sealed record AuthorizeInstitutionalDriverRequest(
    string? Name,
    string? DocumentType = null,
    string? DocumentNumber = null);
