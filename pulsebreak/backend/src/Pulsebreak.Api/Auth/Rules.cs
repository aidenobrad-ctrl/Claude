using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Pulsebreak.Api.Auth;

/// <summary>One thing wrong with one field of a request.</summary>
public sealed record FieldError(string Field, string Message);

/// <summary>
/// What counts as an acceptable email address, username and password. Every request is
/// checked here on the server; whatever the game client checks first is only a courtesy.
/// </summary>
public static partial class Rules
{
    public const int MinPasswordLength = 10, MaxPasswordLength = 128;
    public const int MinUsernameLength = 3, MaxUsernameLength = 20;
    public const int MaxEmailLength = 254;

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex UsernamePattern();

    static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "administrator", "moderator", "mod", "support", "system", "server", "staff", "pulsebreak", "official", "null", "undefined",
    };

    // Not a substitute for a breached-password service, which is worth adding before launch.
    // It stops the passwords that are tried first against every account.
    static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "1234567890", "12345678910", "0123456789", "0987654321", "1q2w3e4r5t", "1qaz2wsx3edc", "qwertyuiop", "qwerty12345", "qwerty123456",
        "password12", "password123", "password1234", "passwordpassword", "iloveyou12", "iloveyou123", "abcdefghij", "abc1234567",
        "letmein123", "welcome123", "football123", "baseball123", "superman123", "trustno1234", "dragon12345", "monkey12345",
        "1111111111", "0000000000", "aaaaaaaaaa", "changeme123", "admin12345", "adminadmin", "p@ssw0rd123", "pa55word123",
    };

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
    public static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();

    public static string? CheckEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "Enter an email address.";
        email = email.Trim();
        if (email.Length > MaxEmailLength) return "That email address is too long.";

        // One address, with something on both sides of the @ and a dot in the domain.
        int at = email.IndexOf('@');
        bool shaped = at > 0 && at == email.LastIndexOf('@') && email.IndexOf('.', at) > at + 1 && !email.EndsWith('.') &&
                      !email.Any(char.IsWhiteSpace) && !email.Any(char.IsControl);
        if (!shaped || !MailAddress.TryCreate(email, out MailAddress? parsed) || parsed.Address != email) return "That does not look like an email address.";
        return null;
    }

    public static string? CheckUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return "Choose a username.";
        username = username.Trim();
        if (username.Length < MinUsernameLength) return $"A username needs at least {MinUsernameLength} characters.";
        if (username.Length > MaxUsernameLength) return $"A username can be at most {MaxUsernameLength} characters.";
        if (!UsernamePattern().IsMatch(username)) return "A username can use letters, numbers and underscores.";
        if (ReservedNames.Contains(username)) return "That username is not available.";
        return null;
    }

    /// <param name="username">If given, the password may not contain it.</param>
    /// <param name="email">If given, the password may not contain the part before the @.</param>
    public static string? CheckPassword(string? password, string? username = null, string? email = null)
    {
        if (string.IsNullOrEmpty(password)) return "Choose a password.";
        if (password.Length < MinPasswordLength) return $"A password needs at least {MinPasswordLength} characters.";
        if (password.Length > MaxPasswordLength) return $"A password can be at most {MaxPasswordLength} characters.";
        if (password.Distinct().Count() < 4 || CommonPasswords.Contains(password)) return "That password is too easy to guess.";

        if (!string.IsNullOrWhiteSpace(username) && password.Contains(username.Trim(), StringComparison.OrdinalIgnoreCase))
            return "A password cannot contain your username.";
        string local = (email ?? "").Split('@')[0].Trim();
        if (local.Length >= 4 && password.Contains(local, StringComparison.OrdinalIgnoreCase))
            return "A password cannot contain your email address.";
        return null;
    }
}
