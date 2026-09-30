using ControleAcessoVeiculos.Application.Auditing;
using ControleAcessoVeiculos.Domain.Enums;

namespace ControleAcessoVeiculos.Application.Tests;

public sealed class AuditTrailServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SearchUsesRecentDefaultPeriodAndNormalizesFilters()
    {
        var store = new FakeStore();
        var service = CreateService(store);

        var result = await service.SearchAsync(new SearchAuditTrailCommand(
            Action: " alteracao ",
            Entity: " Usuario ",
            Page: 2,
            PageSize: 10), TestContext.Current.CancellationToken);

        Assert.Equal(SearchAuditTrailStatus.Success, result.Status);
        Assert.NotNull(result.Result);
        Assert.NotNull(store.Criteria);
        Assert.Equal(FixedNow.AddDays(-30).UtcDateTime, store.Criteria.FromUtc);
        Assert.Equal(FixedNow.UtcDateTime, store.Criteria.ToUtc);
        Assert.Equal(TipoAcaoAuditoria.Alteracao, store.Criteria.Action);
        Assert.Equal("Usuario", store.Criteria.Entity);
        Assert.Equal(2, store.Criteria.Page);
        Assert.Equal(10, store.Criteria.PageSize);
    }

    [Fact]
    public async Task SearchConvertsExplicitOffsetsToUtc()
    {
        var store = new FakeStore();
        var service = CreateService(store);

        var result = await service.SearchAsync(new SearchAuditTrailCommand(
            FromUtc: new DateTimeOffset(2026, 8, 29, 8, 0, 0, TimeSpan.FromHours(-3)),
            ToUtc: new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.FromHours(-3))), TestContext.Current.CancellationToken);

        Assert.Equal(SearchAuditTrailStatus.Success, result.Status);
        Assert.Equal(new DateTime(2026, 8, 29, 11, 0, 0, DateTimeKind.Utc),
            store.Criteria?.FromUtc);
        Assert.Equal(new DateTime(2026, 8, 30, 11, 0, 0, DateTimeKind.Utc),
            store.Criteria?.ToUtc);
    }

    [Fact]
    public async Task SearchRejectsInvalidFiltersBeforeCallingStore()
    {
        var store = new FakeStore();
        var service = CreateService(store);

        var result = await service.SearchAsync(new SearchAuditTrailCommand(
            FromUtc: FixedNow.AddDays(-91),
            ToUtc: FixedNow,
            Action: "Unknown",
            Entity: new string('x', 101),
            RecordId: 0,
            ActorUserId: -1,
            SystemOnly: true,
            Page: 0,
            PageSize: 101), TestContext.Current.CancellationToken);

        Assert.Equal(SearchAuditTrailStatus.Invalid, result.Status);
        Assert.Contains("period", result.Errors.Keys);
        Assert.Contains("action", result.Errors.Keys);
        Assert.Contains("entity", result.Errors.Keys);
        Assert.Contains("recordId", result.Errors.Keys);
        Assert.Contains("actorUserId", result.Errors.Keys);
        Assert.Contains("actor", result.Errors.Keys);
        Assert.Contains("page", result.Errors.Keys);
        Assert.Contains("pageSize", result.Errors.Keys);
        Assert.Null(store.Criteria);
    }

    private static AuditTrailService CreateService(FakeStore store) =>
        new(store, new FixedTimeProvider(FixedNow));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStore : IAuditTrailStore
    {
        public AuditTrailSearchCriteria? Criteria { get; private set; }

        public Task<PagedAuditTrailResult> SearchAsync(
            AuditTrailSearchCriteria criteria,
            CancellationToken cancellationToken)
        {
            Criteria = criteria;
            return Task.FromResult(new PagedAuditTrailResult(
                [],
                criteria.Page,
                criteria.PageSize,
                0,
                0));
        }
    }
}
