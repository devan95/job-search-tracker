using JobSearchTracker.Api.Data;

namespace JobSearchTracker.Api.Tests;

/// <summary>Order 7: the transition rules of OQ-P1.8 and the domain behaviour of an application, without a database.</summary>
public class ApplicationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("saved", "pendingVerification", true)]
    [InlineData("pendingVerification", "pendingVerification", false)]
    [InlineData("applied", "pendingVerification", false)]
    [InlineData("applied", "interview", true)]
    [InlineData("saved", "interview", false)]
    [InlineData("interview", "interview", false)]
    [InlineData("applied", "offer", true)]
    [InlineData("interview", "offer", true)]
    [InlineData("saved", "offer", false)]
    [InlineData("saved", "rejected", false)]
    [InlineData("pendingVerification", "rejected", true)]
    [InlineData("applied", "rejected", true)]
    [InlineData("interview", "rejected", true)]
    [InlineData("offer", "rejected", true)]
    [InlineData("rejected", "rejected", false)]
    [InlineData("saved", "withdrawn", true)]
    [InlineData("applied", "withdrawn", true)]
    [InlineData("rejected", "withdrawn", true)]
    [InlineData("withdrawn", "withdrawn", false)]
    [InlineData("withdrawn", "interview", false)]
    [InlineData("rejected", "interview", false)]
    [InlineData("saved", "applied", false)]
    [InlineData("saved", "saved", false)]
    public void Ordinary_transitions_follow_the_approved_rules(string from, string to, bool allowed) =>
        Assert.Equal(allowed, ApplicationRules.IsAllowedTransition(from, to));

    [Fact]
    public void Any_status_except_withdrawn_may_move_to_withdrawn()
    {
        foreach (var status in ApplicationStatuses.All.Where(s => s != ApplicationStatuses.Withdrawn))
        {
            Assert.True(ApplicationRules.IsAllowedTransition(status, ApplicationStatuses.Withdrawn), status);
        }
    }

    [Theory]
    [InlineData("applied", false, false)]
    [InlineData("interview", false, false)]
    [InlineData("offer", false, false)]
    [InlineData("saved", true, false)]
    [InlineData("pendingVerification", true, false)]
    [InlineData("applied", true, true)]
    [InlineData("saved", false, true)]
    [InlineData("rejected", true, true)]
    [InlineData("rejected", false, true)]
    [InlineData("withdrawn", true, true)]
    [InlineData("withdrawn", false, true)]
    public void Status_and_applied_fact_must_agree(string status, bool applied, bool consistent) =>
        Assert.Equal(consistent, ApplicationRules.IsConsistent(status, applied));

    [Fact]
    public void A_new_application_is_saved_not_applied_and_has_a_created_audit_entry()
    {
        var application = new Application(Guid.NewGuid(), Guid.NewGuid(), Now);

        Assert.Equal(ApplicationStatuses.Saved, application.Status);
        Assert.False(application.IsApplied);
        Assert.Null(application.Method);
        Assert.Null(application.AppliedAtUtc);
        Assert.Null(application.AppliedConfirmedAtUtc);
        var entry = Assert.Single(application.AuditEntries);
        Assert.Equal(ApplicationAuditActions.Created, entry.Action);
        Assert.Null(entry.OldStatus);
        Assert.Equal("saved", entry.NewStatus);
    }

    [Fact]
    public void Confirming_applied_stores_method_manual_and_both_times_separately()
    {
        var application = new Application(Guid.NewGuid(), Guid.NewGuid(), Now);
        var appliedAt = Now.AddDays(-3);

        var entry = application.ConfirmApplied(appliedAt, Now);

        Assert.True(application.IsApplied);
        Assert.Equal(ApplicationStatuses.Applied, application.Status);
        Assert.Equal(ApplicationMethods.Manual, application.Method);
        Assert.Equal(appliedAt, application.AppliedAtUtc);
        Assert.Equal(Now, application.AppliedConfirmedAtUtc);
        Assert.Equal(ApplicationAuditActions.AppliedConfirmed, entry.Action);
        Assert.Equal("saved", entry.OldStatus);
        Assert.False(entry.OldIsApplied);
        Assert.True(entry.NewIsApplied);
        Assert.Equal(appliedAt, entry.NewAppliedAtUtc);
    }

    [Fact]
    public void Later_statuses_keep_the_applied_fact()
    {
        var application = new Application(Guid.NewGuid(), Guid.NewGuid(), Now);
        application.ConfirmApplied(Now, Now);

        foreach (var status in new[] { ApplicationStatuses.Interview, ApplicationStatuses.Offer, ApplicationStatuses.Rejected })
        {
            application.ChangeStatus(status, null, Now);
            Assert.True(application.IsApplied, status);
            Assert.Equal(ApplicationMethods.Manual, application.Method);
            Assert.Equal(Now, application.AppliedAtUtc);
        }
    }

    [Fact]
    public void Confirming_a_withdrawn_application_keeps_its_status_and_adds_the_applied_fact()
    {
        var application = new Application(Guid.NewGuid(), Guid.NewGuid(), Now);
        application.ChangeStatus(ApplicationStatuses.Withdrawn, null, Now);

        application.ConfirmApplied(Now, Now);

        Assert.Equal(ApplicationStatuses.Withdrawn, application.Status);
        Assert.True(application.IsApplied);
    }

    [Fact]
    public void Unapply_clears_the_fact_returns_applied_statuses_to_saved_and_records_the_old_values()
    {
        var application = new Application(Guid.NewGuid(), Guid.NewGuid(), Now);
        application.ConfirmApplied(Now.AddDays(-1), Now);
        application.ChangeStatus(ApplicationStatuses.Interview, null, Now);

        var entry = application.Unapply("Clicked by mistake", Now);

        Assert.False(application.IsApplied);
        Assert.Equal(ApplicationStatuses.Saved, application.Status);
        Assert.Null(application.Method);
        Assert.Null(application.AppliedAtUtc);
        Assert.Null(application.AppliedConfirmedAtUtc);
        Assert.Equal("interview", entry.OldStatus);
        Assert.True(entry.OldIsApplied);
        Assert.Equal(Now.AddDays(-1), entry.OldAppliedAtUtc);
        Assert.Equal("Clicked by mistake", entry.Reason);
    }

    [Fact]
    public void Unapply_keeps_a_rejected_status()
    {
        var application = new Application(Guid.NewGuid(), Guid.NewGuid(), Now);
        application.ConfirmApplied(Now, Now);
        application.ChangeStatus(ApplicationStatuses.Rejected, null, Now);

        application.Unapply("Wrong job", Now);

        Assert.Equal(ApplicationStatuses.Rejected, application.Status);
        Assert.False(application.IsApplied);
    }
}
