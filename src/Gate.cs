using System.Collections.Generic;

namespace Malmr
{
    /// <summary>
    /// What stands between the local player and one metal's veins, and whether it still does.
    ///
    /// Two things, both needed since Robbin's rule of 2026-09-26: the Pickaxes level the
    /// character has earned has reached the metal's Unlocks level, AND the character has beaten
    /// the boss of that metal's biome at one star through Vandi - which is Vandi's count of
    /// kills by this player reaching BossKills, 2 by default. The level says you have mined the
    /// metal by hand for a while. The boss says you have gone back to the fight that opened its
    /// biome and won it again, harder.
    ///
    /// <b>One answer, three askers.</b> The swing, the console and the unlock message all come
    /// here, so they cannot disagree. The first version of this mod computed "is it open" in
    /// the swing and again in the level-up message, and the two drifted apart by a standing
    /// skill bonus before anybody noticed - see Vein.EarnedLevel. A second half to the rule is a
    /// second chance for that, so the rule lives in one place.
    ///
    /// <b>The local player only.</b> The kill count is read through Vandi for the player at this
    /// keyboard, which is the only player a swing is ever decided for - Vein.Open refuses any
    /// other attacker - and the only one the console and the message speak to.
    ///
    /// A snapshot, built fresh each time it is asked for. Nothing here is cached, because both
    /// halves move while the game runs: the skill on every level-up, the count whenever Vandi
    /// credits a kill, and the rule itself whenever Core hands over a host's config.
    /// </summary>
    internal sealed class Gate
    {
        /// <summary>The Unlocks entry this is about.</summary>
        public string Entry;

        /// <summary>The Unlocks level. -1 means the metal is switched off.</summary>
        public int Unlock;

        /// <summary>The earned Pickaxes level, floored - see Vein.EarnedLevel.</summary>
        public float Level;

        /// <summary>The defeat key the metal also waits for, or null when it needs no boss.</summary>
        public string Boss;

        /// <summary>BossKills at the time of asking. 0 when there is no boss.</summary>
        public int KillsNeeded;

        /// <summary>
        /// The local player's count of that boss, as Vandi keeps it, or Bosses.Unreadable when
        /// Vandi could not be asked. Unreadable counts as not met: a metal that cannot prove its
        /// boss stays shut rather than opening on a guess.
        /// </summary>
        public int Kills;

        internal static Gate For(string entry, float level)
        {
            var gate = new Gate
            {
                Entry = entry,
                Unlock = MalmrConfig.LevelFor(entry),
                Level = level,
                Boss = MalmrConfig.BossFor(entry),
            };

            if (gate.Boss != null)
            {
                gate.KillsNeeded = MalmrConfig.BossKills.Value;
                gate.Kills = Bosses.LocalKills(gate.Boss);
            }

            return gate;
        }

        /// <summary>Switched off in Unlocks, or the cap is 0 - no amount of play opens it.</summary>
        public bool Off
        {
            get { return Unlock < 0 || MalmrConfig.MaxExtraChunks.Value <= 0; }
        }

        public bool LevelMet
        {
            get { return Unlock >= 0 && Level >= Unlock; }
        }

        public bool BossMet
        {
            get { return Boss == null || (Kills >= 0 && Kills >= KillsNeeded); }
        }

        /// <summary>
        /// The chunks a swing at this metal takes beyond the struck ones, right now. The boss is
        /// a gate and nothing more: once it is met, the count is the level arithmetic it always
        /// was, so a player who beats the Elder at Pickaxes 45 gets copper's three chunks at
        /// once, not one to grow from.
        /// </summary>
        public int Extra
        {
            get { return BossMet ? MalmrConfig.ExtraChunks(Level, Unlock) : 0; }
        }

        public bool Open
        {
            get { return Extra > 0; }
        }

        /// <summary>
        /// Which half is still missing, as one token for the console and the scenarios that read
        /// it: none, level, boss, both, or off.
        /// </summary>
        public string Missing
        {
            get
            {
                if (Open) return "none";
                if (Off) return "off";
                if (!LevelMet && !BossMet) return "both";
                return LevelMet ? "boss" : "level";
            }
        }

        /// <summary>The same thing in words, for the console's tail and the Verbose log.</summary>
        public string Why()
        {
            if (Unlock < 0) return "switched off in Unlocks";
            if (MalmrConfig.MaxExtraChunks.Value <= 0) return "MaxExtraChunks is 0";
            if (Open) return "open";

            var wants = new List<string>();

            if (!LevelMet)
                wants.Add("Pickaxes " + Unlock + ", you are at " + (int)Level);

            if (!BossMet)
                wants.Add(Bosses.DisplayName(Boss) + " killed " + KillsNeeded
                          + " time(s) through Vandi, you have "
                          + (Kills < 0 ? "a count Vandi would not give" : Kills.ToString()));

            return "needs " + string.Join(" and ", wants.ToArray());
        }
    }
}
