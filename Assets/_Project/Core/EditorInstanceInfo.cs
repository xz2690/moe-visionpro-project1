using System;
using System.Text.RegularExpressions;

namespace MR.Core
{
    /// <summary>
    /// Identifies Multiplayer Play Mode (MPPM) instances in the editor.
    /// Virtual players share the main editor's PlayerPrefs and cannot load some
    /// native plugins (Vivox), so a few systems need to know which one they run in.
    /// Always "main player 1" outside the editor.
    /// </summary>
    public static class EditorInstanceInfo
    {
        private static int? _playerNumber;

        /// <summary>True in an MPPM additional editor (Player 2, Player 3, ...).</summary>
        public static bool IsVirtualPlayer
        {
            get
            {
#if UNITY_EDITOR
                return !Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor;
#else
                return false;
#endif
            }
        }

        /// <summary>1 for the main editor (and device builds), 2+ for MPPM virtual players.</summary>
        public static int PlayerNumber => _playerNumber ??= ResolvePlayerNumber();

        private static int ResolvePlayerNumber()
        {
            if (!IsVirtualPlayer) return 1;

            // MPPM launches virtual players with "-name Player N". Depending on how the
            // arguments are split, the number is in the next token or the one after it.
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-name") continue;
                for (int j = i + 1; j < Math.Min(i + 3, args.Length); j++)
                {
                    var match = Regex.Match(args[j], @"\d+");
                    if (match.Success) return int.Parse(match.Value);
                }
            }
            return 0; // virtual player of unknown number
        }
    }
}
