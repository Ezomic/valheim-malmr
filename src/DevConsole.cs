using System.Collections.Generic;
using HarmonyLib;

namespace Malmr
{
    /// <summary>
    /// `malmr` in the console: your Pickaxes level, what each metal needs - the level and the
    /// boss - and gives you at it, which of the two is still missing, and what every deposit in
    /// this world counts as.
    ///
    /// It exists for two readers. A player who wants to know how far off silver is, without
    /// swinging at a silver vein to find out - and since the boss half, whether it is the level
    /// or Moder they are still short of. And a Devkit scenario, which cannot swing a pickaxe or
    /// put a deposit in the world, but can set the skill, seed Vandi's kill record, run this and
    /// assert on what it printed - so the unlock table, the level arithmetic, the boss gate and
    /// the drop-based classification can be proved in game even though the swing itself cannot.
    ///
    /// <b>isCheat: false, and that is honest rather than lax.</b> Every line only reads: the
    /// config, the player's own skill, Vandi's kill count, and prefab data every client already
    /// holds. Nothing here changes a rule, a skill or the world. A cheat command would need devcommands typed
    /// first, and running any cheat marks the character as having cheated - which Dyrr reads
    /// at the dev server's door.
    ///
    /// <b>The output is written to be asserted on.</b> Facts are name=value tokens with no
    /// spaces inside them, because Devkit's `printed` step matches a substring.
    /// </summary>
    internal static class DevConsole
    {
        /// <summary>
        /// Process-wide, not per world. Terminal's command table is a private static that
        /// nothing clears, so a second registration would leave a duplicate behind.
        /// </summary>
        private static bool _registered;

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        internal static class Hook
        {
            private static void Postfix()
            {
                if (_registered) return;
                _registered = true;

                new Terminal.ConsoleCommand("malmr",
                    "what your Pickaxes level and boss kills open: each metal's unlock level and "
                    + "boss, which is still missing, the extra chunks a swing takes now, and what "
                    + "every deposit in this world counts as",
                    OnCommand, isCheat: false);
            }
        }

        private static void OnCommand(Terminal.ConsoleEventArgs args)
        {
            Terminal term = args.Context;
            if (term == null) return;

            Player player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null)
            {
                term.AddString("malmr: no world loaded");
                return;
            }

            // The level the swing reads, which is the earned one - see Vein.EarnedLevel. A bonus
            // on top is said on its own line so that a player looking at "+2" on their skills
            // page and a different number here knows why, rather than suspecting the mod.
            float level = Vein.EarnedLevel(player);
            float buffed = player.GetSkillLevel(Skills.SkillType.Pickaxes);

            term.AddString("malmr enabled=" + (MalmrConfig.Enabled.Value ? "yes" : "no")
                + " pickaxes=" + (int)level
                + " maxextra=" + MalmrConfig.MaxExtraChunks.Value
                + " step=" + MalmrConfig.LevelsPerExtraChunk.Value
                + " buried=" + (MalmrConfig.LeaveBuried.Value ? "left" : "taken")
                + " bosskills=" + MalmrConfig.BossKills.Value);

            if ((int)buffed != (int)level)
                term.AddString("Your gear and effects put Pickaxes at " + (int)buffed
                    + ". That makes each blow harder but does not open veins early.");

            // One line per metal, both halves of its unlock and which is still missing. The
            // same Gate the swing asks, with the same level, so this is the rule as the swing
            // sees it and not a second copy of it. The tokens come first and carry no spaces,
            // so a scenario can assert "Copper unlock=20 extra=1" as it always could, and
            // "kills=1/2" or "missing=boss" on top; the words in brackets are for a person.
            foreach (KeyValuePair<string, int> entry in MalmrConfig.UnlockTable())
            {
                Gate gate = Gate.For(entry.Key, level);
                int extra = MalmrConfig.Enabled.Value ? gate.Extra : 0;

                string boss = gate.Boss == null
                    ? " boss=none"
                    : " boss=" + gate.Boss + " kills="
                      + (gate.Kills < 0 ? "?" : gate.Kills.ToString()) + "/" + gate.KillsNeeded;

                term.AddString(entry.Key + " unlock=" + entry.Value + " extra=" + extra + boss
                    + " missing=" + gate.Missing
                    + "   (" + (entry.Key == MalmrConfig.AnyMetal ? "any other ore a station smelts, " : "")
                    + gate.Why() + ")");
            }

            // A boss Vandi does not count, or that nothing in this world sets, keeps its metals
            // shut forever while looking exactly like a boss nobody has killed yet. Said here as
            // well as in the log, because this is where a player asking "why is silver still
            // shut" is looking.
            foreach (string hole in Bosses.Holes())
                term.AddString(hole);

            Deposits.Rebuild();

            var notVeins = new List<string>();

            foreach (Deposits.Row row in Deposits.Survey)
            {
                if (row.Kind == null || row.Kind.Entry == null)
                {
                    notVeins.Add(row.Prefab);
                    continue;
                }

                term.AddString(row.Prefab + "=" + row.Kind.Metal + " chunks=" + row.Chunks
                    + "   (" + row.Kind.Why + ")");
            }

            if (notVeins.Count > 0)
                term.AddString("not veins: " + string.Join(", ", notVeins.ToArray()));

            if (!string.IsNullOrEmpty(Deposits.SmeltingLine))
                term.AddString(Deposits.SmeltingLine);
        }
    }
}
