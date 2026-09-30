using ControleAcessoVeiculos.Application.Authentication;
using ControleAcessoVeiculos.Domain.Entities;
using ControleAcessoVeiculos.Domain.Enums;

namespace ControleAcessoVeiculos.Application.Tests;

public sealed class AuthenticatedPasswordChangeServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InvalidInputDoesNotOpenTransaction()
    {
        var store = new FakeStore(CreateUser());
        var service = CreateService(store);

        var result = await service.ChangeAsync(1, string.Empty, "short", TestContext.Current.CancellationToken);

        Assert.Equal(AuthenticatedPasswordChangeStatus.Validation, result.Status);
        Assert.Contains("currentPassword", result.Errors.Keys);
        Assert.Contains("newPassword", result.Errors.Keys);
        Assert.Equal(0, store.TransactionCalls);
    }

    [Fact]
    public async Task WrongCurrentPasswordDoesNotChangeCredentialOrSessions()
    {
        var user = CreateUser();
        var session = CreateSession(1);
        var store = new FakeStore(user, session);
        var service = CreateService(store);

        var result = await service.ChangeAsync(1, "Wrong-password-123!", "New-test-password-456!", TestContext.Current.CancellationToken);

        Assert.Equal(
            AuthenticatedPasswordChangeStatus.InvalidCurrentPassword,
            result.Status);
        Assert.Equal("HASH::Current-test-password-123!", user.SenhaHash);
        Assert.Equal(1, user.VersaoCredencial);
        Assert.False(session.FoiRevogada);
        Assert.Null(store.Audit);
    }

    [Fact]
    public async Task ValidChangeUpdatesCredentialRevokesSessionsAndCreatesMinimalAudit()
    {
        var user = CreateUser();
        var session = CreateSession(1);
        var store = new FakeStore(user, session);
        var service = CreateService(store);

        var result = await service.ChangeAsync(1, "Current-test-password-123!", "New-test-password-456!", TestContext.Current.CancellationToken);

        Assert.Equal(AuthenticatedPasswordChangeStatus.Success, result.Status);
        Assert.Equal("HASH::New-test-password-456!", user.SenhaHash);
        Assert.Equal(2, user.VersaoCredencial);
        Assert.True(session.FoiRevogada);
        Assert.Equal(MotivoRevogacaoSessao.SenhaAlterada, session.MotivoRevogacao);
        Assert.Equal(1, store.Audit?.UserId);
        Assert.Equal(1, store.Audit?.PreviousCredentialVersion);
        Assert.Equal(2, store.Audit?.CurrentCredentialVersion);
        Assert.Equal(FixedNow.UtcDateTime, store.Audit?.OccurredAtUtc);
    }

    [Fact]
    public async Task CurrentPasswordCannotBeReused()
    {
        var user = CreateUser();
        var store = new FakeStore(user);
        var service = CreateService(store);

        var result = await service.ChangeAsync(1, "Current-test-password-123!", "Current-test-password-123!", TestContext.Current.CancellationToken);

        Assert.Equal(AuthenticatedPasswordChangeStatus.Validation, result.Status);
        Assert.Equal(0, store.TransactionCalls);
        Assert.Equal(1, user.VersaoCredencial);
    }

    private static AuthenticatedPasswordChangeService CreateService(FakeStore store) =>
        new(store, new FakePasswordHashService(), new FixedTimeProvider(FixedNow));

    private static Usuario CreateUser() =>
        new(
            "password-change@example.test",
            "HASH::Current-test-password-123!",
            1,
            1);

    private static SessaoAutenticacao CreateSession(int userId) =>
        new(
            userId,
            Guid.NewGuid(),
            "refresh-token-hash",
            FixedNow.UtcDateTime.AddMinutes(-5),
            FixedNow.UtcDateTime.AddHours(1));

    private sealed class FakeStore(
        Usuario? user,
        params SessaoAutenticacao[] sessions) : IAuthenticatedPasswordChangeStore
    {
        public int TransactionCalls { get; private set; }
        public AuthenticatedPasswordChangeAudit? Audit { get; private set; }

        public Task<Usuario?> FindUserForUpdateAsync(
            int userId,
            CancellationToken cancellationToken) =>
            Task.FromResult(userId == 1 ? user : null);

        public Task<IReadOnlyList<SessaoAutenticacao>> FindSessionsForUpdateAsync(
            int userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessaoAutenticacao>>(sessions);

        public Task SaveChangesAsync(
            AuthenticatedPasswordChangeAudit audit,
            CancellationToken cancellationToken)
        {
            Audit = audit;
            return Task.CompletedTask;
        }

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
        {
            TransactionCalls++;
            return await operation(cancellationToken);
        }
    }

    private sealed class FakePasswordHashService : IPasswordHashService
    {
        public string Hash(string password) => $"HASH::{password}";

        public PasswordHashVerificationResult Verify(
            string passwordHash,
            string password) =>
            passwordHash == Hash(password)
                ? PasswordHashVerificationResult.Success
                : PasswordHashVerificationResult.Failed;

        public void PerformDummyVerification(string password)
        {
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
