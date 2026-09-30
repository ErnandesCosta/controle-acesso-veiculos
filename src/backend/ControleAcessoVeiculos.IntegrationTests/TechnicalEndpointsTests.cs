using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ControleAcessoVeiculos.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class TechnicalEndpointsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient(new()
    {
        AllowAutoRedirect = false
    });

    [Fact]
    public async Task HealthReturnsHealthyStatus()
    {
        var response = await _client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal("Healthy", body.Status);
        Assert.NotEqual(default, body.Timestamp);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpointsReturnHealthyStatus(string endpoint)
    {
        var response = await _client.GetAsync(endpoint, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal("Healthy", body.Status);
    }

    [Fact]
    public async Task ReadinessReturnsServiceUnavailableWithoutDatabase()
    {
        using var baseFactory = new WebApplicationFactory<Program>();
        using var unavailableDatabaseFactory = baseFactory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting(
                    "ConnectionStrings:DefaultConnection",
                    "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Password=test;Timeout=1");
                builder.UseSetting(
                    "Authentication:Jwt:SigningKey",
                    "integration-tests-only-signing-key-32-characters");
            });
        using var client = unavailableDatabaseFactory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("Unhealthy", body.Status);
    }

    [Fact]
    public async Task DevelopmentOpenApiDescribesMinimalLoginIdentity()
    {
        using var developmentFactory = factory.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development"));
        using var client = developmentFactory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(responseContent);
        var paths = document.RootElement.GetProperty("paths");
        var securitySchemes = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes");
        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");
        var loginProperties = schemas
            .GetProperty(nameof(LoginResponse))
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .Order()
            .ToArray();
        var userProperties = schemas
            .GetProperty(nameof(LoginUserResponse))
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .Order()
            .ToArray();

        Assert.Equal(["accessToken", "expiresAtUtc", "user"], loginProperties);
        Assert.Equal(
            ["email", "id", "profileName", "requiresPasswordChange"],
            userProperties);
        Assert.Equal(
            "bearer",
            securitySchemes.GetProperty("Bearer").GetProperty("scheme").GetString());
        Assert.False(
            paths.GetProperty("/auth/login")
                .GetProperty("post")
                .TryGetProperty("security", out _));
        Assert.Equal(
            "Bearer",
            paths.GetProperty("/access-records/open")
                .GetProperty("get")
                .GetProperty("security")[0]
                .EnumerateObject()
                .Single()
                .Name);
        Assert.False(paths.TryGetProperty("/weatherforecast", out _));
    }

    [Fact]
    public async Task DevelopmentOpenApiOperationsHaveStableDiscoveryMetadata()
    {
        using var developmentFactory = factory.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development"));
        using var client = developmentFactory.CreateClient();

        var response = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(responseContent);
        var operationIds = new HashSet<string>(StringComparer.Ordinal);
        var operationCount = 0;

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.Name is not ("get" or "post" or "put" or "delete" or "patch"))
                {
                    continue;
                }

                operationCount++;
                var metadata = operation.Value;
                var operationId = metadata.GetProperty("operationId").GetString();
                var summary = metadata.GetProperty("summary").GetString();
                var tags = metadata.GetProperty("tags");

                Assert.False(
                    string.IsNullOrWhiteSpace(operationId),
                    $"{operation.Name.ToUpperInvariant()} {path.Name} has no operationId.");
                Assert.True(
                    operationIds.Add(operationId),
                    $"The operationId '{operationId}' is duplicated.");
                Assert.False(
                    string.IsNullOrWhiteSpace(summary),
                    $"{operation.Name.ToUpperInvariant()} {path.Name} has no summary.");
                Assert.NotEmpty(tags.EnumerateArray());
            }
        }

        Assert.NotEqual(0, operationCount);
    }

    [Fact]
    public async Task DevelopmentSwaggerUiReferencesNativeOpenApiDocument()
    {
        using var developmentFactory = factory.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development"));
        using var client = developmentFactory.CreateClient();

        var response = await client.GetAsync(
            "/swagger/index.html",
            TestContext.Current.CancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        var initializerResponse = await client.GetAsync(
            "/swagger/index.js",
            TestContext.Current.CancellationToken);
        var initializerContent = await initializerResponse.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        initializerResponse.EnsureSuccessStatusCode();
        Assert.Contains("Controle de Acesso de Veículos — API", responseContent);
        Assert.Contains("/openapi/v1.json", initializerContent);
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/swagger/index.html")]
    public async Task InteractiveApiDocumentationIsUnavailableOutsideDevelopment(
        string endpoint)
    {
        var response = await _client.GetAsync(
            endpoint,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record HealthResponse(string Status, DateTime Timestamp);

}
