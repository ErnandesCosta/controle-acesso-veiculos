using ControleAcessoVeiculos.Application.InstitutionalVehicleUsages;

namespace ControleAcessoVeiculos.Application.Tests;

public sealed class InstitutionalVehicleUsageServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 29, 9, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task RegisterDepartureAsync_ShouldValidateBeforeCallingStore()
    {
        var store = new FakeStore();
        var service = new InstitutionalVehicleUsageService(
            store,
            new FixedTimeProvider(FixedNow));

        var result = await service.RegisterDepartureAsync(new RegisterInstitutionalVehicleDepartureCommand(0, 0, -1, ""), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RegisterInstitutionalVehicleDepartureStatus.Invalid, result.Status);
        Assert.Contains("vehicleId", result.Errors.Keys);
        Assert.Contains("driverId", result.Errors.Keys);
        Assert.Contains("departureMileage", result.Errors.Keys);
        Assert.Contains("itinerary", result.Errors.Keys);
        Assert.Equal(0, store.DepartureCalls);
    }

    [Fact]
    public async Task RegisterDepartureAsync_ShouldNormalizeItineraryAndUseServerTime()
    {
        var store = new FakeStore();
        var service = new InstitutionalVehicleUsageService(
            store,
            new FixedTimeProvider(FixedNow));

        var result = await service.RegisterDepartureAsync(new RegisterInstitutionalVehicleDepartureCommand(
                VehicleId: 10,
                DriverId: 11,
                DepartureMileage: 12500,
                Itinerary: "  Campus - Unidade rural  "), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RegisterInstitutionalVehicleDepartureStatus.Success, result.Status);
        Assert.Equal("Campus - Unidade rural", store.LastItinerary);
        Assert.Equal(FixedNow.UtcDateTime, store.LastOccurredAtUtc);
        Assert.Equal(7, store.LastActorUserId);
    }

    [Fact]
    public async Task RegisterReturnAsync_ShouldValidateBeforeCallingStore()
    {
        var store = new FakeStore();
        var service = new InstitutionalVehicleUsageService(
            store,
            new FixedTimeProvider(FixedNow));

        var result = await service.RegisterReturnAsync(usageId: 0, new RegisterInstitutionalVehicleReturnCommand(ReturnMileage: -1), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RegisterInstitutionalVehicleReturnStatus.Invalid, result.Status);
        Assert.Contains("usageId", result.Errors.Keys);
        Assert.Contains("returnMileage", result.Errors.Keys);
        Assert.Equal(0, store.ReturnCalls);
    }

    [Fact]
    public async Task SearchHistoryAsync_ShouldUseDefaultPeriodAndNormalizeFilters()
    {
        var store = new FakeStore();
        var service = new InstitutionalVehicleUsageService(
            store,
            new FixedTimeProvider(FixedNow));

        var result = await service.SearchHistoryAsync(new(
            Plate: " ifp-1e23 ",
            VehicleIdentification: " frota-001 "), TestContext.Current.CancellationToken);

        Assert.Equal(SearchInstitutionalVehicleUsagesStatus.Success, result.Status);
        Assert.NotNull(store.LastSearchCriteria);
        Assert.Equal("IFP1E23", store.LastSearchCriteria.Plate);
        Assert.Equal("FROTA-001", store.LastSearchCriteria.VehicleIdentification);
        Assert.Equal(FixedNow.AddDays(-30).UtcDateTime, store.LastSearchCriteria.FromUtc);
        Assert.Equal(FixedNow.UtcDateTime, store.LastSearchCriteria.ToUtc);
        Assert.Equal(1, store.LastSearchCriteria.Page);
        Assert.Equal(25, store.LastSearchCriteria.PageSize);
    }

    [Fact]
    public async Task SearchHistoryAsync_ShouldRejectInvalidPeriodPaginationAndFilters()
    {
        var store = new FakeStore();
        var service = new InstitutionalVehicleUsageService(
            store,
            new FixedTimeProvider(FixedNow));

        var result = await service.SearchHistoryAsync(new(
            VehicleId: 0,
            DriverId: -1,
            Plate: "---",
            VehicleIdentification: new string('x', 101),
            From: FixedNow,
            To: FixedNow.AddDays(-1),
            Page: 0,
            PageSize: 101), TestContext.Current.CancellationToken);

        Assert.Equal(SearchInstitutionalVehicleUsagesStatus.Invalid, result.Status);
        Assert.Contains("vehicleId", result.Errors.Keys);
        Assert.Contains("driverId", result.Errors.Keys);
        Assert.Contains("plate", result.Errors.Keys);
        Assert.Contains("vehicleIdentification", result.Errors.Keys);
        Assert.Contains("period", result.Errors.Keys);
        Assert.Contains("page", result.Errors.Keys);
        Assert.Contains("pageSize", result.Errors.Keys);
        Assert.Null(store.LastSearchCriteria);
    }

    [Fact]
    public async Task SearchHistoryAsync_ShouldRejectPeriodLongerThanOneYear()
    {
        var store = new FakeStore();
        var service = new InstitutionalVehicleUsageService(
            store,
            new FixedTimeProvider(FixedNow));

        var result = await service.SearchHistoryAsync(new(
            From: FixedNow.AddDays(-367),
            To: FixedNow), TestContext.Current.CancellationToken);

        Assert.Equal(SearchInstitutionalVehicleUsagesStatus.Invalid, result.Status);
        Assert.Contains("period", result.Errors.Keys);
        Assert.Null(store.LastSearchCriteria);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStore : IInstitutionalVehicleUsageStore
    {
        public int DepartureCalls { get; private set; }
        public int ReturnCalls { get; private set; }
        public string? LastItinerary { get; private set; }
        public int LastActorUserId { get; private set; }
        public DateTime LastOccurredAtUtc { get; private set; }
        public InstitutionalVehicleUsageSearchCriteria? LastSearchCriteria { get; private set; }

        public Task<InstitutionalVehicleDepartureStoreResult> TryRegisterDepartureAsync(
            int vehicleId,
            int driverId,
            int departureMileage,
            string itinerary,
            int actorUserId,
            DateTime departureAtUtc,
            CancellationToken cancellationToken)
        {
            DepartureCalls++;
            LastItinerary = itinerary;
            LastActorUserId = actorUserId;
            LastOccurredAtUtc = departureAtUtc;

            return Task.FromResult(new InstitutionalVehicleDepartureStoreResult(
                InstitutionalVehicleDepartureStoreStatus.Success,
                CreateRecord(
                    vehicleId,
                    driverId,
                    departureMileage,
                    itinerary,
                    actorUserId,
                    departureAtUtc)));
        }

        public Task<IReadOnlyList<InstitutionalVehicleUsageRecord>> ListOpenAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstitutionalVehicleUsageRecord>>([]);

        public Task<PagedInstitutionalVehicleUsageResult> SearchAsync(
            InstitutionalVehicleUsageSearchCriteria criteria,
            CancellationToken cancellationToken)
        {
            LastSearchCriteria = criteria;
            return Task.FromResult(new PagedInstitutionalVehicleUsageResult(
                [],
                criteria.Page,
                criteria.PageSize,
                0,
                0));
        }

        public Task<InstitutionalVehicleReturnStoreResult> TryRegisterReturnAsync(
            int usageId,
            int returnMileage,
            int actorUserId,
            DateTime returnAtUtc,
            CancellationToken cancellationToken)
        {
            ReturnCalls++;
            return Task.FromResult(new InstitutionalVehicleReturnStoreResult(
                InstitutionalVehicleReturnStoreStatus.NotFound,
                null));
        }

        private static InstitutionalVehicleUsageRecord CreateRecord(
            int vehicleId,
            int driverId,
            int departureMileage,
            string itinerary,
            int actorUserId,
            DateTime departureAtUtc) =>
            new(
                1,
                vehicleId,
                "ABC1D23",
                null,
                driverId,
                "Motorista de Teste",
                departureAtUtc,
                departureMileage,
                itinerary,
                null,
                null,
                "EmUso",
                actorUserId,
                null);
    }
}
