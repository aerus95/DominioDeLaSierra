using DominioDeLaSierra.Application.Checkout;

namespace DominioDeLaSierra.UnitTests;

public sealed class ProviderReconciliationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 60)]
    [InlineData(3, 240)]
    [InlineData(4, 720)]
    [InlineData(5, 1440)]
    [InlineData(9, 1440)]
    public void Spaces_attempts_with_backoff(int completedAttempts, int delayMinutes)
    {
        var next = ProviderReconciliationRules.NextAttemptAt(completedAttempts, Now);

        Assert.Equal(Now.AddMinutes(delayMinutes), next);
    }

    [Fact]
    public void Skips_a_query_until_the_saved_attempt_is_due()
    {
        var next = ProviderReconciliationRules.NextAttemptAt(1, Now);

        Assert.False(ProviderReconciliationRules.IsDue(next, Now.AddMinutes(14)));
        Assert.True(ProviderReconciliationRules.IsDue(next, next));
        Assert.True(ProviderReconciliationRules.IsDue(null, Now));
    }

    [Theory]
    [InlineData(24)]
    [InlineData(48)]
    [InlineData(24 * 30)]
    public void Does_not_release_stock_because_a_hold_is_old(int hours)
    {
        Assert.False(ProviderReconciliationRules.MayReleaseBecauseOfAge(TimeSpan.FromHours(hours)));
        Assert.False(ProviderReconciliationRules.SupportsQuery(DominioDeLaSierra.Domain.PaymentProvider.Redsys));
        Assert.True(ProviderReconciliationRules.SupportsQuery(DominioDeLaSierra.Domain.PaymentProvider.PayPal));
    }
}
