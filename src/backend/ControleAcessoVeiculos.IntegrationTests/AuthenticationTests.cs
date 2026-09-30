using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ControleAcessoVeiculos.Application.Authentication;
using ControleAcessoVeiculos.Application.Authorization;
using ControleAcessoVeiculos.Domain.Entities;
using ControleAcessoVeiculos.Domain.Enums;
using ControleAcessoVeiculos.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ControleAcessoVeiculos.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class AuthenticationTests(ApiFactory factory)
{
    [Fact]
    public async Task ValidCredentialsIssueTokenForProtectedEndpoint()
    {
        const string password = "Test-only-password-123!";
        var email = await CreateUserAsync(ProfileNames.Administrator, password);
        var userId = await GetUserIdAsync(email);
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/auth/login", new
        {
            email = $"  {email.ToUpperInvariant()}  ",
            password
        }, cancellationToken: TestContext.Current.CancellationToken);
        var responseContent = await login.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var body = JsonSerializer.Deserialize<LoginResponse>(
            responseContent,
            JsonSerializerOptions.Web);

        login.EnsureSuccessStatusCode();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
        Assert.True(body.ExpiresAtUtc > DateTime.UtcNow);
        Assert.Equal(userId, body.User.Id);
        Assert.Equal(email, body.User.Email);
        Assert.Equal(ProfileNames.Administrator, body.User.ProfileName);

        var refreshCookie = Assert.Single(
            login.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("cav_refresh=", StringComparison.Ordinal));
        Assert.Contains("httponly", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/auth", refreshCookie, StringComparison.OrdinalIgnoreCase);

        using (var responseJson = JsonDocument.Parse(responseContent))
        {
            var userProperties = responseJson.RootElement
                .GetProperty("user")
                .EnumerateObject()
                .Select(property => property.Name)
                .Order()
                .ToArray();
            Assert.Equal(
                ["email", "id", "profileName", "requiresPasswordChange"],
                userProperties);
            Assert.False(responseJson.RootElement.TryGetProperty("refreshToken", out _));
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<ControleAcessoVeiculosDbContext>();
            var session = await dbContext.SessoesAutenticacao
                .AsNoTracking()
                .SingleAsync(item => item.UsuarioId == userId, cancellationToken: TestContext.Current.CancellationToken);
            var rawRefreshToken = refreshCookie
                .Split(';', 2)[0]
                .Split('=', 2)[1];

            Assert.Equal(64, session.TokenHash.Length);
            Assert.DoesNotContain(rawRefreshToken, session.TokenHash, StringComparison.Ordinal);
            Assert.True(session.ExpiraEm > DateTime.UtcNow);
            Assert.Null(session.RevogadaEm);
        }

        var audit = await GetSingleAuthenticationAuditAsync(email);
        Assert.Equal(TipoAcaoAuditoria.Login, audit.TipoAcao);
        Assert.Equal(audit.UsuarioId, audit.RegistroId);
        Assert.Equal(nameof(Usuario), audit.Entidade);
        using (var auditState = JsonDocument.Parse(audit.DadosNovos!))
        {
            Assert.Equal(
                AuthenticationAuditOutcome.LoginSucceeded.ToString(),
                auditState.RootElement.GetProperty("outcome").GetString());
            Assert.False(auditState.RootElement.TryGetProperty("lockedUntilUtc", out _));
        }

        var auditContent = string.Join(' ',
            audit.DadosAnteriores,
            audit.DadosNovos,
            audit.Detalhes);
        Assert.DoesNotContain(email, auditContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, auditContent, StringComparison.Ordinal);
        Assert.DoesNotContain(body.AccessToken, auditContent, StringComparison.Ordinal);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.AccessToken);
        var protectedResponse = await client.GetAsync("/access-records/open", TestContext.Current.CancellationToken);

        protectedResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task InvalidCredentialsReturnGenericUnauthorizedResponse()
    {
        using var client = factory.CreateClient();
        var auditsBefore = await CountAuthenticationAuditsAsync();

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            email = $"missing-{Guid.NewGuid():N}@example.test",
            password = "Wrong-password-123!"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Credenciais inválidas.", body?.Message);
        Assert.Equal(auditsBefore, await CountAuthenticationAuditsAsync());
    }

    [Fact]
    public async Task SuccessfulLoginUpgradesOutdatedPasswordHash()
    {
        const string password = "Test-only-password-123!";
        var email = await CreateUserAsync(
            ProfileNames.Administrator,
            password,
            outdatedPasswordHash: true);
        var outdatedHash = await GetPasswordHashAsync(email);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new { email, password }, cancellationToken: TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var upgradedHash = await GetPasswordHashAsync(email);
        Assert.NotEqual(outdatedHash, upgradedHash);

        using var scope = factory.Services.CreateScope();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHashService>();
        Assert.Equal(
            PasswordHashVerificationResult.Success,
            passwordHasher.Verify(upgradedHash, password));
    }

    [Fact]
    public async Task SuccessfulLoginDoesNotRewriteCurrentPasswordHash()
    {
        const string password = "Test-only-password-123!";
        var email = await CreateUserAsync(ProfileNames.Administrator, password);
        var currentHash = await GetPasswordHashAsync(email);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new { email, password }, cancellationToken: TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Equal(currentHash, await GetPasswordHashAsync(email));
    }

    [Fact]
    public async Task AuditFailureRollsBackOutdatedPasswordHashUpgradeAndSession()
    {
        const string password = "Test-only-password-123!";
        var email = await CreateUserAsync(
            ProfileNames.Administrator,
            password,
            outdatedPasswordHash: true);
        var outdatedHash = await GetPasswordHashAsync(email);
        var userId = await GetUserIdAsync(email);
        using var client = factory.CreateClient();

        await InstallRejectingAuthenticationAuditTriggerAsync();
        try
        {
            var response = await client.PostAsJsonAsync("/auth/login", new { email, password }, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await RemoveRejectingAuthenticationAuditTriggerAsync();
        }

        Assert.Equal(outdatedHash, await GetPasswordHashAsync(email));
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        Assert.Equal(0, await dbContext.SessoesAutenticacao.CountAsync(item =>
            item.UsuarioId == userId, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FiveInvalidAttemptsTemporarilyBlockAccount()
    {
        const string password = "Test-only-password-123!";
        var email = await CreateUserAsync(ProfileNames.Administrator, password);
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < LoginService.MaximumFailedAttempts; attempt++)
        {
            var invalidResponse = await client.PostAsJsonAsync("/auth/login", new
            {
                email,
                password = "Wrong-password-123!"
            }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
        }

        var blockedResponse = await client.PostAsJsonAsync("/auth/login", new
        {
            email,
            password
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, blockedResponse.StatusCode);

        var audit = await GetSingleAuthenticationAuditAsync(email);
        using var auditState = JsonDocument.Parse(audit.DadosNovos!);
        Assert.Equal(
            AuthenticationAuditOutcome.AccountLocked.ToString(),
            auditState.RootElement.GetProperty("outcome").GetString());
        Assert.True(auditState.RootElement.GetProperty("lockedUntilUtc").GetDateTime() > DateTime.UtcNow);
        Assert.DoesNotContain(email, audit.DadosNovos, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, audit.DadosNovos, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditFailureShouldRejectLoginAndRollBackUserState()
    {
        const string password = "Test-only-password-123!";
        var email = await CreateUserAsync(ProfileNames.Administrator, password);
        using var client = factory.CreateClient();
        var failedAttempt = await client.PostAsJsonAsync("/auth/login", new
        {
            email,
            password = "Wrong-password-123!"
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, failedAttempt.StatusCode);

        await InstallRejectingAuthenticationAuditTriggerAsync();
        try
        {
            var response = await client.PostAsJsonAsync("/auth/login", new
            {
                email,
                password
            }, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await RemoveRejectingAuthenticationAuditTriggerAsync();
        }

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var user = await dbContext.Usuarios.AsNoTracking().SingleAsync(item => item.Email == email, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, user.TentativasFalhas);
        Assert.Equal(0, await dbContext.Auditorias.CountAsync(item =>
            item.Entidade == nameof(Usuario) &&
            item.RegistroId == user.Id &&
            item.TipoAcao == TipoAcaoAuditoria.Login, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, await dbContext.SessoesAutenticacao.CountAsync(item =>
            item.UsuarioId == user.Id, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InactiveUserCannotAuthenticate()
    {
        const string password = "Test-only-password-123!";
        var email = await CreateUserAsync(ProfileNames.Administrator, password, active: false);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new { email, password }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TransportationProfileCannotOperateVehicleAccess()
    {
        const string password = "Test-only-password-123!";
        var email = await CreateUserAsync(ProfileNames.TransportationDepartment, password);
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/auth/login", new { email, password }, cancellationToken: TestContext.Current.CancellationToken);
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.AccessToken);

        var response = await client.GetAsync("/access-records/open", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanCreateIndividualAccount()
    {
        const string adminPassword = "Test-only-admin-password-123!";
        var adminEmail = await CreateUserAsync(ProfileNames.Administrator, adminPassword);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, adminEmail, adminPassword);
        var suffix = Guid.NewGuid().ToString("N");
        var newUserEmail = $"created-{suffix}@example.test";

        var response = await client.PostAsJsonAsync("/users", new
        {
            name = $"Pessoa Criada {suffix}",
            email = newUserEmail,
            profileName = ProfileNames.Doorman
        }, cancellationToken: TestContext.Current.CancellationToken);
        var created = await response.Content.ReadFromJsonAsync<CreateUserResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);
        Assert.Equal(newUserEmail, created.Email);
        Assert.Equal(ProfileNames.Doorman, created.ProfileName);
        Assert.False(string.IsNullOrWhiteSpace(created.TemporaryCredential));

        using var loginClient = factory.CreateClient();
        var login = await loginClient.PostAsJsonAsync("/auth/login", new
        {
            email = newUserEmail,
            password = created.TemporaryCredential
        }, cancellationToken: TestContext.Current.CancellationToken);
        login.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var audit = await dbContext.Auditorias.AsNoTracking().SingleAsync(item =>
            item.Entidade == nameof(Usuario) &&
            item.RegistroId == created.Id &&
            item.TipoAcao == TipoAcaoAuditoria.Inclusao, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(await GetUserIdAsync(adminEmail), audit.UsuarioId);
        var auditContent = string.Join(' ', audit.DadosAnteriores, audit.DadosNovos, audit.Detalhes);
        Assert.DoesNotContain(newUserEmail, auditContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            created.TemporaryCredential,
            auditContent,
            StringComparison.Ordinal);
        Assert.DoesNotContain("hash", auditContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccountCreationAuditFailureRollsBackPersonAndUser()
    {
        const string adminPassword = "Test-only-admin-password-123!";
        var adminEmail = await CreateUserAsync(ProfileNames.Administrator, adminPassword);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, adminEmail, adminPassword);
        var suffix = Guid.NewGuid().ToString("N");
        var newUserEmail = $"rollback-{suffix}@example.test";

        await InstallRejectingAccountCreationAuditTriggerAsync();
        try
        {
            var response = await client.PostAsJsonAsync("/users", new
            {
                name = $"Pessoa Rollback {suffix}",
                email = newUserEmail,
                profileName = ProfileNames.Doorman
            }, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await RemoveRejectingAccountCreationAuditTriggerAsync();
        }

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        Assert.False(await dbContext.Usuarios.AnyAsync(item => item.Email == newUserEmail, cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(await dbContext.Pessoas.AnyAsync(item => item.Email == newUserEmail, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NonAdministratorCannotCreateAccount()
    {
        const string password = "Test-only-user-password-123!";
        var email = await CreateUserAsync(ProfileNames.Doorman, password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);

        var response = await client.PostAsJsonAsync("/users", new
        {
            name = "Conta Não Autorizada",
            email = $"forbidden-{Guid.NewGuid():N}@example.test",
            password,
            profileName = ProfileNames.Doorman
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task AuthenticateClientAsync(
        HttpClient client,
        string email,
        string password)
    {
        var login = await client.PostAsJsonAsync("/auth/login", new { email, password });
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        login.EnsureSuccessStatusCode();
        Assert.NotNull(body);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.AccessToken);
    }

    private async Task<string> CreateUserAsync(
        string profileName,
        string password,
        bool active = true,
        bool outdatedPasswordHash = false)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHashService>();
        var suffix = Guid.NewGuid().ToString("N");
        var profile = await dbContext.Perfis.SingleOrDefaultAsync(item => item.Nome == profileName);

        if (profile is null)
        {
            profile = new Perfil(profileName, "Perfil criado exclusivamente para teste de integração.");
            dbContext.Perfis.Add(profile);
        }

        var person = new Pessoa($"Pessoa de Teste {suffix}");
        dbContext.Pessoas.Add(person);
        await dbContext.SaveChangesAsync();

        var email = $"auth-{suffix}@example.test";
        var passwordHash = outdatedPasswordHash
            ? CreateOutdatedPasswordHash(password)
            : passwordHasher.Hash(password);
        var user = new Usuario(email, passwordHash, person.Id, profile.Id);

        if (!active)
        {
            user.Desativar(DateTime.UtcNow);
        }

        dbContext.Usuarios.Add(user);
        await dbContext.SaveChangesAsync();
        return email;
    }

    private static string CreateOutdatedPasswordHash(string password)
    {
        var passwordHasher = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions
        {
            IterationCount = 10_000
        }));

        return passwordHasher.HashPassword(new object(), password);
    }

    private async Task<string> GetPasswordHashAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        return await dbContext.Usuarios
            .AsNoTracking()
            .Where(item => item.Email == email)
            .Select(item => item.SenhaHash)
            .SingleAsync();
    }

    private async Task<Auditoria> GetSingleAuthenticationAuditAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var userId = await dbContext.Usuarios
            .Where(item => item.Email == email)
            .Select(item => item.Id)
            .SingleAsync();

        return await dbContext.Auditorias.AsNoTracking().SingleAsync(item =>
            item.Entidade == nameof(Usuario) &&
            item.RegistroId == userId &&
            item.TipoAcao == TipoAcaoAuditoria.Login);
    }

    private async Task<int> CountAuthenticationAuditsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        return await dbContext.Auditorias.CountAsync(item =>
            item.Entidade == nameof(Usuario) &&
            item.TipoAcao == TipoAcaoAuditoria.Login);
    }

    private async Task<int> GetUserIdAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        return await dbContext.Usuarios
            .Where(item => item.Email == email)
            .Select(item => item.Id)
            .SingleAsync();
    }

    private async Task InstallRejectingAccountCreationAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION dbo.reject_account_creation_audit()
            RETURNS trigger AS $$
            BEGIN
                IF NEW.entidade = 'Usuario' AND NEW.tipo_acao = 'Inclusao' THEN
                    RAISE EXCEPTION 'account creation audit rejected for integration test';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER reject_account_creation_audit_trigger
            BEFORE INSERT ON dbo.auditorias
            FOR EACH ROW EXECUTE FUNCTION dbo.reject_account_creation_audit();
            """);
    }

    private async Task RemoveRejectingAccountCreationAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER IF EXISTS reject_account_creation_audit_trigger ON dbo.auditorias;
            DROP FUNCTION IF EXISTS dbo.reject_account_creation_audit();
            """);
    }

    private async Task InstallRejectingAuthenticationAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION dbo.reject_authentication_audit()
            RETURNS trigger AS $$
            BEGIN
                IF NEW.entidade = 'Usuario' AND NEW.tipo_acao = 'Login' THEN
                    RAISE EXCEPTION 'authentication audit rejected for integration test';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER reject_authentication_audit_trigger
            BEFORE INSERT ON dbo.auditorias
            FOR EACH ROW EXECUTE FUNCTION dbo.reject_authentication_audit();
            """);
    }

    private async Task RemoveRejectingAuthenticationAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER IF EXISTS reject_authentication_audit_trigger ON dbo.auditorias;
            DROP FUNCTION IF EXISTS dbo.reject_authentication_audit();
            """);
    }

    private sealed record LoginResponse(
        string AccessToken,
        DateTime ExpiresAtUtc,
        LoginUserResponse User);
    private sealed record LoginUserResponse(
        int Id,
        string Email,
        string ProfileName,
        bool RequiresPasswordChange);
    private sealed record CreateUserResponse(
        int Id,
        string Email,
        string ProfileName,
        string? TemporaryCredential);
    private sealed record ErrorResponse(string Message);
}
