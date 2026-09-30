using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ControleAcessoVeiculos.Application.Accounts;
using ControleAcessoVeiculos.Application.Authentication;
using ControleAcessoVeiculos.Application.Authorization;
using ControleAcessoVeiculos.Domain.Entities;
using ControleAcessoVeiculos.Domain.Enums;
using ControleAcessoVeiculos.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControleAcessoVeiculos.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class TemporaryCredentialTests(ApiFactory factory)
{
    private const string PermanentPassword = "Permanent-test-password-123!";

    [Fact]
    public async Task GeneratedCredentialRestrictsOperationsUntilPasswordChange()
    {
        var administrator = await CreateUserAsync(ProfileNames.Administrator);
        using var administratorClient = factory.CreateClient();
        await AuthenticateClientAsync(
            administratorClient,
            administrator.Email,
            PermanentPassword);
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"temporary-{suffix}@example.test";

        var creation = await administratorClient.PostAsJsonAsync("/users", new
        {
            name = $"Temporary Operator {suffix}",
            email,
            profileName = ProfileNames.Doorman
        }, cancellationToken: TestContext.Current.CancellationToken);
        var created = await creation.Content.ReadFromJsonAsync<CreateUserResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        Assert.Equal("no-store", creation.Headers.CacheControl?.ToString());
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created.TemporaryCredential));
        Assert.True(created.TemporaryCredentialExpiresAtUtc > DateTime.UtcNow);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<ControleAcessoVeiculosDbContext>();
            var passwordHasher = scope.ServiceProvider
                .GetRequiredService<IPasswordHashService>();
            var user = await dbContext.Usuarios.AsNoTracking()
                .SingleAsync(item => item.Email == email, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(user.TrocaSenhaObrigatoria);
            Assert.Equal(
                PasswordHashVerificationResult.Success,
                passwordHasher.Verify(user.SenhaHash, created.TemporaryCredential!));
            Assert.DoesNotContain(
                created.TemporaryCredential!,
                user.SenhaHash,
                StringComparison.Ordinal);
        }

        using var temporaryClient = factory.CreateClient();
        var temporaryLogin = await LoginAsync(
            temporaryClient,
            email,
            created.TemporaryCredential!);
        Assert.True(temporaryLogin.User.RequiresPasswordChange);

        using var reusedCredentialClient = factory.CreateClient();
        var reusedCredentialLogin = await reusedCredentialClient.PostAsJsonAsync("/auth/login", new { email, password = created.TemporaryCredential }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reusedCredentialLogin.StatusCode);

        temporaryClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", temporaryLogin.AccessToken);
        var restrictedOperation = await temporaryClient.GetAsync("/access-records/open", TestContext.Current.CancellationToken);
        var restrictedBody = await restrictedOperation.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, restrictedOperation.StatusCode);
        Assert.Contains("Troca de senha obrigatória", restrictedBody);

        var passwordChange = await temporaryClient.PostAsJsonAsync("/auth/password", new
        {
            currentPassword = created.TemporaryCredential,
            newPassword = PermanentPassword
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, passwordChange.StatusCode);

        var staleTokenOperation = await temporaryClient.GetAsync("/access-records/open", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, staleTokenOperation.StatusCode);

        using var loginClient = factory.CreateClient();
        var oldCredentialLogin = await loginClient.PostAsJsonAsync("/auth/login", new
        {
            email,
            password = created.TemporaryCredential
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, oldCredentialLogin.StatusCode);

        var permanentLogin = await LoginAsync(loginClient, email, PermanentPassword);
        Assert.False(permanentLogin.User.RequiresPasswordChange);

        var auditText = await GetAuditTextAsync(created.Id);
        Assert.DoesNotContain(created.TemporaryCredential!, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(PermanentPassword, auditText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentTemporaryCredentialLoginsCreateOnlyOneSession()
    {
        var user = await CreateUserAsync(
            ProfileNames.Doorman,
            requiresPasswordChange: true,
            temporaryCredentialExpiresAtUtc: DateTime.UtcNow.AddMinutes(30));
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();

        var attempts = await Task.WhenAll(
            firstClient.PostAsJsonAsync("/auth/login", new
            {
                email = user.Email,
                password = PermanentPassword
            }, TestContext.Current.CancellationToken),
            secondClient.PostAsJsonAsync("/auth/login", new
            {
                email = user.Email,
                password = PermanentPassword
            }, TestContext.Current.CancellationToken));

        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(
            attempts,
            response => response.StatusCode == HttpStatusCode.Unauthorized);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        var persistedUser = await dbContext.Usuarios.AsNoTracking()
            .SingleAsync(item => item.Id == user.Id, cancellationToken: TestContext.Current.CancellationToken);
        var sessionCount = await dbContext.SessoesAutenticacao.AsNoTracking()
            .CountAsync(item => item.UsuarioId == user.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(persistedUser.CredencialTemporariaUtilizadaEm);
        Assert.Equal(1, sessionCount);
    }

    [Fact]
    public async Task AdministrativeResetRevokesSessionsAndOnlyLatestCredentialRemainsValid()
    {
        var administrator = await CreateUserAsync(ProfileNames.Administrator);
        var target = await CreateUserAsync(ProfileNames.Doorman);
        using var administratorClient = factory.CreateClient();
        using var targetClient = factory.CreateClient();
        await AuthenticateClientAsync(
            administratorClient,
            administrator.Email,
            PermanentPassword);
        var targetLogin = await LoginAsync(
            targetClient,
            target.Email,
            PermanentPassword);
        targetClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", targetLogin.AccessToken);

        var resetTasks = Enumerable.Range(0, 2)
            .Select(_ => administratorClient.PostAsJsonAsync(
                $"/users/{target.Id}/temporary-credential",
                new { reason = CredentialResetReasons.Forgotten }))
            .ToArray();
        var resetResponses = await Task.WhenAll(resetTasks);
        Assert.All(resetResponses, response => response.EnsureSuccessStatusCode());
        var credentials = new List<string>();
        foreach (var response in resetResponses)
        {
            var body = await response.Content.ReadFromJsonAsync<TemporaryCredentialResponse>(cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(body);
            credentials.Add(body.TemporaryCredential);
        }

        var staleOperation = await targetClient.GetAsync("/access-records/open", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, staleOperation.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHashService>();
        var user = await dbContext.Usuarios.AsNoTracking()
            .SingleAsync(item => item.Id == target.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(user.TrocaSenhaObrigatoria);
        Assert.Equal(3, user.VersaoCredencial);
        Assert.Equal(1, credentials.Count(credential =>
            passwordHasher.Verify(user.SenhaHash, credential) !=
            PasswordHashVerificationResult.Failed));

        var session = await dbContext.SessoesAutenticacao.AsNoTracking()
            .SingleAsync(item => item.UsuarioId == target.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            MotivoRevogacaoSessao.RedefinicaoAdministrativa,
            session.MotivoRevogacao);

        var audits = await dbContext.Auditorias.AsNoTracking()
            .Where(item =>
                item.Entidade == nameof(Usuario) &&
                item.RegistroId == target.Id &&
                item.TipoAcao == TipoAcaoAuditoria.Alteracao)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, audits.Count);
        Assert.All(audits, audit => Assert.Equal(administrator.Id, audit.UsuarioId));
        var auditText = string.Join(' ', audits.SelectMany(audit => new[]
        {
            audit.DadosAnteriores,
            audit.DadosNovos,
            audit.Detalhes
        }));
        Assert.All(credentials, credential =>
            Assert.DoesNotContain(credential, auditText, StringComparison.Ordinal));
    }

    [Fact]
    public async Task NonAdministratorCannotResetCredentialAndAdministratorCannotResetOwn()
    {
        var administrator = await CreateUserAsync(ProfileNames.Administrator);
        var doorman = await CreateUserAsync(ProfileNames.Doorman);
        using var doormanClient = factory.CreateClient();
        await AuthenticateClientAsync(doormanClient, doorman.Email, PermanentPassword);

        var forbidden = await doormanClient.PostAsJsonAsync($"/users/{administrator.Id}/temporary-credential", new { reason = CredentialResetReasons.Forgotten }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var administratorClient = factory.CreateClient();
        await AuthenticateClientAsync(
            administratorClient,
            administrator.Email,
            PermanentPassword);
        var selfReset = await administratorClient.PostAsJsonAsync($"/users/{administrator.Id}/temporary-credential", new { reason = CredentialResetReasons.Forgotten }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, selfReset.StatusCode);
    }

    [Fact]
    public async Task AdministratorCannotChooseAnotherUsersInitialPassword()
    {
        var administrator = await CreateUserAsync(ProfileNames.Administrator);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(
            client,
            administrator.Email,
            PermanentPassword);
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"administrator-password-{suffix}@example.test";

        var response = await client.PostAsJsonAsync("/users", new
        {
            name = $"Rejected Password {suffix}",
            email,
            password = PermanentPassword,
            profileName = ProfileNames.Doorman
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        Assert.False(await dbContext.Usuarios.AnyAsync(item => item.Email == email, cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(await dbContext.Pessoas.AnyAsync(item => item.Email == email, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExpiredTemporaryCredentialReturnsGenericUnauthorized()
    {
        var user = await CreateUserAsync(
            ProfileNames.Doorman,
            requiresPasswordChange: true,
            temporaryCredentialExpiresAtUtc: DateTime.UtcNow.AddMinutes(-1));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            email = user.Email,
            password = PermanentPassword
        }, cancellationToken: TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Credenciais inválidas", body);
        Assert.DoesNotContain("expir", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuditFailureRollsBackAdministrativeResetAndSessionRevocation()
    {
        var administrator = await CreateUserAsync(ProfileNames.Administrator);
        var target = await CreateUserAsync(ProfileNames.Doorman);
        using var administratorClient = factory.CreateClient();
        using var targetClient = factory.CreateClient();
        await AuthenticateClientAsync(
            administratorClient,
            administrator.Email,
            PermanentPassword);
        var targetLogin = await LoginAsync(
            targetClient,
            target.Email,
            PermanentPassword);

        await InstallRejectingResetAuditTriggerAsync();
        try
        {
            var response = await administratorClient.PostAsJsonAsync($"/users/{target.Id}/temporary-credential", new { reason = CredentialResetReasons.Forgotten }, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await RemoveRejectingResetAuditTriggerAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<ControleAcessoVeiculosDbContext>();
            var user = await dbContext.Usuarios.AsNoTracking()
                .SingleAsync(item => item.Id == target.Id, cancellationToken: TestContext.Current.CancellationToken);
            var session = await dbContext.SessoesAutenticacao.AsNoTracking()
                .SingleAsync(item => item.UsuarioId == target.Id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.False(user.TrocaSenhaObrigatoria);
            Assert.Equal(1, user.VersaoCredencial);
            Assert.Null(session.RevogadaEm);
        }

        targetClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", targetLogin.AccessToken);
        var existingSessionOperation = await targetClient.GetAsync("/access-records/open", TestContext.Current.CancellationToken);
        existingSessionOperation.EnsureSuccessStatusCode();
    }

    private async Task<TestUser> CreateUserAsync(
        string profileName,
        bool requiresPasswordChange = false,
        DateTime? temporaryCredentialExpiresAtUtc = null)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHashService>();
        var profile = await dbContext.Perfis.SingleOrDefaultAsync(item => item.Nome == profileName);
        if (profile is null)
        {
            profile = new Perfil(profileName, "Temporary credential integration profile.");
            dbContext.Perfis.Add(profile);
            await dbContext.SaveChangesAsync();
        }

        var suffix = Guid.NewGuid().ToString("N");
        var email = $"temporary-test-{suffix}@example.test";
        var person = new Pessoa($"Temporary Test {suffix}", email: email);
        dbContext.Pessoas.Add(person);
        await dbContext.SaveChangesAsync();
        var user = new Usuario(
            email,
            passwordHasher.Hash(PermanentPassword),
            person.Id,
            profile.Id,
            requiresPasswordChange,
            temporaryCredentialExpiresAtUtc);
        dbContext.Usuarios.Add(user);
        await dbContext.SaveChangesAsync();
        return new TestUser(user.Id, email);
    }

    private static async Task<LoginResponse> LoginAsync(
        HttpClient client,
        string email,
        string password)
    {
        var response = await client.PostAsJsonAsync("/auth/login", new { email, password });
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        response.EnsureSuccessStatusCode();
        return body!;
    }

    private static async Task AuthenticateClientAsync(
        HttpClient client,
        string email,
        string password)
    {
        var login = await LoginAsync(client, email, password);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    private async Task<string> GetAuditTextAsync(int userId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var audits = await dbContext.Auditorias.AsNoTracking()
            .Where(item => item.Entidade == nameof(Usuario) && item.RegistroId == userId)
            .ToListAsync();
        return string.Join(' ', audits.SelectMany(audit => new[]
        {
            audit.DadosAnteriores,
            audit.DadosNovos,
            audit.Detalhes
        }));
    }

    private async Task InstallRejectingResetAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION dbo.reject_temporary_credential_audit()
            RETURNS trigger AS $$
            BEGIN
                IF NEW.entidade = 'Usuario'
                   AND NEW.tipo_acao = 'Alteracao'
                   AND NEW.detalhes = 'Administrator issued a temporary credential.' THEN
                    RAISE EXCEPTION 'temporary credential audit rejected for integration test';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER reject_temporary_credential_audit_trigger
            BEFORE INSERT ON dbo.auditorias
            FOR EACH ROW EXECUTE FUNCTION dbo.reject_temporary_credential_audit();
            """);
    }

    private async Task RemoveRejectingResetAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER IF EXISTS reject_temporary_credential_audit_trigger
                ON dbo.auditorias;
            DROP FUNCTION IF EXISTS dbo.reject_temporary_credential_audit();
            """);
    }

    private sealed record TestUser(int Id, string Email);
    private sealed record LoginResponse(
        string AccessToken,
        LoginUserResponse User);
    private sealed record LoginUserResponse(bool RequiresPasswordChange);
    private sealed record CreateUserResponse(
        int Id,
        string Email,
        string ProfileName,
        string? TemporaryCredential,
        DateTime? TemporaryCredentialExpiresAtUtc);
    private sealed record TemporaryCredentialResponse(
        string TemporaryCredential,
        DateTime TemporaryCredentialExpiresAtUtc);
}
