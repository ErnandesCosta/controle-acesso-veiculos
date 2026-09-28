using ControleAcessoVeiculos.API.Health;
using ControleAcessoVeiculos.API.Endpoints;
using ControleAcessoVeiculos.API.Middleware;
using ControleAcessoVeiculos.API.Observability;
using ControleAcessoVeiculos.API.Security;
using ControleAcessoVeiculos.Application.AccessRecords;
using ControleAcessoVeiculos.Application.Accounts;
using ControleAcessoVeiculos.Application.Authentication;
using ControleAcessoVeiculos.Application.Authorization;
using ControleAcessoVeiculos.Application.Auditing;
using ControleAcessoVeiculos.Application.InstitutionalVehicleUsages;
using ControleAcessoVeiculos.Application.InstitutionalVehicles;
using ControleAcessoVeiculos.Application.InstitutionalDrivers;
using ControleAcessoVeiculos.Application.EventAuthorizations;
using ControleAcessoVeiculos.Application.OperationalSummaries;
using ControleAcessoVeiculos.Infrastructure.Authentication;
using ControleAcessoVeiculos.Infrastructure.Auditing;
using ControleAcessoVeiculos.Infrastructure.AccessRecords;
using ControleAcessoVeiculos.Infrastructure.Data;
using ControleAcessoVeiculos.Infrastructure.InstitutionalVehicleUsages;
using ControleAcessoVeiculos.Infrastructure.InstitutionalVehicles;
using ControleAcessoVeiculos.Infrastructure.InstitutionalDrivers;
using ControleAcessoVeiculos.Infrastructure.EventAuthorizations;
using ControleAcessoVeiculos.Infrastructure.OperationalSummaries;
using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddApiObservability(builder.Configuration);
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["correlationId"] =
            RequestSafetyMiddleware.GetCorrelationId(context.HttpContext);
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "cav_csrf";
    options.Cookie.HttpOnly = false;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/auth";
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ||
        builder.Environment.IsEnvironment("LocalContainer") ||
        builder.Environment.IsEnvironment("Testing")
        ? CookieSecurePolicy.None
        : CookieSecurePolicy.Always;
});
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = RequestSafetyMiddleware.MaximumRequestBodySize);

var rateLimitOptions = builder.Configuration
    .GetSection(ApiRateLimitOptions.SectionName)
    .Get<ApiRateLimitOptions>() ?? new ApiRateLimitOptions();
rateLimitOptions.Validate();
builder.Services.Configure<ApiRateLimitOptions>(
    builder.Configuration.GetSection(ApiRateLimitOptions.SectionName));
builder.Services.AddRateLimiter(options =>
    ApiRateLimiting.Configure(options, rateLimitOptions));

var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>() ?? new JwtOptions();
jwtOptions.Validate();
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));

var sessionOptions = builder.Configuration
    .GetSection(AuthenticationSessionOptions.SectionName)
    .Get<AuthenticationSessionOptions>() ?? new AuthenticationSessionOptions();
var sessionPolicy = sessionOptions.Validate();
builder.Services.Configure<AuthenticationSessionOptions>(
    builder.Configuration.GetSection(AuthenticationSessionOptions.SectionName));
builder.Services.AddSingleton(sessionPolicy);

var temporaryCredentialOptions = builder.Configuration
    .GetSection(TemporaryCredentialOptions.SectionName)
    .Get<TemporaryCredentialOptions>() ?? new TemporaryCredentialOptions();
var temporaryCredentialPolicy = temporaryCredentialOptions.Validate();
builder.Services.Configure<TemporaryCredentialOptions>(
    builder.Configuration.GetSection(TemporaryCredentialOptions.SectionName));
builder.Services.AddSingleton(temporaryCredentialPolicy);

var institutionalTimeZoneId =
    builder.Configuration["Institution:TimeZoneId"] ?? "America/Recife";
TimeZoneInfo institutionalTimeZone;
try
{
    institutionalTimeZone = TimeZoneInfo.FindSystemTimeZoneById(institutionalTimeZoneId);
}
catch (TimeZoneNotFoundException exception)
{
    throw new InvalidOperationException(
        $"O fuso institucional '{institutionalTimeZoneId}' não foi encontrado.",
        exception);
}
catch (InvalidTimeZoneException exception)
{
    throw new InvalidOperationException(
        $"O fuso institucional '{institutionalTimeZoneId}' é inválido.",
        exception);
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "email",
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                if (context.Principal is null ||
                    !AuthenticatedUser.TryGetId(context.Principal, out var userId))
                {
                    context.Fail("Invalid token subject.");
                    return;
                }

                var credentialVersionClaim = context.Principal.FindFirst(
                    AuthenticationClaimTypes.CredentialVersion)?.Value;
                if (!int.TryParse(
                        credentialVersionClaim,
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var credentialVersion) ||
                    credentialVersion <= 0)
                {
                    context.Fail("Invalid credential version.");
                    return;
                }

                var userStore = context.HttpContext.RequestServices
                    .GetRequiredService<IAuthenticationUserStore>();
                if (!await userStore.IsAuthenticationStateValidAsync(
                        userId,
                        credentialVersion,
                        context.HttpContext.RequestAborted))
                {
                    context.Fail("Inactive user account.");
                }
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build())
    .AddPolicy(AuthorizationPolicies.OperateAccess, policy => policy.RequireRole(
        ProfileNames.Doorman,
        ProfileNames.SecurityGuard,
        ProfileNames.Administrator))
    .AddPolicy(AuthorizationPolicies.ReviewAccessRecords, policy => policy.RequireRole(
        ProfileNames.Doorman,
        ProfileNames.SecurityGuard,
        ProfileNames.TransportationDepartment,
        ProfileNames.Administrator))
    .AddPolicy(AuthorizationPolicies.CorrectAccessRecords, policy => policy.RequireRole(
        ProfileNames.Doorman,
        ProfileNames.SecurityGuard,
        ProfileNames.Administrator))
    .AddPolicy(AuthorizationPolicies.ExceptionallyCloseAccessRecords,
        policy => policy.RequireRole(
            ProfileNames.Doorman,
            ProfileNames.SecurityGuard,
            ProfileNames.Administrator))
    .AddPolicy(AuthorizationPolicies.ReviewTransportationRecords, policy => policy.RequireRole(
        ProfileNames.TransportationDepartment,
        ProfileNames.Administrator))
    .AddPolicy(AuthorizationPolicies.ReadInstitutionalVehicleCatalog, policy => policy.RequireRole(
        ProfileNames.Doorman,
        ProfileNames.SecurityGuard,
        ProfileNames.TransportationDepartment,
        ProfileNames.Administrator))
    .AddPolicy(AuthorizationPolicies.ManageInstitutionalVehicleCatalog, policy => policy.RequireRole(
        ProfileNames.TransportationDepartment,
        ProfileNames.Administrator))
    .AddPolicy(AuthorizationPolicies.ReadInstitutionalDrivers, policy => policy.RequireRole(
        ProfileNames.Doorman,
        ProfileNames.SecurityGuard,
        ProfileNames.TransportationDepartment,
        ProfileNames.Administrator))
    .AddPolicy(AuthorizationPolicies.ManageInstitutionalDrivers, policy => policy.RequireRole(
        ProfileNames.TransportationDepartment,
        ProfileNames.Administrator))
    .AddPolicy(AuthorizationPolicies.ReadEventAuthorizations, policy => policy.RequireRole(
        ProfileNames.Doorman,
        ProfileNames.SecurityGuard,
        ProfileNames.TransportationDepartment,
        ProfileNames.Administrator))
    .AddPolicy(AuthorizationPolicies.ManageEventAuthorizations, policy => policy.RequireRole(
        ProfileNames.Administrator))
    .AddPolicy(AuthorizationPolicies.ManageUsers, policy => policy.RequireRole(
        ProfileNames.Administrator))
    .AddPolicy(AuthorizationPolicies.ReviewAuditTrail, policy => policy.RequireRole(
        ProfileNames.Administrator))
    .AddPolicy(AuthorizationPolicies.ReviewOperationalSummary, policy => policy.RequireRole(
        ProfileNames.Doorman,
        ProfileNames.SecurityGuard,
        ProfileNames.TransportationDepartment,
        ProfileNames.Administrator));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(institutionalTimeZone);
builder.Services.AddSingleton<IPasswordHashService, AspNetPasswordHashService>();
builder.Services.AddScoped<IAuthenticationUserStore, AuthenticationUserStore>();
builder.Services.AddScoped<IAuthenticationSessionStore, AuthenticationSessionStore>();
builder.Services.AddScoped<IAuthenticatedPasswordChangeStore, AuthenticatedPasswordChangeStore>();
builder.Services.AddScoped<IUserAccountStore, UserAccountStore>();
builder.Services.AddScoped<IVehicleAccessStore, VehicleAccessStore>();
builder.Services.AddScoped<IInstitutionalVehicleUsageStore, InstitutionalVehicleUsageStore>();
builder.Services.AddScoped<IInstitutionalVehicleCatalogStore, InstitutionalVehicleCatalogStore>();
builder.Services.AddScoped<IInstitutionalDriverStore, InstitutionalDriverStore>();
builder.Services.AddScoped<IEventAuthorizationStore, EventAuthorizationStore>();
builder.Services.AddScoped<IAuditTrailStore, AuditTrailStore>();
builder.Services.AddScoped<IOperationalSummaryStore, OperationalSummaryStore>();
builder.Services.AddScoped<IAccessTokenService, JwtAccessTokenService>();
builder.Services.AddSingleton<IRefreshTokenService, CryptographicRefreshTokenService>();
builder.Services.AddSingleton<ITemporaryCredentialGenerator,
    CryptographicTemporaryCredentialGenerator>();
builder.Services.AddScoped<AuthenticationSessionCookie>();
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<AuthenticationSessionService>();
builder.Services.AddScoped<AuthenticatedPasswordChangeService>();
builder.Services.AddScoped<CreateUserAccountService>();
builder.Services.AddScoped<AdministrativeCredentialResetService>();
builder.Services.AddScoped<UserAccountLifecycleService>();
builder.Services.AddScoped<BootstrapAdministratorService>();
builder.Services.AddScoped<VehicleAccessService>();
builder.Services.AddScoped<InstitutionalVehicleUsageService>();
builder.Services.AddScoped<InstitutionalVehicleCatalogService>();
builder.Services.AddScoped<InstitutionalDriverService>();
builder.Services.AddScoped<EventAuthorizationService>();
builder.Services.AddScoped<AuditTrailService>();
builder.Services.AddScoped<OperationalSummaryService>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? throw new InvalidOperationException(
        "A connection string 'ConnectionStrings:DefaultConnection' não foi configurada.");

builder.Services.AddDbContext<ControleAcessoVeiculosDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseReadinessHealthCheck>(
        "postgresql",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseMiddleware<RequestSafetyMiddleware>();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    var requiresPasswordChange = string.Equals(
        context.User.FindFirst(AuthenticationClaimTypes.RequiresPasswordChange)?.Value,
        bool.TrueString,
        StringComparison.OrdinalIgnoreCase);
    var allowedDuringPasswordChange =
        context.Request.Path.Equals("/auth/password", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.Equals("/auth/logout", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.Equals("/auth/refresh", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.Equals("/auth/csrf", StringComparison.OrdinalIgnoreCase);

    if (context.User.Identity?.IsAuthenticated == true &&
        requiresPasswordChange &&
        !allowedDuringPasswordChange)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            Type = "https://httpstatuses.com/403",
            Title = "Troca de senha obrigatória.",
            Status = StatusCodes.Status403Forbidden,
            Detail = "Defina uma nova senha antes de acessar as operações do sistema."
        });
        return;
    }

    await next();
});
app.UseAuthorization();
app.UseAntiforgery();

var livenessOptions = new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthResponse
};

app.MapHealthChecks("/health", livenessOptions)
    .AllowAnonymous()
    .DisableRateLimiting();
app.MapHealthChecks("/health/live", livenessOptions)
    .AllowAnonymous()
    .DisableRateLimiting();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
})
    .AllowAnonymous()
    .DisableRateLimiting();

app.MapVehicleAccessEndpoints();
app.MapInstitutionalVehicleUsageEndpoints();
app.MapInstitutionalVehicleCatalogEndpoints();
app.MapInstitutionalDriverEndpoints();
app.MapEventAuthorizationEndpoints();
app.MapUserAccountEndpoints();
app.MapAuditTrailEndpoints();
app.MapOperationalSummaryEndpoints();

app.MapPost("/auth/login", async (
    LoginRequest request,
    LoginService loginService,
    AuthenticationSessionCookie sessionCookie,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) ||
        string.IsNullOrWhiteSpace(request.Password) ||
        request.Email.Length > 254 ||
        request.Password.Length > 1024)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["credentials"] = ["E-mail e senha são obrigatórios."]
        });
    }

    var result = await loginService.AuthenticateAsync(
        request.Email,
        request.Password,
        cancellationToken);

    SetAuthenticationResponseHeaders(httpContext.Response);

    if (!result.IsSuccess)
    {
        return Results.Json(
            new LoginErrorResponse("Credenciais inválidas."),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    sessionCookie.Append(
        httpContext.Response,
        result.RefreshToken!,
        result.SessionExpiresAtUtc!.Value);
    SetSessionDeadlineResponseHeaders(
        httpContext.Response,
        result.SessionObservedAtUtc!.Value,
        result.SessionInactivityExpiresAtUtc!.Value,
        result.SessionExpiresAtUtc.Value);

    return Results.Ok(new LoginResponse(
        result.AccessToken!,
        result.ExpiresAtUtc!.Value,
        new LoginUserResponse(
            result.User!.Id,
            result.User.Email,
            result.User.ProfileName,
            result.User.RequiresPasswordChange)));
})
.AllowAnonymous()
.RequireRateLimiting(ApiRateLimiting.LoginPolicy)
.WithName("Login")
.Produces<LoginResponse>(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.Produces<LoginErrorResponse>(StatusCodes.Status401Unauthorized);

app.MapGet("/auth/csrf", (
    Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery,
    HttpContext httpContext) =>
{
    SetAuthenticationResponseHeaders(httpContext.Response);
    var tokens = antiforgery.GetAndStoreTokens(httpContext);
    return Results.Ok(new CsrfTokenResponse(tokens.RequestToken!));
})
.AllowAnonymous()
.WithName("GetAuthenticationCsrfToken")
.Produces<CsrfTokenResponse>(StatusCodes.Status200OK);

app.MapPost("/auth/refresh", async (
    AuthenticationSessionService sessionService,
    AuthenticationSessionCookie sessionCookie,
    Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    SetAuthenticationResponseHeaders(httpContext.Response);

    if (!await IsAntiforgeryRequestValidAsync(antiforgery, httpContext))
    {
        return Results.BadRequest();
    }

    if (!sessionCookie.TryRead(httpContext.Request, out var refreshToken))
    {
        sessionCookie.Delete(httpContext.Response);
        return Results.Unauthorized();
    }

    var result = await sessionService.RenewAsync(
        refreshToken,
        cancellationToken);

    if (!result.IsSuccess)
    {
        sessionCookie.Delete(httpContext.Response);
        return Results.Unauthorized();
    }

    sessionCookie.Append(
        httpContext.Response,
        result.RefreshToken!,
        result.SessionExpiresAtUtc!.Value);
    SetSessionDeadlineResponseHeaders(
        httpContext.Response,
        result.SessionObservedAtUtc!.Value,
        result.SessionInactivityExpiresAtUtc!.Value,
        result.SessionExpiresAtUtc.Value);

    return Results.Ok(new LoginResponse(
        result.AccessToken!,
        result.ExpiresAtUtc!.Value,
        new LoginUserResponse(
            result.User!.Id,
            result.User.Email,
            result.User.ProfileName,
            result.User.RequiresPasswordChange)));
})
.AllowAnonymous()
.RequireRateLimiting(ApiRateLimiting.LoginPolicy)
.WithName("RefreshAuthenticationSession")
.Produces<LoginResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

app.MapPost("/auth/logout", async (
    AuthenticationSessionService sessionService,
    AuthenticationSessionCookie sessionCookie,
    Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    SetAuthenticationResponseHeaders(httpContext.Response);

    if (!await IsAntiforgeryRequestValidAsync(antiforgery, httpContext))
    {
        return Results.BadRequest();
    }

    sessionCookie.TryRead(httpContext.Request, out var refreshToken);
    await sessionService.EndAsync(refreshToken, cancellationToken);
    sessionCookie.Delete(httpContext.Response);
    return Results.NoContent();
})
.AllowAnonymous()
.WithName("Logout")
.Produces(StatusCodes.Status204NoContent);

app.MapPost("/auth/password", async (
    AuthenticatedPasswordChangeRequest request,
    AuthenticatedPasswordChangeService passwordChangeService,
    AuthenticationSessionCookie sessionCookie,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    SetAuthenticationResponseHeaders(httpContext.Response);

    if (!AuthenticatedUser.TryGetId(httpContext.User, out var userId))
    {
        return Results.Unauthorized();
    }

    var result = await passwordChangeService.ChangeAsync(
        userId,
        request.CurrentPassword,
        request.NewPassword,
        cancellationToken);

    if (result.Status == AuthenticatedPasswordChangeStatus.Success)
    {
        sessionCookie.Delete(httpContext.Response);
        return Results.NoContent();
    }

    return result.Status switch
    {
        AuthenticatedPasswordChangeStatus.Unauthorized => Results.Unauthorized(),
        _ => Results.ValidationProblem(result.Errors)
    };
})
.RequireRateLimiting(ApiRateLimiting.PasswordChangePolicy)
.WithName("ChangeAuthenticatedPassword")
.Produces(StatusCodes.Status204NoContent)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status429TooManyRequests);

if (args.Contains("--bootstrap-admin", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var bootstrapService = scope.ServiceProvider
        .GetRequiredService<BootstrapAdministratorService>();
    var bootstrapSection = app.Configuration.GetSection("BootstrapAdmin");
    var name = bootstrapSection["Name"];
    var email = bootstrapSection["Email"];
    var password = bootstrapSection["Password"];

    if (string.IsNullOrWhiteSpace(name) ||
        string.IsNullOrWhiteSpace(email) ||
        string.IsNullOrWhiteSpace(password))
    {
        throw new InvalidOperationException(
            "BootstrapAdmin:Name, BootstrapAdmin:Email e BootstrapAdmin:Password são obrigatórios para o provisionamento inicial.");
    }

    var status = await bootstrapService.BootstrapAsync(name, email, password);

    Console.WriteLine(status switch
    {
        BootstrapAdministratorStatus.Success =>
            "Administrador inicial criado. Remova as variáveis BootstrapAdmin do ambiente.",
        BootstrapAdministratorStatus.AlreadyInitialized =>
            "O banco já possui usuários; nenhum administrador foi criado.",
        _ => "Não foi possível criar o administrador inicial. Revise os valores informados."
    });

    return;
}

app.Run();

static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    return context.Response.WriteAsJsonAsync(new
    {
        Status = report.Status.ToString(),
        Timestamp = DateTime.UtcNow
    });
}

static void SetAuthenticationResponseHeaders(HttpResponse response)
{
    response.Headers.CacheControl = "no-store";
    response.Headers.Pragma = "no-cache";
}

static void SetSessionDeadlineResponseHeaders(
    HttpResponse response,
    DateTime observedAtUtc,
    DateTime inactivityExpiresAtUtc,
    DateTime absoluteExpiresAtUtc)
{
    response.Headers["X-Session-Server-Time"] =
        observedAtUtc.ToString("O", CultureInfo.InvariantCulture);
    response.Headers["X-Session-Inactivity-Expires-At"] =
        inactivityExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture);
    response.Headers["X-Session-Absolute-Expires-At"] =
        absoluteExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture);
}

static async Task<bool> IsAntiforgeryRequestValidAsync(
    Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery,
    HttpContext context)
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
        return true;
    }
    catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException)
    {
        return false;
    }
}

public partial class Program
{
}

public sealed record LoginRequest(string Email, string Password);
public sealed record AuthenticatedPasswordChangeRequest(
    string CurrentPassword,
    string NewPassword);
public sealed record LoginResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    LoginUserResponse User);
public sealed record LoginUserResponse(
    int Id,
    string Email,
    string ProfileName,
    bool RequiresPasswordChange);
public sealed record LoginErrorResponse(string Message);
public sealed record CsrfTokenResponse(string RequestToken);
