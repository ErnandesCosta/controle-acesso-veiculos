namespace ControleAcessoVeiculos.Application.Authentication;

public sealed record LoginResult(
    bool IsSuccess,
    string? AccessToken,
    DateTime? ExpiresAtUtc,
    string? RefreshToken,
    DateTime? SessionObservedAtUtc,
    DateTime? SessionInactivityExpiresAtUtc,
    DateTime? SessionExpiresAtUtc,
    LoginUser? User)
{
    public static LoginResult InvalidCredentials() =>
        new(false, null, null, null, null, null, null, null);

    public static LoginResult Success(
        AccessToken token,
        string refreshToken,
        DateTime sessionObservedAtUtc,
        DateTime sessionInactivityExpiresAtUtc,
        DateTime sessionExpiresAtUtc,
        LoginUser user) =>
        new(
            true,
            token.Value,
            token.ExpiresAtUtc,
            refreshToken,
            sessionObservedAtUtc,
            sessionInactivityExpiresAtUtc,
            sessionExpiresAtUtc,
            user);
}

public sealed record LoginUser(
    int Id,
    string Email,
    string ProfileName,
    bool RequiresPasswordChange);
