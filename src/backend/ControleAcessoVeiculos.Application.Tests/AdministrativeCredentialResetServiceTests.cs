using ControleAcessoVeiculos.Application.Accounts;
using ControleAcessoVeiculos.Application.Authentication;

namespace ControleAcessoVeiculos.Application.Tests;

public sealed class AdministrativeCredentialResetServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 9, 21, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidResetHashesGeneratedCredentialAndReturnsItOnlyToCaller()
    {
        var store = new FakeStore(AdministrativeCredentialResetStoreStatus.Success);
        var service = CreateService(store);

        var result = await service.ResetAsync(userId: 10, actorUserId: 20, CredentialResetReasons.Forgotten, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(AdministrativeCredentialResetStatus.Success, result.Status);
        Assert.Equal("Temporary-test-credential-123!", result.TemporaryCredential);
        Assert.Equal(FixedNow.UtcDateTime.AddMinutes(30),
            result.TemporaryCredentialExpiresAtUtc);
        Assert.Equal("HASH::Temporary-test-credential-123!", store.PasswordHash);
        Assert.Equal(CredentialResetReasons.Forgotten, store.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MotivoLivre")]
    public async Task UnsupportedReasonDoesNotGenerateOrPersistCredential(string? reason)
    {
        var store = new FakeStore(AdministrativeCredentialResetStoreStatus.Success);
        var generator = new FakeGenerator();
        var service = CreateService(store, generator);

        var result = await service.ResetAsync(10, 20, reason, TestContext.Current.CancellationToken);

        Assert.Equal(AdministrativeCredentialResetStatus.Validation, result.Status);
        Assert.Equal(0, generator.Calls);
        Assert.Null(store.PasswordHash);
    }

    [Theory]
    [InlineData(AdministrativeCredentialResetStoreStatus.NotFound,
        AdministrativeCredentialResetStatus.NotFound)]
    [InlineData(AdministrativeCredentialResetStoreStatus.Inactive,
        AdministrativeCredentialResetStatus.Inactive)]
    [InlineData(AdministrativeCredentialResetStoreStatus.SelfReset,
        AdministrativeCredentialResetStatus.SelfReset)]
    public async Task StoreStatusIsMappedWithoutReturningCredential(
        AdministrativeCredentialResetStoreStatus storeStatus,
        AdministrativeCredentialResetStatus expectedStatus)
    {
        var service = CreateService(new FakeStore(storeStatus));

        var result = await service.ResetAsync(10, 20, CredentialResetReasons.SuspectedCompromise, TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Null(result.TemporaryCredential);
    }

    private static AdministrativeCredentialResetService CreateService(
        FakeStore store,
        FakeGenerator? generator = null) =>
        new(
            store,
            new FakePasswordHashService(),
            generator ?? new FakeGenerator(),
            new TemporaryCredentialPolicy(TimeSpan.FromMinutes(30)),
            new FixedTimeProvider(FixedNow));

    private sealed class FakeStore(
        AdministrativeCredentialResetStoreStatus status) : IUserAccountStore
    {
        public string? PasswordHash { get; private set; }
        public string? Reason { get; private set; }

        public Task<AdministrativeCredentialResetStoreResult> TryResetCredentialAsync(
            int userId,
            int actorUserId,
            string passwordHash,
            DateTime occurredAtUtc,
            DateTime expiresAtUtc,
            string reason,
            CancellationToken cancellationToken)
        {
            PasswordHash = passwordHash;
            Reason = reason;
            return Task.FromResult(new AdministrativeCredentialResetStoreResult(status));
        }

        public Task<bool> HasAnyUserAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

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
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UserAccountStoreStateStatus> TrySetActiveAsync(
            int userId,
            bool active,
            int actorUserId,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeGenerator : ITemporaryCredentialGenerator
    {
        public int Calls { get; private set; }

        public string Create()
        {
            Calls++;
            return "Temporary-test-credential-123!";
        }
    }

    private sealed class FakePasswordHashService : IPasswordHashService
    {
        public string Hash(string password) => $"HASH::{password}";
        public PasswordHashVerificationResult Verify(string passwordHash, string password) =>
            PasswordHashVerificationResult.Failed;
        public void PerformDummyVerification(string password)
        {
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
