using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ControleAcessoVeiculos.API.Middleware;
using ControleAcessoVeiculos.Application.Authentication;
using ControleAcessoVeiculos.Application.Authorization;
using ControleAcessoVeiculos.Domain.Entities;
using ControleAcessoVeiculos.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControleAcessoVeiculos.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class RateLimitingTests(ApiFactory factory)
{
    [Fact]
    public async Task LoginLimitShouldReturnCorrelatedProblemDetails()
    {
        using var limitedFactory = CreateLimitedFactory(
            globalPermitLimit: 100,
            loginPermitLimit: 2);
        using var client = limitedFactory.CreateClient();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await SendInvalidLoginAsync(client);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var rejected = await SendInvalidLoginAsync(client);
        var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.StartsWith(
            "application/problem+json",
            rejected.Content.Headers.ContentType?.ToString());
        Assert.NotNull(rejected.Headers.RetryAfter?.Delta);
        Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            GetCorrelationId(rejected),
            body.RootElement.GetProperty("correlationId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            body.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain(
            "missing-rate-limit-user",
            await rejected.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthChecksShouldRemainAvailableAfterGlobalLimit()
    {
        using var limitedFactory = CreateLimitedFactory(
            globalPermitLimit: 1,
            loginPermitLimit: 100);
        using var client = limitedFactory.CreateClient();

        var first = await client.GetAsync("/access-records/open", TestContext.Current.CancellationToken);
        var rejected = await client.GetAsync("/access-records/open", TestContext.Current.CancellationToken);
        var health = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        health.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AuthenticatedUsersShouldHaveIndependentGlobalPartitions()
    {
        using var limitedFactory = CreateLimitedFactory(
            globalPermitLimit: 1,
            loginPermitLimit: 100);
        var firstToken = await CreateUserAndIssueTokenAsync(limitedFactory);
        var secondToken = await CreateUserAndIssueTokenAsync(limitedFactory);
        using var firstClient = CreateAuthenticatedClient(limitedFactory, firstToken);
        using var secondClient = CreateAuthenticatedClient(limitedFactory, secondToken);

        var firstResponse = await firstClient.GetAsync("/access-records/open", TestContext.Current.CancellationToken);
        var firstRejected = await firstClient.GetAsync("/access-records/open", TestContext.Current.CancellationToken);
        var secondResponse = await secondClient.GetAsync("/access-records/open", TestContext.Current.CancellationToken);

        firstResponse.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.TooManyRequests, firstRejected.StatusCode);
        secondResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task PasswordChangeUsesDedicatedAuthenticatedLimit()
    {
        using var limitedFactory = CreateLimitedFactory(
            globalPermitLimit: 100,
            loginPermitLimit: 100,
            passwordChangePermitLimit: 2);
        var token = await CreateUserAndIssueTokenAsync(limitedFactory);
        using var client = CreateAuthenticatedClient(limitedFactory, token);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await SendInvalidPasswordChangeAsync(client);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var rejected = await SendInvalidPasswordChangeAsync(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public void InvalidLimitsShouldFailDuringStartup()
    {
        using var invalidFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:GlobalPermitLimit", "0"));

        var exception = Assert.Throws<InvalidOperationException>(
            invalidFactory.CreateClient);

        Assert.Contains(
            "RateLimiting:GlobalPermitLimit",
            exception.Message,
            StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateLimitedFactory(
        int globalPermitLimit,
        int loginPermitLimit,
        int passwordChangePermitLimit = 100) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "RateLimiting:GlobalPermitLimit",
                globalPermitLimit.ToString());
            builder.UseSetting("RateLimiting:GlobalWindowSeconds", "60");
            builder.UseSetting(
                "RateLimiting:LoginPermitLimit",
                loginPermitLimit.ToString());
            builder.UseSetting("RateLimiting:LoginWindowSeconds", "60");
            builder.UseSetting(
                "RateLimiting:PasswordChangePermitLimit",
                passwordChangePermitLimit.ToString());
            builder.UseSetting("RateLimiting:PasswordChangeWindowSeconds", "60");
        });

    private static async Task<HttpResponseMessage> SendInvalidLoginAsync(
        HttpClient client) =>
        await client.PostAsJsonAsync("/auth/login", new
        {
            email = "missing-rate-limit-user@example.test",
            password = "Invalid-test-password-123!"
        });

    private static async Task<HttpResponseMessage> SendInvalidPasswordChangeAsync(
        HttpClient client) =>
        await client.PostAsJsonAsync("/auth/password", new
        {
            currentPassword = "Wrong-test-password-123!",
            newPassword = "New-test-password-456!"
        });

    private static async Task<string> CreateUserAndIssueTokenAsync(
        WebApplicationFactory<Program> limitedFactory)
    {
        using var scope = limitedFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        var passwordHashService = scope.ServiceProvider
            .GetRequiredService<IPasswordHashService>();
        var tokenService = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
        var profile = await dbContext.Perfis.SingleOrDefaultAsync(item =>
            item.Nome == ProfileNames.Administrator);

        if (profile is null)
        {
            profile = new Perfil(
                ProfileNames.Administrator,
                "Perfil criado exclusivamente para teste de integração.");
            dbContext.Perfis.Add(profile);
        }

        var suffix = Guid.NewGuid().ToString("N");
        var person = new Pessoa($"Pessoa Rate Limit {suffix}");
        dbContext.Pessoas.Add(person);
        await dbContext.SaveChangesAsync();

        var email = $"rate-limit-{suffix}@example.test";
        var user = new Usuario(
            email,
            passwordHashService.Hash("Test-only-password-123!"),
            person.Id,
            profile.Id);
        dbContext.Usuarios.Add(user);
        await dbContext.SaveChangesAsync();

        return tokenService.Issue(
            user.Id,
            email,
            ProfileNames.Administrator,
            user.VersaoCredencial,
            requiresPasswordChange: false).Value;
    }

    private static HttpClient CreateAuthenticatedClient(
        WebApplicationFactory<Program> limitedFactory,
        string token)
    {
        var client = limitedFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string GetCorrelationId(HttpResponseMessage response) =>
        Assert.Single(response.Headers.GetValues(
            RequestSafetyMiddleware.CorrelationIdHeaderName));
}
