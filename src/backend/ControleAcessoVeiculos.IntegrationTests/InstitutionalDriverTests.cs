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
public sealed class InstitutionalDriverTests(ApiFactory factory)
{
    [Fact]
    public async Task TransportationUserCanAuthorizeAndOperationalUserCanListDriver()
    {
        const string password = "Test-only-password-123!";
        var (managerId, managerEmail) = await CreateUserAsync(
            ProfileNames.TransportationDepartment,
            password);
        var (_, doormanEmail) = await CreateUserAsync(ProfileNames.Doorman, password);
        using var manager = factory.CreateClient();
        await AuthenticateClientAsync(manager, managerEmail, password);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $" Motorista {suffix} ";
        var documentNumber = $"DOC{suffix}";

        var response = await manager.PostAsJsonAsync("/institutional-drivers", new { name, documentType = " id ", documentNumber }, cancellationToken: TestContext.Current.CancellationToken);
        var created = await response.Content.ReadFromJsonAsync<DriverResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);
        Assert.Equal(name.Trim(), created.Name);
        Assert.Equal(managerId, created.AuthorizedById);

        using var doorman = factory.CreateClient();
        await AuthenticateClientAsync(doorman, doormanEmail, password);
        var drivers = await doorman.GetFromJsonAsync<List<DriverResponse>>("/institutional-drivers", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(drivers!, item => item.Id == created.Id);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var audits = await dbContext.Auditorias.AsNoTracking()
            .Where(item =>
                (item.Entidade == nameof(Pessoa) && item.RegistroId == created.PersonId) ||
                (item.Entidade == nameof(MotoristaInstitucional) &&
                    item.RegistroId == created.Id))
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, audits.Count);
        Assert.All(audits, audit => Assert.Equal(managerId, audit.UsuarioId));
        var auditContent = string.Join(
            ' ',
            audits.SelectMany(item => new[] { item.DadosNovos, item.Detalhes })
                .Where(item => item is not null));
        Assert.DoesNotContain(name.Trim(), auditContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(documentNumber, auditContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DriverEndpointsEnforceAuthenticationAuthorizationAndValidation()
    {
        const string password = "Test-only-password-123!";
        using var anonymous = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/institutional-drivers", TestContext.Current.CancellationToken)).StatusCode);

        var (_, doormanEmail) = await CreateUserAsync(ProfileNames.Doorman, password);
        using var doorman = factory.CreateClient();
        await AuthenticateClientAsync(doorman, doormanEmail, password);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await doorman.PostAsJsonAsync("/institutional-drivers", new { name = "Motorista" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);

        var (_, managerEmail) = await CreateUserAsync(
            ProfileNames.TransportationDepartment,
            password);
        using var manager = factory.CreateClient();
        await AuthenticateClientAsync(manager, managerEmail, password);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await manager.PostAsJsonAsync("/institutional-drivers", new { name = " ", documentType = "CPF" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task DuplicateAuthorizationConflictsAndDeactivationCanBeReactivated()
    {
        const string password = "Test-only-password-123!";
        var (managerId, email) = await CreateUserAsync(ProfileNames.Administrator, password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new
        {
            name = $"Motorista {suffix}",
            documentType = "ID",
            documentNumber = $"DUP{suffix}"
        };

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync(
                "/institutional-drivers",
                request,
                TestContext.Current.CancellationToken),
            client.PostAsJsonAsync(
                "/institutional-drivers",
                request,
                TestContext.Current.CancellationToken));
        Assert.Single(responses, item => item.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, item => item.StatusCode == HttpStatusCode.Conflict);
        var createdResponse = responses.Single(item => item.StatusCode == HttpStatusCode.Created);
        var created = await createdResponse.Content.ReadFromJsonAsync<DriverResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(created);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/institutional-drivers/{created.Id}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await client.DeleteAsync($"/institutional-drivers/{created.Id}", TestContext.Current.CancellationToken)).StatusCode);
        var active = await client.GetFromJsonAsync<List<DriverResponse>>("/institutional-drivers", cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(active!, item => item.Id == created.Id);

        var reactivation = await client.PostAsJsonAsync("/institutional-drivers", request, cancellationToken: TestContext.Current.CancellationToken);
        var reactivated = await reactivation.Content.ReadFromJsonAsync<DriverResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, reactivation.StatusCode);
        Assert.NotNull(reactivated);
        Assert.Equal(created.Id, reactivated.Id);
        Assert.Equal(managerId, reactivated.UpdatedById);
    }

    [Fact]
    public async Task AuditFailureRollsBackDriverAuthorization()
    {
        const string password = "Test-only-password-123!";
        var (_, email) = await CreateUserAsync(
            ProfileNames.TransportationDepartment,
            password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var documentNumber = $"RB{suffix}";
        await InstallRejectingAuditTriggerAsync();

        try
        {
            var response = await client.PostAsJsonAsync("/institutional-drivers", new
            {
                name = $"Motorista {suffix}",
                documentType = "ID",
                documentNumber
            }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await RemoveRejectingAuditTriggerAsync();
        }

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        Assert.False(await dbContext.Pessoas.AnyAsync(item =>
            item.DocumentoNumero == documentNumber, cancellationToken: TestContext.Current.CancellationToken));
    }

    private async Task<(int UserId, string Email)> CreateUserAsync(
        string profileName,
        string password)
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
        var email = $"driver-{suffix}@example.test";
        var user = new Usuario(email, passwordHasher.Hash(password), person.Id, profile.Id);
        dbContext.Usuarios.Add(user);
        await dbContext.SaveChangesAsync();
        return (user.Id, email);
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

    private async Task InstallRejectingAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE OR REPLACE FUNCTION dbo.reject_driver_authorization_audit()
            RETURNS trigger AS $function$
            BEGIN
                RAISE EXCEPTION 'forced integration test audit failure';
            END;
            $function$ LANGUAGE plpgsql;

            CREATE TRIGGER reject_driver_authorization_audit
            BEFORE INSERT ON dbo.auditorias
            FOR EACH ROW
            WHEN (NEW.entidade = 'MotoristaInstitucional')
            EXECUTE FUNCTION dbo.reject_driver_authorization_audit();
            """);
    }

    private async Task RemoveRejectingAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER IF EXISTS reject_driver_authorization_audit ON dbo.auditorias;
            DROP FUNCTION IF EXISTS dbo.reject_driver_authorization_audit();
            """);
    }

    private sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc);

    private sealed record DriverResponse(
        int Id,
        int PersonId,
        string Name,
        DateTime AuthorizedAtUtc,
        int AuthorizedById,
        DateTime? UpdatedAtUtc,
        int? UpdatedById);
}
