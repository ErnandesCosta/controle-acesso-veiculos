using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ControleAcessoVeiculos.Application.AccessRecords;
using ControleAcessoVeiculos.Application.Authentication;
using ControleAcessoVeiculos.Application.Authorization;
using ControleAcessoVeiculos.Domain.Entities;
using ControleAcessoVeiculos.Domain.Enums;
using ControleAcessoVeiculos.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControleAcessoVeiculos.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class VehicleAccessTests(ApiFactory factory)
{
    [Fact]
    public async Task OperationalUserCanRegisterListAndCloseVehicleAccess()
    {
        const string password = "Test-only-password-123!";
        var (userId, email) = await CreateUserAsync(ProfileNames.Doorman, password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var suffix = Guid.NewGuid().ToString("N");
        var documentNumber = suffix[..11];
        var request = new
        {
            driverName = $"Condutor {suffix}",
            plate = "abc-1d23",
            objective = "Visita técnica",
            categoryName = AccessCategoryNames.Visitor,
            documentType = "CPF",
            documentNumber,
            vehicleType = "Automóvel",
            color = "Prata"
        };

        var entryResponse = await client.PostAsJsonAsync("/access-records/entries", request);
        var entry = await entryResponse.Content.ReadFromJsonAsync<AccessRecordResponse>();

        Assert.Equal(HttpStatusCode.Created, entryResponse.StatusCode);
        Assert.NotNull(entry);
        Assert.Equal("ABC1D23", entry.Plate);
        Assert.Equal("Aberto", entry.Status);
        Assert.Equal(userId, entry.CreatedById);

        var duplicateResponse = await client.PostAsJsonAsync(
            "/access-records/entries",
            request);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        var openRecords = await client.GetFromJsonAsync<List<AccessRecordResponse>>(
            "/access-records/open");
        Assert.Contains(openRecords!, item => item.Id == entry.Id);

        var exitResponse = await client.PostAsync(
            $"/access-records/{entry.Id}/exit",
            content: null);
        var closed = await exitResponse.Content.ReadFromJsonAsync<AccessRecordResponse>();

        Assert.Equal(HttpStatusCode.OK, exitResponse.StatusCode);
        Assert.NotNull(closed);
        Assert.Equal("Encerrado", closed.Status);
        Assert.NotNull(closed.ExitAtUtc);
        Assert.Equal(userId, closed.UpdatedById);

        var secondExit = await client.PostAsync(
            $"/access-records/{entry.Id}/exit",
            content: null);
        Assert.Equal(HttpStatusCode.Conflict, secondExit.StatusCode);

        var missingExit = await client.PostAsync(
            "/access-records/2147483647/exit",
            content: null);
        Assert.Equal(HttpStatusCode.NotFound, missingExit.StatusCode);

        openRecords = await client.GetFromJsonAsync<List<AccessRecordResponse>>(
            "/access-records/open");
        Assert.DoesNotContain(openRecords!, item => item.Id == entry.Id);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        Assert.Equal(1, await dbContext.Veiculos.CountAsync(item => item.Placa == "ABC1D23"));
        Assert.Equal(1, await dbContext.Pessoas.CountAsync(
            item => item.DocumentoTipo == "CPF" && item.DocumentoNumero == documentNumber));

        var audits = await dbContext.Auditorias
            .AsNoTracking()
            .Where(item => item.Entidade == nameof(RegistroAcesso) &&
                item.RegistroId == entry.Id)
            .OrderBy(item => item.Id)
            .ToListAsync();

        Assert.Collection(
            audits,
            audit =>
            {
                Assert.Equal(TipoAcaoAuditoria.Inclusao, audit.TipoAcao);
                Assert.Equal(userId, audit.UsuarioId);
                Assert.Null(audit.DadosAnteriores);
                AssertAuditState(audit.DadosNovos, "Aberto");
            },
            audit =>
            {
                Assert.Equal(TipoAcaoAuditoria.Alteracao, audit.TipoAcao);
                Assert.Equal(userId, audit.UsuarioId);
                AssertAuditState(audit.DadosAnteriores, "Aberto");
                AssertAuditState(audit.DadosNovos, "Encerrado");
            });

        var auditContent = string.Join(
            ' ',
            audits.SelectMany(item => new[]
            {
                item.DadosAnteriores,
                item.DadosNovos,
                item.Detalhes
            }).Where(item => item is not null));
        Assert.DoesNotContain(request.driverName, auditContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(documentNumber, auditContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ABC1D23", auditContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(request.objective, auditContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConcurrentExitAttemptsCloseAndAuditAccessOnlyOnce()
    {
        const string password = "Test-only-password-123!";
        var (userId, email) = await CreateUserAsync(ProfileNames.SecurityGuard, password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var suffix = Guid.NewGuid().ToString("N");
        var entryResponse = await client.PostAsJsonAsync("/access-records/entries", new
        {
            driverName = $"Condutor {suffix}",
            plate = suffix[..7],
            objective = "Entrega",
            categoryName = AccessCategoryNames.Delivery
        });
        var entry = await entryResponse.Content.ReadFromJsonAsync<AccessRecordResponse>();
        entryResponse.EnsureSuccessStatusCode();
        Assert.NotNull(entry);

        var responses = await Task.WhenAll(
            client.PostAsync($"/access-records/{entry.Id}/exit", content: null),
            client.PostAsync($"/access-records/{entry.Id}/exit", content: null));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        Assert.Equal(
            1,
            await dbContext.Auditorias.CountAsync(item =>
                item.Entidade == nameof(RegistroAcesso) &&
                item.RegistroId == entry.Id &&
                item.TipoAcao == TipoAcaoAuditoria.Alteracao &&
                item.UsuarioId == userId));
    }

    [Fact]
    public async Task AuditFailureRollsBackVehicleEntry()
    {
        const string password = "Test-only-password-123!";
        var (_, email) = await CreateUserAsync(ProfileNames.Doorman, password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var suffix = Guid.NewGuid().ToString("N");
        var plate = suffix[..7].ToUpperInvariant();

        await InstallRejectingAuditTriggerAsync();

        try
        {
            var response = await client.PostAsJsonAsync("/access-records/entries", new
            {
                driverName = $"Condutor {suffix}",
                plate,
                objective = "Visita",
                categoryName = AccessCategoryNames.Visitor
            });

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await RemoveRejectingAuditTriggerAsync();
        }

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        Assert.False(await dbContext.Veiculos.AnyAsync(item => item.Placa == plate));
    }

    [Fact]
    public async Task AuditFailureRollsBackVehicleExit()
    {
        const string password = "Test-only-password-123!";
        var (_, email) = await CreateUserAsync(ProfileNames.Doorman, password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var suffix = Guid.NewGuid().ToString("N");
        var entryResponse = await client.PostAsJsonAsync("/access-records/entries", new
        {
            driverName = $"Condutor {suffix}",
            plate = suffix[..7],
            objective = "Visita",
            categoryName = AccessCategoryNames.Visitor
        });
        var entry = await entryResponse.Content.ReadFromJsonAsync<AccessRecordResponse>();
        entryResponse.EnsureSuccessStatusCode();
        Assert.NotNull(entry);

        await InstallRejectingAuditTriggerAsync();

        try
        {
            var response = await client.PostAsync(
                $"/access-records/{entry.Id}/exit",
                content: null);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await RemoveRejectingAuditTriggerAsync();
        }

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var accessRecord = await dbContext.RegistrosAcesso
            .AsNoTracking()
            .SingleAsync(item => item.Id == entry.Id);
        Assert.Equal(StatusRegistroAcesso.Aberto, accessRecord.Status);
        Assert.Null(accessRecord.DataHoraSaida);
        Assert.Equal(
            1,
            await dbContext.Auditorias.CountAsync(item =>
                item.Entidade == nameof(RegistroAcesso) && item.RegistroId == entry.Id));
    }

    [Theory]
    [InlineData(ProfileNames.Doorman)]
    [InlineData(ProfileNames.SecurityGuard)]
    [InlineData(ProfileNames.TransportationDepartment)]
    [InlineData(ProfileNames.Administrator)]
    public async Task AuthorizedProfileCanCorrectClosedAccessWithoutRewritingHistory(
        string correctorProfile)
    {
        const string password = "Test-only-password-123!";
        var (creatorUserId, creatorEmail) = await CreateUserAsync(
            ProfileNames.Doorman,
            password);
        var (correctorUserId, correctorEmail) = await CreateUserAsync(
            correctorProfile,
            password);
        var suffix = Guid.NewGuid().ToString("N");
        var plate = suffix[..7].ToUpperInvariant();
        var driverName = $"Condutor {suffix}";
        using var creatorClient = factory.CreateClient();
        await AuthenticateClientAsync(creatorClient, creatorEmail, password);
        var entryResponse = await creatorClient.PostAsJsonAsync("/access-records/entries", new
        {
            driverName,
            plate,
            objective = "Visita inicial",
            categoryName = AccessCategoryNames.Visitor,
            observation = "Observação inicial"
        });
        var entry = await entryResponse.Content.ReadFromJsonAsync<AccessRecordResponse>();
        entryResponse.EnsureSuccessStatusCode();
        Assert.NotNull(entry);
        var exitResponse = await creatorClient.PostAsync(
            $"/access-records/{entry.Id}/exit",
            null);
        var closed = await exitResponse.Content.ReadFromJsonAsync<AccessRecordResponse>();
        exitResponse.EnsureSuccessStatusCode();
        Assert.NotNull(closed);

        const string justification = "Categoria e objetivo conferidos pelo operador.";
        using var correctorClient = factory.CreateClient();
        await AuthenticateClientAsync(correctorClient, correctorEmail, password);
        var correctionResponse = await correctorClient.PutAsJsonAsync(
            $"/access-records/{entry.Id}/correction",
            new
            {
                objective = "Entrega autorizada",
                categoryName = AccessCategoryNames.Delivery,
                observation = "Correção de teste",
                justification
            });
        var corrected = await correctionResponse.Content
            .ReadFromJsonAsync<AccessRecordResponse>();

        Assert.Equal(HttpStatusCode.OK, correctionResponse.StatusCode);
        Assert.NotNull(corrected);
        Assert.Equal(entry.Id, corrected.Id);
        Assert.Equal(entry.VehicleId, corrected.VehicleId);
        Assert.Equal(entry.PersonId, corrected.PersonId);
        Assert.Equal(entry.Plate, corrected.Plate);
        Assert.Equal(entry.DriverName, corrected.DriverName);
        Assert.Equal(entry.EntryAtUtc, corrected.EntryAtUtc, TimeSpan.FromMilliseconds(1));
        Assert.NotNull(closed.ExitAtUtc);
        Assert.NotNull(corrected.ExitAtUtc);
        Assert.Equal(
            closed.ExitAtUtc.GetValueOrDefault(),
            corrected.ExitAtUtc.GetValueOrDefault(),
            TimeSpan.FromMilliseconds(1));
        Assert.Equal("Encerrado", corrected.Status);
        Assert.Equal(creatorUserId, corrected.CreatedById);
        Assert.Equal(correctorUserId, corrected.UpdatedById);
        Assert.Equal("Entrega autorizada", corrected.Objective);
        Assert.Equal(AccessCategoryNames.Delivery, corrected.CategoryName);
        Assert.Equal("Correção de teste", corrected.Observation);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var correctionAudit = await dbContext.Auditorias
            .AsNoTracking()
            .SingleAsync(item => item.Entidade == nameof(RegistroAcesso) &&
                item.RegistroId == entry.Id && item.Detalhes == justification);
        Assert.Equal(TipoAcaoAuditoria.Alteracao, correctionAudit.TipoAcao);
        Assert.Equal(correctorUserId, correctionAudit.UsuarioId);
        Assert.Null(correctionAudit.DadosAnteriores);
        Assert.NotNull(correctionAudit.DadosNovos);
        using var auditJson = JsonDocument.Parse(correctionAudit.DadosNovos);
        var changedFields = auditJson.RootElement.GetProperty("changedFields")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => item is not null)
            .Cast<string>()
            .ToArray();
        Assert.Equal(
            new[] { "categoryName", "objective", "observation" },
            changedFields);
        Assert.DoesNotContain("Visita inicial", correctionAudit.DadosNovos,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Entrega autorizada", correctionAudit.DadosNovos,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Observação inicial", correctionAudit.DadosNovos,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Correção de teste", correctionAudit.DadosNovos,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccessCorrectionRequiresAuthentication()
    {
        var request = new
        {
            objective = "Objetivo corrigido",
            categoryName = AccessCategoryNames.Visitor,
            observation = (string?)null,
            justification = "Justificativa válida para o teste."
        };
        using var anonymousClient = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymousClient.PutAsJsonAsync(
                "/access-records/1/correction", request)).StatusCode);
    }

    [Fact]
    public async Task AccessCorrectionRejectsInvalidMissingAndUnchangedRecords()
    {
        const string password = "Test-only-password-123!";
        var (_, email) = await CreateUserAsync(ProfileNames.Administrator, password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var suffix = Guid.NewGuid().ToString("N");
        var entryResponse = await client.PostAsJsonAsync("/access-records/entries", new
        {
            driverName = $"Condutor {suffix}",
            plate = suffix[..7],
            objective = "Visita técnica",
            categoryName = AccessCategoryNames.Visitor
        });
        var entry = await entryResponse.Content.ReadFromJsonAsync<AccessRecordResponse>();
        entryResponse.EnsureSuccessStatusCode();
        Assert.NotNull(entry);

        var unchanged = await client.PutAsJsonAsync(
            $"/access-records/{entry.Id}/correction",
            new
            {
                objective = "Visita técnica",
                categoryName = AccessCategoryNames.Visitor,
                observation = (string?)null,
                justification = "Dados conferidos sem alteração efetiva."
            });
        Assert.Equal(HttpStatusCode.Conflict, unchanged.StatusCode);

        var invalid = await client.PutAsJsonAsync(
            $"/access-records/{entry.Id}/correction",
            new
            {
                objective = "",
                categoryName = "Desconhecida",
                observation = new string('x', 1001),
                justification = "curta"
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var missing = await client.PutAsJsonAsync(
            "/access-records/2147483647/correction",
            new
            {
                objective = "Entrega autorizada",
                categoryName = AccessCategoryNames.Delivery,
                observation = (string?)null,
                justification = "Correção válida para registro inexistente."
            });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task AuditFailureRollsBackAccessCorrection()
    {
        const string password = "Test-only-password-123!";
        var (_, email) = await CreateUserAsync(ProfileNames.SecurityGuard, password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var suffix = Guid.NewGuid().ToString("N");
        var entryResponse = await client.PostAsJsonAsync("/access-records/entries", new
        {
            driverName = $"Condutor {suffix}",
            plate = suffix[..7],
            objective = "Visita original",
            categoryName = AccessCategoryNames.Visitor,
            observation = "Observação original"
        });
        var entry = await entryResponse.Content.ReadFromJsonAsync<AccessRecordResponse>();
        entryResponse.EnsureSuccessStatusCode();
        Assert.NotNull(entry);

        await InstallRejectingAuditTriggerAsync();
        try
        {
            var response = await client.PutAsJsonAsync(
                $"/access-records/{entry.Id}/correction",
                new
                {
                    objective = "Entrega corrigida",
                    categoryName = AccessCategoryNames.Delivery,
                    observation = "Observação corrigida",
                    justification = "Correção que deve ser revertida no teste."
                });
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await RemoveRejectingAuditTriggerAsync();
        }

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var record = await dbContext.RegistrosAcesso
            .AsNoTracking()
            .SingleAsync(item => item.Id == entry.Id);
        var category = await dbContext.CategoriasAcesso
            .AsNoTracking()
            .SingleAsync(item => item.Id == record.CategoriaAcessoId);
        Assert.Equal("Visita original", record.Objetivo);
        Assert.Equal("Observação original", record.Observacao);
        Assert.Equal(AccessCategoryNames.Visitor, category.Nome);
        Assert.Equal(1, await dbContext.Auditorias.CountAsync(item =>
            item.Entidade == nameof(RegistroAcesso) && item.RegistroId == entry.Id));
    }

    [Fact]
    public async Task GeneralAccessHistoryEnforcesDedicatedReviewPermission()
    {
        const string password = "Test-only-password-123!";
        using var anonymousClient = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymousClient.GetAsync("/access-records/history")).StatusCode);

        var (_, transportationEmail) = await CreateUserAsync(
            ProfileNames.TransportationDepartment,
            password);
        using var transportationClient = factory.CreateClient();
        await AuthenticateClientAsync(transportationClient, transportationEmail, password);
        Assert.Equal(
            HttpStatusCode.OK,
            (await transportationClient.GetAsync("/access-records/history")).StatusCode);

        var (_, doormanEmail) = await CreateUserAsync(ProfileNames.Doorman, password);
        using var doormanClient = factory.CreateClient();
        await AuthenticateClientAsync(doormanClient, doormanEmail, password);
        Assert.Equal(
            HttpStatusCode.OK,
            (await doormanClient.GetAsync("/access-records/history")).StatusCode);
    }

    [Fact]
    public async Task TransportationProfileCannotOperateGeneralAccess()
    {
        const string password = "Test-only-password-123!";
        var (_, email) = await CreateUserAsync(
            ProfileNames.TransportationDepartment,
            password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);

        var entry = await client.PostAsJsonAsync("/access-records/entries", new
        {
            driverName = "Condutor não autorizado",
            plate = "TST1A23",
            objective = "Operação não autorizada",
            categoryName = AccessCategoryNames.Visitor
        });
        var exit = await client.PostAsync("/access-records/1/exit", null);

        Assert.Equal(HttpStatusCode.Forbidden, entry.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, exit.StatusCode);
    }

    [Fact]
    public async Task OperationalUserCanFilterOrderAndPaginateGeneralAccessHistory()
    {
        const string password = "Test-only-password-123!";
        var (_, email) = await CreateUserAsync(ProfileNames.SecurityGuard, password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var suffix = Guid.NewGuid().ToString("N");
        var plate = suffix[..7].ToUpperInvariant();
        var driverName = $"Condutor Histórico {suffix}";
        var accessIds = new List<int>();

        for (var index = 0; index < 3; index++)
        {
            var entryResponse = await client.PostAsJsonAsync("/access-records/entries", new
            {
                driverName,
                plate,
                objective = "Visita técnica",
                categoryName = AccessCategoryNames.Visitor
            });
            var entry = await entryResponse.Content.ReadFromJsonAsync<AccessRecordResponse>();
            entryResponse.EnsureSuccessStatusCode();
            Assert.NotNull(entry);
            accessIds.Add(entry.Id);

            if (index < 2)
            {
                (await client.PostAsync($"/access-records/{entry.Id}/exit", null))
                    .EnsureSuccessStatusCode();
            }
        }

        var formattedPlate = plate.Insert(3, "-").ToLowerInvariant();
        var driverFilter = Uri.EscapeDataString($"histórico {suffix[..8]}".ToUpperInvariant());
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var response = await client.GetAsync(
            $"/access-records/history?plate={formattedPlate}&driverName={driverFilter}" +
            $"&categoryName=visitante&from={from}&to={to}&page=1&pageSize=2");
        var result = await response.Content.ReadFromJsonAsync<AccessHistoryResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(accessIds[2], result.Items[0].Id);
        Assert.Equal(accessIds[1], result.Items[1].Id);
        Assert.All(result.Items, item =>
        {
            Assert.Equal(plate, item.Plate);
            Assert.Equal(driverName, item.DriverName);
            Assert.Equal(AccessCategoryNames.Visitor, item.CategoryName);
        });

        var closed = await client.GetFromJsonAsync<AccessHistoryResponse>(
            $"/access-records/history?plate={formattedPlate}&status=encerrado" +
            $"&from={from}&to={to}&page=2&pageSize=1");
        Assert.NotNull(closed);
        Assert.Equal(2, closed.TotalCount);
        Assert.Equal(2, closed.TotalPages);
        Assert.Single(closed.Items);
        Assert.Equal("Encerrado", closed.Items[0].Status);

        var invalid = await client.GetAsync(
            $"/access-records/history?from={to}&to={from}&status=999&pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task AnonymousUserCannotOperateVehicleAccess()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/access-records/open");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(ProfileNames.Doorman)]
    [InlineData(ProfileNames.SecurityGuard)]
    [InlineData(ProfileNames.Administrator)]
    public async Task AuthorizedProfileCanExceptionallyCloseAccessWithoutInventingExitTime(
        string profileName)
    {
        const string password = "Test-only-password-123!";
        var (userId, email) = await CreateUserAsync(profileName, password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var suffix = Guid.NewGuid().ToString("N");
        var entryResponse = await client.PostAsJsonAsync("/access-records/entries", new
        {
            driverName = $"Condutor {suffix}",
            plate = suffix[..7],
            objective = "Visita técnica",
            categoryName = AccessCategoryNames.Visitor
        });
        var entry = await entryResponse.Content.ReadFromJsonAsync<AccessRecordResponse>();
        entryResponse.EnsureSuccessStatusCode();
        Assert.NotNull(entry);

        var closureResponse = await client.PostAsJsonAsync(
            $"/access-records/{entry.Id}/exceptional-closure",
            new
            {
                reason = "RegistroDeSaidaOmitido",
                observation = "Saída confirmada posteriormente pelo responsável operacional."
            });
        var closed = await closureResponse.Content.ReadFromJsonAsync<AccessRecordResponse>();

        Assert.Equal(HttpStatusCode.OK, closureResponse.StatusCode);
        Assert.NotNull(closed);
        Assert.Equal("Encerrado", closed.Status);
        Assert.Equal("Excepcional", closed.ClosureType);
        Assert.Equal("RegistroDeSaidaOmitido", closed.ExceptionalClosureReason);
        Assert.Null(closed.ExitAtUtc);
        Assert.NotNull(closed.RegularizedAtUtc);
        Assert.Equal(userId, closed.UpdatedById);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var audit = await dbContext.Auditorias
            .AsNoTracking()
            .SingleAsync(item => item.Entidade == nameof(RegistroAcesso) &&
                item.RegistroId == entry.Id &&
                item.Detalhes == "Vehicle access exceptionally closed.");
        Assert.Equal(userId, audit.UsuarioId);
        AssertAuditState(audit.DadosAnteriores, "Aberto");
        AssertAuditState(audit.DadosNovos, "Encerrado");
        Assert.DoesNotContain(
            "Saída confirmada",
            audit.DadosNovos,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExceptionalClosureEnforcesPermissionAndValidation()
    {
        const string password = "Test-only-password-123!";
        var request = new
        {
            reason = "RegistroDeSaidaOmitido",
            observation = "Saída confirmada posteriormente pelo responsável operacional."
        };
        using var anonymousClient = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymousClient.PostAsJsonAsync(
                "/access-records/1/exceptional-closure", request)).StatusCode);

        var (_, transportationEmail) = await CreateUserAsync(
            ProfileNames.TransportationDepartment,
            password);
        using var transportationClient = factory.CreateClient();
        await AuthenticateClientAsync(transportationClient, transportationEmail, password);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await transportationClient.PostAsJsonAsync(
                "/access-records/1/exceptional-closure", request)).StatusCode);

        var (_, administratorEmail) = await CreateUserAsync(
            ProfileNames.Administrator,
            password);
        using var administratorClient = factory.CreateClient();
        await AuthenticateClientAsync(administratorClient, administratorEmail, password);
        var invalid = await administratorClient.PostAsJsonAsync(
            "/access-records/1/exceptional-closure",
            new
            {
                reason = "Desconhecido",
                observation = "curta",
                observedExitAtUtc = DateTimeOffset.UtcNow.AddHours(1)
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task ConcurrentExceptionalClosureAttemptsPersistOnlyOnce()
    {
        const string password = "Test-only-password-123!";
        var (userId, email) = await CreateUserAsync(ProfileNames.SecurityGuard, password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var suffix = Guid.NewGuid().ToString("N");
        var entryResponse = await client.PostAsJsonAsync("/access-records/entries", new
        {
            driverName = $"Condutor {suffix}",
            plate = suffix[..7],
            objective = "Entrega",
            categoryName = AccessCategoryNames.Delivery
        });
        var entry = await entryResponse.Content.ReadFromJsonAsync<AccessRecordResponse>();
        entryResponse.EnsureSuccessStatusCode();
        Assert.NotNull(entry);
        var request = new
        {
            reason = "RegistroDeSaidaOmitido",
            observation = "Saída confirmada posteriormente pelo responsável operacional."
        };

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync($"/access-records/{entry.Id}/exceptional-closure", request),
            client.PostAsJsonAsync($"/access-records/{entry.Id}/exceptional-closure", request));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        Assert.Equal(1, await dbContext.Auditorias.CountAsync(item =>
            item.Entidade == nameof(RegistroAcesso) &&
            item.RegistroId == entry.Id &&
            item.UsuarioId == userId &&
            item.Detalhes == "Vehicle access exceptionally closed."));
    }

    [Fact]
    public async Task AuditFailureRollsBackExceptionalClosure()
    {
        const string password = "Test-only-password-123!";
        var (_, email) = await CreateUserAsync(ProfileNames.Doorman, password);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email, password);
        var suffix = Guid.NewGuid().ToString("N");
        var entryResponse = await client.PostAsJsonAsync("/access-records/entries", new
        {
            driverName = $"Condutor {suffix}",
            plate = suffix[..7],
            objective = "Visita técnica",
            categoryName = AccessCategoryNames.Visitor
        });
        var entry = await entryResponse.Content.ReadFromJsonAsync<AccessRecordResponse>();
        entryResponse.EnsureSuccessStatusCode();
        Assert.NotNull(entry);

        await InstallRejectingAuditTriggerAsync();
        try
        {
            var response = await client.PostAsJsonAsync(
                $"/access-records/{entry.Id}/exceptional-closure",
                new
                {
                    reason = "OperacaoEmContingencia",
                    observation = "Registro regularizado após operação em contingência."
                });
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await RemoveRejectingAuditTriggerAsync();
        }

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var record = await dbContext.RegistrosAcesso
            .AsNoTracking()
            .SingleAsync(item => item.Id == entry.Id);
        Assert.Equal(StatusRegistroAcesso.Aberto, record.Status);
        Assert.Null(record.TipoEncerramento);
        Assert.Null(record.DataHoraRegularizacao);
        Assert.Equal(1, await dbContext.Auditorias.CountAsync(item =>
            item.Entidade == nameof(RegistroAcesso) && item.RegistroId == entry.Id));
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

        var email = $"access-{suffix}@example.test";
        var user = new Usuario(email, passwordHasher.Hash(password), person.Id, profile.Id);
        dbContext.Usuarios.Add(user);
        await dbContext.SaveChangesAsync();

        return (user.Id, email);
    }

    private async Task InstallRejectingAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE OR REPLACE FUNCTION dbo.reject_vehicle_access_audit()
            RETURNS trigger AS $function$
            BEGIN
                RAISE EXCEPTION 'forced integration test audit failure';
            END;
            $function$ LANGUAGE plpgsql;

            CREATE TRIGGER reject_vehicle_access_audit
            BEFORE INSERT ON dbo.auditorias
            FOR EACH ROW
            WHEN (NEW.entidade = 'RegistroAcesso')
            EXECUTE FUNCTION dbo.reject_vehicle_access_audit();
            """);
    }

    private async Task RemoveRejectingAuditTriggerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER IF EXISTS reject_vehicle_access_audit ON dbo.auditorias;
            DROP FUNCTION IF EXISTS dbo.reject_vehicle_access_audit();
            """);
    }

    private static void AssertAuditState(string? json, string expectedStatus)
    {
        Assert.NotNull(json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expectedStatus, document.RootElement.GetProperty("status").GetString());
    }

    private sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc);

    private sealed record AccessRecordResponse(
        int Id,
        int VehicleId,
        string Plate,
        int PersonId,
        string? DriverName,
        string? CategoryName,
        string? Objective,
        string Status,
        DateTime EntryAtUtc,
        DateTime? ExitAtUtc,
        int CreatedById,
        int? UpdatedById,
        string? Observation,
        string? ClosureType = null,
        string? ExceptionalClosureReason = null,
        string? ExceptionalClosureObservation = null,
        DateTime? RegularizedAtUtc = null);

    private sealed record AccessHistoryResponse(
        List<AccessRecordResponse> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages);
}
