using ControleAcessoVeiculos.Application.Authentication;

namespace ControleAcessoVeiculos.API.Security;

public sealed class AuthenticationSessionOptions
{
    public const string SectionName = "Authentication:Session";

    public int InactivityTimeoutMinutes { get; init; } = 15;
    public int AbsoluteLifetimeHours { get; init; } = 12;
    public string RefreshCookieName { get; init; } = "cav_refresh";

    public AuthenticationSessionPolicy Validate()
    {
        if (InactivityTimeoutMinutes is < 5 or > 240)
        {
            throw new InvalidOperationException(
                "Authentication:Session:InactivityTimeoutMinutes deve estar entre 5 e 240 minutos.");
        }

        if (AbsoluteLifetimeHours is < 1 or > 24)
        {
            throw new InvalidOperationException(
                "Authentication:Session:AbsoluteLifetimeHours deve estar entre 1 e 24 horas.");
        }

        if (string.IsNullOrWhiteSpace(RefreshCookieName) ||
            RefreshCookieName.Any(character =>
                char.IsWhiteSpace(character) || character is ';' or '=' or ','))
        {
            throw new InvalidOperationException(
                "Authentication:Session:RefreshCookieName é inválido.");
        }

        return new AuthenticationSessionPolicy(
            TimeSpan.FromMinutes(InactivityTimeoutMinutes),
            TimeSpan.FromHours(AbsoluteLifetimeHours)).Validate();
    }
}
