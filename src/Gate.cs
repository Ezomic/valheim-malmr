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
    /// <b>One answer, four askers.</b> The swing, the bar, the console and the unlock message
    /// all come here, so they cannot disagree. The first version of this mod computed "is it
    /// open" in the swing and again in the level-up message, and the two drifted apart by a
    /// standing skill bonus before anybody noticed - see Vein.EarnedLevel. A second half to the
    /// rule is a second chance for that, so the rule lives in one place.
    ///
    /// <b>Open or shut, nothing in between.</b> Until 2026-09-26 an open metal also carried a
    /// count - how many extra chunks a swing took, growing every ten levels up to a cap. The bar
    /// replaced that mechanic, and there is nothing left to grow into: a metal is either yours to
    /// vein mine or it is not.
    ///
    /// <b>The local player only.</b> The kill count is read through Vandi for the player at this
    /// keyboard, which is the only player a swing is ever decided for - Vein refuses any other
    /// attacker - and the only one the console and the message speak to.
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
                wants.Add(Bosses.DisplayName(Boss) + " killed " + KillsNeeded
                          + " time(s) through Vandi, you have "
                          + (Kills < 0 ? "a count Vandi would not give" : Kills.ToString()));

            return "needs " + string.Join(" and ", wants.ToArray());
        }

        /// <summary>
        /// What is missing, as the player reads it on screen when a swing in vein mode meets a
        /// shut metal: "Pickaxes 60 and Bonemass beaten at one star". Only the missing halves,
        /// so a player who has the level is not told about it again.
        ///
        /// The boss half is said in stars, not kills, because stars are what the player sees on
        /// the boss: Vandi brings a boss back one star harder per repeat kill, so kill number N
        /// is the boss at N-1 stars. That is only a translation of BossKills, never a second
        /// rule - a host who sets BossKills 3 is asking for the two-star kill, and that is what
        /// the line says.
        /// </summary>
        public string Needs()
        {
            var wants = new List<string>();

            if (!LevelMet) wants.Add("Pickaxes " + Unlock);

            if (!BossMet)
            {
                string boss = Bosses.DisplayName(Boss);
                int stars = KillsNeeded - 1;

                wants.Add(stars <= 0
                    ? boss + " beaten"
                    : boss + " beaten at " + (stars == 1 ? "one star" : stars + " stars"));
            }

            return string.Join(" and ", wants.ToArray());
        }
    }
}
