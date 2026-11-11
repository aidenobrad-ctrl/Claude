using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Pulsebreak.Api.Auth;

public enum PasswordCheck
{
    Wrong,
    Correct,
    /// <summary>Correct, but hashed with older settings than today's. Hash it again and store the result.</summary>
    CorrectNeedsRehash,
}

/// <summary>
/// Turns passwords into Argon2id hashes and checks passwords against them. A password is
/// never stored, logged or compared directly.
///
/// The stored text carries everything needed to check it later: the algorithm, its cost
/// settings and the salt. That is what lets the cost be raised over time; old hashes keep
/// working and are upgraded the next time their owner signs in.
/// </summary>
public sealed class PasswordHasher(IOptions<AuthOptions> options)
{
    const int SaltBytes = 16, HashBytes = 32;

    // Each hash takes real memory and a core for a moment. Capping how many run at once
    // keeps a burst of sign-ins from exhausting the machine.
    readonly SemaphoreSlim _gate = new(Math.Max(2, Environment.ProcessorCount));
    readonly AuthOptions _options = options.Value;

    public async Task<string> HashAsync(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        int memory = _options.Argon2MemoryKb, iterations = _options.Argon2Iterations, lanes = _options.Argon2Parallelism;
        byte[] hash = await DeriveAsync(password, salt, memory, iterations, lanes, HashBytes);
        return $"$argon2id$v=19$m={memory},t={iterations},p={lanes}${Encode(salt)}${Encode(hash)}";
    }

    public async Task<PasswordCheck> VerifyAsync(string password, string stored)
    {
        if (string.IsNullOrEmpty(password) ||
            !TryParse(stored, out int memory, out int iterations, out int lanes, out byte[] salt, out byte[] expected))
            return PasswordCheck.Wrong;

        byte[] actual = await DeriveAsync(password, salt, memory, iterations, lanes, expected.Length);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected)) return PasswordCheck.Wrong;

        bool current = memory == _options.Argon2MemoryKb && iterations == _options.Argon2Iterations && lanes == _options.Argon2Parallelism;
        return current ? PasswordCheck.Correct : PasswordCheck.CorrectNeedsRehash;
    }

    /// <summary>
    /// Does the work of a check and throws the result away. Used when someone signs in to an
    /// account that does not exist, so the reply takes as long as it would for one that does
    /// and timing gives nothing away.
    /// </summary>
    public async Task BurnAsync(string password)
    {
        if (string.IsNullOrEmpty(password)) return;
        await DeriveAsync(password, new byte[SaltBytes], _options.Argon2MemoryKb, _options.Argon2Iterations, _options.Argon2Parallelism, HashBytes);
    }

    async Task<byte[]> DeriveAsync(string password, byte[] salt, int memoryKb, int iterations, int lanes, int length)
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
                {
                    Salt = salt,
                    MemorySize = memoryKb,
                    Iterations = iterations,
                    DegreeOfParallelism = lanes,
                };
                return argon.GetBytes(length);
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    static bool TryParse(string stored, out int memory, out int iterations, out int lanes, out byte[] salt, out byte[] hash)
    {
        memory = iterations = lanes = 0;
        salt = hash = [];

        // $argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>
        string[] parts = (stored ?? "").Split('$');
        if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19") return false;

        foreach (string setting in parts[3].Split(','))
        {
            string[] pair = setting.Split('=');
            if (pair.Length != 2 || !int.TryParse(pair[1], out int value) || value <= 0) return false;
            switch (pair[0])
            {
                case "m": memory = value; break;
                case "t": iterations = value; break;
                case "p": lanes = value; break;
                default: return false;
            }
        }
        // Refuse settings no honest hash of ours would carry, so a tampered row cannot be
        // used to make every check against it enormously expensive.
        if (memory is < 8 or > 1 << 21 || iterations is < 1 or > 64 || lanes is < 1 or > 64) return false;

        return TryDecode(parts[4], out salt) && TryDecode(parts[5], out hash) && salt.Length >= 8 && hash.Length >= 16;
    }

    static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    static bool TryDecode(string text, out byte[] bytes)
    {
        bytes = [];
        string padded = text.PadRight(text.Length + (4 - text.Length % 4) % 4, '=');
        try
        {
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
