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
public sealed class EventAuthorizationTests(ApiFactory factory)
{
    [Fact]
    public async Task AdministratorCanManageAndDoormanCanReadEvent()
    {
        const string password = "Test-only-password-123!";
        var (managerId, managerEmail) = await CreateUserAsync(
            ProfileNames.Administrator,
            password);
        var (_, doormanEmail) = await CreateUserAsync(ProfileNames.Doorman, password);
        using var manager = factory.CreateClient();
        await AuthenticateClientAsync(manager, managerEmail, password);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"Jardim Digital {suffix}";
        var responsible = $"Coordenação {suffix}";
        var plate = $"EV{suffix[..5]}";
        var startsAtUtc = DateTimeOffset.UtcNow.AddDays(1);
        var endsAtUtc = startsAtUtc.AddDays(2);

        var createResponse = await manager.PostAsJsonAsync("/event-authorizations", Request(name, responsible, plate, startsAtUtc, endsAtUtc), cancellationToken: TestContext.Current.CancellationToken);
        var created = await createResponse.Content
            .ReadFromJsonAsync<EventAuthorizationResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);
        Assert.Equal(plate.ToUpperInvariant(), created.VehicleRules[0].Plate);
        Assert.Equal(3, created.VehicleRules[1].Quantity);
        Assert.Equal(managerId, created.CreatedById);

        using var doorman = factory.CreateClient();
        await AuthenticateClientAsync(doorman, doormanEmail, password);
        var page = await doorman.GetFromJsonAsync<EventAuthorizationPage>($"/event-authorizations?name={Uri.EscapeDataString(name)}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(page!.Items, item => item.Id == created.Id);

        var updatedName = $"Evento atualizado {suffix}";
        var updateResponse = await manager.PutAsJsonAsync($"/event-authorizations/{created.Id}", Request(updatedName, responsible, plate, startsAtUtc, endsAtUtc.AddDays(1)), cancellationToken: TestContext.Current.CancellationToken);
        var updated = await updateResponse.Content
            .ReadFromJsonAsync<EventAuthorizationResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(updatedName, updated!.Name);
        Assert.Equal(managerId, updated.UpdatedById);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await manager.DeleteAsync($"/event-authorizations/{created.Id}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await manager.DeleteAsync($"/event-authorizations/{created.Id}", TestContext.Current.CancellationToken)).StatusCode);
        var cancelled = await doorman.GetFromJsonAsync<EventAuthorizationPage>($"/event-authorizations?name={Uri.EscapeDataString(updatedName)}&active=false", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(cancelled!.Items, item => item.Id == created.Id && !item.Active);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var audits = await dbContext.Auditorias.AsNoTracking()
            .Where(audit => audit.Entidade == nameof(EventoAcesso) &&
                audit.RegistroId == created.Id)
            .OrderBy(audit => audit.Id)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(3, audits.Count);
        Assert.Equal(
            new[]
            {
                TipoAcaoAuditoria.Inclusao,
                TipoAcaoAuditoria.Alteracao,
                TipoAcaoAuditoria.Alteracao
            },
            audits.Select(audit => audit.TipoAcao));
        Assert.All(audits, audit => Assert.Equal(managerId, audit.UsuarioId));
        var auditContent = string.Join(
            ' ',
            audits.SelectMany(audit => new[]
            {
                audit.DadosAnteriores,
                audit.DadosNovos,
                audit.Detalhes
            }).Where(value => value is not null));
        Assert.DoesNotContain(name, auditContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(responsible, auditContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(plate, auditContent, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ProfileNames.Doorman)]
    [InlineData(ProfileNames.SecurityGuard)]
    [InlineData(ProfileNames.TransportationDepartment)]
    public async Task NonAdministratorProfilesCanReadButCannotManageEvents(string profileName)
    {
        const string password = "Test-only-password-123!";
        var (_, email) = await CreateUserAsync(profileName, password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/event-authorizations", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/event-authorizations", Request(
                    "Evento",
                    "Responsável",
                    "ABC1D23",
                    DateTimeOffset.UtcNow.AddDays(1),
                    DateTimeOffset.UtcNow.AddDays(2)), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync("/event-authorizations/1", Request(
                    "Evento",
                    "Responsável",
                    "ABC1D23",
                    DateTimeOffset.UtcNow.AddDays(1),
                    DateTimeOffset.UtcNow.AddDays(2)), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.DeleteAsync("/event-authorizations/1", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task AnonymousAccessIsRejectedAndAdministratorReachesValidation()
    {
        const string password = "Test-only-password-123!";
        using var anonymous = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/event-authorizations", TestContext.Current.CancellationToken)).StatusCode);

        var (_, administratorEmail) = await CreateUserAsync(
            ProfileNames.Administrator,
            password);
        using var administrator = factory.CreateClient();
        await AuthenticateClientAsync(administrator, administratorEmail, password);
        Assert.Equal(
            HttpStatusCode.OK,
            (await administrator.GetAsync("/event-authorizations", TestContext.Current.CancellationToken)).StatusCode);

        var administratorInvalid = await administrator.PostAsJsonAsync("/event-authorizations", new
        {
            name = " ",
            responsible = " ",
            startsAtUtc = DateTimeOffset.UtcNow.AddDays(2),
            endsAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            area = " ",
            overnightAllowed = false,
            vehicleRules = Array.Empty<object>()
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, administratorInvalid.StatusCode);
    }

    [Fact]
    public async Task AuditFailureRollsBackEventCreation()
    {
        const string password = "Test-only-password-123!";
        var (_, email) = await CreateUserAsync(
            ProfileNames.Administrator,
            password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"Rollback {suffix}";
        await InstallRejectingAuditTriggerAsync();

        try
        {
            var response = await client.PostAsJsonAsync("/event-authorizations", Request(
                    name,
                    "Responsável de teste",
                    $"RB{suffix[..5]}",
                    DateTimeOffset.UtcNow.AddDays(1),
                    DateTimeOffset.UtcNow.AddDays(2)), cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await RemoveRejectingAuditTriggerAsync();
        }

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        Assert.False(await dbContext.EventosAcesso.AnyAsync(entity => entity.Nome == name, cancellationToken: TestContext.Current.CancellationToken));
    }

    private static object Request(
        string name,
        string responsible,
        string plate,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc) =>
        new
        {
            name,
            responsible,
            startsAtUtc,
            endsAtUtc,
            area = "Pátio central",
            overnightAllowed = true,
            notes = "Observação operacional",
            vehicleRules = new object[]
            {
                new { vehicleType = "Automóvel", quantity = 1, plate },
                new { vehicleType = "Ônibus", quantity = 3, plate = (string?)null }
            }
        };

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
        var email = $"event-{suffix}@example.test";
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
            CREATE OR REPLACE FUNCTION dbo.reject_event_authorization_audit()
            RETURNS trigger AS $function$
            BEGIN
                RAISE EXCEPTION 'forced integration test audit failure';
            END;
            $function$ LANGUAGE plpgsql;

            CREATE TRIGGER reject_event_authorization_audit
            BEFORE INSERT ON dbo.auditorias
            FOR EACH ROW
            WHEN (NEW.entidade = 'EventoAcesso')
            EXECUTE FUNCTION dbo.reject_event_authorization_audit();
            """);
    }

    private async Task RemoveRejectingAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER IF EXISTS reject_event_authorization_audit ON dbo.auditorias;
            DROP FUNCTION IF EXISTS dbo.reject_event_authorization_audit();
            """);
    }

    private sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc);

    private sealed record EventVehicleRuleResponse(
        int Id,
        string VehicleType,
        int Quantity,
        string? Plate);

    private sealed record EventAuthorizationResponse(
        int Id,
        string Name,
        string Responsible,
        DateTime StartsAtUtc,
        DateTime EndsAtUtc,
        string Area,
        bool OvernightAllowed,
        string? Notes,
        bool Active,
        int CreatedById,
        DateTime CreatedAtUtc,
        int? UpdatedById,
        DateTime? UpdatedAtUtc,
        IReadOnlyList<EventVehicleRuleResponse> VehicleRules);

    private sealed record EventAuthorizationPage(
        IReadOnlyList<EventAuthorizationResponse> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages);
}
