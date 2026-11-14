using System.Security.Cryptography;

namespace RigSight.Core.Security;

public enum PairCodeResult { Success, WrongCode, Expired, LockedOut }

/// <summary>Short-lived, rate-limited pairing code shown on the PC.</summary>
public sealed class PairingSession
{
    private readonly object _gate = new();
    private const int MaxAttempts = 3;
    private string _code = "";
    private DateTimeOffset _expiresAt;
    private int _attempts;
    private bool _locked;

    public PairingSession(TimeSpan? lifetime = null)
    {
        Lifetime = lifetime ?? TimeSpan.FromMinutes(10);
        Regenerate();
    }

    public TimeSpan Lifetime { get; }

    public string Code
    {
        get { lock (_gate) return _code; }
    }

    public DateTimeOffset ExpiresAt
    {
        get { lock (_gate) return _expiresAt; }
    }

    public bool IsExpired
    {
        get { lock (_gate) return DateTimeOffset.UtcNow >= _expiresAt || _locked; }
    }

    public string Regenerate()
    {
        lock (_gate)
        {
            _code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            _expiresAt = DateTimeOffset.UtcNow + Lifetime;
            _attempts = 0;
            _locked = false;
            return _code;
        }
    }

    public PairCodeResult TryRedeem(string? candidate)
    {
        lock (_gate)
        {
            if (_locked) return PairCodeResult.LockedOut;
            if (DateTimeOffset.UtcNow >= _expiresAt) return PairCodeResult.Expired;

            // Constant-time compare avoids leaking the code digit-by-digit.
            var input = (candidate ?? "").PadRight(6).Substring(0, 6);
            var equal = CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(input),
                System.Text.Encoding.ASCII.GetBytes(_code));
            if (equal)
            {
                _attempts = 0;
                return PairCodeResult.Success;
            }

            _attempts++;
            if (_attempts >= MaxAttempts)
            {
                _locked = true;
                return PairCodeResult.LockedOut;
            }
            return PairCodeResult.WrongCode;
        }
    }
}
