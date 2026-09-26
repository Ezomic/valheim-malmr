using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace Malmr
{
    /// <summary>
    /// `malmr` in the console: your Pickaxes level, what each metal needs - the level and the
    /// boss - and whether it is open to you, which half is still missing, and what every deposit
    /// in this world counts as. Two verbs on top: `malmr vein on|off` switches vein mode the way
    /// the key does, and `malmr progress` reads the bar of the nearest deposit.
    ///
    /// It exists for two readers. A player who wants to know how far off silver is, without
    /// swinging at a silver vein to find out - and whether it is the level or Moder they are
    /// still short of. And a Devkit scenario, which can set the skill, seed Vandi's kill record,
    /// run this and assert on what it printed - so the unlock table, the boss gate and the
    /// drop-based classification are proved in game. The two verbs are for the swing scenarios:
    /// a scenario cannot tap a key, and it needs the bar as a number to watch it rise.
    ///
    /// <b>isCheat: false, and that is honest rather than lax.</b> Every line only reads: the
    /// config, the player's own skill, Vandi's kill count, a deposit's ZDO, and prefab data every
    /// client already holds. `malmr vein` flips the same switch the key flips, which any player
    /// can do by hand. Nothing here changes a rule, a skill or the world. A cheat command would
    /// need devcommands typed first, and running any cheat marks the character as having
    /// cheated - which Dyrr reads at the dev server's door.
    ///
    /// <b>The output is written to be asserted on.</b> Facts are name=value tokens with no
    /// spaces inside them, because Devkit's `printed` step matches a substring.
    /// </summary>
    internal static class DevConsole
    {
        /// <summary>How far `malmr progress` looks for a deposit.</summary>
        private const float ProgressReach = 10f;

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
                    "[vein on|off | progress] - what your Pickaxes level and boss kills open, "
                    + "and what every deposit counts as. vein switches vein mining like its key; "
                    + "progress reads the bar of the nearest deposit",
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

            string verb = args.Length > 1 ? args[1].ToLowerInvariant() : "";

            if (verb == "vein")
            {
                VeinVerb(term, args);
                return;
            }

            if (verb == "progress")
            {
                Progress(term, player);
                return;
            }

            if (verb.Length > 0)
            {
                term.AddString("malmr: unknown '" + args[1] + "'. Try malmr, malmr vein on|off or malmr progress.");
                return;
            }

            Report(term, player);
        }

        // ---------------------------------------------------------------- malmr vein

        private static void VeinVerb(Terminal term, Terminal.ConsoleEventArgs args)
        {
            string state = args.Length > 2 ? args[2].ToLowerInvariant() : "";

            if (state == "on") VeinMode.Set(true);
            else if (state == "off") VeinMode.Set(false);
            else if (state.Length > 0)
            {
                term.AddString("malmr vein: on or off, not '" + args[2] + "'");
                return;
            }

            // Also with no word at all, which is the question "is it on". The key is named so a
            // player reading this knows how to do the same without the console.
            term.AddString("malmr vein=" + (VeinMode.On ? "on" : "off")
                + (MalmrConfig.Enabled.Value ? "" : " (the mod is switched off in its config)")
                + "   (the key is " + Opened.KeyName(MalmrConfig.VeinToggleKey.Value)
                + ", tapped with a pickaxe out)");
        }

        // ---------------------------------------------------------------- malmr progress

        /// <summary>
        /// The nearest deposit within ProgressReach, read the way the bar reads it. The whole
        /// scene is searched, which is fine for a command someone types and would not be for
        /// something run every frame.
        /// </summary>
        private static void Progress(Terminal term, Player player)
        {
            Vector3 here = player.transform.position;

            Component nearest = null;
            float best = ProgressReach;

            foreach (MineRock5 many in Object.FindObjectsByType<MineRock5>(FindObjectsSortMode.None))
                Consider(many, here, ref nearest, ref best);

            foreach (MineRock few in Object.FindObjectsByType<MineRock>(FindObjectsSortMode.None))
                Consider(few, here, ref nearest, ref best);

            // reach= is on every answer, found or not, so a scenario can prove the verb ran without
            // knowing whether a deposit stands nearby. The command's own echo cannot carry it.
            string reach = " reach=" + ProgressReach.ToString("0", CultureInfo.InvariantCulture);

            if (nearest == null)
            {
                term.AddString("malmr progress=none" + reach + "   (no deposit within that many metres)");
                return;
            }

            ZNetView nview;
            nearest.TryGetComponent(out nview);
            Ledger.Reading reading = Ledger.Read(nearest, nview);
            if (reading == null)
            {
                term.AddString("malmr progress=none" + reach + "   (the nearest deposit has no live ZDO)");
                return;
            }

            Deposits.Kind kind = Deposits.Of(nearest);
            bool vein = kind != null && kind.Entry != null;
            bool open = vein && Gate.For(kind.Entry, Vein.EarnedLevel(player)).Open;

            term.AddString("malmr progress prefab=" + Utils.GetPrefabName(nearest.gameObject)
                + " metal=" + (vein ? kind.Metal : "none")
                + " open=" + (open ? "yes" : "no")
                + " percent=" + reading.Percent
                + " damage=" + reading.Progress.ToString("0.0", CultureInfo.InvariantCulture)
                + " total=" + reading.Total.ToString("0.0", CultureInfo.InvariantCulture)
                + " chunks=" + reading.Standing + "/" + reading.Chunks
                + " owner=" + (nview.IsOwner() ? "self" : "other")
                + " distance=" + best.ToString("0.0", CultureInfo.InvariantCulture)
                + reach);
        }

        private static void Consider(Component rock, Vector3 here, ref Component nearest, ref float best)
        {
            if (rock == null) return;

            ZNetView nview;
            if (!rock.TryGetComponent(out nview) || !nview.IsValid()) return;

            float distance = Vector3.Distance(here, rock.transform.position);
            if (distance > best) return;

            best = distance;
            nearest = rock;
        }

        // ---------------------------------------------------------------- malmr

        private static void Report(Terminal term, Player player)
        {
            // The level the swing reads, which is the earned one - see Vein.EarnedLevel. A bonus
            // on top is said on its own line so that a player looking at "+2" on their skills
            // page and a different number here knows why, rather than suspecting the mod.
            float level = Vein.EarnedLevel(player);
            float buffed = player.GetSkillLevel(Skills.SkillType.Pickaxes);

            term.AddString("malmr enabled=" + (MalmrConfig.Enabled.Value ? "yes" : "no")
                + " pickaxes=" + (int)level
                + " vein=" + (VeinMode.On ? "on" : "off")
                + " bosskills=" + MalmrConfig.BossKills.Value);

            if ((int)buffed != (int)level)
                term.AddString("Your gear and effects put Pickaxes at " + (int)buffed
                    + ". That makes each blow harder but does not open veins early.");

            // One line per metal, both halves of its unlock and which is still missing. The
            // same Gate the swing asks, with the same level, so this is the rule as the swing
            // sees it and not a second copy of it. The tokens come first and carry no spaces,
            // so a scenario can assert "Copper unlock=50 open=yes" and "kills=1/2" or
            // "missing=boss" on top; the words in brackets are for a person.
            foreach (KeyValuePair<string, int> entry in MalmrConfig.UnlockTable())
            {
                Gate gate = Gate.For(entry.Key, level);
                bool open = MalmrConfig.Enabled.Value && gate.Open;

                string boss = gate.Boss == null
                    ? " boss=none"
                    : " boss=" + gate.Boss + " kills="
                      + (gate.Kills < 0 ? "?" : gate.Kills.ToString()) + "/" + gate.KillsNeeded;

                term.AddString(entry.Key + " unlock=" + entry.Value + " open=" + (open ? "yes" : "no")
                    + boss + " missing=" + gate.Missing
                    + "   (" + (entry.Key == MalmrConfig.AnyMetal ? "any other ore a furnace takes, " : "")
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
