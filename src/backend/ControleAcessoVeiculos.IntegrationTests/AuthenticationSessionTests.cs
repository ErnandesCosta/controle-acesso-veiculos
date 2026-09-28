using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControleAcessoVeiculos.Application.Authentication;
using ControleAcessoVeiculos.Domain.Entities;
using ControleAcessoVeiculos.Domain.Enums;
using ControleAcessoVeiculos.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControleAcessoVeiculos.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class AuthenticationSessionTests(ApiFactory factory)
{
    [Fact]
    public async Task LoginPublishesConfirmedSessionDeadlinesWithoutChangingBodyContract()
    {
        const string password = "Test-only-password-123!";
        var email = await CreateUserAsync(password);
        using var client = factory.CreateClient();
        var requestedAtUtc = DateTime.UtcNow;

        var login = await client.PostAsJsonAsync("/auth/login", new { email, password });

        login.EnsureSuccessStatusCode();
        var completedAtUtc = DateTime.UtcNow;
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));

        var inactivityDeadline = ReadUtcHeader(
            login,
            "X-Session-Inactivity-Expires-At");
        var serverTime = ReadUtcHeader(login, "X-Session-Server-Time");
        var absoluteDeadline = ReadUtcHeader(
            login,
            "X-Session-Absolute-Expires-At");

        Assert.InRange(
            serverTime,
            requestedAtUtc,
            completedAtUtc);
        Assert.Equal(
            TimeSpan.FromMinutes(15),
            inactivityDeadline - serverTime);
        Assert.InRange(
            inactivityDeadline,
            requestedAtUtc.AddMinutes(15),
            completedAtUtc.AddMinutes(15));
        Assert.InRange(
            absoluteDeadline,
            requestedAtUtc.AddHours(12),
            completedAtUtc.AddHours(12));
        Assert.True(inactivityDeadline < absoluteDeadline);
    }

    [Fact]
    public async Task RefreshRequiresValidAntiforgeryToken()
    {
        const string password = "Test-only-password-123!";
        var email = await CreateUserAsync(password);
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();

        var response = await client.PostAsync("/auth/refresh", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RefreshRotatesTokenAndReplayedTokenRevokesFamily()
    {
        const string password = "Test-only-password-123!";
        var email = await CreateUserAsync(password);
        var userId = await GetUserIdAsync(email);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });

        var csrfResponse = await client.GetAsync("/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.NotNull(csrf);
        var csrfCookie = GetCookie(csrfResponse, "cav_csrf");

        var login = await client.PostAsJsonAsync("/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var firstRefreshToken = GetCookie(login, "cav_refresh");
        var absoluteDeadline = ReadUtcHeader(
            login,
            "X-Session-Absolute-Expires-At");
        var refreshRequestedAtUtc = DateTime.UtcNow;

        var firstRefresh = await SendSessionRequestAsync(
            client,
            "/auth/refresh",
            csrf.RequestToken,
            csrfCookie,
            firstRefreshToken);
        firstRefresh.EnsureSuccessStatusCode();
        var refreshCompletedAtUtc = DateTime.UtcNow;
        var refreshedLogin = await firstRefresh.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(refreshedLogin);
        Assert.False(string.IsNullOrWhiteSpace(refreshedLogin.AccessToken));
        Assert.Equal(
            absoluteDeadline,
            ReadUtcHeader(firstRefresh, "X-Session-Absolute-Expires-At"),
            TimeSpan.FromMicroseconds(1));
        Assert.InRange(
            ReadUtcHeader(firstRefresh, "X-Session-Inactivity-Expires-At"),
            refreshRequestedAtUtc.AddMinutes(15),
            refreshCompletedAtUtc.AddMinutes(15));
        Assert.InRange(
            ReadUtcHeader(firstRefresh, "X-Session-Server-Time"),
            refreshRequestedAtUtc,
            refreshCompletedAtUtc);
        var secondRefreshToken = GetCookie(firstRefresh, "cav_refresh");
        Assert.NotEqual(firstRefreshToken, secondRefreshToken);

        var replay = await SendSessionRequestAsync(
            client,
            "/auth/refresh",
            csrf.RequestToken,
            csrfCookie,
            firstRefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var successorAfterReplay = await SendSessionRequestAsync(
            client,
            "/auth/refresh",
            csrf.RequestToken,
            csrfCookie,
            secondRefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, successorAfterReplay.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        var sessions = await dbContext.SessoesAutenticacao
            .AsNoTracking()
            .Where(item => item.UsuarioId == userId)
            .OrderBy(item => item.CriadaEm)
            .ToListAsync();

        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, session => Assert.NotNull(session.RevogadaEm));
        Assert.Contains(sessions, session =>
            session.MotivoRevogacao == MotivoRevogacaoSessao.ReutilizacaoDetectada);
        Assert.Contains(
            await dbContext.Auditorias.AsNoTracking().ToListAsync(),
            audit => audit.RegistroId == userId &&
                audit.DadosNovos!.Contains(
                    AuthenticationAuditOutcome.TokenReuseDetected.ToString(),
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task LogoutRevokesSessionFamilyAndExpiresCookie()
    {
        const string password = "Test-only-password-123!";
        var email = await CreateUserAsync(password);
        var userId = await GetUserIdAsync(email);
        using var client = factory.CreateClient();

        var csrfResponse = await client.GetAsync("/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.NotNull(csrf);

        var login = await client.PostAsJsonAsync("/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        logoutRequest.Headers.Add("X-CSRF-TOKEN", csrf.RequestToken);

        var logout = await client.SendAsync(logoutRequest);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var deletedCookie = Assert.Single(
            logout.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("cav_refresh=", StringComparison.Ordinal));
        Assert.Contains("expires=", deletedCookie, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        var session = await dbContext.SessoesAutenticacao
            .AsNoTracking()
            .SingleAsync(item => item.UsuarioId == userId);
        Assert.NotNull(session.RevogadaEm);
        Assert.Equal(MotivoRevogacaoSessao.Logout, session.MotivoRevogacao);

        var audit = await dbContext.Auditorias.AsNoTracking().SingleAsync(item =>
            item.RegistroId == userId && item.TipoAcao == TipoAcaoAuditoria.Logout);
        Assert.Contains(
            AuthenticationAuditOutcome.LogoutSucceeded.ToString(),
            audit.DadosNovos,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task InactiveProfileCannotRenewAndRevokesSessionFamily()
    {
        const string password = "Test-only-password-123!";
        var email = await CreateUserAsync(password);
        var userId = await GetUserIdAsync(email);
        using var client = factory.CreateClient();

        var csrfResponse = await client.GetAsync("/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.NotNull(csrf);
        var login = await client.PostAsJsonAsync("/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<ControleAcessoVeiculosDbContext>();
            var profileId = await dbContext.Usuarios
                .Where(item => item.Id == userId)
                .Select(item => item.PerfilId)
                .SingleAsync();
            await dbContext.Perfis
                .Where(item => item.Id == profileId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Ativo, false));
        }

        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        refreshRequest.Headers.Add("X-CSRF-TOKEN", csrf.RequestToken);
        var refresh = await client.SendAsync(refreshRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        using var verificationScope = factory.Services.CreateScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        var session = await verificationContext.SessoesAutenticacao
            .AsNoTracking()
            .SingleAsync(item => item.UsuarioId == userId);
        Assert.NotNull(session.RevogadaEm);
        Assert.Equal(
            MotivoRevogacaoSessao.ContaDesativada,
            session.MotivoRevogacao);
    }

    [Fact]
    public async Task ConcurrentRefreshDoesNotCreateTwoValidSuccessors()
    {
        const string password = "Test-only-password-123!";
        var email = await CreateUserAsync(password);
        var userId = await GetUserIdAsync(email);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });

        var csrfResponse = await client.GetAsync("/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.NotNull(csrf);
        var csrfCookie = GetCookie(csrfResponse, "cav_csrf");
        var login = await client.PostAsJsonAsync("/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var refreshToken = GetCookie(login, "cav_refresh");

        var responses = await Task.WhenAll(
            SendSessionRequestAsync(
                client,
                "/auth/refresh",
                csrf.RequestToken,
                csrfCookie,
                refreshToken),
            SendSessionRequestAsync(
                client,
                "/auth/refresh",
                csrf.RequestToken,
                csrfCookie,
                refreshToken));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response =>
            response.StatusCode == HttpStatusCode.Unauthorized));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        var activeSuccessors = await dbContext.SessoesAutenticacao.CountAsync(item =>
            item.UsuarioId == userId && item.RevogadaEm == null);
        Assert.True(activeSuccessors <= 1);
    }

    private static async Task<HttpResponseMessage> SendSessionRequestAsync(
        HttpClient client,
        string path,
        string csrfToken,
        string csrfCookie,
        string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        request.Headers.Add(
            "Cookie",
            $"cav_csrf={csrfCookie}; cav_refresh={refreshToken}");
        return await client.SendAsync(request);
    }

    private static string GetCookie(HttpResponseMessage response, string name)
    {
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith($"{name}=", StringComparison.Ordinal));
        return cookie.Split(';', 2)[0].Split('=', 2)[1];
    }

    private static DateTime ReadUtcHeader(
        HttpResponseMessage response,
        string name)
    {
        var value = Assert.Single(response.Headers.GetValues(name));
        return DateTime.Parse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
    }

    private async Task<string> CreateUserAsync(string password)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHashService>();
        var suffix = Guid.NewGuid().ToString("N");
        var profile = new Perfil(
            $"SessionTest{suffix}",
            "Perfil criado exclusivamente para teste de integracao.");
        dbContext.Perfis.Add(profile);

        var person = new Pessoa($"Pessoa de Teste {suffix}");
        dbContext.Pessoas.Add(person);
        await dbContext.SaveChangesAsync();

        var email = $"session-{suffix}@example.test";
        dbContext.Usuarios.Add(new Usuario(
            email,
            passwordHasher.Hash(password),
            person.Id,
            profile.Id));
        await dbContext.SaveChangesAsync();
        return email;
    }

    private async Task<int> GetUserIdAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        return await dbContext.Usuarios
            .Where(item => item.Email == email)
            .Select(item => item.Id)
            .SingleAsync();
    }

    private sealed record CsrfResponse(string RequestToken);
    private sealed record LoginResponse(
        string AccessToken,
        DateTime ExpiresAtUtc,
        LoginUserResponse User);
    private sealed record LoginUserResponse(int Id, string Email, string ProfileName);
}
