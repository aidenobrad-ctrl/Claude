using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

// This file is also compiled into the backend (backend/src/Pulsebreak.Api): the API writes
// tickets with it and game servers check them with it. Keep it free of UnityEngine.
#nullable disable

namespace Pulsebreak.Shared.Net
{
    /// <summary>What a valid ticket says about the player holding it.</summary>
    public sealed class TicketClaims
    {
        /// <summary>The account's id.</summary>
        public string AccountId = "";
        /// <summary>The account's username. A game server shows this, not whatever the client claims.</summary>
        public string Name = "";
        public bool EmailVerified;
        /// <summary>"casual" or "ranked".</summary>
        public string Mode = "";
        /// <summary>Seconds since 1970 (UTC) after which the ticket is worthless.</summary>
        public long ExpiresAt;
        /// <summary>
        /// Matchmaking's id for the one match this ticket gets into, or empty for a ticket good
        /// for any server. A matchmade server admits only tickets for its own match.
        /// </summary>
        public string MatchId = "";
    }

    /// <summary>
    /// A join ticket: a short note from the backend saying "this connection belongs to this
    /// account", signed so that a game server can believe it without asking the backend.
    ///
    /// A player gets one from the backend just before joining and hands it to the game
    /// server, which checks the signature with a key only the backend and the game servers
    /// know. A ticket lasts about a minute and says nothing secret, so seeing one is of no
    /// use to anyone once it has expired.
    ///
    /// The format is a standard signed token (JWS, HMAC-SHA256), written and read here so
    /// that both ends use the same code.
    /// </summary>
    public static class GameTicket
    {
        public const string Audience = "pulsebreak-game";
        public const string Casual = "casual", Ranked = "ranked";
        /// <summary>A key shorter than this is refused: it would be guessable.</summary>
        public const int MinKeyBytes = 32;
        /// <summary>Longest ticket a game server will look at.</summary>
        public const int MaxLength = 700;

        const string Header = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";      // {"alg":"HS256","typ":"JWT"}

        public static string Create(TicketClaims claims, byte[] key, long issuedAt)
        {
            if (key == null || key.Length < MinKeyBytes) throw new ArgumentException("The ticket key must be at least " + MinKeyBytes + " bytes.");

            var json = new StringBuilder(160);
            json.Append("{\"aud\":");
            AppendString(json, Audience);
            json.Append(",\"sub\":");
            AppendString(json, claims.AccountId);
            json.Append(",\"name\":");
            AppendString(json, claims.Name);
            json.Append(",\"ev\":").Append(claims.EmailVerified ? "true" : "false");
            json.Append(",\"mode\":");
            AppendString(json, claims.Mode);
            if (!string.IsNullOrEmpty(claims.MatchId))
            {
                json.Append(",\"match\":");
                AppendString(json, claims.MatchId);
            }
            json.Append(",\"iat\":").Append(issuedAt.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"exp\":").Append(claims.ExpiresAt.ToString(CultureInfo.InvariantCulture));
            json.Append('}');

            string signed = Header + "." + Encode(Encoding.UTF8.GetBytes(json.ToString()));
            return signed + "." + Encode(Sign(signed, key));
        }

        /// <summary>
        /// True if the ticket was signed with this key, is meant for game servers and has not
        /// expired. On failure <paramref name="problem"/> says why, in words fit for a log.
        /// </summary>
        /// <param name="now">Seconds since 1970 (UTC).</param>
        public static bool TryVerify(string ticket, byte[] key, long now, out TicketClaims claims, out string problem)
        {
            claims = null;
            if (key == null || key.Length < MinKeyBytes) return Fail(out problem, "no ticket key is configured");
            if (string.IsNullOrEmpty(ticket)) return Fail(out problem, "no ticket was presented");
            if (ticket.Length > MaxLength) return Fail(out problem, "the ticket is too long");

            int firstDot = ticket.IndexOf('.');
            int lastDot = ticket.LastIndexOf('.');
            if (firstDot <= 0 || lastDot <= firstDot + 1 || lastDot == ticket.Length - 1) return Fail(out problem, "the ticket is malformed");

            // Only one way of signing is accepted, and it is compared as text: a ticket cannot
            // talk its way into a weaker check by claiming a different algorithm.
            if (!string.Equals(ticket.Substring(0, firstDot), Header, StringComparison.Ordinal)) return Fail(out problem, "the ticket is not signed the way this server expects");

            if (!TryDecode(ticket.Substring(lastDot + 1), out byte[] signature)) return Fail(out problem, "the ticket is malformed");
            if (!SameBytes(signature, Sign(ticket.Substring(0, lastDot), key))) return Fail(out problem, "the ticket's signature is wrong");

            // From here on the contents are known to be what the backend wrote.
            if (!TryDecode(ticket.Substring(firstDot + 1, lastDot - firstDot - 1), out byte[] payload)) return Fail(out problem, "the ticket is malformed");
            Dictionary<string, object> fields = FlatJson.Parse(Encoding.UTF8.GetString(payload));
            if (fields == null) return Fail(out problem, "the ticket is malformed");

            if (Text(fields, "aud") != Audience) return Fail(out problem, "the ticket is for something else");
            if (!(fields.TryGetValue("exp", out object exp) && exp is long expiresAt)) return Fail(out problem, "the ticket has no expiry");
            if (now >= expiresAt) return Fail(out problem, "the ticket has expired");

            claims = new TicketClaims
            {
                AccountId = Text(fields, "sub"),
                Name = Text(fields, "name"),
                EmailVerified = fields.TryGetValue("ev", out object verified) && verified is bool flag && flag,
                Mode = Text(fields, "mode"),
                ExpiresAt = expiresAt,
                MatchId = Text(fields, "match"),
            };
            if (claims.AccountId.Length == 0 || claims.Name.Length == 0)
            {
                claims = null;
                return Fail(out problem, "the ticket names nobody");
            }
            problem = "";
            return true;
        }

        static bool Fail(out string problem, string why)
        {
            problem = why;
            return false;
        }

        static string Text(Dictionary<string, object> fields, string name) =>
            fields.TryGetValue(name, out object value) && value is string text ? text : "";

        static byte[] Sign(string text, byte[] key)
        {
            using (var hmac = new HMACSHA256(key)) return hmac.ComputeHash(Encoding.ASCII.GetBytes(text));
        }

        /// <summary>Compares without stopping at the first difference, so timing says nothing about how close a forgery was.</summary>
        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int difference = 0;
            for (int i = 0; i < a.Length; i++) difference |= a[i] ^ b[i];
            return difference == 0;
        }

        /// <summary>Base64 for URLs: '-' and '_' in place of '+' and '/', no padding.</summary>
        static string Encode(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        static bool TryDecode(string text, out byte[] bytes)
        {
            bytes = null;
            foreach (char c in text)
            {
                bool allowed = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!allowed) return false;
            }
            string padded = text.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
                case 1: return false;
            }
            try
            {
                bytes = Convert.FromBase64String(padded);
            }
            catch (FormatException)
            {
                return false;
            }

            // The same bytes can be spelled more than one way in base64. Only the spelling
            // this code would have written is accepted, so a ticket has exactly one form.
            if (Encode(bytes) == text) return true;
            bytes = null;
            return false;
        }

        static void AppendString(StringBuilder json, string value)
        {
            json.Append('"');
            foreach (char c in value ?? "")
            {
                if (c == '"' || c == '\\') json.Append('\\').Append(c);
                else if (c < ' ') json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else json.Append(c);
            }
            json.Append('"');
        }
    }

    /// <summary>
    /// Reads one JSON object whose values are all text, whole numbers, true, false or null:
    /// exactly what a ticket contains, and nothing more. Anything else makes it give up.
    /// </summary>
    static class FlatJson
    {
        /// <returns>Field name to string, long, bool or null; or null if the text is not such an object.</returns>
        public static Dictionary<string, object> Parse(string text)
        {
            var fields = new Dictionary<string, object>();
            int i = 0;
            Skip(text, ref i);
            if (!Take(text, ref i, '{')) return null;
            Skip(text, ref i);
            if (Take(text, ref i, '}')) return End(text, i) ? fields : null;

            while (true)
            {
                Skip(text, ref i);
                if (!ReadString(text, ref i, out string name)) return null;
                Skip(text, ref i);
                if (!Take(text, ref i, ':')) return null;
                Skip(text, ref i);
                if (!ReadValue(text, ref i, out object value)) return null;
                if (fields.ContainsKey(name)) return null;           // a repeated field is a trick, not a ticket
                fields[name] = value;

                Skip(text, ref i);
                if (Take(text, ref i, ',')) continue;
                if (Take(text, ref i, '}')) return End(text, i) ? fields : null;
                return null;
            }
        }

        static bool End(string text, int i)
        {
            Skip(text, ref i);
            return i == text.Length;
        }

        static void Skip(string text, ref int i)
        {
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t' || text[i] == '\n' || text[i] == '\r')) i++;
        }

        static bool Take(string text, ref int i, char expected)
        {
            if (i >= text.Length || text[i] != expected) return false;
            i++;
            return true;
        }

        static bool ReadValue(string text, ref int i, out object value)
        {
            value = null;
            if (i >= text.Length) return false;
            if (text[i] == '"')
            {
                bool ok = ReadString(text, ref i, out string s);
                value = s;
                return ok;
            }
            if (Word(text, ref i, "true")) { value = true; return true; }
            if (Word(text, ref i, "false")) { value = false; return true; }
            if (Word(text, ref i, "null")) return true;

            int start = i;
            if (i < text.Length && text[i] == '-') i++;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9') i++;
            if (i == start || i - start > 19) return false;
            if (!long.TryParse(text.Substring(start, i - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number)) return false;
            value = number;
            return true;
        }

        static bool Word(string text, ref int i, string word)
        {
            if (string.CompareOrdinal(text, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }

        static bool ReadString(string text, ref int i, out string value)
        {
            value = null;
            if (!Take(text, ref i, '"')) return false;
            var result = new StringBuilder();
            while (i < text.Length)
            {
                char c = text[i++];
                if (c == '"')
                {
                    value = result.ToString();
                    return true;
                }
                if (c < ' ') return false;
                if (c != '\\')
                {
                    result.Append(c);
                    continue;
                }

                if (i >= text.Length) return false;
                char escape = text[i++];
                switch (escape)
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        if (i + 4 > text.Length) return false;
                        if (!int.TryParse(text.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code)) return false;
                        result.Append((char)code);
                        i += 4;
                        break;
                    default:
                        return false;
                }
            }
            return false;
        }
    }
}
