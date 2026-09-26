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
    /// run this and assert on what it printed - so the unlock table, the boss gate, the
    /// Mistlands rule and the drop-based classification are proved in game. The two verbs are
    /// for the swing scenarios: a scenario cannot tap a key, and it needs the bar as a number to
    /// watch it rise.
    ///
    /// <b>isCheat: false, and that is honest rather than lax.</b> Every line only reads: the
    /// config, the player's own skill, the boss kill count (Vandi's, or the character's own
    /// without it), a deposit's ZDO, and prefab data every client already holds. `malmr vein`
    /// flips the same switch the key flips, which any player can do by hand. Nothing here
    /// changes a rule, a skill or the world. A cheat command would need devcommands typed
    /// first, and running any cheat marks the character as having cheated - which Dyrr reads
    /// at the dev server's door.
    ///
    /// <b>The output is written to be asserted on.</b> Facts are name=value tokens with no
    /// spaces inside them, because Devkit's `printed` step matches a substring.
    /// </summary>
    internal static class DevConsole
    {
        /// <summary>How far `malmr progress` looks for a deposit unless told otherwise.</summary>
        private const float ProgressReach = 10f;

        /// <summary>
        /// The most it may be told. A scenario asking about something it put down a few metres
        /// away needs a little more than ten on a slope, and nothing needs the whole scene.
        /// </summary>
        private const float ProgressReachMax = 50f;

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
                    "[vein on|off | progress [prefab] [metres]] - what your Pickaxes level and boss "
                    + "kills open, and what every deposit counts as. vein switches vein mining like "
                    + "its key; progress reads the bar of the nearest deposit within 10 metres, or "
                    + "the nearest of that name, or within that many metres",
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
                ProgressVerb(term, player, args);
                return;
            }

            if (verb.Length > 0)
            {
                term.AddString("malmr: unknown '" + args[1] + "'. Try malmr, malmr vein on|off or malmr progress [prefab] [metres].");
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
        /// `malmr progress [prefab] [metres]`. A number where the name would go is the reach,
        /// since no deposit prefab is called 20.
        ///
        /// The reach exists for malmr-mistlands.txt, which stands a giant brain eight metres out
        /// and asks about it. Ten metres is measured from you to the deposit's pivot, height
        /// included, so on a Mistlands slope the brain could stand six metres above or below and
        /// read as "none" - a failure about the ground, not about Malmr. Found in review on
        /// 2026-09-26.
        /// </summary>
        private static void ProgressVerb(Terminal term, Player player, Terminal.ConsoleEventArgs args)
        {
            string only = null;
            float reach = ProgressReach;
            int next = 2;

            float number;
            if (args.Length > next && !float.TryParse(args[next], NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                only = args[next];
                next++;
            }

            if (args.Length > next)
            {
                if (!float.TryParse(args[next], NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                    || number <= 0f)
                {
                    term.AddString("malmr progress: '" + args[next] + "' is not a number of metres");
                    return;
                }

                reach = Mathf.Min(number, ProgressReachMax);
            }

            Progress(term, player, only, reach);
        }

        /// <summary>
        /// The nearest deposit within reach, or the nearest of one prefab name, read the way the
        /// bar reads it. The whole scene is searched, which is fine for a command someone types
        /// and would not be for something run every frame.
        ///
        /// The name is for scenarios that stand two deposits near each other: "nearest" alone is
        /// a geometry tie waiting to happen, and would read whichever pivot was closer.
        /// </summary>
        private static void Progress(Terminal term, Player player, string only, float reachMetres)
        {
            Vector3 here = player.transform.position;

            Component nearest = null;
            float best = reachMetres;

            foreach (MineRock5 many in Object.FindObjectsByType<MineRock5>(FindObjectsSortMode.None))
                Consider(many, here, only, ref nearest, ref best);

            foreach (MineRock few in Object.FindObjectsByType<MineRock>(FindObjectsSortMode.None))
                Consider(few, here, only, ref nearest, ref best);

            // reach= is on every answer, found or not, so a scenario can prove the verb ran without
            // knowing whether a deposit stands nearby. The command's own echo cannot carry it.
            string reach = " reach=" + reachMetres.ToString("0.#", CultureInfo.InvariantCulture);

            if (nearest == null)
            {
                term.AddString("malmr progress=none" + reach + "   (no "
                    + (string.IsNullOrEmpty(only) ? "deposit" : only) + " within that many metres)");
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

            // The swing's own three questions, in its order: is it a vein, does its biome leave
            // it to the hand, is the metal open to you. open= is the answer the swing would act
            // on; handonly= says whether the biome alone refused it, so a scenario can tell a
            // Mistlands copper vein from a shut one.
            Deposits.Kind kind = Deposits.Of(nearest);
            bool vein = kind != null && kind.Entry != null;
            bool handOnly = vein && Deposits.HandOnly(nearest, kind.Entry);
            bool open = vein && !handOnly && Gate.For(kind.Entry, Vein.EarnedLevel(player)).Open;

            term.AddString("malmr progress prefab=" + Utils.GetPrefabName(nearest.gameObject)
                + " metal=" + (vein ? kind.Metal : "none")
                + " open=" + (open ? "yes" : "no")
                + " biome=" + Deposits.BiomeOf(nearest)
                + " handonly=" + (handOnly ? "yes" : "no")
                + " percent=" + reading.Percent
                + " damage=" + reading.Progress.ToString("0.0", CultureInfo.InvariantCulture)
                + " total=" + reading.Total.ToString("0.0", CultureInfo.InvariantCulture)
                // stale=yes: progress was stored, but the deposit has grown back since, so the
                // bar reads empty - see Ledger. A player asking why a restocked vein starts at
                // 0% gets the answer here rather than in the code.
                + " stale=" + (reading.Stale ? "yes" : "no")
                + " chunks=" + reading.Standing + "/" + reading.Chunks
                + " owner=" + (nview.IsOwner() ? "self" : "other")
                + " distance=" + best.ToString("0.0", CultureInfo.InvariantCulture)
                + reach);
        }

        private static void Consider(Component rock, Vector3 here, string only, ref Component nearest, ref float best)
        {
            if (rock == null) return;

            if (!string.IsNullOrEmpty(only)
                && !string.Equals(Utils.GetPrefabName(rock.gameObject), only, System.StringComparison.OrdinalIgnoreCase))
                return;

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

            // bosscount= says which count the boss half is read from, vandi or game, because the
            // two ask for different numbers and a player comparing notes with a friend on another
            // setup needs to see why. The sentence after it says the same for a person.
            term.AddString("malmr enabled=" + (MalmrConfig.Enabled.Value ? "yes" : "no")
                + " pickaxes=" + (int)level
                + " vein=" + (VeinMode.On ? "on" : "off")
                + " bosskills=" + MalmrConfig.BossKills.Value
                + " bosscount=" + Bosses.CountName
                + "   (" + (MalmrConfig.BossKills.Value <= 0
                    ? "BossKills is 0, so no metal waits for a boss"
                    : Bosses.ThroughVandi
                        ? "a boss counts through Vandi: " + Bosses.KillsNeeded
                          + " kills of a boss you summoned, " + (Bosses.KillsNeeded == 2 ? "the one-star kill" : "as BossKills says")
                        : "Vandi is not installed, so a boss counts once this character has killed it, as the game counts kills")
                + ")");

            if ((int)buffed != (int)level)
                term.AddString("Your gear and effects put Pickaxes at " + (int)buffed
                    + ". That makes each blow harder but does not open veins early.");

            // The deposit-level rule, one line, before the per-metal ones it can overrule.
            term.AddString("mistlands=" + MalmrConfig.MistlandsText()
                + "   (only these vein mine in the Mistlands; anything else there is mined by hand)");

            // One line per metal, both halves of its unlock and which is still missing. The
            // same Gate the swing asks, with the same level, so this is the rule as the swing
            // sees it and not a second copy of it. The tokens come first and carry no spaces,
            // so a scenario can assert "Copper unlock=30 open=yes" and "kills=1/2" or
            // "missing=boss" on top; the words in brackets are for a person.
            foreach (KeyValuePair<string, int> entry in MalmrConfig.UnlockTable())
            {
                Gate gate = Gate.For(entry.Key, level);
                bool open = MalmrConfig.Enabled.Value && gate.Open;

                string boss = gate.Boss == null
                    ? " boss=none"
                    : " boss=" + gate.Boss + " kills="
                      + (gate.Kills < 0 ? "?" : gate.Kills.ToString()) + "/" + gate.KillsNeeded;

                string called = entry.Key == MalmrConfig.AnyMetal
                    ? "any other ore a furnace takes, "
                    : Deposits.Nouns(entry.Key) + ", ";

                term.AddString(entry.Key + " unlock=" + entry.Value + " open=" + (open ? "yes" : "no")
                    + boss + " missing=" + gate.Missing
                    + "   (" + called + gate.Why() + ")");
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
