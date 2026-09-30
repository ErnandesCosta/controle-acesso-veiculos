using ControleAcessoVeiculos.Application.OperationalSummaries;

namespace ControleAcessoVeiculos.Application.Tests;

public sealed class OperationalSummaryServiceTests
{
    private static readonly TimeZoneInfo InstitutionalTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Recife");

    [Fact]
    public async Task ExplicitLocalDateUsesExclusiveUtcDayBoundaries()
    {
        var store = new FakeStore();
        var service = new OperationalSummaryService(
            store,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero)),
            InstitutionalTimeZone);

        var result = await service.GetAsync(new DateOnly(2026, 8, 30), TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2026, 8, 30), result.LocalDate);
        Assert.Equal("America/Recife", result.TimeZoneId);
        Assert.Equal(new DateTime(2026, 8, 30, 3, 0, 0, DateTimeKind.Utc),
            result.PeriodStartUtc);
        Assert.Equal(new DateTime(2026, 8, 31, 3, 0, 0, DateTimeKind.Utc),
            result.PeriodEndUtcExclusive);
        Assert.Equal(store.Criteria?.PeriodStartUtc, result.PeriodStartUtc);
        Assert.Equal(store.Criteria?.PeriodEndUtcExclusive, result.PeriodEndUtcExclusive);
        Assert.Same(store.Totals.GeneralAccess, result.GeneralAccess);
        Assert.Same(store.Totals.InstitutionalUsages, result.InstitutionalUsages);
        Assert.Same(store.Totals.EventAccess, result.EventAccess);
    }

    [Fact]
    public async Task MissingDateUsesCurrentInstitutionalDate()
    {
        var store = new FakeStore();
        var service = new OperationalSummaryService(
            store,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 31, 1, 30, 0, TimeSpan.Zero)),
            InstitutionalTimeZone);

        var result = await service.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2026, 8, 30), result.LocalDate);
    }

    private sealed class FakeStore : IOperationalSummaryStore
    {
        public OperationalSummaryCriteria? Criteria { get; private set; }

        public OperationalSummaryTotals Totals { get; } = new(
            new GeneralAccessDailyTotals(1, 2, 3, 4, 5),
            new InstitutionalUsageDailyTotals(5, 6, 7, 8),
            new EventAccessDailyTotals(9, 10));

        public Task<OperationalSummaryTotals> GetAsync(
            OperationalSummaryCriteria criteria,
            CancellationToken cancellationToken)
        {
            Criteria = criteria;
            return Task.FromResult(Totals);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
