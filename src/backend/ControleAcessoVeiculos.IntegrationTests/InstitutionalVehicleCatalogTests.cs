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
public sealed class InstitutionalVehicleCatalogTests(ApiFactory factory)
{
    [Fact]
    public async Task TransportationUserCanCreateAndOperationalUserCanListVehicle()
    {
        const string password = "Test-only-password-123!";
        var (transportationUserId, transportationEmail) = await CreateUserAsync(
            ProfileNames.TransportationDepartment,
            password);
        var (_, doormanEmail) = await CreateUserAsync(ProfileNames.Doorman, password);
        using var managementClient = factory.CreateClient();
        await AuthenticateClientAsync(managementClient, transportationEmail, password);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var plate = $"if-{suffix[..5]}";
        var identification = $" frota-{suffix} ";

        var createResponse = await managementClient.PostAsJsonAsync("/institutional-vehicles", new
        {
            plate,
            identification,
            vehicleType = " Automóvel ",
            brand = " Marca Fictícia ",
            model = " Modelo de Teste ",
            color = " Branco ",
            year = 2026
        }, cancellationToken: TestContext.Current.CancellationToken);
        var created = await createResponse.Content
            .ReadFromJsonAsync<InstitutionalVehicleResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);
        var createdPlate = Assert.IsType<string>(created.Plate);
        var createdIdentification = Assert.IsType<string>(created.Identification);
        Assert.Equal(plate.Replace("-", string.Empty).ToUpperInvariant(), createdPlate);
        Assert.Equal(identification.Trim().ToUpperInvariant(), createdIdentification);
        Assert.Equal("Automóvel", created.VehicleType);

        using var operationalClient = factory.CreateClient();
        await AuthenticateClientAsync(operationalClient, doormanEmail, password);
        var vehicles = await operationalClient
            .GetFromJsonAsync<List<InstitutionalVehicleResponse>>("/institutional-vehicles", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(vehicles!, vehicle => vehicle.Id == created.Id);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var audit = await dbContext.Auditorias
            .AsNoTracking()
            .SingleAsync(item => item.Entidade == nameof(Veiculo) &&
                item.RegistroId == created.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TipoAcaoAuditoria.Inclusao, audit.TipoAcao);
        Assert.Equal(transportationUserId, audit.UsuarioId);
        var auditContent = string.Join(' ', audit.DadosNovos, audit.Detalhes);
        Assert.DoesNotContain(createdPlate, auditContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            createdIdentification,
            auditContent,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CatalogEnforcesAuthenticationAuthorizationAndValidation()
    {
        const string password = "Test-only-password-123!";
        var (_, doormanEmail) = await CreateUserAsync(ProfileNames.Doorman, password);
        using var anonymousClient = factory.CreateClient();

        var anonymousResponse = await anonymousClient.GetAsync("/institutional-vehicles", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var operationalClient = factory.CreateClient();
        await AuthenticateClientAsync(operationalClient, doormanEmail, password);
        var forbiddenResponse = await operationalClient.PostAsJsonAsync("/institutional-vehicles", new { plate = "ABC-1D23", identification = "FROTA-TESTE" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await operationalClient.PutAsJsonAsync("/institutional-vehicles/1", new { plate = "ABC-1D23", identification = "FROTA-TESTE" }, cancellationToken: TestContext.Current.CancellationToken))
            .StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await operationalClient.DeleteAsync("/institutional-vehicles/1", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await operationalClient.PostAsync("/institutional-vehicles/1/reactivation", content: null, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);

        var (_, transportationEmail) = await CreateUserAsync(
            ProfileNames.TransportationDepartment,
            password);
        using var managementClient = factory.CreateClient();
        await AuthenticateClientAsync(managementClient, transportationEmail, password);
        var invalidResponse = await managementClient.PostAsJsonAsync("/institutional-vehicles", new { plate = "---", identification = (string?)null, year = 2028 }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }

    [Fact]
    public async Task ConcurrentDuplicateRegistrationsCreateVehicleOnlyOnce()
    {
        const string password = "Test-only-password-123!";
        var (_, email) = await CreateUserAsync(ProfileNames.Administrator, password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var request = new
        {
            plate = $"IF{suffix[..5]}",
            identification = $"FROTA-{suffix}",
            vehicleType = "Automóvel"
        };

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync(
                "/institutional-vehicles",
                request,
                TestContext.Current.CancellationToken),
            client.PostAsJsonAsync(
                "/institutional-vehicles",
                request,
                TestContext.Current.CancellationToken));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task TransportationUserCanUpdateDeactivateAndReactivateVehicle()
    {
        const string password = "Test-only-password-123!";
        var (userId, email) = await CreateUserAsync(
            ProfileNames.TransportationDepartment,
            password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var originalPlate = $"IF{suffix[..5]}";
        var originalIdentification = $"FROTA-{suffix}";
        var createResponse = await client.PostAsJsonAsync("/institutional-vehicles", new { plate = originalPlate, identification = originalIdentification }, cancellationToken: TestContext.Current.CancellationToken);
        var created = await createResponse.Content
            .ReadFromJsonAsync<InstitutionalVehicleResponse>(cancellationToken: TestContext.Current.CancellationToken);
        createResponse.EnsureSuccessStatusCode();
        Assert.NotNull(created);
        var updatedPlate = $"UP{suffix[..5]}";
        var updatedIdentification = $"ATUAL-{suffix}";

        var updateResponse = await client.PutAsJsonAsync($"/institutional-vehicles/{created.Id}", new
        {
            plate = updatedPlate.ToLowerInvariant(),
            identification = updatedIdentification.ToLowerInvariant(),
            vehicleType = " Van ",
            brand = " Marca Fictícia ",
            model = " Modelo de Teste ",
            color = " Branco ",
            year = 2026
        }, cancellationToken: TestContext.Current.CancellationToken);
        var updated = await updateResponse.Content
            .ReadFromJsonAsync<InstitutionalVehicleResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.NotNull(updated);
        Assert.Equal(updatedPlate, updated.Plate);
        Assert.Equal(updatedIdentification, updated.Identification);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/institutional-vehicles/{created.Id}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await client.DeleteAsync($"/institutional-vehicles/{created.Id}", TestContext.Current.CancellationToken)).StatusCode);
        var active = await client.GetFromJsonAsync<List<InstitutionalVehicleResponse>>("/institutional-vehicles", cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(active!, item => item.Id == created.Id);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync($"/institutional-vehicles/{created.Id}/reactivation", content: null, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await client.PostAsync($"/institutional-vehicles/{created.Id}/reactivation", content: null, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        active = await client.GetFromJsonAsync<List<InstitutionalVehicleResponse>>("/institutional-vehicles", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(active!, item => item.Id == created.Id);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var audits = await dbContext.Auditorias.AsNoTracking()
            .Where(item => item.Entidade == nameof(Veiculo) &&
                item.RegistroId == created.Id)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(4, audits.Count);
        Assert.All(audits, audit => Assert.Equal(userId, audit.UsuarioId));
        Assert.Contains("changedFields", Assert.IsType<string>(audits[1].DadosNovos));
        var auditContent = string.Join(
            ' ',
            audits.SelectMany(item => new[]
            {
                item.DadosAnteriores,
                item.DadosNovos,
                item.Detalhes
            }).Where(item => item is not null));
        Assert.DoesNotContain(originalPlate, auditContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(originalIdentification, auditContent,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(updatedPlate, auditContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(updatedIdentification, auditContent,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateRejectsDuplicateVehicleIdentity()
    {
        const string password = "Test-only-password-123!";
        var (_, email) = await CreateUserAsync(ProfileNames.Administrator, password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var firstPlate = $"AA{suffix[..5]}";
        var first = await client.PostAsJsonAsync("/institutional-vehicles", new { plate = firstPlate, identification = $"FIRST-{suffix}" }, cancellationToken: TestContext.Current.CancellationToken);
        var second = await client.PostAsJsonAsync("/institutional-vehicles", new { plate = $"BB{suffix[..5]}", identification = $"SECOND-{suffix}" }, cancellationToken: TestContext.Current.CancellationToken);
        var secondVehicle = await second.Content
            .ReadFromJsonAsync<InstitutionalVehicleResponse>(cancellationToken: TestContext.Current.CancellationToken);
        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        Assert.NotNull(secondVehicle);

        var duplicate = await client.PutAsJsonAsync($"/institutional-vehicles/{secondVehicle.Id}", new { plate = firstPlate, identification = $"SECOND-{suffix}" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task AuditFailureRollsBackVehicleCreation()
    {
        const string password = "Test-only-password-123!";
        var (_, email) = await CreateUserAsync(
            ProfileNames.TransportationDepartment,
            password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var identification = $"ROLLBACK-{Guid.NewGuid():N}".ToUpperInvariant();
        var existingIdentification = $"PRESERVED-{Guid.NewGuid():N}".ToUpperInvariant();
        var existingResponse = await client.PostAsJsonAsync("/institutional-vehicles", new { plate = (string?)null, identification = existingIdentification }, cancellationToken: TestContext.Current.CancellationToken);
        var existing = await existingResponse.Content
            .ReadFromJsonAsync<InstitutionalVehicleResponse>(cancellationToken: TestContext.Current.CancellationToken);
        existingResponse.EnsureSuccessStatusCode();
        Assert.NotNull(existing);
        await InstallRejectingAuditTriggerAsync();

        try
        {
            var response = await client.PostAsJsonAsync("/institutional-vehicles", new { plate = (string?)null, identification }, cancellationToken: TestContext.Current.CancellationToken);
            var updateResponse = await client.PutAsJsonAsync($"/institutional-vehicles/{existing.Id}", new { plate = (string?)null, identification = $"CHANGED-{Guid.NewGuid():N}" }, cancellationToken: TestContext.Current.CancellationToken);
            var deactivateResponse = await client.DeleteAsync($"/institutional-vehicles/{existing.Id}", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(HttpStatusCode.InternalServerError, updateResponse.StatusCode);
            Assert.Equal(HttpStatusCode.InternalServerError, deactivateResponse.StatusCode);
        }
        finally
        {
            await RemoveRejectingAuditTriggerAsync();
        }

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        Assert.False(await dbContext.Veiculos
            .AnyAsync(vehicle => vehicle.IdentificacaoVeiculo == identification, cancellationToken: TestContext.Current.CancellationToken));
        var preserved = await dbContext.Veiculos.AsNoTracking()
            .SingleAsync(vehicle => vehicle.Id == existing.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(existingIdentification, preserved.IdentificacaoVeiculo);
        Assert.True(preserved.Ativo);
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

        var email = $"fleet-{suffix}@example.test";
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
            CREATE OR REPLACE FUNCTION dbo.reject_vehicle_catalog_audit()
            RETURNS trigger AS $function$
            BEGIN
                RAISE EXCEPTION 'forced integration test audit failure';
            END;
            $function$ LANGUAGE plpgsql;

            CREATE TRIGGER reject_vehicle_catalog_audit
            BEFORE INSERT ON dbo.auditorias
            FOR EACH ROW
            WHEN (NEW.entidade = 'Veiculo')
            EXECUTE FUNCTION dbo.reject_vehicle_catalog_audit();
            """);
    }

    private async Task RemoveRejectingAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER IF EXISTS reject_vehicle_catalog_audit ON dbo.auditorias;
            DROP FUNCTION IF EXISTS dbo.reject_vehicle_catalog_audit();
            """);
    }

    private sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc);

    private sealed record InstitutionalVehicleResponse(
        int Id,
        string? Plate,
        string? Identification,
        string? VehicleType,
        string? Brand,
        string? Model,
        string? Color,
        int? Year,
        DateTime CreatedAtUtc);
}
