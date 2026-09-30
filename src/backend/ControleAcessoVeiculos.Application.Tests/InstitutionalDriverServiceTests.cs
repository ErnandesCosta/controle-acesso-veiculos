using ControleAcessoVeiculos.Application.InstitutionalDrivers;

namespace ControleAcessoVeiculos.Application.Tests;

public sealed class InstitutionalDriverServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 29, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task AuthorizeAsync_ShouldValidateBeforeCallingStore()
    {
        var store = new FakeStore();
        var service = new InstitutionalDriverService(
            store,
            new FixedTimeProvider(FixedNow));

        var result = await service.AuthorizeAsync(new AuthorizeInstitutionalDriverCommand("", "CPF", null), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(AuthorizeInstitutionalDriverStatus.Invalid, result.Status);
        Assert.Contains("name", result.Errors.Keys);
        Assert.Contains("document", result.Errors.Keys);
        Assert.Equal(0, store.AuthorizeCalls);
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldNormalizeDataAndUseServerTime()
    {
        var store = new FakeStore();
        var service = new InstitutionalDriverService(
            store,
            new FixedTimeProvider(FixedNow));

        var result = await service.AuthorizeAsync(new AuthorizeInstitutionalDriverCommand(
                " Motorista de Teste ",
                " cpf ",
                " 12345678900 "), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(AuthorizeInstitutionalDriverStatus.Success, result.Status);
        Assert.NotNull(store.LastDriver);
        Assert.Equal("Motorista de Teste", store.LastDriver.Name);
        Assert.Equal("CPF", store.LastDriver.DocumentType);
        Assert.Equal("12345678900", store.LastDriver.DocumentNumber);
        Assert.Equal(7, store.LastActorUserId);
        Assert.Equal(FixedNow.UtcDateTime, store.LastOccurredAtUtc);
    }

    [Fact]
    public async Task DeactivateAsync_ShouldUseServerTimeAndActor()
    {
        var store = new FakeStore();
        var service = new InstitutionalDriverService(
            store,
            new FixedTimeProvider(FixedNow));

        var result = await service.DeactivateAsync(10, 7, TestContext.Current.CancellationToken);

        Assert.Equal(DeactivateInstitutionalDriverStatus.Success, result.Status);
        Assert.Equal(10, store.LastDeactivatedDriverId);
        Assert.Equal(7, store.LastActorUserId);
        Assert.Equal(FixedNow.UtcDateTime, store.LastOccurredAtUtc);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStore : IInstitutionalDriverStore
    {
        public int AuthorizeCalls { get; private set; }
        public InstitutionalDriverData? LastDriver { get; private set; }
        public int LastDeactivatedDriverId { get; private set; }
        public int LastActorUserId { get; private set; }
        public DateTime LastOccurredAtUtc { get; private set; }

        public Task<InstitutionalDriverStoreAuthorization> TryAuthorizeAsync(
            InstitutionalDriverData driver,
            int actorUserId,
            DateTime occurredAtUtc,
            CancellationToken cancellationToken)
        {
            AuthorizeCalls++;
            LastDriver = driver;
            LastActorUserId = actorUserId;
            LastOccurredAtUtc = occurredAtUtc;

            return Task.FromResult(new InstitutionalDriverStoreAuthorization(
                InstitutionalDriverStoreAuthorizationStatus.Success,
                new InstitutionalDriverRecord(
                    1,
                    2,
                    driver.Name,
                    occurredAtUtc,
                    actorUserId,
                    null,
                    null)));
        }

        public Task<IReadOnlyList<InstitutionalDriverRecord>> ListActiveAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstitutionalDriverRecord>>([]);

        public Task<DeactivateInstitutionalDriverResult> TryDeactivateAsync(
            int driverId,
            int actorUserId,
            DateTime occurredAtUtc,
            CancellationToken cancellationToken)
        {
            LastDeactivatedDriverId = driverId;
            LastActorUserId = actorUserId;
            LastOccurredAtUtc = occurredAtUtc;

            return Task.FromResult(new DeactivateInstitutionalDriverResult(
                DeactivateInstitutionalDriverStatus.Success));
        }
    }
}
