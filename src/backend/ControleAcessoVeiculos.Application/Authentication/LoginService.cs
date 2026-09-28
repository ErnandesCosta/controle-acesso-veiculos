using ControleAcessoVeiculos.Domain.Entities;

namespace ControleAcessoVeiculos.Application.Authentication;

public sealed class LoginService(
    IAuthenticationUserStore userStore,
    IPasswordHashService passwordHashService,
    IAccessTokenService accessTokenService,
    IRefreshTokenService refreshTokenService,
    IAuthenticationSessionStore sessionStore,
    AuthenticationSessionPolicy sessionPolicy,
    TimeProvider timeProvider)
{
    public const int MaximumFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<LoginResult> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
        => await sessionStore.ExecuteInTransactionAsync(
            transactionCancellationToken => AuthenticateInTransactionAsync(
                email,
                password,
                transactionCancellationToken),
            cancellationToken);

    private async Task<LoginResult> AuthenticateInTransactionAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = Usuario.NormalizarEmail(email);
        var authenticationUser = await userStore.FindByEmailAsync(
            normalizedEmail,
            cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (authenticationUser is null ||
            !authenticationUser.User.PodeAutenticar(now) ||
            !authenticationUser.ProfileIsActive)
        {
            passwordHashService.PerformDummyVerification(password);
            return LoginResult.InvalidCredentials();
        }

        var passwordVerification = passwordHashService.Verify(
            authenticationUser.User.SenhaHash,
            password);

        if (passwordVerification == PasswordHashVerificationResult.Failed)
        {
            authenticationUser.User.RegistrarTentativaFalha(
                now,
                MaximumFailedAttempts,
                LockoutDuration);
            var audit = authenticationUser.User.BloqueadoAte > now
                ? new AuthenticationAudit(
                    authenticationUser.User.Id,
                    AuthenticationAuditOutcome.AccountLocked,
                    now,
                    authenticationUser.User.BloqueadoAte)
                : null;
            await userStore.SaveChangesAsync(audit, cancellationToken);
            return LoginResult.InvalidCredentials();
        }

        if (passwordVerification == PasswordHashVerificationResult.SuccessRehashNeeded)
        {
            authenticationUser.User.AtualizarSenhaHash(
                passwordHashService.Hash(password),
                now);
        }

        if (authenticationUser.User.TrocaSenhaObrigatoria)
        {
            authenticationUser.User.ConsumirCredencialTemporaria(now);
        }

        authenticationUser.User.RegistrarAutenticacaoBemSucedida(now);
        var refreshToken = refreshTokenService.Create();
        var session = new SessaoAutenticacao(
            authenticationUser.User.Id,
            Guid.NewGuid(),
            refreshToken.Hash,
            now,
            now.Add(sessionPolicy.AbsoluteLifetime));
        sessionStore.Add(session);
        await userStore.SaveChangesAsync(
            new AuthenticationAudit(
                authenticationUser.User.Id,
                AuthenticationAuditOutcome.LoginSucceeded,
                now),
            cancellationToken);

        var token = accessTokenService.Issue(
            authenticationUser.User.Id,
            authenticationUser.User.Email,
            authenticationUser.ProfileName,
            authenticationUser.User.VersaoCredencial,
            authenticationUser.User.TrocaSenhaObrigatoria);

        return LoginResult.Success(
            token,
            refreshToken.Value,
            now,
            sessionPolicy.GetInactivityDeadline(
                session.UltimaAtividadeEm,
                session.ExpiraEm),
            session.ExpiraEm,
            new LoginUser(
                authenticationUser.User.Id,
                authenticationUser.User.Email,
                authenticationUser.ProfileName,
                authenticationUser.User.TrocaSenhaObrigatoria));
    }
}
