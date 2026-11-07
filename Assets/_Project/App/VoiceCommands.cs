using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MR.App
{
    public enum CommandKind { None, CreateSession, QuickJoin, JoinCode, EnterRoom, LeaveRoom, LeaveSession, Mute, Unmute, Help }

    public readonly struct VoiceCommand
    {
        public readonly CommandKind Kind;
        /// <summary>The session code for JoinCode, already upper-cased.</summary>
        public readonly string Code;
        /// <summary>Set when the words looked like a code but were incomplete or ambiguous.</summary>
        public readonly string Problem;

        public VoiceCommand(CommandKind kind, string code = null, string problem = null)
        {
            Kind = kind;
            Code = code;
            Problem = problem;
        }

        public static readonly VoiceCommand None = new(CommandKind.None);
    }

    /// <summary>
    /// Turns a recognized sentence into a lobby command. Session codes are spelled out
    /// ("join code F R seven M K M", NATO words work too), so letters, digit words and the
    /// recognizer's usual homophones ("see" for C, "for" for 4) are all accepted.
    /// </summary>
    public static class VoiceCommands
    {
        // Session codes observed from the service: 6 characters, uppercase consonants and digits 2-9.
        public const int CodeLength = 6;

        private static readonly Regex Create = new(@"\b(create|make|start|open|new)\b.*\b(session|room|lobby)\b|\bcreate\b", RegexOptions.IgnoreCase);
        private static readonly Regex QuickJoin = new(@"\bquick\s*join\b|\bjoin\s+(a|any|some)\s+(session|room|lobby)\b|\bfind\s+(a\s+)?(session|room)\b", RegexOptions.IgnoreCase);
        private static readonly Regex JoinCode = new(@"\b(join|enter|use)\b.*\bcode\b(?<rest>.*)$|\bjoin\s+(?<rest2>[A-Za-z0-9 \-]+)$", RegexOptions.IgnoreCase);
        private static readonly Regex EnterRoom = new(@"\b(enter|open|go\s+(in)?to|into)\b.*\broom\b|\benter\b", RegexOptions.IgnoreCase);
        private static readonly Regex LeaveRoom = new(@"\b(leave|exit|close)\b.*\broom\b|\bback\s+to\s+(the\s+)?lobby\b", RegexOptions.IgnoreCase);
        private static readonly Regex LeaveSession = new(@"\b(leave|exit|quit)\b.*\b(session|lobby)\b|\bdisconnect\b|\blog\s*out\b", RegexOptions.IgnoreCase);
        private static readonly Regex Unmute = new(@"\bun\s*mute\b|\bmic(rophone)?\s+on\b", RegexOptions.IgnoreCase);
        private static readonly Regex Mute = new(@"\bmute\b|\bmic(rophone)?\s+off\b|\bbe\s+quiet\b", RegexOptions.IgnoreCase);
        private static readonly Regex Help = new(@"\bhelp\b|\bwhat\s+can\s+(i|you)\s+(say|do)\b", RegexOptions.IgnoreCase);

        private static readonly Dictionary<string, char> Spelled = new()
        {
            // NATO alphabet
            ["alpha"] = 'A', ["bravo"] = 'B', ["charlie"] = 'C', ["delta"] = 'D', ["echo"] = 'E', ["foxtrot"] = 'F',
            ["golf"] = 'G', ["hotel"] = 'H', ["india"] = 'I', ["juliet"] = 'J', ["juliett"] = 'J', ["kilo"] = 'K',
            ["lima"] = 'L', ["mike"] = 'M', ["november"] = 'N', ["oscar"] = 'O', ["papa"] = 'P', ["quebec"] = 'Q',
            ["romeo"] = 'R', ["sierra"] = 'S', ["tango"] = 'T', ["uniform"] = 'U', ["victor"] = 'V', ["whiskey"] = 'W',
            ["x-ray"] = 'X', ["xray"] = 'X', ["yankee"] = 'Y', ["zulu"] = 'Z',
            // Letter names and common mishearings
            ["ay"] = 'A', ["eh"] = 'A', ["bee"] = 'B', ["be"] = 'B', ["see"] = 'C', ["sea"] = 'C', ["cee"] = 'C',
            ["dee"] = 'D', ["gee"] = 'G', ["aitch"] = 'H', ["eye"] = 'I', ["jay"] = 'J', ["kay"] = 'K', ["el"] = 'L',
            ["em"] = 'M', ["en"] = 'N', ["oh"] = 'O', ["pee"] = 'P', ["pea"] = 'P', ["cue"] = 'Q', ["queue"] = 'Q',
            ["are"] = 'R', ["ess"] = 'S', ["tee"] = 'T', ["tea"] = 'T', ["you"] = 'U', ["vee"] = 'V',
            ["double-u"] = 'W', ["ex"] = 'X', ["why"] = 'Y', ["zee"] = 'Z', ["zed"] = 'Z',
            // Digits
            ["zero"] = '0', ["one"] = '1', ["won"] = '1', ["two"] = '2', ["to"] = '2', ["too"] = '2',
            ["three"] = '3', ["free"] = '3', ["four"] = '4', ["for"] = '4', ["five"] = '5', ["six"] = '6',
            ["seven"] = '7', ["eight"] = '8', ["ate"] = '8', ["nine"] = '9',
        };

        public static VoiceCommand Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return VoiceCommand.None;
            string t = text.Trim();

            // Codes first: "join code ..." must not be taken for "join a session" or "enter".
            var join = JoinCode.Match(t);
            if (join.Success)
            {
                string rest = join.Groups["rest"].Success ? join.Groups["rest"].Value : join.Groups["rest2"].Value;
                if (!QuickJoin.IsMatch(t) || join.Groups["rest"].Success)
                {
                    string code = ParseCode(rest, out string problem);
                    if (code != null) return new VoiceCommand(CommandKind.JoinCode, code);
                    if (problem != null) return new VoiceCommand(CommandKind.JoinCode, null, problem);
                }
            }

            if (QuickJoin.IsMatch(t)) return new VoiceCommand(CommandKind.QuickJoin);
            if (Help.IsMatch(t)) return new VoiceCommand(CommandKind.Help);
            if (Unmute.IsMatch(t)) return new VoiceCommand(CommandKind.Unmute);
            if (Mute.IsMatch(t)) return new VoiceCommand(CommandKind.Mute);
            if (LeaveSession.IsMatch(t)) return new VoiceCommand(CommandKind.LeaveSession);
            if (LeaveRoom.IsMatch(t)) return new VoiceCommand(CommandKind.LeaveRoom);
            if (EnterRoom.IsMatch(t)) return new VoiceCommand(CommandKind.EnterRoom);
            if (Create.IsMatch(t)) return new VoiceCommand(CommandKind.CreateSession);
            return VoiceCommand.None;
        }

        /// <summary>
        /// "F R seven M K M" / "foxtrot romeo 7 mike kilo mike" / "FR7MKM" → "FR7MKM".
        /// Returns null when nothing usable was said; sets <paramref name="problem"/> when
        /// something was said but it is not a full code.
        /// </summary>
        public static string ParseCode(string spoken, out string problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(spoken)) return null;

            var code = new StringBuilder();
            foreach (var raw in spoken.Split(' ', ',', '.', ';', ':', '\t'))
            {
                string word = raw.Trim().Trim('"', '\'').ToLowerInvariant();
                if (word.Length == 0 || word == "is" || word == "the" || word == "code" || word == "number") continue;

                if (Spelled.TryGetValue(word, out char spelledChar))
                {
                    code.Append(spelledChar);
                    continue;
                }
                // A run of letters/digits ("FR7MKM", "7", "mk") is taken character by character.
                bool alnum = true;
                foreach (char c in word) if (!char.IsLetterOrDigit(c)) { alnum = false; break; }
                if (alnum && (word.Length == 1 || word.Length >= 2 && LooksLikeCode(word)))
                {
                    code.Append(word.ToUpperInvariant());
                    continue;
                }
                // Any other word means this was not a spelled code.
                if (code.Length == 0) return null;
            }

            if (code.Length == 0) return null;
            string result = code.ToString();
            if (result.Length == CodeLength) return result;
            problem = result.Length < CodeLength
                ? $"I heard {Spell(result)}, that is only {result.Length} characters; codes have {CodeLength}."
                : $"I heard {Spell(result)}, that is too long for a code.";
            return null;
        }

        /// <summary>A whole token counts as part of a code when it has a digit or no vowels (codes have neither "a e i o u").</summary>
        private static bool LooksLikeCode(string word)
        {
            bool hasDigit = false, hasVowel = false;
            foreach (char c in word)
            {
                if (char.IsDigit(c)) hasDigit = true;
                else if ("aeiou".IndexOf(c) >= 0) hasVowel = true;
            }
            return hasDigit || !hasVowel;
        }

        /// <summary>"FR7MKM" → "F R 7 M K M", for speaking a code back.</summary>
        public static string Spell(string code)
        {
            var sb = new StringBuilder();
            foreach (char c in code) { if (sb.Length > 0) sb.Append(' '); sb.Append(c); }
            return sb.ToString();
        }

        public const string HelpText =
            "You can say: create a session, quick join, join code and spell it, enter the room, leave the room, mute, unmute, or leave the session.";
    }
}
