using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Malmr
{
    /// <summary>
    /// Everything tunable, bound in one place so the .cfg reads as a document rather than as
    /// whatever order the code happened to need things in.
    ///
    /// Note the standing BepInEx trap: every entry is written to disk on first run and the
    /// saved value beats a new default in code. Changing a default here does nothing on a
    /// machine that has already run the plugin - edit
    /// <c>&lt;profile&gt;\BepInEx\config\ezomic.valheim.malmr.cfg</c> as part of the same
    /// change. When a config-driven change appears to do nothing in game, read the cfg
    /// before reading any code.
    ///
    /// The three strings below are parsed on demand and re-parsed only when their text changes,
    /// because Core can rewrite them live when a host's values arrive, and so can a config
    /// manager. A parse cached once at load would keep the joining player on their own table
    /// for the whole evening while the log said the host's was in force.
    ///
    /// <b>What went on 2026-09-26.</b> The first mechanic took a few extra chunks a swing, and
    /// six settings belonged to it: the cap, the growth per ten levels, the buried-chunk rule and
    /// three per-chunk costs. Robbin replaced the mechanic with the bar that fills until the whole
    /// deposit breaks, and all six went with it rather than staying as keys nothing reads. No
    /// Malmr cfg existed on any machine yet, so nobody carries a stale one.
    /// </summary>
    internal static class MalmrConfig
    {
        internal static ConfigEntry<bool> Enabled;

        internal static ConfigEntry<string> Unlocks;
        internal static ConfigEntry<string> Deposits;

        internal static ConfigEntry<string> Bosses;
        internal static ConfigEntry<int> BossKills;

        internal static ConfigEntry<KeyCode> VeinToggleKey;

        internal static ConfigEntry<bool> AnnounceUnlocks;
        internal static ConfigEntry<bool> Verbose;

        /// <summary>The key a "*" entry is stored under in the parsed table.</summary>
        internal const string AnyMetal = "*";

        internal static void Bind(ConfigFile cfg)
        {
            // Every mod here has one, and it means the same thing every time: loaded, bound,
            // patched, and deciding nothing. Not "unloaded" - a plugin cannot unload itself,
            // and a switch that pretends otherwise is a lie somebody will debug.
            Enabled = cfg.Bind("Malmr", "Enabled", true,
                "Off leaves the plugin loaded and changing nothing. Every swing is vanilla again, "
                + "and a deposit's stored progress stays where it was until the mod is back on.");

            // One string rather than one line per metal. A line per metal can only name the
            // metals this file knew about when it was written, and the whole point of deriving
            // the metal from the drops is that a mod-added ore turns up without a new build.
            // A string can carry that ore's name the moment someone wants to give it a level.
            //
            // The levels are Robbin's, 2026-09-26: tin 40 and ten more per metal up to bloodgold
            // at 90. They are late on purpose. Taking a whole deposit in one go is a big thing
            // to hand out, so each metal has been mined by hand for a long while first.
            //
            // * sits at 90 with bloodgold, the top of the table. An ore nobody named has no
            // biome this file knows, so there is nothing to place it by, and the one placement
            // that can never open a mod's ore ahead of the vanilla metals is the last one. The
            // cost lands on a mod ore that belongs early - it waits until 90 - and the fix for
            // that is one pair naming it, which is what the table is for.
            Unlocks = cfg.Bind("Unlocks", "Unlocks",
                "Tin:40, Copper:50, Iron:60, Silver:70, Flametal:80, Gold:90, *:90",
                "The Pickaxes level at which each metal's deposits can be vein mined. Comma "
                + "separated Name:Level pairs. The name is the smelted metal, the one that comes "
                + "OUT of the smelter, so Iron covers muddy scrap piles and anything else whose "
                + "drop smelts into iron. A trailing \"New\" is ignored when names are compared, "
                + "so Flametal covers both the old meteorite flametal and the Ashlands one. Names "
                + "are prefab names, not what the game shows: Gold is the Deep North metal the "
                + "game calls Bloodgold. A name can also be the dropped item itself: nothing "
                + "smelts obsidian, so obsidian rocks are left out, and Obsidian:70 (silver's "
                + "level, the same biome) would add them. The * entry is the level for any other "
                + "ore, meaning a drop that goes into a furnace: a station that makes one of the "
                + "metals named here, or burns the same fuel as one that does. That is how an ore "
                + "added by another mod joins in without a new build, whether it goes in the "
                + "vanilla smelter or its own forge. It sits at 90 with bloodgold because an ore "
                + "nobody named has no biome to place it by, and last is the one place it cannot "
                + "skip ahead of the vanilla metals; name the ore here to give it its own level. "
                + "Things that only go into a kiln, the windmill or the eitr refinery are not ore "
                + "and never match *. Remove * and unnamed ores are never vein mined. A metal "
                + "missing from this list is never vein mined. A level of -1 switches that metal "
                + "off. The level compared is the one you have earned, the big number on the "
                + "skills page. A bonus from gear, food or an effect makes each blow harder but "
                + "does not open a metal early.");

            // A name table as the override, not the rule. The rule is the drops: a deposit is
            // whatever metal its drop table smelts into, read from the running game. This is
            // only for a deposit the rule cannot read correctly - one whose ore no smelting
            // station takes, or one a player simply wants to class differently.
            Deposits = cfg.Bind("Unlocks", "Deposits", "",
                "Overrides, as Prefab:Name pairs, comma separated, e.g. "
                + "\"MineRock_Obsidian:Obsidian, goldvein_frac:Gold\". The prefab is the "
                + "deposit's own name as the log lists it on world load, and the Name is looked "
                + "up in Unlocks like any other. Empty is the normal state: every deposit is "
                + "classed by what it drops, and the log says what each one came out as. Use a "
                + "Name that is not in Unlocks (for example none) to rule a deposit out.");

            // The second half of an unlock, Robbin's rule of 2026-09-26: the level says you have
            // mined the metal by hand for a while, the boss says you have gone back to the fight
            // that opened its biome and won it again at one star. Keyed by metal like Unlocks
            // and matched the same way, so the two lines read side by side and a mod ore named
            // in one can be named in the other. A separate string rather than a third field on
            // each Unlocks pair: "Copper:50:defeated_gdking" would have turned a line people
            // edit into one they have to count colons in.
            //
            // Defeat keys rather than creature names, spelled the way Vandi's BossBiomes spells
            // them, because the count is Vandi's and Vandi files it under that key. Gold is on
            // Fader, not on a Deep North boss of its own: Vandi and Utangard both pair
            // defeated_fader with the Deep North, and a key Vandi does not count can never be
            // met - a metal on it would stay shut forever and look exactly like one that was
            // merely waiting. The world-load log lists every boss with the key it sets, which is
            // how the Deep North's own boss gets read the first time somebody looks.
            Bosses = cfg.Bind("Unlocks", "Bosses",
                "Copper:defeated_gdking, Tin:defeated_gdking, Iron:defeated_bonemass, "
                + "Silver:defeated_dragon, Obsidian:defeated_dragon, Flametal:defeated_fader, "
                + "Gold:defeated_fader",
                "The boss each metal also waits for, as Metal:bosskey pairs, comma separated. A "
                + "metal opens when BOTH hold: your Pickaxes level has reached its Unlocks level, "
                + "and you have killed its boss at least BossKills times through Vandi. The boss "
                + "is the one whose biome the metal comes from: the Elder for tin and copper, "
                + "Bonemass for iron, Moder for silver and obsidian, Fader for flametal. Gold, the "
                + "Deep North's bloodgold, is on Fader too, the same pairing Vandi and Utangard use "
                + "for the Deep North. The key is the boss's defeat key, the one the game sets when "
                + "it dies, spelled as in Vandi's BossBiomes. Names match Unlocks the same way, so "
                + "Flametal covers both flametals. A metal not in this list needs no boss, only "
                + "the level, and that includes every other ore under * unless you add a "
                + "*:bosskey pair. Only a boss Vandi counts can ever be met: a key missing from "
                + "Vandi's BossBiomes reads as no kills forever, and the log says so when a world "
                + "loads.");

            BossKills = cfg.Bind("Unlocks", "BossKills", 2,
                "How many times you must have killed a metal's boss, as Vandi counts them: kills of "
                + "a boss you summoned yourself at its altar. 2 is the one-star kill. Vandi brings "
                + "a boss back one star harder for every repeat kill, so your first kill is the "
                + "boss as the game ships it and your second is the boss at one star. That second "
                + "fight is the price of the vein. 1 asks only that you have beaten it once. 0 "
                + "switches the boss half off, and every metal opens on the Pickaxes level alone. "
                + "Vandi counts kills, not stars, so with its HarderBosses off the second kill is a "
                + "plain boss and still counts.");

            // A KeyCode, so Core leaves it with the player whatever the host runs - keys are one
            // of the two types its sync exempts. It is still declared Local in the plugin, which
            // says the same thing where somebody reading the sync list will look for it.
            //
            // Left Alt because Robbin asked for Alt. Two other mods in this suite read it: Jafna's
            // height hold is Left Alt with a hoe out, and Taum's follow toggle is Alt held with E
            // on a boar. This only listens with a pickaxe out, which keeps it off Jafna's, and a
            // tap that has E pressed inside it does not count, which keeps it off Taum's - see
            // VeinMode.
            VeinToggleKey = cfg.Bind("Controls", "VeinToggleKey", KeyCode.LeftAlt,
                "Tap this with a pickaxe out to switch vein mining on, and tap it again to switch "
                + "it off. It stays on across swings, tool changes and deposits until you tap it "
                + "again; a small marker under the crosshair says it is on while a pickaxe is out. "
                + "While it is on, your pickaxe blows on a deposit of an open metal fill a bar "
                + "instead of breaking chunks, and when the bar is full the whole deposit breaks "
                + "at once. A tap is a short press and release: holding it, pressing E during it, "
                + "or leaving the game with Alt+Tab does not switch anything. With any other tool "
                + "in hand the key does nothing here. None switches the key off.");

            AnnounceUnlocks = cfg.Bind("Display", "AnnounceUnlocks", true,
                "Say so in the middle of the screen when a metal's veins open for you, whichever "
                + "of the two came last: the Pickaxes level-up or the boss kill. Once per opening. "
                + "A metal already open when you log in is not announced again. Without it the "
                + "only way to find out is to switch vein mining on and swing, or to type malmr "
                + "in the console.");

            // Not synced by intent - see the plugin. A diagnostic flag is personal, and a
            // host turning on someone else's logging is not a thing anybody asked for.
            Verbose = cfg.Bind("Display", "Verbose", false,
                "Write a line to BepInEx/LogOutput.log for every vein mining blow this machine "
                + "handles as the deposit's owner (what it added and where the deposit stands), "
                + "for every deposit that breaks whole, and for every blow this machine sends. "
                + "The table of every deposit and what it resolved to is written once per world "
                + "whatever this says.");
        }

        // ---------------------------------------------------------------- Unlocks

        private static string _unlocksRaw;
        private static Dictionary<string, int> _unlocks;

        /// <summary>
        /// The parsed Unlocks table, keyed by the name as written, compared case-insensitively.
        /// Bad entries are logged once per text and skipped, never thrown: a typo in one pair
        /// should cost that metal and nothing else.
        /// </summary>
        internal static Dictionary<string, int> UnlockTable()
        {
            string raw = Unlocks.Value ?? "";
            if (_unlocks != null && raw == _unlocksRaw) return _unlocks;

            _unlocksRaw = raw;
            _unlocks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in Pairs(raw))
            {
                int level;
                if (!int.TryParse(pair.Value, out level))
                {
                    MalmrPlugin.Log.LogWarning("Unlocks: '" + pair.Key + ":" + pair.Value
                        + "' has no whole-number level, so " + pair.Key + " is left out.");
                    continue;
                }

                _unlocks[pair.Key] = level;
            }

            Changed();
            return _unlocks;
        }

        /// <summary>
        /// The table entry a name matches, or null. Exact first, then with a trailing "New"
        /// taken off both sides.
        ///
        /// The suffix rule is narrow on purpose. Ashlands brought a second flametal - its prefabs
        /// are FlametalNew and FlametalOreNew beside the older Flametal and FlametalOre, which are
        /// asset data this file cannot read offline - and a player writing "Flametal" means
        /// both. A prefix match would have done that too, and would also have made "Iron" claim
        /// anything a mod names IronSomething; stripping one known suffix claims nothing else.
        /// </summary>
        internal static string MatchEntry(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var table = UnlockTable();
            if (table.ContainsKey(name)) return Canonical(table, name);

            string bare = WithoutNew(name);
            foreach (var key in table.Keys)
            {
                if (key == AnyMetal) continue;
                if (string.Equals(WithoutNew(key), bare, StringComparison.OrdinalIgnoreCase))
                    return key;
            }

            return null;
        }

        /// <summary>The level for an entry the table holds, or -1.</summary>
        internal static int LevelFor(string entry)
        {
            if (entry == null) return -1;

            int level;
            return UnlockTable().TryGetValue(entry, out level) ? level : -1;
        }

        // ---------------------------------------------------------------- Deposits

        private static string _depositsRaw;
        private static Dictionary<string, string> _deposits;

        /// <summary>
        /// Bumped whenever any of the three strings changes, so cached classifications know, and
        /// so the unlock message can tell a changed rule from a metal opening. See Opened.
        /// </summary>
        internal static int Revision { get; private set; }

        /// <summary>The override for a deposit prefab, or null when it has none.</summary>
        internal static string DepositOverride(string prefab)
        {
            string raw = Deposits.Value ?? "";
            if (_deposits == null || raw != _depositsRaw)
            {
                _depositsRaw = raw;
                _deposits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var pair in Pairs(raw))
                    _deposits[pair.Key] = pair.Value;

                Changed();
            }

            string name;
            return _deposits.TryGetValue(prefab ?? "", out name) ? name : null;
        }

        // ---------------------------------------------------------------- Bosses

        private static string _bossesRaw;
        private static Dictionary<string, string> _bosses;

        /// <summary>
        /// The defeat key an Unlocks entry also waits for, lowercased, or null when it needs no
        /// boss.
        ///
        /// Matched the way MatchEntry matches a metal: exact first, then with a trailing "New"
        /// taken off both sides, so the two lines may spell a metal differently and still meet.
        /// A "*" pair is only ever the "*" entry's, never a fallback for a named metal - a named
        /// metal left out of Bosses needs no boss, which is what the cfg text promises.
        ///
        /// Null as well when BossKills is 0 or less, which is the switch for the whole boss half.
        /// Answering that here rather than in every caller is what keeps the swing, the console
        /// and the unlock message from disagreeing about whether a boss is needed.
        /// </summary>
        internal static string BossFor(string entry)
        {
            if (string.IsNullOrEmpty(entry) || BossKills.Value <= 0) return null;

            Dictionary<string, string> table = BossTable();

            string key;
            if (table.TryGetValue(entry, out key)) return key;

            if (entry == AnyMetal) return null;

            string bare = WithoutNew(entry);
            foreach (KeyValuePair<string, string> pair in table)
            {
                if (pair.Key == AnyMetal) continue;
                if (string.Equals(WithoutNew(pair.Key), bare, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;
            }

            return null;
        }

        /// <summary>
        /// The parsed Bosses line, metal to defeat key. Lowercased keys, because Vandi files its
        /// counts lowercased and so does the game every global key on its way in.
        /// </summary>
        internal static Dictionary<string, string> BossTable()
        {
            string raw = Bosses.Value ?? "";
            if (_bosses != null && raw == _bossesRaw) return _bosses;

            _bossesRaw = raw;
            _bosses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in Pairs(raw))
                _bosses[pair.Key] = pair.Value.ToLowerInvariant();

            Changed();
            return _bosses;
        }

        private static void Changed()
        {
            Revision++;
        }

        // ---------------------------------------------------------------- parsing

        private static IEnumerable<KeyValuePair<string, string>> Pairs(string raw)
        {
            foreach (string part in raw.Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.Length == 0) continue;

                // The LAST colon, so a name with a colon in it cannot split in the wrong
                // place. Nothing vanilla has one; a mod's prefab might.
                int colon = trimmed.LastIndexOf(':');
                if (colon <= 0 || colon == trimmed.Length - 1)
                {
                    MalmrPlugin.Log.LogWarning("Skipped '" + trimmed
                        + "' - it needs to be Name:Value.");
                    continue;
                }

                yield return new KeyValuePair<string, string>(
                    trimmed.Substring(0, colon).Trim(), trimmed.Substring(colon + 1).Trim());
            }
        }

        private static string WithoutNew(string name)
        {
            return name.EndsWith("New", StringComparison.OrdinalIgnoreCase) && name.Length > 3
                ? name.Substring(0, name.Length - 3)
                : name;
        }

        private static string Canonical(Dictionary<string, int> table, string name)
        {
            foreach (var key in table.Keys)
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return key;
            return name;
        }
    }
}
