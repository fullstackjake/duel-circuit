using System.Collections.Generic;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The checks' DP moves, through the ledger like every other DP change (owner rule: <see cref="Profile.Earn"/> and
    /// <see cref="Profile.Spend"/> only): DP a check hands out is <see cref="Ledger.Debug"/> earned, DP it takes away is
    /// <see cref="Ledger.Debug"/> spent, and a check that puts a save back puts the ledger back with its DP.
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>Checks only: <paramref name="dp"/> more DP (<see cref="Ledger.Debug"/>).</summary>
        private static void DebugAddDp(Profile profile, int dp) => profile.Earn(Ledger.Debug, dp);

        /// <summary>Checks only: DP made exactly <paramref name="dp"/>, the difference earned or spent as <see cref="Ledger.Debug"/>.</summary>
        private static void DebugSetDp(Profile profile, int dp)
        {
            if (dp > profile.Dp) profile.Earn(Ledger.Debug, dp - profile.Dp);
            else profile.Spend(Ledger.Debug, profile.Dp - dp);
        }

        /// <summary>Checks only: DP at least <paramref name="dp"/> (<see cref="Ledger.Debug"/> earned when short).</summary>
        private static void DebugAtLeastDp(Profile profile, int dp) => profile.Earn(Ledger.Debug, dp - profile.Dp);

        /// <summary>A save's DP and its ledger, as a check found them.</summary>
        private sealed class DpSnapshot
        {
            public int Dp;
            public Dictionary<string, int> Earned = new Dictionary<string, int>(), Spent = new Dictionary<string, int>();
        }

        private static DpSnapshot SnapshotDp(Profile profile) => new DpSnapshot
        {
            Dp = profile.Dp, Earned = new Dictionary<string, int>(profile.DpEarned), Spent = new Dictionary<string, int>(profile.DpSpent),
        };

        /// <summary>Puts a save's DP back as <see cref="SnapshotDp"/> found it, with its ledger (so it still reconciles).</summary>
        private static void RestoreDp(Profile profile, DpSnapshot snapshot)
        {
            profile.Dp = snapshot.Dp;
            profile.DpEarned.Clear();
            foreach (var p in snapshot.Earned) profile.DpEarned[p.Key] = p.Value;
            profile.DpSpent.Clear();
            foreach (var p in snapshot.Spent) profile.DpSpent[p.Key] = p.Value;
        }
    }
}
