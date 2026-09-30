using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
public sealed class AccessEntryCandidateTests(ApiFactory factory)
{
    private const string Password = "Test-only-password-123!";

    [Fact]
    public async Task CandidateSearchEnforcesAuthenticationAuthorizationAndValidation()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/access-records/entry-candidates?query=ABC", TestContext.Current.CancellationToken))
                .StatusCode);

        var transportationEmail = await CreateUserAsync(
            ProfileNames.TransportationDepartment);
        using var transportation = factory.CreateClient();
        await AuthenticateClientAsync(transportation, transportationEmail);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await transportation.GetAsync("/access-records/entry-candidates?query=ABC", TestContext.Current.CancellationToken)).StatusCode);

        var doormanEmail = await CreateUserAsync(ProfileNames.Doorman);
        using var doorman = factory.CreateClient();
        await AuthenticateClientAsync(doorman, doormanEmail);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await doorman.GetAsync("/access-records/entry-candidates?query=AB", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task SearchReturnsOnlyEligibleMinimalCandidatesAndSelectionUsesCanonicalData()
    {
        var email = await CreateUserAsync(ProfileNames.Doorman);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email);
        var suffix = Guid.NewGuid().ToString("N");
        var eligible = await CreateCandidateAsync(
            $"Condutor recorrente {suffix}",
            suffix[..7],
            $"DOC{suffix[..12]}",
            $"candidate-{suffix}@example.test");
        var institutional = await CreateCandidateAsync(
            $"Condutor recorrente {suffix}",
            suffix[7..14],
            $"INS{suffix[..12]}",
            $"institutional-{suffix}@example.test",
            isInstitutional: true);
        await CreateCandidateAsync(
            $"Condutor recorrente {suffix}",
            suffix[14..21],
            $"OLD{suffix[..12]}",
            $"expired-{suffix}@example.test",
            relationshipEnd: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        var formattedPlate = $"{eligible.Plate[..3]}-{eligible.Plate[3..]}".ToLowerInvariant();
        var plateResponse = await client.GetAsync($"/access-records/entry-candidates?query={formattedPlate}", TestContext.Current.CancellationToken);
        var rawResponse = await plateResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var plateCandidates = await plateResponse.Content
            .ReadFromJsonAsync<List<CandidateResponse>>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, plateResponse.StatusCode);
        var plateCandidate = Assert.Single(plateCandidates!);
        Assert.Equal(eligible.VehicleId, plateCandidate.VehicleId);
        Assert.Equal(eligible.PersonId, plateCandidate.PersonId);
        Assert.Equal(eligible.Plate, plateCandidate.Plate);
        Assert.DoesNotContain(eligible.DocumentNumber, rawResponse, StringComparison.Ordinal);
        Assert.DoesNotContain(eligible.Email!, rawResponse, StringComparison.OrdinalIgnoreCase);

        var nameResponse = await client.GetFromJsonAsync<List<CandidateResponse>>($"/access-records/entry-candidates?query={suffix}", cancellationToken: TestContext.Current.CancellationToken);
        var nameCandidate = Assert.Single(nameResponse!);
        Assert.Equal(eligible.VehicleId, nameCandidate.VehicleId);

        var entryResponse = await client.PostAsJsonAsync("/access-records/entries", new
        {
            driverName = "Nome adulterado pelo cliente",
            plate = "ZZZ9Z99",
            objective = "Atendimento",
            categoryName = AccessCategoryNames.Visitor,
            vehicleType = "Caminhão",
            vehicleId = eligible.VehicleId,
            personId = eligible.PersonId
        }, cancellationToken: TestContext.Current.CancellationToken);
        var entry = await entryResponse.Content.ReadFromJsonAsync<AccessRecordResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, entryResponse.StatusCode);
        Assert.NotNull(entry);
        Assert.Equal(eligible.VehicleId, entry.VehicleId);
        Assert.Equal(eligible.PersonId, entry.PersonId);
        Assert.Equal(eligible.Plate, entry.Plate);
        Assert.Equal(eligible.DriverName, entry.DriverName);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        Assert.False(await dbContext.Veiculos.AnyAsync(item => item.Placa == "ZZZ9Z99", cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(await dbContext.Pessoas.AnyAsync(item => item.Nome == "Nome adulterado pelo cliente", cancellationToken: TestContext.Current.CancellationToken));

        var incompatibleResponse = await client.PostAsJsonAsync("/access-records/entries", new
        {
            driverName = eligible.DriverName,
            plate = eligible.Plate,
            objective = "Atendimento",
            categoryName = AccessCategoryNames.Visitor,
            vehicleId = eligible.VehicleId,
            personId = institutional.PersonId
        }, cancellationToken: TestContext.Current.CancellationToken);
        var incompatibleBody = await incompatibleResponse.Content
            .ReadFromJsonAsync<ConflictResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, incompatibleResponse.StatusCode);
        Assert.NotNull(incompatibleBody);
        Assert.Equal(
            "O veículo ou condutor selecionado não está mais disponível.",
            Assert.Single(incompatibleBody.Errors["accessRecord"]));
    }

    [Fact]
    public async Task CandidateSearchLimitsEveryResponseToTenItems()
    {
        var email = await CreateUserAsync(ProfileNames.SecurityGuard);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email);
        var suffix = Guid.NewGuid().ToString("N")[..10];

        for (var index = 0; index < 11; index++)
        {
            await CreateCandidateAsync(
                $"Busca limitada {suffix} {index}",
                $"{suffix[..4]}{index:D3}",
                $"LIM{suffix}{index:D2}",
                null);
        }

        var candidates = await client.GetFromJsonAsync<List<CandidateResponse>>($"/access-records/entry-candidates?query={suffix}", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(candidates);
        Assert.Equal(10, candidates.Count);
    }

    [Fact]
    public async Task ConcurrentEntriesForSelectedCandidateCreateOnlyOneOpenAccess()
    {
        var email = await CreateUserAsync(ProfileNames.Doorman);
        using var client = factory.CreateClient();
        await AuthenticateClientAsync(client, email);
        var suffix = Guid.NewGuid().ToString("N");
        var candidate = await CreateCandidateAsync(
            $"Condutor concorrente {suffix}",
            suffix[..7],
            $"CON{suffix[..12]}",
            null);
        var request = new
        {
            driverName = candidate.DriverName,
            plate = candidate.Plate,
            objective = "Atendimento",
            categoryName = AccessCategoryNames.Visitor,
            vehicleId = candidate.VehicleId,
            personId = candidate.PersonId
        };

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync(
                "/access-records/entries",
                request,
                TestContext.Current.CancellationToken),
            client.PostAsJsonAsync(
                "/access-records/entries",
                request,
                TestContext.Current.CancellationToken));

        Assert.Single(responses, item => item.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, item => item.StatusCode == HttpStatusCode.Conflict);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        Assert.Equal(
            1,
            await dbContext.RegistrosAcesso.CountAsync(item =>
                item.VeiculoId == candidate.VehicleId &&
                item.DataHoraSaida == null, cancellationToken: TestContext.Current.CancellationToken));
    }

    private async Task<string> CreateUserAsync(string profileName)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHashService>();
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

        var person = new Pessoa($"Pessoa de teste {suffix}");
        dbContext.Pessoas.Add(person);
        await dbContext.SaveChangesAsync();
        var email = $"candidate-user-{suffix}@example.test";
        dbContext.Usuarios.Add(new Usuario(
            email,
            passwordHasher.Hash(Password),
            person.Id,
            profile.Id));
        await dbContext.SaveChangesAsync();
        return email;
    }

    private async Task<CandidateSeed> CreateCandidateAsync(
        string driverName,
        string plate,
        string documentNumber,
        string? email,
        bool isInstitutional = false,
        DateOnly? relationshipEnd = null)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControleAcessoVeiculosDbContext>();
        var person = new Pessoa(driverName, "ID", documentNumber, email: email);
        var vehicle = new Veiculo(
            plate,
            "Automóvel",
            identificacaoVeiculo: null,
            ehInstitucional: isInstitutional,
            marca: "Marca fictícia",
            modelo: "Modelo fictício",
            cor: "Prata");
        dbContext.AddRange(person, vehicle);
        await dbContext.SaveChangesAsync();
        dbContext.PessoasVeiculos.Add(new PessoaVeiculo(
            person.Id,
            vehicle.Id,
            "Condutor",
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30),
            relationshipEnd));
        await dbContext.SaveChangesAsync();

        return new CandidateSeed(
            vehicle.Id,
            person.Id,
            vehicle.Placa!,
            person.Nome,
            documentNumber,
            email);
    }

    private static async Task AuthenticateClientAsync(HttpClient client, string email)
    {
        var login = await client.PostAsJsonAsync(
            "/auth/login",
            new { email, password = Password });
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        login.EnsureSuccessStatusCode();
        Assert.NotNull(body);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.AccessToken);
    }

    private sealed record CandidateSeed(
        int VehicleId,
        int PersonId,
        string Plate,
        string DriverName,
        string DocumentNumber,
        string? Email);

    private sealed record CandidateResponse(
        int VehicleId,
        int PersonId,
        string Plate,
        string DriverName,
        string? VehicleType,
        string? Brand,
        string? Model,
        string? Color);

    private sealed record AccessRecordResponse(
        int Id,
        int VehicleId,
        string Plate,
        int PersonId,
        string DriverName);

    private sealed record ConflictResponse(Dictionary<string, string[]> Errors);

    private sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc);
}
