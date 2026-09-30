using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ControleAcessoVeiculos.Application.AccessRecords;
using ControleAcessoVeiculos.Application.Authentication;
using ControleAcessoVeiculos.Application.Authorization;
using ControleAcessoVeiculos.Domain.Entities;
using ControleAcessoVeiculos.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControleAcessoVeiculos.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class EventAccessAssociationTests(ApiFactory factory)
{
    private const string Password = "Test-only-password-123!";

    [Fact]
    public async Task SpecificPlateTakesPrecedenceAndExitDoesNotRestoreCapacity()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var plate = $"SP{suffix[..5]}";
        var eventAuthorization = await CreateEventAsync(
            client,
            [
                new { vehicleType = "Automóvel", quantity = 1, plate },
                new { vehicleType = "Automóvel", quantity = 2, plate = (string?)null }
            ]);

        var response = await RegisterEntryAsync(
            client,
            plate,
            "Automóvel",
            eventAuthorization.Id);
        var access = await response.Content.ReadFromJsonAsync<AccessRecordResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(access);
        Assert.Equal(eventAuthorization.Id, access.EventAuthorizationId);
        Assert.Equal(eventAuthorization.Name, access.EventAuthorizationName);
        Assert.NotNull(access.EventVehicleRuleId);

        var current = await FindEventAsync(client, eventAuthorization.Name);
        var plateRule = Assert.Single(current.VehicleRules, rule => rule.Plate == plate);
        var quotaRule = Assert.Single(current.VehicleRules, rule => rule.Plate is null);
        Assert.Equal(1, plateRule.ConsumedQuantity);
        Assert.Equal(0, plateRule.RemainingQuantity);
        Assert.Equal(0, quotaRule.ConsumedQuantity);
        Assert.Equal(2, quotaRule.RemainingQuantity);

        (await client.PostAsync($"/access-records/{access.Id}/exit", null, TestContext.Current.CancellationToken))
            .EnsureSuccessStatusCode();
        var repeated = await RegisterEntryAsync(
            client,
            plate,
            "Automóvel",
            eventAuthorization.Id);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);

        var history = await client.GetFromJsonAsync<AccessHistoryResponse>($"/access-records/history?plate={plate}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(history!.Items, item =>
            item.Id == access.Id &&
            item.EventAuthorizationId == eventAuthorization.Id &&
            item.EventVehicleRuleId == plateRule.Id);
    }

    [Fact]
    public async Task ConcurrentEntriesCannotExceedVehicleTypeQuota()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var eventAuthorization = await CreateEventAsync(
            client,
            [new { vehicleType = "Van", quantity = 1, plate = (string?)null }]);

        var responses = await Task.WhenAll(
            RegisterEntryAsync(client, $"A{suffix[..6]}", "Van", eventAuthorization.Id),
            RegisterEntryAsync(client, $"B{suffix[..6]}", "Van", eventAuthorization.Id));

        Assert.Single(responses, item => item.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, item => item.StatusCode == HttpStatusCode.Conflict);
        var current = await FindEventAsync(client, eventAuthorization.Name);
        var rule = Assert.Single(current.VehicleRules);
        Assert.Equal(1, rule.ConsumedQuantity);
        Assert.Equal(0, rule.RemainingQuantity);

        var changedRules = await client.PutAsJsonAsync($"/event-authorizations/{eventAuthorization.Id}", EventRequest(
                eventAuthorization.Name,
                eventAuthorization.StartsAtUtc,
                eventAuthorization.EndsAtUtc,
                [new { vehicleType = "Van", quantity = 2, plate = (string?)null }]), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, changedRules.StatusCode);
    }

    [Fact]
    public async Task EventAssociationRejectsMissingInactiveOutsideAndUnauthorizedEvents()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var missing = await RegisterEntryAsync(client, $"M{suffix[..6]}", "Van", int.MaxValue);
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);

        var future = await CreateEventAsync(
            client,
            [new { vehicleType = "Van", quantity = 2, plate = (string?)null }],
            DateTimeOffset.UtcNow.AddDays(2),
            DateTimeOffset.UtcNow.AddDays(3));
        var outside = await RegisterEntryAsync(
            client,
            $"F{suffix[..6]}",
            "Van",
            future.Id);
        Assert.Equal(HttpStatusCode.Conflict, outside.StatusCode);

        var active = await CreateEventAsync(
            client,
            [new { vehicleType = "Ônibus", quantity = 2, plate = (string?)null }]);
        var unauthorized = await RegisterEntryAsync(
            client,
            $"U{suffix[..6]}",
            "Motocicleta",
            active.Id);
        Assert.Equal(HttpStatusCode.Conflict, unauthorized.StatusCode);

        (await client.DeleteAsync($"/event-authorizations/{active.Id}", TestContext.Current.CancellationToken))
            .EnsureSuccessStatusCode();
        var inactive = await RegisterEntryAsync(
            client,
            $"I{suffix[..6]}",
            "Ônibus",
            active.Id);
        Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode);
    }

    private async Task<HttpClient> CreateAdministratorClientAsync()
    {
        var (_, email) = await CreateUserAsync(ProfileNames.Administrator, Password);
        var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, Password);
        return client;
    }

    private static Task<HttpResponseMessage> RegisterEntryAsync(
        HttpClient client,
        string plate,
        string vehicleType,
        int eventAuthorizationId) =>
        client.PostAsJsonAsync("/access-records/entries", new
        {
            driverName = $"Condutor {plate}",
            plate,
            objective = "Participação em evento",
            categoryName = AccessCategoryNames.Event,
            vehicleType,
            eventAuthorizationId
        });

    private static async Task<EventAuthorizationResponse> CreateEventAsync(
        HttpClient client,
        object[] rules,
        DateTimeOffset? startsAtUtc = null,
        DateTimeOffset? endsAtUtc = null)
    {
        var name = $"Evento {Guid.NewGuid():N}";
        var start = startsAtUtc ?? DateTimeOffset.UtcNow.AddMinutes(-5);
        var end = endsAtUtc ?? DateTimeOffset.UtcNow.AddDays(1);
        var response = await client.PostAsJsonAsync(
            "/event-authorizations",
            EventRequest(name, start, end, rules));
        var result = await response.Content.ReadFromJsonAsync<EventAuthorizationResponse>();
        response.EnsureSuccessStatusCode();
        return Assert.IsType<EventAuthorizationResponse>(result);
    }

    private static object EventRequest(
        string name,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        object[] rules) =>
        new
        {
            name,
            responsible = "Coordenação de Teste",
            startsAtUtc,
            endsAtUtc,
            area = "Área de teste",
            overnightAllowed = true,
            vehicleRules = rules
        };

    private static object EventRequest(
        string name,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        object[] rules) =>
        EventRequest(
            name,
            new DateTimeOffset(startsAtUtc, TimeSpan.Zero),
            new DateTimeOffset(endsAtUtc, TimeSpan.Zero),
            rules);

    private static async Task<EventAuthorizationResponse> FindEventAsync(
        HttpClient client,
        string name)
    {
        var page = await client.GetFromJsonAsync<EventAuthorizationPage>(
            $"/event-authorizations?name={Uri.EscapeDataString(name)}");
        return Assert.Single(page!.Items, item => item.Name == name);
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
        var email = $"event-access-{suffix}@example.test";
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

    private sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc);

    private sealed record EventVehicleRuleResponse(
        int Id,
        string VehicleType,
        int Quantity,
        string? Plate,
        int ConsumedQuantity,
        int RemainingQuantity);

    private sealed record EventAuthorizationResponse(
        int Id,
        string Name,
        DateTime StartsAtUtc,
        DateTime EndsAtUtc,
        IReadOnlyList<EventVehicleRuleResponse> VehicleRules);

    private sealed record EventAuthorizationPage(
        IReadOnlyList<EventAuthorizationResponse> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages);

    private sealed record AccessRecordResponse(
        int Id,
        string Plate,
        string Status,
        int? EventAuthorizationId,
        string? EventAuthorizationName,
        int? EventVehicleRuleId);

    private sealed record AccessHistoryResponse(
        IReadOnlyList<AccessRecordResponse> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages);
}
