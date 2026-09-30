using System.Collections.Generic;

namespace Malmr
{
    /// <summary>
    /// What stands between the local player and one metal's veins, and whether it still does.
    ///
    /// Two things, both needed since Robbin's rule of 2026-09-26: the Pickaxes level the
    /// character has earned has reached the metal's Unlocks level, AND the character has beaten
    /// the boss of that metal's biome. How the boss is counted depends on whether Vandi is
    /// installed - through Vandi it is the one-star kill, BossKills (2) of this player's kills;
    /// without it, one kill as the game's own tally has it. Bosses keeps both roads; this only
    /// asks. The level says you have mined the metal by hand for a while. The boss says you have
    /// won the fight that opened its biome, and with Vandi, won it again, harder.
    ///
    /// <b>One answer, four askers.</b> The swing, the bar, the console and the unlock message
    /// all come here, so they cannot disagree. The first version of this mod computed "is it
    /// open" in the swing and again in the level-up message, and the two drifted apart by a
    /// standing skill bonus before anybody noticed - see Vein.EarnedLevel. A second half to the
    /// rule is a second chance for that, so the rule lives in one place.
    ///
    /// <b>The metal, not the deposit.</b> The Mistlands rule - only the giant brains and stone
    /// vein mine there - is about where a deposit stands, and a metal has no place. It is asked
    /// separately, per deposit, in Deposits.HandOnly, by the same three callers that hold a
    /// deposit. Those three ask through For over a Kind, which is every entry a deposit answers
    /// to rather than one.
    ///
    /// <b>Open or shut, nothing in between.</b> Until 2026-09-26 an open metal also carried a
    /// count - how many extra chunks a swing took, growing every ten levels up to a cap. The bar
    /// replaced that mechanic, and there is nothing left to grow into: a metal is either yours to
    /// vein mine or it is not.
    ///
    /// <b>The local player only.</b> The kill count is read for the player at this keyboard,
    /// which is the only player a swing is ever decided for - Vein refuses any other attacker -
    /// and the only one the console and the message speak to.
    ///
    /// A snapshot, built fresh each time it is asked for. Nothing here is cached, because both
    /// halves move while the game runs: the skill on every level-up, the count whenever a boss
    /// dies, and the rule itself whenever Core hands over a host's config.
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

        /// <summary>Kills needed at the time of asking: BossKills through Vandi, 1 without. 0 when there is no boss.</summary>
        public int KillsNeeded;

        /// <summary>
        /// The local player's count of that boss, by whichever count applies, or
        /// Bosses.Unreadable when it could not be read. Unreadable counts as not met: a metal
        /// that cannot prove its boss stays shut rather than opening on a guess.
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
                gate.KillsNeeded = Bosses.KillsNeeded;
                gate.Kills = Bosses.LocalKills(gate.Boss);
            }

            return gate;
        }

        /// <summary>
        /// The gate in front of a whole deposit: every entry it answers to, the one it goes by
        /// first. The first one shut is the answer, so what the player is told is a half that is
        /// really missing; with all of them open it is the deposit's own.
        ///
        /// Every entry and not only the strictest, because a full bar hands out everything the
        /// deposit drops, and no single entry covers two others when their bosses differ: with
        /// Stone:60 on Eikthyr and Obsidian:50 on Moder, neither gate holds the other's boss.
        /// See Deposits.Classify. Where Entries holds one entry, which is every ore deposit and
        /// every rock that drops nothing but stone, this is For(kind.Entry).
        /// </summary>
        internal static Gate For(Deposits.Kind kind, float level)
        {
            Gate own = For(kind.Entry, level);
            if (!own.Open) return own;

            foreach (string entry in kind.Entries)
            {
                if (entry == kind.Entry) continue;

                Gate other = For(entry, level);
                if (!other.Open) return other;
            }

            return own;
        }

        /// <summary>Switched off in Unlocks - no amount of play opens it.</summary>
        public bool Off
        {
            get { return Unlock < 0; }
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
        /// Whether this player may vein mine the metal right now. The boss is a gate and nothing
        /// more, the level likewise: both met, it is open, whichever landed first.
        /// </summary>
        public bool Open
        {
            get { return !Off && LevelMet && BossMet; }
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
            if (Off) return "switched off in Unlocks";
            if (Open) return "open";

            var wants = new List<string>();

            if (!LevelMet)
                wants.Add("Pickaxes " + Unlock + ", you are at " + (int)Level);

            if (!BossMet)
            {
                string have = Kills < 0 ? "a count that could not be read" : Kills.ToString();

                wants.Add(Bosses.ThroughVandi
                    ? Bosses.DisplayName(Boss) + " killed " + KillsNeeded + " time(s) through Vandi, you have " + have
                    : Bosses.DisplayName(Boss) + " killed once by this character, as the game counts it, you have " + have);
            }

            return "needs " + string.Join(" and ", wants.ToArray());
        }

        /// <summary>
        /// What is missing, as the player reads it on screen when a swing in vein mode meets a
        /// shut metal: "Pickaxes 40 and Bonemass beaten at one star through Vandi". Only the
        /// missing halves, so a player who has the level is not told about it again.
        /// </summary>
        public string Needs()
        {
            var wants = new List<string>();

            if (!LevelMet) wants.Add("Pickaxes " + Unlock);
            if (!BossMet) wants.Add(Bosses.Phrase(Boss));

            return string.Join(" and ", wants.ToArray());
        }

        /// <summary>
        /// What opened it, for the unlock message: "Pickaxes 30 and The Elder beaten at one star
        /// through Vandi", or the level alone for a metal with no boss. Both halves, because the
        /// message is the one place the player learns what the rule was, and the boss half is
        /// what says which count applied. Built from the same phrase Needs uses, so what a
        /// player is told they lack and what they are told they earned read alike.
        /// </summary>
        public string Earned()
        {
            string level = "Pickaxes " + Unlock;
            return Boss == null ? level : level + " and " + Bosses.Phrase(Boss);
        }
    }
}
