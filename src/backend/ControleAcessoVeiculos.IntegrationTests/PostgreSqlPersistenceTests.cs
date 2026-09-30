using ControleAcessoVeiculos.Domain.Entities;
using ControleAcessoVeiculos.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ControleAcessoVeiculos.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class PostgreSqlPersistenceTests(ApiFactory factory)
{
    [Fact]
    public async Task MigrationsCreateTheExpectedSchema()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();

        var migrations = await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("20260825222017_InitialCreate", migrations);
        Assert.Contains("20260826183028_AlignMvpDataModel", migrations);
        Assert.Contains("20260827232042_AddAuthenticationSecurity", migrations);
        Assert.Contains("20260829110009_AddInstitutionalDriverAuthorization", migrations);
        Assert.Contains("20260829154230_AddInstitutionalUsageHistoryIndexes", migrations);
        Assert.Contains("20260830065224_AllowSystemAuditActors", migrations);
        Assert.Contains("20260830202007_AddEventAuthorizationCatalog", migrations);
        Assert.Contains("20260830204741_LinkEventAuthorizationsToAccessRecords", migrations);
        Assert.Contains("20260913185634_AddSecureAuthenticationSessions", migrations);

        await dbContext.Database.OpenConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'dbo'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name;
            """;
        var tables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                tables.Add(reader.GetString(0));
            }
        }

        Assert.Equal(13, tables.Count);
        Assert.Contains("eventos_acesso", tables);
        Assert.Contains("autorizacoes_veiculos_eventos", tables);
        Assert.Contains("sessoes_autenticacao", tables);

        command.CommandText = """
            SELECT constraint_name
            FROM information_schema.table_constraints
            WHERE table_schema = 'dbo'
              AND table_name IN (
                  'eventos_acesso',
                  'autorizacoes_veiculos_eventos',
                  'sessoes_autenticacao')
            UNION ALL
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'dbo'
              AND tablename IN (
                  'autorizacoes_veiculos_eventos',
                  'sessoes_autenticacao');
            """;
        var constraints = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                constraints.Add(reader.GetString(0));
            }
        }

        Assert.Contains("ck_eventos_acesso_periodo", constraints);
        Assert.Contains("ck_autorizacoes_veiculos_eventos_quantidade", constraints);
        Assert.Contains("ux_autorizacoes_veiculos_eventos_evento_placa", constraints);
        Assert.Contains(
            "ux_autorizacoes_veiculos_eventos_evento_tipo_sem_placa",
            constraints);
        Assert.Contains("ck_sessoes_autenticacao_periodo", constraints);
        Assert.Contains("ck_sessoes_autenticacao_revogacao", constraints);
        Assert.Contains("ck_sessoes_autenticacao_rotacao", constraints);
        Assert.Contains("ck_sessoes_autenticacao_motivo_revogacao", constraints);
        Assert.Contains("ck_sessoes_autenticacao_token_hash", constraints);
        Assert.Contains("ck_sessoes_autenticacao_token_substituto_hash", constraints);
        Assert.Contains("fk_sessoes_autenticacao_usuarios_usuario_id", constraints);
        Assert.Contains("ux_sessoes_autenticacao_token_hash", constraints);

        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.table_constraints
            WHERE table_schema = 'dbo'
              AND table_name = 'registros_acesso'
              AND constraint_name = 'fk_registros_acesso_autorizacoes_eventos'
              AND constraint_type = 'FOREIGN KEY';
            """;
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task SystemAuditActorMigrationHasSafeUpgradeAndDowngrade()
    {
        using var scope = factory.Services.CreateScope();
        var sourceContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        var sourceConnectionString = sourceContext.Database.GetConnectionString()!;
        var databaseName = $"migration_{Guid.NewGuid():N}";
        var adminBuilder = new NpgsqlConnectionStringBuilder(sourceConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };

        await using (var adminConnection = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await adminConnection.OpenAsync(TestContext.Current.CancellationToken);
            await using var createCommand = adminConnection.CreateCommand();
            createCommand.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await createCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        try
        {
            var testBuilder = new NpgsqlConnectionStringBuilder(sourceConnectionString)
            {
                Database = databaseName,
                Pooling = false
            };
            var options = new DbContextOptionsBuilder<ControleAcessoVeiculosDbContext>()
                .UseNpgsql(testBuilder.ConnectionString)
                .Options;
            await using var dbContext = new ControleAcessoVeiculosDbContext(options);
            var migrator = dbContext.GetService<IMigrator>();

            await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(await IsAuditActorNullableAsync(dbContext));

            await dbContext.Database.ExecuteSqlRawAsync("""
                INSERT INTO dbo.auditorias
                    (tipo_acao, entidade, registro_id, usuario_id, detalhes)
                VALUES
                    ('Inclusao', 'Usuario', 1, NULL, 'Temporary system audit migration test.');
                """, cancellationToken: TestContext.Current.CancellationToken);

            var unsafeDowngrade = await Assert.ThrowsAnyAsync<Exception>(() =>
                migrator.MigrateAsync("20260829165235_AddGeneralAccessHistoryIndex", TestContext.Current.CancellationToken));
            Assert.Contains(
                "Cannot require an audit actor while system audit records exist.",
                unsafeDowngrade.ToString(),
                StringComparison.Ordinal);

            await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM dbo.auditorias WHERE usuario_id IS NULL", cancellationToken: TestContext.Current.CancellationToken);
            await migrator.MigrateAsync("20260829165235_AddGeneralAccessHistoryIndex", TestContext.Current.CancellationToken);
            Assert.False(await IsAuditActorNullableAsync(dbContext));

            await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(await IsAuditActorNullableAsync(dbContext));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var adminConnection = new NpgsqlConnection(adminBuilder.ConnectionString);
            await adminConnection.OpenAsync(TestContext.Current.CancellationToken);
            await using var dropCommand = adminConnection.CreateCommand();
            dropCommand.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
            await dropCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task DatabaseRejectsDuplicatePersonalDocument()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        var documentNumber = Guid.NewGuid().ToString("N")[..12];

        dbContext.Pessoas.Add(new Pessoa(
            "Pessoa de Teste A",
            "TESTE",
            documentNumber));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        dbContext.Pessoas.Add(new Pessoa(
            "Pessoa de Teste B",
            "TESTE",
            documentNumber));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PersonVehicleRelationshipIsPersisted()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ControleAcessoVeiculosDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var pessoa = new Pessoa($"Pessoa {suffix}");
        var veiculo = new Veiculo(
            $"T{suffix[..7]}",
            "Teste",
            null,
            false);

        dbContext.Pessoas.Add(pessoa);
        dbContext.Veiculos.Add(veiculo);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var relacao = new PessoaVeiculo(
            pessoa.Id,
            veiculo.Id,
            "Condutor",
            DateOnly.FromDateTime(DateTime.UtcNow));
        dbContext.PessoasVeiculos.Add(relacao);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        dbContext.ChangeTracker.Clear();

        var persisted = await dbContext.PessoasVeiculos.SingleAsync(item =>
            item.PessoaId == pessoa.Id && item.VeiculoId == veiculo.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Condutor", persisted.TipoRelacao);
        Assert.True(persisted.Ativo);
    }

    private static async Task<bool> IsAuditActorNullableAsync(
        ControleAcessoVeiculosDbContext dbContext)
    {
        await dbContext.Database.OpenConnectionAsync();
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'dbo'
              AND table_name = 'auditorias'
              AND column_name = 'usuario_id';
            """;

        return string.Equals(
            (string?)await command.ExecuteScalarAsync(),
            "YES",
            StringComparison.Ordinal);
    }
}
