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
public sealed class OperationalSummaryTests(ApiFactory factory)
{
    private const string Password = "Test-only-password-123!";

    [Theory]
    [InlineData(ProfileNames.Doorman)]
    [InlineData(ProfileNames.SecurityGuard)]
    [InlineData(ProfileNames.TransportationDepartment)]
    [InlineData(ProfileNames.Administrator)]
    public async Task OperationalProfilesCanReadDailySummary(string profileName)
    {
        using var client = await CreateAuthenticatedClientAsync(profileName);

        var response = await client.GetAsync("/operations/daily-summary?date=2099-01-15", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousAndUnrelatedProfilesCannotReadDailySummary()
    {
        using var anonymousClient = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymousClient.GetAsync("/operations/daily-summary", TestContext.Current.CancellationToken)).StatusCode);

        using var unrelatedClient = await CreateAuthenticatedClientAsync(
            $"PerfilSemResumo{Guid.NewGuid():N}");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await unrelatedClient.GetAsync("/operations/daily-summary", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task SummaryUsesLocalDayBoundariesAndCountsCarryOverRecords()
    {
        var (actorUserId, email) = await CreateUserAsync(
            ProfileNames.Administrator,
            Password);
        // Keep this scenario before records created with the current clock by other
        // tests sharing the same PostgreSQL fixture and after Recife's historical
        // daylight-saving rules stopped applying.
        var localDate = new DateOnly(2010, 1, 15);
        var periodStartUtc = new DateTime(2010, 1, 15, 3, 0, 0, DateTimeKind.Utc);
        var periodEndUtc = periodStartUtc.AddDays(1);
        var sensitiveName = $"Pessoa Resumo {Guid.NewGuid():N}";
        await SeedSummaryRecordsAsync(
            actorUserId,
            sensitiveName,
            periodStartUtc,
            periodEndUtc);

        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, Password);
        var response = await client.GetAsync($"/operations/daily-summary?date={localDate:yyyy-MM-dd}", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var summary = await response.Content.ReadFromJsonAsync<DailySummaryResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(summary);
        Assert.Equal(localDate, summary.LocalDate);
        Assert.Equal("America/Recife", summary.TimeZoneId);
        Assert.Equal(periodStartUtc, summary.PeriodStartUtc);
        Assert.Equal(periodEndUtc, summary.PeriodEndUtcExclusive);
        Assert.Equal(new DailyTotals(3, 2, 1, 3, 3), summary.GeneralAccess);
        Assert.Equal(new InstitutionalTotals(2, 2, 2, 2), summary.InstitutionalUsages);
        Assert.Equal(new EventTotals(3, 2), summary.EventAccess);
        Assert.DoesNotContain(sensitiveName, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plate", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("driver", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidDateReturnsBadRequest()
    {
        using var client = await CreateAuthenticatedClientAsync(ProfileNames.Doorman);

        var response = await client.GetAsync("/operations/daily-summary?date=not-a-date", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task SeedSummaryRecordsAsync(
        int actorUserId,
        string personName,
        DateTime periodStartUtc,
        DateTime periodEndUtc)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        var person = new Pessoa(personName);
        var category = new CategoriaAcesso($"Resumo {suffix}");
        dbContext.AddRange(person, category);
        await dbContext.SaveChangesAsync();

        var generalVehicles = Enumerable.Range(0, 7)
            .Select(index => new Veiculo($"G{suffix}{index}", "Automóvel", null, false))
            .ToArray();
        var institutionalVehicles = Enumerable.Range(0, 5)
            .Select(index => new Veiculo(
                null,
                "Van",
                $"FROTA-{suffix}-{index}",
                true))
            .ToArray();
        dbContext.AddRange(generalVehicles);
        dbContext.AddRange(institutionalVehicles);

        var eventOne = new EventoAcesso(
            $"Evento A {suffix}",
            "Responsável de Teste",
            periodStartUtc,
            periodEndUtc,
            "Área A",
            false,
            null,
            actorUserId,
            DateTime.UtcNow);
        var eventTwo = new EventoAcesso(
            $"Evento B {suffix}",
            "Responsável de Teste",
            periodStartUtc,
            periodEndUtc,
            "Área B",
            false,
            null,
            actorUserId,
            DateTime.UtcNow);
        dbContext.AddRange(eventOne, eventTwo);
        await dbContext.SaveChangesAsync();

        var ruleOne = new AutorizacaoVeiculoEvento(
            eventOne.Id,
            "AUTOMÓVEL",
            10);
        var ruleTwo = new AutorizacaoVeiculoEvento(
            eventTwo.Id,
            "AUTOMÓVEL",
            10);
        dbContext.AddRange(ruleOne, ruleTwo);
        await dbContext.SaveChangesAsync();

        var carryOverClosed = Access(
            generalVehicles[0],
            periodStartUtc.AddHours(-1));
        carryOverClosed.RegistrarSaida(periodStartUtc.AddHours(7), actorUserId);
        var dayClosed = Access(
            generalVehicles[1],
            periodStartUtc.AddHours(1),
            ruleOne.Id);
        dayClosed.RegistrarSaida(periodStartUtc.AddHours(9), actorUserId);
        var dayOpenFirstEvent = Access(
            generalVehicles[2],
            periodStartUtc.AddHours(2),
            ruleOne.Id);
        var dayOpenSecondEvent = Access(
            generalVehicles[3],
            periodStartUtc.AddHours(3),
            ruleTwo.Id);
        var carryOverOpen = Access(
            generalVehicles[4],
            periodStartUtc.AddDays(-1));
        var nextDay = Access(generalVehicles[5], periodEndUtc);
        var exceptionallyClosed = Access(
            generalVehicles[6],
            periodStartUtc.AddDays(-2));
        exceptionallyClosed.RegistrarEncerramentoExcepcional(
            MotivoEncerramentoExcepcional.RegistroDeSaidaOmitido,
            "Saída confirmada posteriormente pelo responsável operacional.",
            dataHoraSaidaObservada: null,
            dataHoraRegularizacao: periodStartUtc.AddHours(5),
            atualizadoPorId: actorUserId);
        dbContext.AddRange(
            carryOverClosed,
            dayClosed,
            dayOpenFirstEvent,
            dayOpenSecondEvent,
            carryOverOpen,
            nextDay,
            exceptionallyClosed);

        var institutionalCarryOverClosed = Usage(
            institutionalVehicles[0],
            periodStartUtc.AddHours(-1));
        institutionalCarryOverClosed.RegistrarRetorno(
            periodStartUtc.AddHours(2),
            101,
            actorUserId);
        var institutionalDayClosed = Usage(
            institutionalVehicles[1],
            periodStartUtc.AddHours(1));
        institutionalDayClosed.RegistrarRetorno(
            periodStartUtc.AddHours(4),
            101,
            actorUserId);
        var institutionalDayOpen = Usage(
            institutionalVehicles[2],
            periodStartUtc.AddHours(5));
        var institutionalCarryOverOpen = Usage(
            institutionalVehicles[3],
            periodStartUtc.AddDays(-1));
        var institutionalNextDay = Usage(institutionalVehicles[4], periodEndUtc);
        dbContext.AddRange(
            institutionalCarryOverClosed,
            institutionalDayClosed,
            institutionalDayOpen,
            institutionalCarryOverOpen,
            institutionalNextDay);

        await dbContext.SaveChangesAsync();

        RegistroAcesso Access(
            Veiculo vehicle,
            DateTime entryAtUtc,
            int? eventRuleId = null) =>
            new(
                vehicle.Id,
                person.Id,
                category.Id,
                entryAtUtc,
                "Teste de resumo diário",
                actorUserId,
                autorizacaoVeiculoEventoId: eventRuleId);

        UsoVeiculoInstitucional Usage(Veiculo vehicle, DateTime departureAtUtc) =>
            new(
                vehicle.Id,
                person.Id,
                departureAtUtc,
                100,
                "Itinerário de teste",
                actorUserId);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string profileName)
    {
        var (_, email) = await CreateUserAsync(profileName, Password);
        var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, Password);
        return client;
    }

    private async Task<(int UserId, string Email)> CreateUserAsync(
        string profileName,
        string password)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        var passwordHasher = scope.ServiceProvider
            .GetRequiredService<IPasswordHashService>();
        var suffix = Guid.NewGuid().ToString("N");
        var profile = await dbContext.Perfis.SingleOrDefaultAsync(
            item => item.Nome == profileName);

        if (profile is null)
        {
            profile = new Perfil(
                profileName,
                "Perfil criado exclusivamente para teste de integração.");
            dbContext.Perfis.Add(profile);
        }

        var person = new Pessoa($"Usuário Resumo {suffix}");
        dbContext.Pessoas.Add(person);
        await dbContext.SaveChangesAsync();

        var email = $"summary-{suffix}@example.test";
        var user = new Usuario(
            email,
            passwordHasher.Hash(password),
            person.Id,
            profile.Id);
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

    private sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc);

    private sealed record DailySummaryResponse(
        DateOnly LocalDate,
        string TimeZoneId,
        DateTime PeriodStartUtc,
        DateTime PeriodEndUtcExclusive,
        DailyTotals GeneralAccess,
        InstitutionalTotals InstitutionalUsages,
        EventTotals EventAccess);

    private sealed record DailyTotals(
        int Entries,
        int Exits,
        int ExceptionalClosures,
        int OpenAtStart,
        int OpenAtEnd);

    private sealed record InstitutionalTotals(
        int Departures,
        int Returns,
        int OpenAtStart,
        int OpenAtEnd);

    private sealed record EventTotals(int Entries, int EventsWithEntries);
}
