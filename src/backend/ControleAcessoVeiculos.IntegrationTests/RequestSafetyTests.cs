using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ControleAcessoVeiculos.API.Middleware;
using ControleAcessoVeiculos.Application.Authentication;
using ControleAcessoVeiculos.Application.Authorization;
using ControleAcessoVeiculos.Domain.Entities;
using ControleAcessoVeiculos.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControleAcessoVeiculos.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class RequestSafetyTests(ApiFactory factory)
{
    [Fact]
    public async Task ResponseShouldContainServerGeneratedCorrelationId()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var value = Assert.Single(response.Headers.GetValues(
            RequestSafetyMiddleware.CorrelationIdHeaderName));
        Assert.True(Guid.TryParseExact(value, "D", out _));
    }

    [Fact]
    public async Task ValidCorrelationIdShouldBeNormalizedAndReturned()
    {
        using var client = factory.CreateClient();
        var correlationId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(
            RequestSafetyMiddleware.CorrelationIdHeaderName,
            correlationId.ToString("B").ToUpperInvariant());

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Equal(
            correlationId.ToString("D"),
            Assert.Single(response.Headers.GetValues(
                RequestSafetyMiddleware.CorrelationIdHeaderName)));
    }

    [Fact]
    public async Task InvalidCorrelationIdShouldBeReplaced()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            RequestSafetyMiddleware.CorrelationIdHeaderName,
            "invalid-correlation-id");

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var value = Assert.Single(response.Headers.GetValues(
            RequestSafetyMiddleware.CorrelationIdHeaderName));
        Assert.NotEqual("invalid-correlation-id", value);
        Assert.True(Guid.TryParseExact(value, "D", out _));
    }

    [Fact]
    public async Task OversizedPayloadShouldReturnProblemDetails()
    {
        factory.RequestLogs.Clear();
        using var client = factory.CreateClient();
        using var content = new StringContent(
            new string('a', RequestSafetyMiddleware.MaximumRequestBodySize + 1),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/auth/login", content, TestContext.Current.CancellationToken);
        var body = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(413, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            GetCorrelationId(response),
            body.RootElement.GetProperty("correlationId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            body.RootElement.GetProperty("traceId").GetString()));
        Assert.Contains(
            factory.RequestLogs.Messages,
            message => message.Contains("413", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RequestLogShouldExcludeQueryAndAuthorizationHeader()
    {
        factory.RequestLogs.Clear();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "secret-test-token");

        var response = await client.GetAsync("/health?document=secret-test-document", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Contains(
            factory.RequestLogs.Messages,
            message => message.Contains("GET /health", StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.RequestLogs.Messages,
            message => message.Contains("secret-test", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("GET", "GET")]
    [InlineData("POST", "POST")]
    [InlineData("CUSTOM", "<other-method>")]
    [InlineData("GET\r\nInjected: true", "<other-method>")]
    [InlineData("", "<other-method>")]
    [InlineData(null, "<other-method>")]
    public void RequestMethodForLogsShouldUseFixedSafeValues(
        string? method,
        string expected)
    {
        Assert.Equal(expected, RequestSafetyMiddleware.GetSafeRequestMethod(method));
    }

    [Fact]
    public async Task UnauthorizedResponseShouldUseCorrelatedProblemDetails()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/access-records/open", TestContext.Current.CancellationToken);
        var body = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(401, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            GetCorrelationId(response),
            body.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task UnhandledExceptionShouldNotExposeInternalDetails()
    {
        factory.RequestLogs.Clear();
        using var client = factory.CreateClient();
        var accessToken = await CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync("/__tests/unhandled-error", TestContext.Current.CancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(responseText);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType?.ToString());
        Assert.Equal(500, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            GetCorrelationId(response),
            body.RootElement.GetProperty("correlationId").GetString());
        Assert.DoesNotContain("sensitive-database-password", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            factory.RequestLogs.Messages,
            message => message.Contains(
                "sensitive-database-password",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            factory.RequestLogs.Messages,
            message => message.Contains(
                typeof(InvalidOperationException).FullName!,
                StringComparison.Ordinal));
    }

    private static async Task<JsonDocument> ReadProblemDetailsAsync(
        HttpResponseMessage response)
    {
        Assert.StartsWith(
            "application/problem+json",
            response.Content.Headers.ContentType?.ToString());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private async Task<string> CreateAccessTokenAsync()
    {
        using var scope = factory.Services.CreateScope();
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
        var person = new Pessoa($"Pessoa Request Safety {suffix}");
        dbContext.Pessoas.Add(person);
        await dbContext.SaveChangesAsync();

        var email = $"request-safety-{suffix}@example.test";
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

    private static string GetCorrelationId(HttpResponseMessage response) =>
        Assert.Single(response.Headers.GetValues(
            RequestSafetyMiddleware.CorrelationIdHeaderName));
}
