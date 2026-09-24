using System.Security.Cryptography;
using System.Text;

namespace LibraryBookManager.Api.Identity;

public static class SessionTokens
{
    public static readonly TimeSpan IdleLimit = TimeSpan.FromDays(30);

    /// <summary>256 random bits, URL-safe. Handed to the client once; only its hash is kept.</summary>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string HashOf(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
