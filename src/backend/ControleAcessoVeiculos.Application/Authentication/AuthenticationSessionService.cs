using ControleAcessoVeiculos.Domain.Enums;

namespace ControleAcessoVeiculos.Application.Authentication;

public sealed class AuthenticationSessionService(
    IAuthenticationSessionStore sessionStore,
    IAuthenticationUserStore userStore,
    IRefreshTokenService refreshTokenService,
    IAccessTokenService accessTokenService,
    AuthenticationSessionPolicy sessionPolicy,
    TimeProvider timeProvider)
{
    public Task<RenewSessionResult> RenewAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 512)
        {
            return Task.FromResult(RenewSessionResult.Invalid());
        }

        var presentedHash = refreshTokenService.ComputeHash(refreshToken);
        var successor = refreshTokenService.Create();

        return sessionStore.ExecuteInTransactionAsync(
            async transactionCancellationToken =>
            {
                var authenticationSession =
                    await sessionStore.FindByTokenHashForUpdateAsync(
                        presentedHash,
                        transactionCancellationToken);

                if (authenticationSession is null)
                {
                    return RenewSessionResult.Invalid();
                }

                var now = timeProvider.GetUtcNow().UtcDateTime;
                var session = authenticationSession.Session;

                if (session.FoiConsumida || session.FoiRevogada)
                {
                    await RevokeFamilyAsync(
                        session.FamiliaId,
                        now,
                        MotivoRevogacaoSessao.ReutilizacaoDetectada,
                        transactionCancellationToken);
                    await userStore.SaveChangesAsync(
                        new AuthenticationAudit(
                            session.UsuarioId,
                            AuthenticationAuditOutcome.TokenReuseDetected,
                            now),
                        transactionCancellationToken);
                    return RenewSessionResult.Invalid();
                }

                if (!authenticationSession.User.Ativo ||
                    !authenticationSession.ProfileIsActive)
                {
                    await RevokeFamilyAsync(
                        session.FamiliaId,
                        now,
                        MotivoRevogacaoSessao.ContaDesativada,
                        transactionCancellationToken);
                    await userStore.SaveChangesAsync(
                        new AuthenticationAudit(
                            session.UsuarioId,
                            AuthenticationAuditOutcome.SessionRevoked,
                            now),
                        transactionCancellationToken);
                    return RenewSessionResult.Invalid();
                }

                if (authenticationSession.User.TrocaSenhaObrigatoria &&
                    authenticationSession.User.CredencialTemporariaExpiraEm <= now)
                {
                    await RevokeFamilyAsync(
                        session.FamiliaId,
                        now,
                        MotivoRevogacaoSessao.Expiracao,
                        transactionCancellationToken);
                    await userStore.SaveChangesAsync(null, transactionCancellationToken);
                    return RenewSessionResult.Invalid();
                }

                if (!session.PodeSerRenovada(
                        now,
                        sessionPolicy.InactivityTimeout))
                {
                    session.Revogar(now, MotivoRevogacaoSessao.Expiracao);
                    await userStore.SaveChangesAsync(null, transactionCancellationToken);
                    return RenewSessionResult.Invalid();
                }

                var nextSession = session.Rotacionar(
                    successor.Hash,
                    now,
                    sessionPolicy.InactivityTimeout);
                sessionStore.Add(nextSession);
                await userStore.SaveChangesAsync(null, transactionCancellationToken);

                var accessToken = accessTokenService.Issue(
                    authenticationSession.User.Id,
                    authenticationSession.User.Email,
                    authenticationSession.ProfileName,
                    authenticationSession.User.VersaoCredencial,
                    authenticationSession.User.TrocaSenhaObrigatoria);

                return RenewSessionResult.Success(
                    accessToken,
                    successor.Value,
                    now,
                    sessionPolicy.GetInactivityDeadline(
                        nextSession.UltimaAtividadeEm,
                        nextSession.ExpiraEm),
                    nextSession.ExpiraEm,
                    new LoginUser(
                        authenticationSession.User.Id,
                        authenticationSession.User.Email,
                        authenticationSession.ProfileName,
                        authenticationSession.User.TrocaSenhaObrigatoria));
            },
            cancellationToken);
    }

    public async Task EndAsync(
        string? refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 512)
        {
            return;
        }

        var tokenHash = refreshTokenService.ComputeHash(refreshToken);

        await sessionStore.ExecuteInTransactionAsync(
            async transactionCancellationToken =>
            {
                var authenticationSession =
                    await sessionStore.FindByTokenHashForUpdateAsync(
                        tokenHash,
                        transactionCancellationToken);

                if (authenticationSession is null)
                {
                    return false;
                }

                var now = timeProvider.GetUtcNow().UtcDateTime;
                await RevokeFamilyAsync(
                    authenticationSession.Session.FamiliaId,
                    now,
                    MotivoRevogacaoSessao.Logout,
                    transactionCancellationToken);
                await userStore.SaveChangesAsync(
                    new AuthenticationAudit(
                        authenticationSession.User.Id,
                        AuthenticationAuditOutcome.LogoutSucceeded,
                        now),
                    transactionCancellationToken);
                return true;
            },
            cancellationToken);
    }

    private async Task RevokeFamilyAsync(
        Guid familyId,
        DateTime now,
        MotivoRevogacaoSessao reason,
        CancellationToken cancellationToken)
    {
        var family = await sessionStore.FindFamilyForUpdateAsync(
            familyId,
            cancellationToken);

        foreach (var session in family)
        {
            session.Revogar(now, reason);
        }
    }
}

public sealed record RenewSessionResult(
    bool IsSuccess,
    string? AccessToken,
    DateTime? ExpiresAtUtc,
    string? RefreshToken,
    DateTime? SessionObservedAtUtc,
    DateTime? SessionInactivityExpiresAtUtc,
    DateTime? SessionExpiresAtUtc,
    LoginUser? User)
{
    public static RenewSessionResult Invalid() =>
        new(false, null, null, null, null, null, null, null);

    public static RenewSessionResult Success(
        AccessToken accessToken,
        string refreshToken,
        DateTime sessionObservedAtUtc,
        DateTime sessionInactivityExpiresAtUtc,
        DateTime sessionExpiresAtUtc,
        LoginUser user) =>
        new(
            true,
            accessToken.Value,
            accessToken.ExpiresAtUtc,
            refreshToken,
            sessionObservedAtUtc,
            sessionInactivityExpiresAtUtc,
            sessionExpiresAtUtc,
            user);
}
