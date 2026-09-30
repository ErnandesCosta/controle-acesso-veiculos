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
public sealed class AuditTrailTests(ApiFactory factory)
{
    private const string Password = "Test-only-password-123!";

    [Fact]
    public async Task AdministratorCanFilterSystemEventsAndReadStructuredState()
    {
        var administrator = await CreateUserAsync(ProfileNames.Administrator);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, administrator.Email);
        var entity = $"AuditQuery{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;

        await AddAuditAsync(new Auditoria(
            now.AddMinutes(-2),
            TipoAcaoAuditoria.Inclusao,
            entity,
            1,
            administrator.Id,
            dadosNovos: """{"active":true}""",
            detalhes: "human test event"));
        await AddAuditAsync(new Auditoria(
            now.AddMinutes(-1),
            TipoAcaoAuditoria.Alteracao,
            entity,
            2,
            null,
            dadosAnteriores: """{"active":false}""",
            dadosNovos: """{"active":true}""",
            detalhes: "system test event"));

        var response = await client.GetAsync($"/audits?entity={entity}&systemOnly=true&page=1&pageSize=10", TestContext.Current.CancellationToken);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);

        response.EnsureSuccessStatusCode();
        Assert.Equal(1, document.RootElement.GetProperty("totalCount").GetInt32());
        var audit = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("System", audit.GetProperty("actorType").GetString());
        Assert.Equal("Alteracao", audit.GetProperty("action").GetString());
        Assert.Null(audit.GetProperty("actorUserId").GetInt32OrNull());
        Assert.False(audit.GetProperty("previousState").GetProperty("active").GetBoolean());
        Assert.True(audit.GetProperty("newState").GetProperty("active").GetBoolean());
        Assert.DoesNotContain(administrator.Email, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Password, json, StringComparison.Ordinal);
        Assert.DoesNotContain("hash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuditTrailUsesStableDescendingPaginationAndHumanActorFilter()
    {
        var administrator = await CreateUserAsync(ProfileNames.Administrator);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, administrator.Email);
        var entity = $"AuditPage{Guid.NewGuid():N}";
        var occurredAt = DateTime.UtcNow.AddMinutes(-1);

        for (var recordId = 1; recordId <= 3; recordId++)
        {
            await AddAuditAsync(new Auditoria(
                occurredAt,
                TipoAcaoAuditoria.Consulta,
                entity,
                recordId,
                administrator.Id));
        }

        var response = await client.GetAsync($"/audits?entity={entity}&actorUserId={administrator.Id}&systemOnly=false&page=1&pageSize=2", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        response.EnsureSuccessStatusCode();
        Assert.Equal(3, document.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("totalPages").GetInt32());
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.True(items[0].GetProperty("id").GetInt32() > items[1].GetProperty("id").GetInt32());
        Assert.All(items, item =>
        {
            Assert.Equal("Human", item.GetProperty("actorType").GetString());
            Assert.Equal(administrator.Id, item.GetProperty("actorUserId").GetInt32());
        });
    }

    [Fact]
    public async Task NonAdministratorCannotReadAuditTrail()
    {
        var user = await CreateUserAsync(ProfileNames.Doorman);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, user.Email);

        var response = await client.GetAsync("/audits", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InvalidAuditPeriodReturnsCorrelatedProblemDetails()
    {
        var administrator = await CreateUserAsync(ProfileNames.Administrator);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, administrator.Email);

        var response = await client.GetAsync("/audits?fromUtc=2026-01-01T00:00:00Z&toUtc=2026-04-02T00:00:01Z", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("period", out _));
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("correlationId").GetString()));
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

        var email = $"audit-{suffix}@example.test";
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

    private async Task AddAuditAsync(Auditoria audit)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        dbContext.Auditorias.Add(audit);
        await dbContext.SaveChangesAsync();
    }

    private sealed record TestUser(int Id, string Email);
    private sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc);
}

internal static class NullableJsonElementExtensions
{
    public static int? GetInt32OrNull(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : element.GetInt32();
}
