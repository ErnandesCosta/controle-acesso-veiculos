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
public sealed class InstitutionalVehicleUsageTests(ApiFactory factory)
{
    [Fact]
    public async Task OperationalUserCanRegisterListAndReturnInstitutionalVehicle()
    {
        const string password = "Test-only-password-123!";
        var (userId, email) = await CreateUserAsync(ProfileNames.Doorman, password);
        var catalog = await CreateCatalogAsync(userId, institutionalVehicle: true);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var request = new
        {
            vehicleId = catalog.VehicleId,
            driverId = catalog.DriverId,
            departureMileage = 12500,
            itinerary = "Campus - Unidade rural"
        };

        var departureResponse = await client.PostAsJsonAsync("/institutional-vehicle-usages/departures", request, cancellationToken: TestContext.Current.CancellationToken);
        var departure = await departureResponse.Content
            .ReadFromJsonAsync<InstitutionalUsageResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, departureResponse.StatusCode);
        Assert.NotNull(departure);
        Assert.Equal("EmUso", departure.Status);
        Assert.Equal(userId, departure.CreatedById);
        Assert.Equal(catalog.Plate, departure.Plate);

        var openUsages = await client.GetFromJsonAsync<List<InstitutionalUsageResponse>>("/institutional-vehicle-usages/open", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(openUsages!, item => item.Id == departure.Id);

        var invalidReturn = await client.PostAsJsonAsync($"/institutional-vehicle-usages/{departure.Id}/returns", new { returnMileage = 12499 }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidReturn.StatusCode);

        using (var deactivationScope = factory.Services.CreateScope())
        {
            var deactivationDbContext = deactivationScope.ServiceProvider
                .GetRequiredService<ControleAcessoVeiculosDbContext>();
            var authorization = await deactivationDbContext.MotoristasInstitucionais
                .SingleAsync(item => item.PessoaId == catalog.DriverId, cancellationToken: TestContext.Current.CancellationToken);
            authorization.Desativar(DateTime.UtcNow, userId);
            await deactivationDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var returnResponse = await client.PostAsJsonAsync($"/institutional-vehicle-usages/{departure.Id}/returns", new { returnMileage = 12542 }, cancellationToken: TestContext.Current.CancellationToken);
        var returned = await returnResponse.Content
            .ReadFromJsonAsync<InstitutionalUsageResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);
        Assert.NotNull(returned);
        Assert.Equal("Concluido", returned.Status);
        Assert.Equal(12542, returned.ReturnMileage);
        Assert.NotNull(returned.ReturnAtUtc);
        Assert.Equal(userId, returned.UpdatedById);

        openUsages = await client.GetFromJsonAsync<List<InstitutionalUsageResponse>>("/institutional-vehicle-usages/open", cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(openUsages!, item => item.Id == departure.Id);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var audits = await dbContext.Auditorias
            .AsNoTracking()
            .Where(item => item.Entidade == nameof(UsoVeiculoInstitucional) &&
                item.RegistroId == departure.Id)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Collection(
            audits,
            audit =>
            {
                Assert.Equal(TipoAcaoAuditoria.Inclusao, audit.TipoAcao);
                Assert.Equal(userId, audit.UsuarioId);
                AssertAuditState(audit.DadosNovos, "EmUso");
            },
            audit =>
            {
                Assert.Equal(TipoAcaoAuditoria.Alteracao, audit.TipoAcao);
                Assert.Equal(userId, audit.UsuarioId);
                AssertAuditState(audit.DadosAnteriores, "EmUso");
                AssertAuditState(audit.DadosNovos, "Concluido");
            });

        var auditContent = string.Join(
            ' ',
            audits.SelectMany(item => new[]
            {
                item.DadosAnteriores,
                item.DadosNovos,
                item.Detalhes
            }).Where(item => item is not null));
        Assert.DoesNotContain(catalog.DriverName, auditContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(catalog.Plate, auditContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(request.itinerary, auditContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DepartureRejectsInvalidOrUnavailableCatalogData()
    {
        const string password = "Test-only-password-123!";
        var (userId, email) = await CreateUserAsync(ProfileNames.SecurityGuard, password);
        var catalog = await CreateCatalogAsync(userId, institutionalVehicle: false);
        var unauthorizedDriverCatalog = await CreateCatalogAsync(
            userId,
            institutionalVehicle: true,
            authorizeDriver: false);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);

        var invalidResponse = await client.PostAsJsonAsync("/institutional-vehicle-usages/departures", new
        {
            vehicleId = 0,
            driverId = 0,
            departureMileage = -1,
            itinerary = ""
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        var unavailableResponse = await client.PostAsJsonAsync("/institutional-vehicle-usages/departures", new
        {
            vehicleId = catalog.VehicleId,
            driverId = catalog.DriverId,
            departureMileage = 100,
            itinerary = "Campus"
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unavailableResponse.StatusCode);

        var unauthorizedDriverResponse = await client.PostAsJsonAsync("/institutional-vehicle-usages/departures", new
        {
            vehicleId = unauthorizedDriverCatalog.VehicleId,
            driverId = unauthorizedDriverCatalog.DriverId,
            departureMileage = 100,
            itinerary = "Campus"
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedDriverResponse.StatusCode);
    }

    [Fact]
    public async Task AnonymousUserCannotOperateInstitutionalVehicleUsages()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/institutional-vehicle-usages/open", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InstitutionalHistoryRequiresTransportationReviewPermission()
    {
        const string password = "Test-only-password-123!";
        using var anonymousClient = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymousClient.GetAsync("/institutional-vehicle-usages/history", TestContext.Current.CancellationToken)).StatusCode);

        var (_, doormanEmail) = await CreateUserAsync(ProfileNames.Doorman, password);
        using var doormanClient = factory.CreateClient();
        await AuthenticateClientAsync(doormanClient, doormanEmail, password);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await doormanClient.GetAsync("/institutional-vehicle-usages/history", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task InactiveInstitutionalVehicleBlocksDepartureButAllowsOpenReturn()
    {
        const string password = "Test-only-password-123!";
        var (userId, email) = await CreateUserAsync(ProfileNames.Doorman, password);
        var catalog = await CreateCatalogAsync(userId, institutionalVehicle: true);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var request = new
        {
            vehicleId = catalog.VehicleId,
            driverId = catalog.DriverId,
            departureMileage = 2000,
            itinerary = "Campus - Destino fictício"
        };
        var departureResponse = await client.PostAsJsonAsync("/institutional-vehicle-usages/departures", request, cancellationToken: TestContext.Current.CancellationToken);
        var departure = await departureResponse.Content
            .ReadFromJsonAsync<InstitutionalUsageResponse>(cancellationToken: TestContext.Current.CancellationToken);
        departureResponse.EnsureSuccessStatusCode();
        Assert.NotNull(departure);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<ControleAcessoVeiculosDbContext>();
            var vehicle = await dbContext.Veiculos
                .SingleAsync(item => item.Id == catalog.VehicleId, cancellationToken: TestContext.Current.CancellationToken);
            vehicle.Desativar(DateTime.UtcNow);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync("/institutional-vehicle-usages/departures", request, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync($"/institutional-vehicle-usages/{departure.Id}/returns", new { returnMileage = 2010 }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task TransportationUserCanFilterOrderAndPaginateInstitutionalHistory()
    {
        const string password = "Test-only-password-123!";
        var (userId, email) = await CreateUserAsync(
            ProfileNames.TransportationDepartment,
            password);
        var catalog = await CreateCatalogAsync(userId, institutionalVehicle: true);
        var now = DateTime.UtcNow;
        await CreateCompletedUsagesAsync(catalog, userId, now);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var formattedPlate = catalog.Plate.Insert(2, "-").ToLowerInvariant();
        var from = Uri.EscapeDataString(now.AddDays(-10).ToString("O"));
        var to = Uri.EscapeDataString(now.AddDays(1).ToString("O"));

        var response = await client.GetAsync($"/institutional-vehicle-usages/history?plate={formattedPlate}" +
            $"&driverId={catalog.DriverId}&from={from}&to={to}&page=1&pageSize=2", TestContext.Current.CancellationToken);
        var result = await response.Content.ReadFromJsonAsync<InstitutionalHistoryResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(2, result.Items.Count);
        Assert.True(result.Items[0].DepartureAtUtc > result.Items[1].DepartureAtUtc);
        Assert.All(result.Items, item =>
        {
            Assert.Equal(catalog.VehicleId, item.VehicleId);
            Assert.Equal(catalog.DriverId, item.DriverId);
        });

        var secondPage = await client.GetFromJsonAsync<InstitutionalHistoryResponse>($"/institutional-vehicle-usages/history?vehicleIdentification=" +
            $"{Uri.EscapeDataString(catalog.VehicleIdentification.ToLowerInvariant())}" +
            $"&from={from}&to={to}&page=2&pageSize=2", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(secondPage);
        Assert.Single(secondPage.Items);

        var invalid = await client.GetAsync($"/institutional-vehicle-usages/history?from={to}&to={from}&pageSize=101", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRequestsCreateAndReturnInstitutionalUsageOnlyOnce()
    {
        const string password = "Test-only-password-123!";
        var (userId, email) = await CreateUserAsync(ProfileNames.SecurityGuard, password);
        var catalog = await CreateCatalogAsync(userId, institutionalVehicle: true);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var departureRequest = new
        {
            vehicleId = catalog.VehicleId,
            driverId = catalog.DriverId,
            departureMileage = 8000,
            itinerary = "Campus - Cidade"
        };

        var departures = await Task.WhenAll(
            client.PostAsJsonAsync(
                "/institutional-vehicle-usages/departures",
                departureRequest,
                TestContext.Current.CancellationToken),
            client.PostAsJsonAsync(
                "/institutional-vehicle-usages/departures",
                departureRequest,
                TestContext.Current.CancellationToken));

        Assert.Single(departures, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(departures, response => response.StatusCode == HttpStatusCode.Conflict);
        var createdResponse = departures.Single(
            response => response.StatusCode == HttpStatusCode.Created);
        var usage = await createdResponse.Content
            .ReadFromJsonAsync<InstitutionalUsageResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(usage);

        var returns = await Task.WhenAll(
            client.PostAsJsonAsync(
                $"/institutional-vehicle-usages/{usage.Id}/returns",
                new { returnMileage = 8010 },
                TestContext.Current.CancellationToken),
            client.PostAsJsonAsync(
                $"/institutional-vehicle-usages/{usage.Id}/returns",
                new { returnMileage = 8011 },
                TestContext.Current.CancellationToken));

        Assert.Single(returns, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(returns, response => response.StatusCode == HttpStatusCode.Conflict);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        Assert.Equal(
            1,
            await dbContext.UsosVeiculosInstitucionais.CountAsync(item =>
                item.VeiculoId == catalog.VehicleId, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(
            2,
            await dbContext.Auditorias.CountAsync(item =>
                item.Entidade == nameof(UsoVeiculoInstitucional) &&
                item.RegistroId == usage.Id &&
                item.UsuarioId == userId, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuditFailureRollsBackInstitutionalDepartureAndReturn()
    {
        const string password = "Test-only-password-123!";
        var (userId, email) = await CreateUserAsync(ProfileNames.Doorman, password);
        var departureCatalog = await CreateCatalogAsync(userId, institutionalVehicle: true);
        var returnCatalog = await CreateCatalogAsync(userId, institutionalVehicle: true);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var existingResponse = await client.PostAsJsonAsync("/institutional-vehicle-usages/departures", new
        {
            vehicleId = returnCatalog.VehicleId,
            driverId = returnCatalog.DriverId,
            departureMileage = 4000,
            itinerary = "Campus - Município"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var existing = await existingResponse.Content
            .ReadFromJsonAsync<InstitutionalUsageResponse>(cancellationToken: TestContext.Current.CancellationToken);
        existingResponse.EnsureSuccessStatusCode();
        Assert.NotNull(existing);

        await InstallRejectingAuditTriggerAsync();

        try
        {
            var departureResponse = await client.PostAsJsonAsync("/institutional-vehicle-usages/departures", new
            {
                vehicleId = departureCatalog.VehicleId,
                driverId = departureCatalog.DriverId,
                departureMileage = 5000,
                itinerary = "Campus - Zona rural"
            }, cancellationToken: TestContext.Current.CancellationToken);
            var returnResponse = await client.PostAsJsonAsync($"/institutional-vehicle-usages/{existing.Id}/returns", new { returnMileage = 4010 }, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.InternalServerError, departureResponse.StatusCode);
            Assert.Equal(HttpStatusCode.InternalServerError, returnResponse.StatusCode);
        }
        finally
        {
            await RemoveRejectingAuditTriggerAsync();
        }

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        Assert.False(await dbContext.UsosVeiculosInstitucionais.AnyAsync(item =>
            item.VeiculoId == departureCatalog.VehicleId, cancellationToken: TestContext.Current.CancellationToken));
        var preserved = await dbContext.UsosVeiculosInstitucionais
            .AsNoTracking()
            .SingleAsync(item => item.Id == existing.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(StatusUsoVeiculoInstitucional.EmUso, preserved.Status);
        Assert.Null(preserved.DataHoraEntrada);
        Assert.Equal(
            1,
            await dbContext.Auditorias.CountAsync(item =>
                item.Entidade == nameof(UsoVeiculoInstitucional) &&
                item.RegistroId == existing.Id, cancellationToken: TestContext.Current.CancellationToken));
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

        var email = $"institutional-{suffix}@example.test";
        var user = new Usuario(email, passwordHasher.Hash(password), person.Id, profile.Id);
        dbContext.Usuarios.Add(user);
        await dbContext.SaveChangesAsync();

        return (user.Id, email);
    }

    private async Task<InstitutionalCatalog> CreateCatalogAsync(
        int actorUserId,
        bool institutionalVehicle,
        bool authorizeDriver = true)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var plate = $"IF{suffix[..5]}".ToUpperInvariant();
        var driverName = $"Motorista {suffix}";
        var vehicle = new Veiculo(
            plate,
            "Automóvel",
            $"FROTA-{suffix[..8]}",
            institutionalVehicle);
        var driver = new Pessoa(driverName, tipoVinculo: "Servidor");
        dbContext.Veiculos.Add(vehicle);
        dbContext.Pessoas.Add(driver);
        await dbContext.SaveChangesAsync();
        if (authorizeDriver)
        {
            dbContext.MotoristasInstitucionais.Add(new MotoristaInstitucional(
                driver.Id,
                actorUserId,
                DateTime.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        return new InstitutionalCatalog(
            vehicle.Id,
            driver.Id,
            plate,
            vehicle.IdentificacaoVeiculo!,
            driverName);
    }

    private async Task CreateCompletedUsagesAsync(
        InstitutionalCatalog catalog,
        int actorUserId,
        DateTime now)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();

        foreach (var daysAgo in new[] { 1, 2, 3 })
        {
            var departure = now.AddDays(-daysAgo);
            var usage = new UsoVeiculoInstitucional(
                catalog.VehicleId,
                catalog.DriverId,
                departure,
                1000 + daysAgo * 100,
                $"Itinerário fictício {daysAgo}",
                actorUserId);
            usage.RegistrarRetorno(
                departure.AddHours(2),
                1010 + daysAgo * 100,
                actorUserId);
            dbContext.UsosVeiculosInstitucionais.Add(usage);
        }

        await dbContext.SaveChangesAsync();
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
            CREATE OR REPLACE FUNCTION dbo.reject_institutional_usage_audit()
            RETURNS trigger AS $function$
            BEGIN
                RAISE EXCEPTION 'forced integration test audit failure';
            END;
            $function$ LANGUAGE plpgsql;

            CREATE TRIGGER reject_institutional_usage_audit
            BEFORE INSERT ON dbo.auditorias
            FOR EACH ROW
            WHEN (NEW.entidade = 'UsoVeiculoInstitucional')
            EXECUTE FUNCTION dbo.reject_institutional_usage_audit();
            """);
    }

    private async Task RemoveRejectingAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER IF EXISTS reject_institutional_usage_audit ON dbo.auditorias;
            DROP FUNCTION IF EXISTS dbo.reject_institutional_usage_audit();
            """);
    }

    private static void AssertAuditState(string? json, string expectedStatus)
    {
        Assert.NotNull(json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expectedStatus, document.RootElement.GetProperty("status").GetString());
    }

    private sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc);

    private sealed record InstitutionalCatalog(
        int VehicleId,
        int DriverId,
        string Plate,
        string VehicleIdentification,
        string DriverName);

    private sealed record InstitutionalHistoryResponse(
        List<InstitutionalUsageResponse> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages);

    private sealed record InstitutionalUsageResponse(
        int Id,
        int VehicleId,
        string? Plate,
        int DriverId,
        string DriverName,
        DateTime DepartureAtUtc,
        int DepartureMileage,
        string Itinerary,
        DateTime? ReturnAtUtc,
        int? ReturnMileage,
        string Status,
        int CreatedById,
        int? UpdatedById);
}
