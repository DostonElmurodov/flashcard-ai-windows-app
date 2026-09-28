using System.Text;
using System.Text.RegularExpressions;
using Mavrylo.Models;
using Microsoft.AspNetCore.Identity;
namespace Mavrylo.Services;

public sealed class AccountCredentialsException(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public static class EmailCredentials
{
    // Match .NET's Unicode whitespace trimming in database lookups of older stored addresses.
    public const string TrimCharacters = " \t\n\r\v\f\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000";
    private static readonly PasswordHasher<AppUser> Hasher = new();
    private static readonly AppUser DummyUser = new();
    private static readonly string DummyHash = Hasher.HashPassword(DummyUser, "unused-account-timing-password");
    public static string Normalize(string? email) => email?.Trim().ToLowerInvariant() ?? "";
    public static bool ValidEmail(string email)
    {
        if (email.Length > 254 || email.Any(c => c > 127)) return false;
        var parts = email.Split('@');
        if (parts.Length != 2 || parts[0].Length is < 1 or > 64 || parts[0].StartsWith('.') || parts[0].EndsWith('.') || parts[0].Contains("..")) return false;
        if (!Regex.IsMatch(parts[0], @"\A[a-z0-9.!#$%&'*+/=?^_`{|}~-]+\z", RegexOptions.CultureInvariant)) return false;
        var labels = parts[1].Split('.');
        return labels.Length >= 2 && labels.All(label => label.Length is >= 1 and <= 63 && Regex.IsMatch(label, @"\A[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\z", RegexOptions.CultureInvariant));
    }
    public static bool ValidPassword(string? password) => password != null && !string.IsNullOrWhiteSpace(password)
        && password.EnumerateRunes().Count() is >= 8 and <= 64;
    public static string Hash(AppUser user, string password) => Hasher.HashPassword(user, password);
    public static bool Verify(AppUser? user, string password)
    {
        if (user?.PasswordHash == null) { Hasher.VerifyHashedPassword(DummyUser, DummyHash, password); return false; }
        try
        {
            if (user.PasswordHash.StartsWith("$2", StringComparison.Ordinal))
                return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
            return Hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or BCrypt.Net.SaltParseException) { return false; }
    }
}
