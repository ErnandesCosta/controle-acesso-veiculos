using ControleAcessoVeiculos.Application.Authentication;

namespace ControleAcessoVeiculos.Application.Tests;

public sealed class AuthenticationSessionPolicyTests
{
    private static readonly DateTime Now =
        new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void InactivityDeadlineUsesConfirmedFifteenMinutePolicy()
    {
        var policy = CreatePolicy();

        var deadline = policy.GetInactivityDeadline(
            Now,
            Now.AddHours(12));

        Assert.Equal(Now.AddMinutes(15), deadline);
    }

    [Fact]
    public void InactivityDeadlineNeverExceedsAbsoluteExpiration()
    {
        var policy = CreatePolicy();

        var deadline = policy.GetInactivityDeadline(
            Now.AddHours(11).AddMinutes(50),
            Now.AddHours(12));

        Assert.Equal(Now.AddHours(12), deadline);
    }

    private static AuthenticationSessionPolicy CreatePolicy() =>
        new(TimeSpan.FromMinutes(15), TimeSpan.FromHours(12));
}
