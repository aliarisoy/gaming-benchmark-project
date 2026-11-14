using RigSight.Core.Security;

namespace RigSight.Core.Tests;

public sealed class PairingSessionTests
{
    [Fact]
    public void Code_IsAlwaysSixDigits()
    {
        var session = new PairingSession();
        Assert.Matches("^[0-9]{6}$", session.Code);
        Assert.False(session.IsExpired);
    }

    [Fact]
    public void CorrectCode_Succeeds()
    {
        var session = new PairingSession();
        Assert.Equal(PairCodeResult.Success, session.TryRedeem(session.Code));
    }

    [Fact]
    public void WrongCode_DoesNotLockBeforeThirdAttempt()
    {
        var session = new PairingSession();
        var wrong = session.Code == "000000" ? "111111" : "000000";

        Assert.Equal(PairCodeResult.WrongCode, session.TryRedeem(wrong));
        Assert.Equal(PairCodeResult.WrongCode, session.TryRedeem(wrong));
        Assert.Equal(PairCodeResult.Success, session.TryRedeem(session.Code));
    }

    [Fact]
    public void ThreeWrongCodes_LockOutSession()
    {
        var session = new PairingSession();
        var wrong = session.Code == "000000" ? "111111" : "000000";

        Assert.Equal(PairCodeResult.WrongCode, session.TryRedeem(wrong));
        Assert.Equal(PairCodeResult.WrongCode, session.TryRedeem(wrong));
        Assert.Equal(PairCodeResult.LockedOut, session.TryRedeem(wrong));
        Assert.Equal(PairCodeResult.LockedOut, session.TryRedeem(session.Code));
        Assert.True(session.IsExpired);
    }

    [Fact]
    public void Regenerate_UnlocksAndChangesCode()
    {
        var session = new PairingSession();
        var wrong = session.Code == "000000" ? "111111" : "000000";
        session.TryRedeem(wrong);
        session.TryRedeem(wrong);
        session.TryRedeem(wrong);

        var oldCode = session.Code;
        var newCode = session.Regenerate();
        Assert.Matches("^[0-9]{6}$", newCode);
        Assert.NotEqual(oldCode, newCode);
        Assert.False(session.IsExpired);
        Assert.Equal(PairCodeResult.Success, session.TryRedeem(newCode));
    }

    [Fact]
    public void ExpiredCode_IsRejected()
    {
        var session = new PairingSession(TimeSpan.FromMilliseconds(20));
        Thread.Sleep(60);

        Assert.True(session.IsExpired);
        Assert.Equal(PairCodeResult.Expired, session.TryRedeem(session.Code));
    }

    [Fact]
    public void ShortOrMissingCode_IsWrongNotException()
    {
        var session = new PairingSession();
        Assert.NotEqual(PairCodeResult.Success, session.TryRedeem(null));
        Assert.NotEqual(PairCodeResult.Success, session.TryRedeem("12"));
    }
}
