using System.Security.Cryptography;

namespace LibraryBookManager.Api.Identity;

/// <summary>
/// REQ-AUTH-002: salted PBKDF2-SHA256. The stored form carries its own iteration count and
/// salt ("pbkdf2-sha256$iterations$salt$hash"), so the count can be raised without
/// invalidating existing passwords.
/// </summary>
public sealed class PasswordHasher
{
    public const int DefaultIterations = 600_000; // OWASP guidance for PBKDF2-HMAC-SHA256
    private const string Scheme = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private readonly int _iterations;
    private readonly Lazy<string> _decoy;

    public PasswordHasher(IConfiguration configuration)
    {
        _iterations = configuration.GetValue("Passwords:Iterations", DefaultIterations);
        if (_iterations < 1) throw new InvalidOperationException("Passwords:Iterations must be positive.");
        _decoy = new Lazy<string>(() => Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltBytes))));
    }

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Scheme}${_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations) || iterations < 1)
            return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    /// Spends the same effort as checking a real password, for sign-ins to an email with no
    /// account, so response time does not reveal whether an account exists.
    /// </summary>
    public void VerifyDecoy(string password) => Verify(password, _decoy.Value);
}
