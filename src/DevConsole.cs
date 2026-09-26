using System.Collections.Generic;
using HarmonyLib;

namespace Malmr
{
    /// <summary>
    /// `malmr` in the console: your Pickaxes level, what each metal needs and gives you at it,
    /// and what every deposit in this world counts as.
    ///
    /// It exists for two readers. A player who wants to know how far off silver is, without
    /// swinging at a silver vein to find out. And a Devkit scenario, which cannot swing a
    /// pickaxe or put a deposit in the world, but can set the skill, run this and assert on
    /// what it printed - so the unlock table, the level arithmetic and the drop-based
    /// classification can be proved in game even though the swing itself cannot.
    ///
    /// <b>isCheat: false, and that is honest rather than lax.</b> Every line only reads: the
    /// config, the player's own skill, and prefab data every client already holds. Nothing
    /// here changes a rule, a skill or the world. A cheat command would need devcommands typed
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
                    "what your Pickaxes level opens: each metal's unlock level, the extra chunks "
                    + "a swing takes now, and what every deposit in this world counts as",
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

            float level = player.GetSkillLevel(Skills.SkillType.Pickaxes);

            term.AddString("malmr enabled=" + (MalmrConfig.Enabled.Value ? "yes" : "no")
                + " pickaxes=" + (int)level
                + " maxextra=" + MalmrConfig.MaxExtraChunks.Value
                + " step=" + MalmrConfig.LevelsPerExtraChunk.Value
                + " buried=" + (MalmrConfig.LeaveBuried.Value ? "left" : "taken"));

            foreach (KeyValuePair<string, int> entry in MalmrConfig.UnlockTable())
            {
                int extra = MalmrConfig.Enabled.Value ? MalmrConfig.ExtraChunks(level, entry.Value) : 0;

                term.AddString(entry.Key + " unlock=" + entry.Value + " extra=" + extra
                    + (entry.Key == MalmrConfig.AnyMetal ? "   (any other ore a station smelts)" : ""));
            }

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
