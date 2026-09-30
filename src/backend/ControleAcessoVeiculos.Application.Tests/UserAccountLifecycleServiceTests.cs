using ControleAcessoVeiculos.Application.Accounts;

namespace ControleAcessoVeiculos.Application.Tests;

public sealed class UserAccountLifecycleServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SearchValidatesPaginationBeforeCallingStore()
    {
        var store = new FakeStore();
        var service = CreateService(store);

        var result = await service.SearchAsync(new SearchUserAccountsCommand(
            Search: new string('x', 255),
            Page: 0,
            PageSize: 101), TestContext.Current.CancellationToken);

        Assert.Equal(SearchUserAccountsStatus.Invalid, result.Status);
        Assert.Contains("search", result.Errors.Keys);
        Assert.Contains("page", result.Errors.Keys);
        Assert.Contains("pageSize", result.Errors.Keys);
        Assert.Null(store.SearchCriteria);
    }

    [Fact]
    public async Task SearchNormalizesFilterAndReturnsStorePage()
    {
        var store = new FakeStore();
        var service = CreateService(store);

        var result = await service.SearchAsync(new SearchUserAccountsCommand(
            "  ADMIN@EXAMPLE.TEST  ",
            Active: true,
            Page: 2,
            PageSize: 10), TestContext.Current.CancellationToken);

        Assert.Equal(SearchUserAccountsStatus.Success, result.Status);
        Assert.NotNull(result.Result);
        Assert.Equal("admin@example.test", store.SearchCriteria?.Search);
        Assert.True(store.SearchCriteria?.Active);
        Assert.Equal(2, store.SearchCriteria?.Page);
        Assert.Equal(10, store.SearchCriteria?.PageSize);
    }

    [Fact]
    public async Task DeactivateRejectsActorOwnAccountBeforeCallingStore()
    {
        var store = new FakeStore();
        var service = CreateService(store);

        var result = await service.DeactivateAsync(7, actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ChangeUserAccountStateStatus.SelfDeactivation, result.Status);
        Assert.Equal(0, store.StateChangeCalls);
    }

    [Fact]
    public async Task StateChangesUseServerTimeAndRequestedState()
    {
        var store = new FakeStore();
        var service = CreateService(store);

        var deactivated = await service.DeactivateAsync(8, actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);
        var reactivated = await service.ReactivateAsync(8, actorUserId: 7, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ChangeUserAccountStateStatus.Success, deactivated.Status);
        Assert.Equal(ChangeUserAccountStateStatus.Success, reactivated.Status);
        Assert.Equal([false, true], store.RequestedStates);
        Assert.Equal(FixedNow.UtcDateTime, store.UpdatedAtUtc);
    }

    private static UserAccountLifecycleService CreateService(FakeStore store) =>
        new(store, new FixedTimeProvider(FixedNow));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStore : IUserAccountStore
    {
        public UserAccountSearchCriteria? SearchCriteria { get; private set; }
        public int StateChangeCalls { get; private set; }
        public List<bool> RequestedStates { get; } = [];
        public DateTime UpdatedAtUtc { get; private set; }

        public Task<bool> HasAnyUserAsync(CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<CreatedUserAccount?> TryCreateAsync(
            string name,
            string normalizedEmail,
            string passwordHash,
            string profileName,
            DateTime? temporaryCredentialExpiresAtUtc,
            AccountCreationAudit audit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PagedUserAccountResult> SearchAsync(
            UserAccountSearchCriteria criteria,
            CancellationToken cancellationToken)
        {
            SearchCriteria = criteria;
            return Task.FromResult(new PagedUserAccountResult(
                [],
                criteria.Page,
                criteria.PageSize,
                0,
                0));
        }

        public Task<UserAccountStoreStateStatus> TrySetActiveAsync(
            int userId,
            bool active,
            int actorUserId,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken)
        {
            StateChangeCalls++;
            RequestedStates.Add(active);
            UpdatedAtUtc = updatedAtUtc;
            return Task.FromResult(UserAccountStoreStateStatus.Success);
        }

        public Task<AdministrativeCredentialResetStoreResult> TryResetCredentialAsync(
            int userId,
            int actorUserId,
            string passwordHash,
            DateTime occurredAtUtc,
            DateTime expiresAtUtc,
            string reason,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
