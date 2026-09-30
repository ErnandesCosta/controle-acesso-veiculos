using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ControleAcessoVeiculos.Application.Authentication;
using ControleAcessoVeiculos.Application.Authorization;
using ControleAcessoVeiculos.Domain.Entities;
using ControleAcessoVeiculos.Domain.Enums;
using ControleAcessoVeiculos.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControleAcessoVeiculos.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class UserAccountLifecycleTests(ApiFactory factory)
{
    private const string Password = "Test-only-password-123!";

    [Fact]
    public async Task AdministratorCanSearchPaginatedAccountsWithoutPasswordHashes()
    {
        var administrator = await CreateUserAsync(ProfileNames.Administrator);
        var target = await CreateUserAsync(ProfileNames.Doorman);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, administrator.Email);

        var response = await client.GetAsync($"/users?search={Uri.EscapeDataString(target.Email.ToUpperInvariant())}&active=true&page=1&pageSize=10", TestContext.Current.CancellationToken);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<PagedUsersResponse>(json, JsonOptions);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(result);
        var account = Assert.Single(result.Items);
        Assert.Equal(target.Id, account.Id);
        Assert.Equal(target.Email, account.Email);
        Assert.Equal(ProfileNames.Doorman, account.ProfileName);
        Assert.True(account.Active);
        Assert.Equal(1, result.TotalCount);
        Assert.DoesNotContain("senhaHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonAdministratorCannotSearchAccounts()
    {
        var user = await CreateUserAsync(ProfileNames.Doorman);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, user.Email);

        var response = await client.GetAsync("/users", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeactivationRevokesExistingTokenAndReactivationRestoresLogin()
    {
        var administrator = await CreateUserAsync(ProfileNames.Administrator);
        var target = await CreateUserAsync(ProfileNames.Doorman);
        using var administratorClient = factory.CreateClient();
        using var targetClient = factory.CreateClient();
        await AuthenticateClientAsync(administratorClient, administrator.Email);
        await AuthenticateClientAsync(targetClient, target.Email);

        var deactivation = await administratorClient.DeleteAsync($"/users/{target.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deactivation.StatusCode);

        var requestWithExistingToken = await targetClient.GetAsync("/access-records/open", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, requestWithExistingToken.StatusCode);

        using (var deactivationScope = factory.Services.CreateScope())
        {
            var deactivationContext = deactivationScope.ServiceProvider
                .GetRequiredService<ControleAcessoVeiculosDbContext>();
            var session = await deactivationContext.SessoesAutenticacao
                .AsNoTracking()
                .SingleAsync(item => item.UsuarioId == target.Id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(session.RevogadaEm);
            Assert.Equal(
                MotivoRevogacaoSessao.ContaDesativada,
                session.MotivoRevogacao);
        }

        using var loginClient = factory.CreateClient();
        var inactiveLogin = await loginClient.PostAsJsonAsync("/auth/login", new
        {
            email = target.Email,
            password = Password
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, inactiveLogin.StatusCode);

        var reactivation = await administratorClient.PostAsync($"/users/{target.Id}/reactivation", content: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, reactivation.StatusCode);

        var restoredLogin = await loginClient.PostAsJsonAsync("/auth/login", new
        {
            email = target.Email,
            password = Password
        }, cancellationToken: TestContext.Current.CancellationToken);
        restoredLogin.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var audits = await dbContext.Auditorias
            .AsNoTracking()
            .Where(item =>
                item.Entidade == nameof(Usuario) &&
                item.RegistroId == target.Id &&
                item.TipoAcao == TipoAcaoAuditoria.Alteracao)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, audits.Count);
        Assert.All(audits, audit => Assert.Equal(administrator.Id, audit.UsuarioId));
        using (var deactivationState = JsonDocument.Parse(audits[0].DadosNovos!))
        {
            Assert.False(deactivationState.RootElement.GetProperty("active").GetBoolean());
        }

        using (var reactivationState = JsonDocument.Parse(audits[1].DadosNovos!))
        {
            Assert.True(reactivationState.RootElement.GetProperty("active").GetBoolean());
        }

        Assert.DoesNotContain(target.Email, string.Join(' ', audits.SelectMany(audit =>
            new[] { audit.DadosAnteriores, audit.DadosNovos, audit.Detalhes })));
    }

    [Fact]
    public async Task AdministratorCannotDeactivateOwnAccount()
    {
        var administrator = await CreateUserAsync(ProfileNames.Administrator);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, administrator.Email);

        var response = await client.DeleteAsync($"/users/{administrator.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(await IsUserActiveAsync(administrator.Id));
    }

    [Fact]
    public async Task ConcurrentDeactivationKeepsOneAdministratorActive()
    {
        var first = await CreateUserAsync(ProfileNames.Administrator);
        var second = await CreateUserAsync(ProfileNames.Administrator);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        await AuthenticateClientAsync(firstClient, first.Email);
        await AuthenticateClientAsync(secondClient, second.Email);

        var temporarilyInactiveIds = await DeactivateOtherAdministratorsAsync(first.Id, second.Id);
        try
        {
            var responses = await Task.WhenAll(
                firstClient.DeleteAsync(
                    $"/users/{second.Id}",
                    TestContext.Current.CancellationToken),
                secondClient.DeleteAsync(
                    $"/users/{first.Id}",
                    TestContext.Current.CancellationToken));

            Assert.Equal(1, responses.Count(response =>
                response.StatusCode == HttpStatusCode.NoContent));
            Assert.Equal(1, responses.Count(response =>
                response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Unauthorized));
            Assert.Equal(1, await CountActiveAdministratorsAsync(first.Id, second.Id));
        }
        finally
        {
            await ReactivateUsersDirectlyAsync(
                temporarilyInactiveIds.Append(first.Id).Append(second.Id));
        }
    }

    [Fact]
    public async Task AuditFailureRollsBackAccountDeactivation()
    {
        var administrator = await CreateUserAsync(ProfileNames.Administrator);
        var target = await CreateUserAsync(ProfileNames.Doorman);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, administrator.Email);

        await InstallRejectingAccountAuditTriggerAsync();
        try
        {
            var response = await client.DeleteAsync($"/users/{target.Id}", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await RemoveRejectingAccountAuditTriggerAsync();
        }

        Assert.True(await IsUserActiveAsync(target.Id));
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        Assert.Equal(0, await dbContext.Auditorias.CountAsync(item =>
            item.Entidade == nameof(Usuario) &&
            item.RegistroId == target.Id &&
            item.TipoAcao == TipoAcaoAuditoria.Alteracao, cancellationToken: TestContext.Current.CancellationToken));
    }

    private async Task<TestUser> CreateUserAsync(string profileName)
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

        var email = $"account-{suffix}@example.test";
        var user = new Usuario(email, passwordHasher.Hash(Password), person.Id, profile.Id);
        dbContext.Usuarios.Add(user);
        await dbContext.SaveChangesAsync();
        return new TestUser(user.Id, email);
    }

    private static async Task AuthenticateClientAsync(HttpClient client, string email)
    {
        var login = await client.PostAsJsonAsync("/auth/login", new
        {
            email,
            password = Password
        });
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        login.EnsureSuccessStatusCode();
        Assert.NotNull(body);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.AccessToken);
    }

    private async Task<bool> IsUserActiveAsync(int userId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        return await dbContext.Usuarios
            .Where(user => user.Id == userId)
            .Select(user => user.Ativo)
            .SingleAsync();
    }

    private async Task<IReadOnlyList<int>> DeactivateOtherAdministratorsAsync(
        int firstId,
        int secondId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var ids = await dbContext.Usuarios
            .Where(user => user.Ativo && user.Id != firstId && user.Id != secondId)
            .Join(
                dbContext.Perfis.Where(profile => profile.Nome == ProfileNames.Administrator),
                user => user.PerfilId,
                profile => profile.Id,
                (user, _) => user.Id)
            .ToListAsync();

        await dbContext.Usuarios
            .Where(user => ids.Contains(user.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Ativo, false));
        return ids;
    }

    private async Task<int> CountActiveAdministratorsAsync(int firstId, int secondId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        return await dbContext.Usuarios.CountAsync(user =>
            user.Ativo && (user.Id == firstId || user.Id == secondId));
    }

    private async Task ReactivateUsersDirectlyAsync(IEnumerable<int> userIds)
    {
        var ids = userIds.Distinct().ToArray();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Usuarios
            .Where(user => ids.Contains(user.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Ativo, true));
    }

    private async Task InstallRejectingAccountAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION dbo.reject_account_state_audit()
            RETURNS trigger AS $$
            BEGIN
                IF NEW.entidade = 'Usuario' AND NEW.tipo_acao = 'Alteracao' THEN
                    RAISE EXCEPTION 'account state audit rejected for integration test';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER reject_account_state_audit_trigger
            BEFORE INSERT ON dbo.auditorias
            FOR EACH ROW EXECUTE FUNCTION dbo.reject_account_state_audit();
            """);
    }

    private async Task RemoveRejectingAccountAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER IF EXISTS reject_account_state_audit_trigger ON dbo.auditorias;
            DROP FUNCTION IF EXISTS dbo.reject_account_state_audit();
            """);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record TestUser(int Id, string Email);
    private sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc);
    private sealed record UserResponse(
        int Id,
        string Email,
        string ProfileName,
        bool Active);
    private sealed record PagedUsersResponse(
        IReadOnlyList<UserResponse> Items,
        int TotalCount);
}
