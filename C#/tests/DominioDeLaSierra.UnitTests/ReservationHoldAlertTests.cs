using DominioDeLaSierra.Application.Checkout;

namespace DominioDeLaSierra.UnitTests;

public sealed class ReservationHoldAlertTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Threshold = TimeSpan.FromHours(24);

    [Fact]
    public void Keeps_quiet_one_minute_before_the_threshold()
    {
        var age = Threshold - TimeSpan.FromMinutes(1);

        var decision = ReservationHoldAlertRules.Decide(age, Threshold, paidUnsettled: false);

        Assert.False(decision.Open);
        Assert.False(ReservationHoldAlertRules.IsDue(Now - age, Now, Threshold));
    }

    [Fact]
    public void Opens_a_hold_exactly_at_the_threshold()
    {
        var decision = ReservationHoldAlertRules.Decide(Threshold, Threshold, paidUnsettled: false);

        Assert.True(decision.Open);
        Assert.Equal(ReservationHoldSeverity.Held, decision.Severity);
        Assert.Equal(ReservationHoldReconciliation.Holding, decision.Reconciliation);
        Assert.True(ReservationHoldAlertRules.IsDue(Now - Threshold, Now, Threshold));
    }

    [Fact]
    public void Opens_a_hold_one_minute_after_the_threshold()
    {
        var age = Threshold + TimeSpan.FromMinutes(1);

        var decision = ReservationHoldAlertRules.Decide(age, Threshold, paidUnsettled: false);

        Assert.True(decision.Open);
        Assert.Equal(ReservationHoldSeverity.Held, decision.Severity);
        Assert.True(ReservationHoldAlertRules.IsDue(Now - age, Now, Threshold));
    }

    [Fact]
    public void Opens_a_critical_incident_without_waiting_for_the_threshold()
    {
        var decision = ReservationHoldAlertRules.Decide(TimeSpan.FromMinutes(1), Threshold, paidUnsettled: true);

        Assert.True(decision.Open);
        Assert.Equal(ReservationHoldSeverity.Critical, decision.Severity);
        Assert.Equal(ReservationHoldReconciliation.PaidUnsettled, decision.Reconciliation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(24 * 30 + 1)]
    public void Rejects_an_unusable_threshold_and_keeps_24_hours(int hours)
    {
        Assert.Equal(ReservationHoldAlertRules.DefaultThreshold, ReservationHoldAlertRules.Threshold(hours));
    }

    [Fact]
    public void Accepts_a_configured_threshold()
    {
        Assert.Equal(TimeSpan.FromHours(24), ReservationHoldAlertRules.Threshold(24));
        Assert.Equal(TimeSpan.FromHours(48), ReservationHoldAlertRules.Threshold(48));
    }
}
