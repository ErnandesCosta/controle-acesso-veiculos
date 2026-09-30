using ControleAcessoVeiculos.Infrastructure.Data;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace ControleAcessoVeiculos.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("controle_acesso_tests")
        .WithUsername("integration_tests")
        .WithPassword("integration-tests-only")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted(
            "pg_isready",
            "-U",
            "integration_tests",
            "-d",
            "controle_acesso_tests"))
        .Build();

    public RequestLogCaptureProvider RequestLogs { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();

        await dbContext.Database.MigrateAsync();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        Dispose();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            "ConnectionStrings:DefaultConnection",
            _postgres.GetConnectionString());
        builder.UseSetting(
            "Authentication:Jwt:SigningKey",
            "integration-tests-only-signing-key-32-characters");
        builder.UseSetting("RateLimiting:GlobalPermitLimit", "10000");
        builder.UseSetting("RateLimiting:LoginPermitLimit", "10000");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(RequestLogs);
            services.AddTransient<IStartupFilter, FailureEndpointStartupFilter>();
        });
    }

    private sealed class FailureEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            application =>
            {
                next(application);
                application.Map("/__tests/unhandled-error", branch =>
                    branch.Run(_ => throw new InvalidOperationException(
                        "sensitive-database-password")));
            };
    }
}
