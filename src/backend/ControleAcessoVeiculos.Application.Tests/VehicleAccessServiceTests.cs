using ControleAcessoVeiculos.Application.AccessRecords;
using ControleAcessoVeiculos.Domain.Enums;

namespace ControleAcessoVeiculos.Application.Tests;

public sealed class VehicleAccessServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 28, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task RegisterEntryAsync_ShouldValidateBeforeCallingStore()
    {
        var store = new FakeVehicleAccessStore();
        var service = new VehicleAccessService(store, new FixedTimeProvider(FixedNow));

        var result = await service.RegisterEntryAsync(new RegisterVehicleEntryCommand("", "---", "", "Desconhecida"), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RegisterVehicleEntryStatus.Invalid, result.Status);
        Assert.Contains("driverName", result.Errors.Keys);
        Assert.Contains("plate", result.Errors.Keys);
        Assert.Contains("objective", result.Errors.Keys);
        Assert.Contains("categoryName", result.Errors.Keys);
        Assert.Equal(0, store.RegisterCalls);
    }

    [Fact]
    public async Task RegisterEntryAsync_ShouldNormalizeDataAndUseServerTime()
    {
        var store = new FakeVehicleAccessStore();
        var service = new VehicleAccessService(store, new FixedTimeProvider(FixedNow));

        var result = await service.RegisterEntryAsync(new RegisterVehicleEntryCommand(
                "  Condutor de Teste  ",
                "abc-1d23",
                "  Visita técnica  ",
                "visitante",
                " cpf ",
                " 12345678900 ",
                EventAuthorizationId: 12), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RegisterVehicleEntryStatus.Success, result.Status);
        Assert.Equal(1, store.RegisterCalls);
        Assert.NotNull(store.LastEntry);
        Assert.Equal("Condutor de Teste", store.LastEntry.DriverName);
        Assert.Equal("ABC1D23", store.LastEntry.Plate);
        Assert.Equal("Visitante", store.LastEntry.CategoryName);
        Assert.Equal("CPF", store.LastEntry.DocumentType);
        Assert.Equal(12, store.LastEntry.EventAuthorizationId);
        Assert.Equal(FixedNow.UtcDateTime, store.LastEntryAtUtc);
        Assert.Equal(7, store.LastActorUserId);
    }

    [Fact]
    public async Task RegisterEntryAsync_ShouldRejectInvalidEventAuthorizationId()
    {
        var store = new FakeVehicleAccessStore();
        var service = new VehicleAccessService(store, new FixedTimeProvider(FixedNow));

        var result = await service.RegisterEntryAsync(new RegisterVehicleEntryCommand(
                "Condutor",
                "ABC1D23",
                "Evento",
                AccessCategoryNames.Event,
                EventAuthorizationId: 0), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RegisterVehicleEntryStatus.Invalid, result.Status);
        Assert.Contains("eventAuthorizationId", result.Errors.Keys);
        Assert.Equal(0, store.RegisterCalls);
    }

    [Fact]
    public async Task RegisterEntryAsync_ShouldRejectIncompleteCandidateSelection()
    {
        var store = new FakeVehicleAccessStore();
        var service = new VehicleAccessService(store, new FixedTimeProvider(FixedNow));

        var result = await service.RegisterEntryAsync(new RegisterVehicleEntryCommand(
                "Condutor",
                "ABC1D23",
                "Visita",
                AccessCategoryNames.Visitor,
                VehicleId: 5), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RegisterVehicleEntryStatus.Invalid, result.Status);
        Assert.Contains("candidate", result.Errors.Keys);
        Assert.Equal(0, store.RegisterCalls);
    }

    [Fact]
    public async Task SearchEntryCandidatesAsync_ShouldNormalizeAndLimitSearch()
    {
        var store = new FakeVehicleAccessStore();
        var service = new VehicleAccessService(store, new FixedTimeProvider(FixedNow));

        var result = await service.SearchEntryCandidatesAsync(new SearchAccessEntryCandidatesCommand("  abc-1  "), TestContext.Current.CancellationToken);

        Assert.Equal(SearchAccessEntryCandidatesStatus.Success, result.Status);
        Assert.NotNull(store.LastCandidateSearchCriteria);
        Assert.Equal("abc-1", store.LastCandidateSearchCriteria.Query);
        Assert.Equal("ABC1", store.LastCandidateSearchCriteria.PlatePrefix);
        Assert.Equal(DateOnly.FromDateTime(FixedNow.UtcDateTime),
            store.LastCandidateSearchCriteria.ActiveOn);
        Assert.Equal(10, store.LastCandidateSearchCriteria.Limit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    public async Task SearchEntryCandidatesAsync_ShouldRejectInvalidQuery(string query)
    {
        var store = new FakeVehicleAccessStore();
        var service = new VehicleAccessService(store, new FixedTimeProvider(FixedNow));

        var result = await service.SearchEntryCandidatesAsync(new SearchAccessEntryCandidatesCommand(query), TestContext.Current.CancellationToken);

        Assert.Equal(SearchAccessEntryCandidatesStatus.Invalid, result.Status);
        Assert.Contains("query", result.Errors.Keys);
        Assert.Null(store.LastCandidateSearchCriteria);
    }

    [Fact]
    public async Task SearchHistoryAsync_ShouldUseDefaultsAndNormalizeFilters()
    {
        var store = new FakeVehicleAccessStore();
        var service = new VehicleAccessService(store, new FixedTimeProvider(FixedNow));

        var result = await service.SearchHistoryAsync(new(
            Plate: "abc-1d23",
            DriverName: "  Condutor  ",
            CategoryName: "visitante",
            Status: "encerrado"), TestContext.Current.CancellationToken);

        Assert.Equal(SearchVehicleAccessesStatus.Success, result.Status);
        Assert.NotNull(store.LastSearchCriteria);
        Assert.Equal("ABC1D23", store.LastSearchCriteria.Plate);
        Assert.Equal("Condutor", store.LastSearchCriteria.DriverName);
        Assert.Equal(AccessCategoryNames.Visitor, store.LastSearchCriteria.CategoryName);
        Assert.Equal(StatusRegistroAcesso.Encerrado,
            store.LastSearchCriteria.Status);
        Assert.Equal(FixedNow.AddDays(-30).UtcDateTime, store.LastSearchCriteria.FromUtc);
        Assert.Equal(FixedNow.UtcDateTime, store.LastSearchCriteria.ToUtc);
        Assert.Equal(1, store.LastSearchCriteria.Page);
        Assert.Equal(25, store.LastSearchCriteria.PageSize);
    }

    [Fact]
    public async Task SearchHistoryAsync_ShouldRejectInvalidFiltersBeforeCallingStore()
    {
        var store = new FakeVehicleAccessStore();
        var service = new VehicleAccessService(store, new FixedTimeProvider(FixedNow));

        var result = await service.SearchHistoryAsync(new(
            Plate: "---",
            DriverName: "x",
            CategoryName: "Desconhecida",
            Status: "999",
            From: FixedNow,
            To: FixedNow.AddDays(-1),
            Page: 0,
            PageSize: 101), TestContext.Current.CancellationToken);

        Assert.Equal(SearchVehicleAccessesStatus.Invalid, result.Status);
        Assert.Null(store.LastSearchCriteria);
        Assert.Contains("plate", result.Errors.Keys);
        Assert.Contains("driverName", result.Errors.Keys);
        Assert.Contains("categoryName", result.Errors.Keys);
        Assert.Contains("status", result.Errors.Keys);
        Assert.Contains("period", result.Errors.Keys);
        Assert.Contains("page", result.Errors.Keys);
        Assert.Contains("pageSize", result.Errors.Keys);
    }

    [Fact]
    public async Task SearchHistoryAsync_ShouldRejectPeriodLongerThanOneYear()
    {
        var store = new FakeVehicleAccessStore();
        var service = new VehicleAccessService(store, new FixedTimeProvider(FixedNow));

        var result = await service.SearchHistoryAsync(new(
            From: FixedNow.AddDays(-367),
            To: FixedNow), TestContext.Current.CancellationToken);

        Assert.Equal(SearchVehicleAccessesStatus.Invalid, result.Status);
        Assert.Contains("period", result.Errors.Keys);
        Assert.Null(store.LastSearchCriteria);
    }

    [Fact]
    public async Task CorrectAsync_ShouldValidateBeforeCallingStore()
    {
        var store = new FakeVehicleAccessStore();
        var service = new VehicleAccessService(store, new FixedTimeProvider(FixedNow));

        var result = await service.CorrectAsync(accessRecordId: 1, new CorrectVehicleAccessCommand(
                "",
                "Desconhecida",
                new string('x', 1001),
                "curta"), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CorrectVehicleAccessStatus.Invalid, result.Status);
        Assert.Equal(0, store.CorrectionCalls);
        Assert.Contains("objective", result.Errors.Keys);
        Assert.Contains("categoryName", result.Errors.Keys);
        Assert.Contains("observation", result.Errors.Keys);
        Assert.Contains("justification", result.Errors.Keys);
    }

    [Fact]
    public async Task CorrectAsync_ShouldNormalizeDataAndUseServerContext()
    {
        var store = new FakeVehicleAccessStore();
        var service = new VehicleAccessService(store, new FixedTimeProvider(FixedNow));

        var result = await service.CorrectAsync(accessRecordId: 5, new CorrectVehicleAccessCommand(
                "  Entrega autorizada  ",
                " entrega ",
                "  Conferido  ",
                "  Correção conferida pelo vigilante.  "), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CorrectVehicleAccessStatus.Success, result.Status);
        Assert.Equal(1, store.CorrectionCalls);
        Assert.Equal(5, store.LastCorrectedAccessRecordId);
        Assert.Equal("Entrega autorizada", store.LastCorrection!.Objective);
        Assert.Equal(AccessCategoryNames.Delivery, store.LastCorrection.CategoryName);
        Assert.Equal("Conferido", store.LastCorrection.Observation);
        Assert.Equal("Correção conferida pelo vigilante.",
            store.LastCorrection.Justification);
        Assert.Equal(7, store.LastCorrectionActorUserId);
        Assert.Equal(FixedNow.UtcDateTime, store.LastCorrectedAtUtc);
    }

    [Fact]
    public async Task CloseExceptionallyAsync_ShouldValidateBeforeCallingStore()
    {
        var store = new FakeVehicleAccessStore();
        var service = new VehicleAccessService(store, new FixedTimeProvider(FixedNow));

        var result = await service.CloseExceptionallyAsync(5, new ExceptionallyCloseVehicleAccessCommand(
                "MotivoInexistente",
                "curta",
                FixedNow.AddMinutes(1)), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CloseVehicleAccessStatus.Invalid, result.Status);
        Assert.Equal(0, store.ExceptionalClosureCalls);
        Assert.Contains("reason", result.Errors!.Keys);
        Assert.Contains("observation", result.Errors.Keys);
        Assert.Contains("observedExitAtUtc", result.Errors.Keys);
    }

    [Fact]
    public async Task CloseExceptionallyAsync_ShouldNormalizeAndUseServerTime()
    {
        var store = new FakeVehicleAccessStore();
        var service = new VehicleAccessService(store, new FixedTimeProvider(FixedNow));
        var observedAt = FixedNow.AddMinutes(-15);

        await service.CloseExceptionallyAsync(5, new ExceptionallyCloseVehicleAccessCommand(
                " registrodesaidaomitido ",
                "  Saída confirmada pelo responsável operacional.  ",
                observedAt), actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, store.ExceptionalClosureCalls);
        Assert.Equal(5, store.LastExceptionallyClosedAccessRecordId);
        Assert.Equal(
            MotivoEncerramentoExcepcional.RegistroDeSaidaOmitido,
            store.LastExceptionalClosure!.Reason);
        Assert.Equal(
            "Saída confirmada pelo responsável operacional.",
            store.LastExceptionalClosure.Observation);
        Assert.Equal(observedAt.UtcDateTime, store.LastExceptionalClosure.ObservedExitAtUtc);
        Assert.Equal(FixedNow.UtcDateTime, store.LastRegularizedAtUtc);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeVehicleAccessStore : IVehicleAccessStore
    {
        public int RegisterCalls { get; private set; }
        public VehicleEntryData? LastEntry { get; private set; }
        public int LastActorUserId { get; private set; }
        public DateTime LastEntryAtUtc { get; private set; }
        public VehicleAccessSearchCriteria? LastSearchCriteria { get; private set; }
        public AccessEntryCandidateSearchCriteria? LastCandidateSearchCriteria
        {
            get;
            private set;
        }
        public int CorrectionCalls { get; private set; }
        public int LastCorrectedAccessRecordId { get; private set; }
        public VehicleAccessCorrectionData? LastCorrection { get; private set; }
        public int LastCorrectionActorUserId { get; private set; }
        public DateTime LastCorrectedAtUtc { get; private set; }
        public int ExceptionalClosureCalls { get; private set; }
        public int LastExceptionallyClosedAccessRecordId { get; private set; }
        public ExceptionalVehicleAccessClosureData? LastExceptionalClosure { get; private set; }
        public DateTime LastRegularizedAtUtc { get; private set; }

        public Task<VehicleAccessStoreRegistration> TryRegisterEntryAsync(
            VehicleEntryData entry,
            int actorUserId,
            DateTime entryAtUtc,
            CancellationToken cancellationToken)
        {
            RegisterCalls++;
            LastEntry = entry;
            LastActorUserId = actorUserId;
            LastEntryAtUtc = entryAtUtc;

            return Task.FromResult(new VehicleAccessStoreRegistration(
                VehicleAccessStoreRegistrationStatus.Success,
                new VehicleAccessRecord(
                    1,
                    2,
                    entry.Plate,
                    3,
                    entry.DriverName,
                    entry.CategoryName,
                    entry.Objective,
                    entryAtUtc,
                    null,
                    "Aberto",
                    actorUserId,
                    null,
                    entry.Observation)));
        }

        public Task<IReadOnlyList<VehicleAccessRecord>> ListOpenAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VehicleAccessRecord>>([]);

        public Task<IReadOnlyList<AccessEntryCandidate>> SearchEntryCandidatesAsync(
            AccessEntryCandidateSearchCriteria criteria,
            CancellationToken cancellationToken)
        {
            LastCandidateSearchCriteria = criteria;
            return Task.FromResult<IReadOnlyList<AccessEntryCandidate>>([]);
        }

        public Task<PagedVehicleAccessResult> SearchAsync(
            VehicleAccessSearchCriteria criteria,
            CancellationToken cancellationToken)
        {
            LastSearchCriteria = criteria;
            return Task.FromResult(new PagedVehicleAccessResult([], criteria.Page,
                criteria.PageSize, 0, 0));
        }

        public Task<VehicleAccessCorrectionStoreResult> TryCorrectAsync(
            int accessRecordId,
            VehicleAccessCorrectionData correction,
            int actorUserId,
            DateTime correctedAtUtc,
            CancellationToken cancellationToken)
        {
            CorrectionCalls++;
            LastCorrectedAccessRecordId = accessRecordId;
            LastCorrection = correction;
            LastCorrectionActorUserId = actorUserId;
            LastCorrectedAtUtc = correctedAtUtc;

            return Task.FromResult(new VehicleAccessCorrectionStoreResult(
                VehicleAccessCorrectionStoreStatus.Success,
                new VehicleAccessRecord(
                    accessRecordId,
                    2,
                    "ABC1D23",
                    3,
                    "Condutor",
                    correction.CategoryName,
                    correction.Objective,
                    correctedAtUtc.AddHours(-1),
                    null,
                    "Aberto",
                    4,
                    actorUserId,
                    correction.Observation)));
        }

        public Task<CloseVehicleAccessResult> TryCloseAsync(
            int accessRecordId,
            int actorUserId,
            DateTime exitAtUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CloseVehicleAccessResult(
                CloseVehicleAccessStatus.NotFound,
                null));

        public Task<CloseVehicleAccessResult> TryCloseExceptionallyAsync(
            int accessRecordId,
            ExceptionalVehicleAccessClosureData closure,
            int actorUserId,
            DateTime regularizedAtUtc,
            CancellationToken cancellationToken)
        {
            ExceptionalClosureCalls++;
            LastExceptionallyClosedAccessRecordId = accessRecordId;
            LastExceptionalClosure = closure;
            LastRegularizedAtUtc = regularizedAtUtc;
            return Task.FromResult(new CloseVehicleAccessResult(
                CloseVehicleAccessStatus.Success,
                null));
        }
    }
}
