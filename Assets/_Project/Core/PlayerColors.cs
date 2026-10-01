using UnityEngine;

namespace MR.Core
{
    /// <summary>
    /// Stable per-player colors. Uses FNV-1a instead of string.GetHashCode so every
    /// platform (Mono editor, IL2CPP Android, IL2CPP visionOS) picks the same color.
    /// </summary>
    public static class PlayerColors
    {
        private static readonly Color[] Palette =
        {
            new(0.20f, 0.50f, 0.95f),
            new(0.90f, 0.30f, 0.30f),
            new(0.20f, 0.80f, 0.35f),
            new(0.95f, 0.70f, 0.10f),
            new(0.70f, 0.30f, 0.90f),
            new(0.10f, 0.85f, 0.85f),
            new(0.95f, 0.50f, 0.20f),
            new(0.80f, 0.20f, 0.60f)
        };

        public static Color ForPlayerId(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return Color.gray;
            return Palette[StableHash(playerId) % (uint)Palette.Length];
        }

        public static Color ForClientId(ulong clientId) =>
            Palette[clientId % (ulong)Palette.Length];

        private static uint StableHash(string s)
        {
            uint hash = 2166136261;
            foreach (char c in s)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }
}
