using System;
using System.Collections.Generic;
using System.Text;

// This file is also compiled into the backend (backend/src/Pulsebreak.Api): party chat and
// reports are cleaned by the same rules as chat in a match. Keep it free of UnityEngine.
#nullable disable

namespace Pulsebreak.Shared.Social
{
    /// <summary>
    /// The rules for chat text, shared by the game, its servers and the backend: what a
    /// message may contain, and the filter that hides strong language when a player has it on.
    ///
    /// The filter matches whole words, and the commonest disguises: letters repeated, digits or
    /// symbols standing in for letters, and endings added to a few stems. It is a courtesy,
    /// not a wall; muting and reporting are what deal with someone set on being abusive.
    /// </summary>
    public static class ChatText
    {
        public const int MaxLength = 140;

        // The word list is kept in ROT13 so it does not read out of the source file. It is
        // decoded once, the first time anything is filtered.
        static readonly string[] EncodedWords = { "shpx", "fuvg", "phag", "ovgpu", "onfgneq", "nffubyr", "qvpx", "pbpx", "chffl", "juber", "fyhg", "jnaxre", "gjng", "cevpx", "obyybpxf", "zbgureshpxre", "snt", "snttbg", "ergneq", "avttre", "avttn", "fcvp", "puvax", "xvxr", "genaal", "qlxr", "pbba", "tbbx", "jrgonpx", "enturnq", "cnxv" };
        // Stems that also match with an ending added ("...ing", "...ed", "...s").
        static readonly string[] EncodedStems = { "shpx", "fuvg", "phag", "ovgpu", "ergneq", "juber", "fyhg", "jnax" };

        static HashSet<string> _words;
        static string[] _stems;

        /// <summary>
        /// Trims, removes control and invisible characters, turns runs of spaces into one and
        /// caps the length. Null if nothing worth sending is left.
        /// </summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var builder = new StringBuilder(Math.Min(text.Length, MaxLength));
            bool space = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    space = builder.Length > 0;
                    continue;
                }
                if (char.IsControl(c) || IsInvisible(c)) continue;
                if (space)
                {
                    builder.Append(' ');
                    space = false;
                }
                if (builder.Length >= MaxLength) break;
                builder.Append(c);
            }
            string cleaned = builder.ToString().Trim();
            if (cleaned.Length > MaxLength) cleaned = cleaned.Substring(0, MaxLength);
            return cleaned.Length == 0 ? null : cleaned;
        }

        /// <summary>The same text with strong language turned into asterisks.</summary>
        public static string Filter(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            Load();
            char[] output = text.ToCharArray();
            int start = -1;
            for (int i = 0; i <= text.Length; i++)
            {
                bool part = i < text.Length && IsWordChar(text[i]);
                if (part && start < 0) start = i;
                if (part || start < 0) continue;
                if (Blocked(text, start, i - start))
                {
                    for (int j = start; j < i; j++) output[j] = '*';
                }
                start = -1;
            }
            return new string(output);
        }

        /// <summary>True if the text has anything the filter would hide.</summary>
        public static bool HasStrongLanguage(string text) => !string.IsNullOrEmpty(text) && Filter(text) != text;

        static bool Blocked(string text, int start, int length)
        {
            string word = Normalize(text, start, length);
            if (word.Length < 3) return false;
            if (_words.Contains(word)) return true;
            foreach (string stem in _stems)
            {
                if (word.StartsWith(stem, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>Lower case, look-alike digits and symbols turned into the letters they stand for, and repeated letters made single.</summary>
        static string Normalize(string text, int start, int length)
        {
            var builder = new StringBuilder(length);
            char last = '\0';
            for (int i = start; i < start + length; i++)
            {
                char c = Letter(char.ToLowerInvariant(text[i]));
                if (c == last) continue;
                builder.Append(c);
                last = c;
            }
            return builder.ToString();
        }

        static char Letter(char c)
        {
            switch (c)
            {
                case '0': return 'o';
                case '1': return 'i';
                case '!': return 'i';
                case '3': return 'e';
                case '4': return 'a';
                case '@': return 'a';
                case '5': return 's';
                case '$': return 's';
                case '7': return 't';
                default: return c;
            }
        }

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '@' || c == '$' || c == '!';

        static bool IsInvisible(char c) =>
            c == '\u200B' || c == '\u200C' || c == '\u200D' || c == '\u2060' || c == '\uFEFF' || (c >= '\u202A' && c <= '\u202E');

        static void Load()
        {
            if (_words != null) return;
            var words = new HashSet<string>(StringComparer.Ordinal);
            foreach (string encoded in EncodedWords)
            {
                string plain = Rot13(encoded);
                words.Add(Normalize(plain, 0, plain.Length));
            }
            var stems = new string[EncodedStems.Length];
            for (int i = 0; i < stems.Length; i++)
            {
                string plain = Rot13(EncodedStems[i]);
                stems[i] = Normalize(plain, 0, plain.Length);
            }
            _stems = stems;
            _words = words;
        }

        static string Rot13(string text)
        {
            char[] chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c >= 'a' && c <= 'z') chars[i] = (char)('a' + (c - 'a' + 13) % 26);
            }
            return new string(chars);
        }
    }
}
