using System.Collections.Generic;
using Ygo.Engine.Data;

namespace YgoCampaign.Game
{
    /// <summary>
    /// What packs and the grading desk sound like (docs/dev/AUDIO-PASS-1-SPEC-2026-11-20.md, section H): the hooks a pack
    /// screen calls (opened, each card dealt, each card turned) and the grading desk's number beat. Layout code never plays a
    /// pack sound itself, so a rebuilt pack screen keeps calling these. Every rarity from Secret up adds its prize chime (the
    /// duel's) to the burst, so a Secret, a Ghost and a Starlight each sound like themselves.
    /// </summary>
    public static class PackSounds
    {
        /// <summary>The pack tears open.</summary>
        public static void Opened() => Sounds.Play("pack_tear");

        /// <summary>A card dealt in (0 first): a soft tick, a little higher each card.</summary>
        public static void Dealt(int index) => Sounds.PlayExact("draw", 0.35f, 1f + 0.02f * System.Math.Min(index, 8));

        /// <summary>A card turning face-up: everything its reveal sounds like (<see cref="RevealSounds"/>), timed from now.</summary>
        public static void Turned(Rarity rarity, bool hit)
        {
            foreach (var (name, delay, volume) in RevealSounds(rarity, hit)) Sounds.PlayLater(delay, name, volume);
        }

        /// <summary>A reveal's sounds: name, delay from the turn (seconds), volume. A card that isn't a hit only flips.</summary>
        public static IReadOnlyList<(string name, float delay, float volume)> RevealSounds(Rarity rarity, bool hit)
        {
            var sounds = new List<(string, float, float)> { ("flip", 0f, 0.8f) };
            if (!hit) return sounds;
            if (rarity < Rarity.UltraRare)
            {
                sounds.Add(("rare", 0.26f, 0.7f));
                return sounds;
            }
            sounds.Add(("burst", 0.26f, 1f));
            if (ChimeFor(rarity) is string chime) sounds.Add((chime, 0.46f, 0.85f));
            return sounds;
        }

        /// <summary>
        /// The grading desk stamps the number: a 10 bursts, pings, and adds the rarity's prize chime when it has one; below 10,
        /// the rare shimmer.
        /// </summary>
        public static void SlabNumber(Rarity rarity, int slab)
        {
            if (slab < 10)
            {
                Sounds.Play("rare", 0.8f);
                return;
            }
            Sounds.Play("burst", 0.8f);
            Sounds.PlayLater(0.25f, "prize_ping", 0.85f);
            if (ChimeFor(rarity) is string chime) Sounds.PlayLater(0.45f, chime, 0.85f);
        }

        /// <summary>The prize chime a rarity plays (Secret Rare and up), or null.</summary>
        public static string? ChimeFor(Rarity rarity) => rarity switch
        {
            Rarity.GhostGoldRare or Rarity.GhostRare => "prize_ghost",
            Rarity.QuarterCenturySecretRare or Rarity.StarlightRare => "prize_starlight",
            >= Rarity.SecretRare and <= Rarity.CollectorsRare => "prize_secret",
            _ => null,
        };
    }
}
