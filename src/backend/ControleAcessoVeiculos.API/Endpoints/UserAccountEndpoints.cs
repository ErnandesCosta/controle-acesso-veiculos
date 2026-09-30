using ControleAcessoVeiculos.API.Security;
using ControleAcessoVeiculos.Application.Accounts;

namespace ControleAcessoVeiculos.API.Endpoints;

public static class UserAccountEndpoints
{
    public static IEndpointRouteBuilder MapUserAccountEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/users")
            .RequireAuthorization(AuthorizationPolicies.ManageUsers)
            .WithTags("Users");

        group.MapGet(string.Empty, SearchAsync)
            .WithName("SearchUsers")
            .WithSummary("Consulta contas de usuário");
        group.MapPost(string.Empty, CreateAsync)
            .WithName("CreateUser")
            .WithSummary("Cria uma conta com credencial temporária");
        group.MapDelete("/{id:int}", DeactivateAsync)
            .WithName("DeactivateUser")
            .WithSummary("Desativa uma conta de usuário");
        group.MapPost("/{id:int}/reactivation", ReactivateAsync)
            .WithName("ReactivateUser")
            .WithSummary("Reativa uma conta de usuário");
        group.MapPost("/{id:int}/temporary-credential", ResetCredentialAsync)
            .RequireRateLimiting(ApiRateLimiting.PasswordChangePolicy)
            .WithName("ResetUserCredential")
            .WithSummary("Emite uma nova credencial temporária para a conta")
            .Produces<TemporaryCredentialResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<IResult> SearchAsync(
        [AsParameters] SearchUserAccountsRequest request,
        UserAccountLifecycleService service,
        CancellationToken cancellationToken)
    {
        var result = await service.SearchAsync(
            new SearchUserAccountsCommand(
                request.Search,
                request.Active,
                request.Page,
                request.PageSize),
            cancellationToken);

        return result.Status == SearchUserAccountsStatus.Success
            ? Results.Ok(result.Result)
            : Results.ValidationProblem(result.Errors);
    }

    private static async Task<IResult> CreateAsync(
        CreateUserRequest request,
        HttpContext httpContext,
        CreateUserAccountService service,
        CancellationToken cancellationToken)
    {
        if (!AuthenticatedUser.TryGetId(httpContext.User, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateAsync(
            new CreateUserAccountCommand(
                request.Name,
                request.Email,
                request.Password,
                request.ProfileName),
            actorUserId,
            cancellationToken);

        if (result.Status == CreateUserAccountStatus.Success)
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            httpContext.Response.Headers.Pragma = "no-cache";
            return Results.Created(
                $"/users/{result.UserId}",
                new CreateUserResponse(
                    result.UserId!.Value,
                    result.Email!,
                    result.ProfileName!,
                    result.TemporaryCredential,
                    result.TemporaryCredentialExpiresAtUtc));
        }

        return result.Status switch
        {
            CreateUserAccountStatus.Conflict => Results.Conflict(new
            {
                Message = "Não foi possível criar a conta.",
                Errors = result.Errors
            }),
            _ => Results.ValidationProblem(result.Errors)
        };
    }

    private static async Task<IResult> ResetCredentialAsync(
        int id,
        AdministrativeCredentialResetRequest request,
        HttpContext httpContext,
        AdministrativeCredentialResetService service,
        CancellationToken cancellationToken)
    {
        if (!AuthenticatedUser.TryGetId(httpContext.User, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var result = await service.ResetAsync(
            id,
            actorUserId,
            request.Reason,
            cancellationToken);

        if (result.Status == AdministrativeCredentialResetStatus.Success)
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            httpContext.Response.Headers.Pragma = "no-cache";
            return Results.Ok(new TemporaryCredentialResponse(
                result.TemporaryCredential!,
                result.TemporaryCredentialExpiresAtUtc!.Value));
        }

        return result.Status switch
        {
            AdministrativeCredentialResetStatus.NotFound => Results.NotFound(new
            {
                Message = "Conta de usuário não encontrada."
            }),
            AdministrativeCredentialResetStatus.Inactive => Results.Conflict(new
            {
                Message = "Reative a conta antes de redefinir sua credencial."
            }),
            AdministrativeCredentialResetStatus.SelfReset => Results.Conflict(new
            {
                Message = "Use a troca autenticada para alterar a própria senha."
            }),
            _ => Results.ValidationProblem(result.Errors)
        };
    }

    private static Task<IResult> DeactivateAsync(
        int id,
        HttpContext httpContext,
        UserAccountLifecycleService service,
        CancellationToken cancellationToken) =>
        ChangeStateAsync(id, reactivate: false, httpContext, service, cancellationToken);

    private static Task<IResult> ReactivateAsync(
        int id,
        HttpContext httpContext,
        UserAccountLifecycleService service,
        CancellationToken cancellationToken) =>
        ChangeStateAsync(id, reactivate: true, httpContext, service, cancellationToken);

    private static async Task<IResult> ChangeStateAsync(
        int id,
        bool reactivate,
        HttpContext httpContext,
        UserAccountLifecycleService service,
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
            ChangeUserAccountStateStatus.Success => Results.NoContent(),
            ChangeUserAccountStateStatus.NotFound => Results.NotFound(new
            {
                Message = "Conta de usuário não encontrada."
            }),
            ChangeUserAccountStateStatus.SelfDeactivation => Results.Conflict(new
            {
                Message = "Um administrador não pode desativar a própria conta.",
                Errors = result.Errors
            }),
            ChangeUserAccountStateStatus.LastAdministrator => Results.Conflict(new
            {
                Message = "A operação deixaria o sistema sem administrador ativo."
            }),
            ChangeUserAccountStateStatus.Conflict => Results.Conflict(new
            {
                Message = reactivate
                    ? "A conta de usuário já está ativa."
                    : "A conta de usuário já está inativa."
            }),
            _ => Results.ValidationProblem(result.Errors)
        };
    }
}

public sealed record SearchUserAccountsRequest(
    string? Search = null,
    bool? Active = null,
    int Page = 1,
    int PageSize = 25);

public sealed record CreateUserRequest(
    string Name,
    string Email,
    string? Password,
    string ProfileName);

public sealed record CreateUserResponse(
    int Id,
    string Email,
    string ProfileName,
    string? TemporaryCredential,
    DateTime? TemporaryCredentialExpiresAtUtc);

public sealed record AdministrativeCredentialResetRequest(string Reason);

public sealed record TemporaryCredentialResponse(
    string TemporaryCredential,
    DateTime TemporaryCredentialExpiresAtUtc);
