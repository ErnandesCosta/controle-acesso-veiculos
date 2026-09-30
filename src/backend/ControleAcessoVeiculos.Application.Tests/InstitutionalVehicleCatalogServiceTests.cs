using ControleAcessoVeiculos.Application.InstitutionalVehicles;

namespace ControleAcessoVeiculos.Application.Tests;

public sealed class InstitutionalVehicleCatalogServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 29, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_ShouldValidateBeforeCallingStore()
    {
        var store = new FakeStore();
        var service = new InstitutionalVehicleCatalogService(
            store,
            new FixedTimeProvider(FixedNow));

        var result = await service.CreateAsync(new CreateInstitutionalVehicleCommand(
                Plate: "---",
                Identification: null,
                VehicleType: new string('x', 51),
                Year: 2028), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CreateInstitutionalVehicleStatus.Invalid, result.Status);
        Assert.Contains("plate", result.Errors.Keys);
        Assert.Contains("vehicleType", result.Errors.Keys);
        Assert.Contains("year", result.Errors.Keys);
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task CreateAsync_ShouldNormalizeDataAndUseServerTime()
    {
        var store = new FakeStore();
        var service = new InstitutionalVehicleCatalogService(
            store,
            new FixedTimeProvider(FixedNow));

        var result = await service.CreateAsync(new CreateInstitutionalVehicleCommand(
                Plate: " abc-1d23 ",
                Identification: " patrimônio 001 ",
                VehicleType: " Automóvel ",
                Brand: " Marca Fictícia "), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CreateInstitutionalVehicleStatus.Success, result.Status);
        Assert.Equal(1, store.CreateCalls);
        Assert.NotNull(store.LastVehicle);
        Assert.Equal("ABC1D23", store.LastVehicle.Plate);
        Assert.Equal("PATRIMÔNIO 001", store.LastVehicle.Identification);
        Assert.Equal("Automóvel", store.LastVehicle.VehicleType);
        Assert.Equal("Marca Fictícia", store.LastVehicle.Brand);
        Assert.Equal(7, store.LastActorUserId);
        Assert.Equal(FixedNow.UtcDateTime, store.LastCreatedAtUtc);
    }

    [Fact]
    public async Task UpdateAsync_ShouldValidateBeforeCallingStore()
    {
        var store = new FakeStore();
        var service = new InstitutionalVehicleCatalogService(
            store,
            new FixedTimeProvider(FixedNow));

        var result = await service.UpdateAsync(0, new UpdateInstitutionalVehicleCommand(null, null, Year: 2028), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(UpdateInstitutionalVehicleStatus.Invalid, result.Status);
        Assert.Contains("vehicleId", result.Errors.Keys);
        Assert.Contains("identification", result.Errors.Keys);
        Assert.Contains("year", result.Errors.Keys);
        Assert.Equal(0, store.UpdateCalls);
    }

    [Fact]
    public async Task UpdateAsync_ShouldNormalizeDataAndUseServerTime()
    {
        var store = new FakeStore();
        var service = new InstitutionalVehicleCatalogService(
            store,
            new FixedTimeProvider(FixedNow));

        var result = await service.UpdateAsync(10, new UpdateInstitutionalVehicleCommand(
                " abc-1d23 ",
                " frota-01 ",
                " Van ",
                " Marca "), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(UpdateInstitutionalVehicleStatus.Success, result.Status);
        Assert.Equal(1, store.UpdateCalls);
        Assert.NotNull(store.LastVehicle);
        Assert.Equal("ABC1D23", store.LastVehicle.Plate);
        Assert.Equal("FROTA-01", store.LastVehicle.Identification);
        Assert.Equal(FixedNow.UtcDateTime, store.LastUpdatedAtUtc);
        Assert.Equal(7, store.LastActorUserId);
    }

    [Fact]
    public async Task StateChanges_ShouldValidateAndForwardRequestedState()
    {
        var store = new FakeStore();
        var service = new InstitutionalVehicleCatalogService(
            store,
            new FixedTimeProvider(FixedNow));

        var invalid = await service.DeactivateAsync(0, actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);
        var deactivated = await service.DeactivateAsync(10, actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);
        var reactivated = await service.ReactivateAsync(10, actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ChangeInstitutionalVehicleStateStatus.Invalid, invalid.Status);
        Assert.Equal(ChangeInstitutionalVehicleStateStatus.Success, deactivated.Status);
        Assert.Equal(ChangeInstitutionalVehicleStateStatus.Success, reactivated.Status);
        Assert.Equal(new[] { false, true }, store.RequestedStates);
        Assert.Equal(FixedNow.UtcDateTime, store.LastUpdatedAtUtc);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStore : IInstitutionalVehicleCatalogStore
    {
        public int CreateCalls { get; private set; }
        public InstitutionalVehicleData? LastVehicle { get; private set; }
        public int LastActorUserId { get; private set; }
        public DateTime LastCreatedAtUtc { get; private set; }
        public DateTime LastUpdatedAtUtc { get; private set; }
        public int UpdateCalls { get; private set; }
        public List<bool> RequestedStates { get; } = [];

        public Task<InstitutionalVehicleStoreRegistration> TryCreateAsync(
            InstitutionalVehicleData vehicle,
            int actorUserId,
            DateTime createdAtUtc,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            LastVehicle = vehicle;
            LastActorUserId = actorUserId;
            LastCreatedAtUtc = createdAtUtc;

            return Task.FromResult(new InstitutionalVehicleStoreRegistration(
                InstitutionalVehicleStoreRegistrationStatus.Success,
                new InstitutionalVehicleRecord(
                    1,
                    vehicle.Plate,
                    vehicle.Identification,
                    vehicle.VehicleType,
                    vehicle.Brand,
                    vehicle.Model,
                    vehicle.Color,
                    vehicle.Year,
                    createdAtUtc)));
        }

        public Task<IReadOnlyList<InstitutionalVehicleRecord>> ListActiveAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstitutionalVehicleRecord>>([]);

        public Task<InstitutionalVehicleStoreUpdate> TryUpdateAsync(
            int vehicleId,
            InstitutionalVehicleData vehicle,
            int actorUserId,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken)
        {
            UpdateCalls++;
            LastVehicle = vehicle;
            LastActorUserId = actorUserId;
            LastUpdatedAtUtc = updatedAtUtc;
            return Task.FromResult(new InstitutionalVehicleStoreUpdate(
                InstitutionalVehicleStoreUpdateStatus.Success,
                new InstitutionalVehicleRecord(
                    vehicleId,
                    vehicle.Plate,
                    vehicle.Identification,
                    vehicle.VehicleType,
                    vehicle.Brand,
                    vehicle.Model,
                    vehicle.Color,
                    vehicle.Year,
                    updatedAtUtc)));
        }

        public Task<InstitutionalVehicleStoreStateStatus> TrySetActiveAsync(
            int vehicleId,
            bool active,
            int actorUserId,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken)
        {
            RequestedStates.Add(active);
            LastActorUserId = actorUserId;
            LastUpdatedAtUtc = updatedAtUtc;
            return Task.FromResult(InstitutionalVehicleStoreStateStatus.Success);
        }
    }
}
