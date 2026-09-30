using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ControleAcessoVeiculos.Application.Authentication;
using ControleAcessoVeiculos.Application.Authorization;
using ControleAcessoVeiculos.Domain.Entities;
using ControleAcessoVeiculos.Domain.Enums;
using ControleAcessoVeiculos.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControleAcessoVeiculos.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class AuthenticatedPasswordChangeTests(ApiFactory factory)
{
    [Fact]
    public async Task PasswordChangeRevokesAllPreviousAuthenticationMaterial()
    {
        const string currentPassword = "Current-test-password-123!";
        const string newPassword = "New-test-password-456!";
        var email = await CreateUserAsync(currentPassword);
        var userId = await GetUserIdAsync(email);
        using var client = factory.CreateClient();

        var login = await LoginAsync(client, email, currentPassword);
        var oldAccessToken = login.AccessToken;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", oldAccessToken);

        var change = await client.PostAsJsonAsync("/auth/password", new
        {
            currentPassword,
            newPassword
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Contains(
            change.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("cav_refresh=", StringComparison.Ordinal) &&
                value.Contains("expires=", StringComparison.OrdinalIgnoreCase));

        var oldTokenResponse = await client.GetAsync("/access-records/open", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, oldTokenResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var oldLogin = await client.PostAsJsonAsync("/auth/login", new
        {
            email,
            password = currentPassword
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        var newLogin = await client.PostAsJsonAsync("/auth/login", new
        {
            email,
            password = newPassword
        }, cancellationToken: TestContext.Current.CancellationToken);
        newLogin.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        var user = await dbContext.Usuarios.AsNoTracking()
            .SingleAsync(item => item.Id == userId, cancellationToken: TestContext.Current.CancellationToken);
        var previousSessions = await dbContext.SessoesAutenticacao.AsNoTracking()
            .Where(item => item.UsuarioId == userId && item.MotivoRevogacao != null)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        var audit = await dbContext.Auditorias.AsNoTracking().SingleAsync(item =>
            item.RegistroId == userId &&
            item.TipoAcao == TipoAcaoAuditoria.Alteracao &&
            item.Detalhes == "User changed their authentication password.", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, user.VersaoCredencial);
        Assert.Contains(previousSessions, session =>
            session.MotivoRevogacao == MotivoRevogacaoSessao.SenhaAlterada);
        var auditContent = string.Join(' ',
            audit.DadosAnteriores,
            audit.DadosNovos,
            audit.Detalhes);
        Assert.DoesNotContain(email, auditContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(currentPassword, auditContent, StringComparison.Ordinal);
        Assert.DoesNotContain(newPassword, auditContent, StringComparison.Ordinal);
        Assert.DoesNotContain("HASH::", auditContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongCurrentPasswordPreservesCredentialAndSession()
    {
        const string currentPassword = "Current-test-password-123!";
        var email = await CreateUserAsync(currentPassword);
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, email, currentPassword);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.PostAsJsonAsync("/auth/password", new
        {
            currentPassword = "Wrong-test-password-123!",
            newPassword = "New-test-password-456!"
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var protectedResponse = await client.GetAsync("/access-records/open", TestContext.Current.CancellationToken);
        protectedResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ConcurrentChangesAcceptCurrentPasswordOnlyOnce()
    {
        const string currentPassword = "Current-test-password-123!";
        var email = await CreateUserAsync(currentPassword);
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, email, currentPassword);

        var responses = await Task.WhenAll(
            SendChangeAsync(client, login.AccessToken, currentPassword, "First-new-password-456!"),
            SendChangeAsync(client, login.AccessToken, currentPassword, "Second-new-password-456!"));

        Assert.Equal(1, responses.Count(item => item.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(1, responses.Count(item =>
            item.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task AnonymousPasswordChangeIsRejected()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/password", new
        {
            currentPassword = "Current-test-password-123!",
            newPassword = "New-test-password-456!"
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuditFailureRollsBackPasswordVersionAndSessionRevocation()
    {
        const string currentPassword = "Current-test-password-123!";
        var email = await CreateUserAsync(currentPassword);
        var userId = await GetUserIdAsync(email);
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, email, currentPassword);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);

        await InstallRejectingPasswordChangeAuditTriggerAsync();
        try
        {
            var response = await client.PostAsJsonAsync("/auth/password", new
            {
                currentPassword,
                newPassword = "New-test-password-456!"
            }, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await RemoveRejectingPasswordChangeAuditTriggerAsync();
        }

        var protectedResponse = await client.GetAsync("/access-records/open", TestContext.Current.CancellationToken);
        protectedResponse.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        var user = await dbContext.Usuarios.AsNoTracking()
            .SingleAsync(item => item.Id == userId, cancellationToken: TestContext.Current.CancellationToken);
        var session = await dbContext.SessoesAutenticacao.AsNoTracking()
            .SingleAsync(item => item.UsuarioId == userId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, user.VersaoCredencial);
        Assert.Null(session.RevogadaEm);
        Assert.Null(session.MotivoRevogacao);
    }

    private async Task<string> CreateUserAsync(string password)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        var passwordHashService = scope.ServiceProvider
            .GetRequiredService<IPasswordHashService>();
        var profile = await dbContext.Perfis.SingleOrDefaultAsync(item =>
            item.Nome == ProfileNames.Doorman);
        if (profile is null)
        {
            profile = new Perfil(
                ProfileNames.Doorman,
                "Perfil criado exclusivamente para teste de integração.");
            dbContext.Perfis.Add(profile);
        }

        var suffix = Guid.NewGuid().ToString("N");
        var person = new Pessoa($"Pessoa Password Change {suffix}");
        dbContext.Pessoas.Add(person);
        await dbContext.SaveChangesAsync();

        var user = new Usuario(
            $"password-change-{suffix}@example.test",
            passwordHashService.Hash(password),
            person.Id,
            profile.Id);
        dbContext.Usuarios.Add(user);
        await dbContext.SaveChangesAsync();
        return user.Email;
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

    private async Task InstallRejectingPasswordChangeAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION dbo.reject_password_change_audit()
            RETURNS trigger AS $$
            BEGIN
                IF NEW.entidade = 'Usuario'
                   AND NEW.tipo_acao = 'Alteracao'
                   AND NEW.detalhes = 'User changed their authentication password.' THEN
                    RAISE EXCEPTION 'password change audit rejected for integration test';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER reject_password_change_audit_trigger
            BEFORE INSERT ON dbo.auditorias
            FOR EACH ROW EXECUTE FUNCTION dbo.reject_password_change_audit();
            """);
    }

    private async Task RemoveRejectingPasswordChangeAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER IF EXISTS reject_password_change_audit_trigger ON dbo.auditorias;
            DROP FUNCTION IF EXISTS dbo.reject_password_change_audit();
            """);
    }

    private static async Task<LoginResponse> LoginAsync(
        HttpClient client,
        string email,
        string password)
    {
        var response = await client.PostAsJsonAsync("/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static async Task<HttpResponseMessage> SendChangeAsync(
        HttpClient client,
        string accessToken,
        string currentPassword,
        string newPassword)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/password");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new { currentPassword, newPassword });
        return await client.SendAsync(request);
    }

    private sealed record LoginResponse(string AccessToken);
}
