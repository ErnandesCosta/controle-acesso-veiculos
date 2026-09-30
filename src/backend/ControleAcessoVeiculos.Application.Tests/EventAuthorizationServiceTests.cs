using ControleAcessoVeiculos.Application.EventAuthorizations;

namespace ControleAcessoVeiculos.Application.Tests;

public sealed class EventAuthorizationServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_ShouldValidateBeforeCallingStore()
    {
        var store = new FakeStore();
        var service = CreateService(store);

        var result = await service.CreateAsync(new CreateEventAuthorizationCommand(
                null,
                null,
                FixedNow.AddDays(2),
                FixedNow.AddDays(1),
                null,
                false,
                []), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(EventAuthorizationOperationStatus.Invalid, result.Status);
        Assert.Contains("name", result.Errors.Keys);
        Assert.Contains("period", result.Errors.Keys);
        Assert.Contains("vehicleRules", result.Errors.Keys);
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task CreateAsync_ShouldNormalizeRulesAndUseServerTime()
    {
        var store = new FakeStore();
        var service = CreateService(store);

        var result = await service.CreateAsync(ValidCommand([
                new(" automóvel ", 1, " abc-1d23 "),
                new(" ônibus ", 3)
            ]), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(EventAuthorizationOperationStatus.Success, result.Status);
        Assert.Equal(1, store.CreateCalls);
        Assert.Equal(FixedNow.UtcDateTime, store.LastOccurredAtUtc);
        Assert.Equal("AUTOMÓVEL", store.LastData!.VehicleRules[0].VehicleType);
        Assert.Equal("ABC1D23", store.LastData.VehicleRules[0].Plate);
        Assert.Equal("ÔNIBUS", store.LastData.VehicleRules[1].VehicleType);
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectDuplicateAndNonUnitPlateRules()
    {
        var store = new FakeStore();
        var service = CreateService(store);

        var result = await service.CreateAsync(ValidCommand([
                new("Automóvel", 2, "ABC-1234"),
                new("Van", 1, "ABC1234"),
                new("Ônibus", 2),
                new(" ônibus ", 3)
            ]), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(EventAuthorizationOperationStatus.Invalid, result.Status);
        Assert.Contains("vehicleRules[0]", result.Errors.Keys);
        Assert.Contains("vehicleRules[1]", result.Errors.Keys);
        Assert.Contains("vehicleRules[3]", result.Errors.Keys);
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task SearchAsync_ShouldUseBoundedDefaultPeriod()
    {
        var store = new FakeStore();
        var service = CreateService(store);

        var result = await service.SearchAsync(new SearchEventAuthorizationsCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(EventAuthorizationOperationStatus.Success, result.Status);
        Assert.Equal(FixedNow.UtcDateTime, store.LastCriteria!.FromUtc);
        Assert.Equal(FixedNow.AddDays(30).UtcDateTime, store.LastCriteria.ToUtc);
        Assert.True(store.LastCriteria.Active);
    }

    [Fact]
    public async Task CancelAsync_ShouldValidateIdAndUseActor()
    {
        var store = new FakeStore();
        var service = CreateService(store);

        var invalid = await service.CancelAsync(0, actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);
        var success = await service.CancelAsync(10, actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(EventAuthorizationOperationStatus.Invalid, invalid.Status);
        Assert.Equal(EventAuthorizationOperationStatus.Success, success.Status);
        Assert.Equal(1, store.CancelCalls);
        Assert.Equal(7, store.LastActorUserId);
    }

    private static EventAuthorizationService CreateService(FakeStore store) =>
        new(store, new FixedTimeProvider(FixedNow));

    private static CreateEventAuthorizationCommand ValidCommand(
        IReadOnlyList<EventVehicleRuleInput> rules) =>
        new(
            " Jardim Digital ",
            " Coordenação de Extensão ",
            FixedNow.AddDays(1),
            FixedNow.AddDays(2),
            " Pátio central ",
            true,
            rules,
            " Evento acadêmico ");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStore : IEventAuthorizationStore
    {
        public int CreateCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public int LastActorUserId { get; private set; }
        public DateTime LastOccurredAtUtc { get; private set; }
        public EventAuthorizationData? LastData { get; private set; }
        public EventAuthorizationSearchCriteria? LastCriteria { get; private set; }

        public Task<EventAuthorizationStoreResult> TryCreateAsync(
            EventAuthorizationData data,
            int actorUserId,
            DateTime occurredAtUtc,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            LastData = data;
            LastActorUserId = actorUserId;
            LastOccurredAtUtc = occurredAtUtc;
            return Task.FromResult(new EventAuthorizationStoreResult(
                EventAuthorizationStoreStatus.Success,
                Map(data, actorUserId, occurredAtUtc)));
        }

        public Task<PagedEventAuthorizations> SearchAsync(
            EventAuthorizationSearchCriteria criteria,
            CancellationToken cancellationToken)
        {
            LastCriteria = criteria;
            return Task.FromResult(new PagedEventAuthorizations([], 1, 25, 0, 0));
        }

        public Task<EventAuthorizationStoreResult> TryUpdateAsync(
            int eventId,
            EventAuthorizationData data,
            int actorUserId,
            DateTime occurredAtUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EventAuthorizationStoreResult(
                EventAuthorizationStoreStatus.Success,
                Map(data, actorUserId, occurredAtUtc) with { Id = eventId }));

        public Task<EventAuthorizationStoreStatus> TryCancelAsync(
            int eventId,
            int actorUserId,
            DateTime occurredAtUtc,
            CancellationToken cancellationToken)
        {
            CancelCalls++;
            LastActorUserId = actorUserId;
            LastOccurredAtUtc = occurredAtUtc;
            return Task.FromResult(EventAuthorizationStoreStatus.Success);
        }

        private static EventAuthorizationRecord Map(
            EventAuthorizationData data,
            int actorUserId,
            DateTime occurredAtUtc) =>
            new(
                1,
                data.Name,
                data.Responsible,
                data.StartsAtUtc,
                data.EndsAtUtc,
                data.Area,
                data.OvernightAllowed,
                data.Notes,
                true,
                actorUserId,
                occurredAtUtc,
                null,
                null,
                []);
    }
}
