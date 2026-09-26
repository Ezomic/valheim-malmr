using System;
using System.Collections.Generic;
using BepInEx.Configuration;

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
    /// The two strings below are parsed on demand and re-parsed only when their text changes,
    /// because Core can rewrite them live when a host's values arrive, and so can a config
    /// manager. A parse cached once at load would keep the joining player on their own table
    /// for the whole evening while the log said the host's was in force.
    /// </summary>
    internal static class MalmrConfig
    {
        internal static ConfigEntry<bool> Enabled;

        internal static ConfigEntry<string> Unlocks;
        internal static ConfigEntry<int> LevelsPerExtraChunk;
        internal static ConfigEntry<int> MaxExtraChunks;
        internal static ConfigEntry<string> Deposits;
        internal static ConfigEntry<bool> LeaveBuried;

        internal static ConfigEntry<float> DurabilityPerChunk;
        internal static ConfigEntry<float> StaminaPerChunk;
        internal static ConfigEntry<bool> ExtraChunksTrainSkill;

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
                "Off leaves the plugin loaded and changing nothing. Every swing is vanilla again.");

            // One string rather than one line per metal. A line per metal can only name the
            // metals this file knew about when it was written, and the whole point of deriving
            // the metal from the drops is that a mod-added ore turns up without a new build.
            // A string can carry that ore's name the moment someone wants to give it a level.
            Unlocks = cfg.Bind("Unlocks", "Unlocks",
                "Copper:20, Tin:10, Iron:30, Silver:40, Flametal:60, Gold:70, *:50",
                "The Pickaxes level at which each metal's deposits start giving way along the "
                + "vein. Comma separated Name:Level pairs. The name is the smelted metal, the one "
                + "that comes OUT of the smelter, so Iron covers muddy scrap piles and anything "
                + "else whose drop smelts into iron. A trailing \"New\" is ignored when names are "
                + "compared, so Flametal covers both the old meteorite flametal and the Ashlands "
                + "one. Names are prefab names, not what the game shows: Gold is the Deep North "
                + "metal the game calls Bloodgold. A name can also be the dropped item itself "
                + "(Obsidian:40 would make obsidian rocks vein-mineable, since nothing smelts "
                + "obsidian). The * entry is the level for any other ore, meaning a drop that goes "
                + "into a furnace: a station that makes one of the metals named here, or burns "
                + "the same fuel as one that does. That is how an ore added by another mod joins "
                + "in without a new build, whether it goes in the vanilla smelter or its own "
                + "forge. Things that only go into a kiln, the windmill or the eitr refinery are "
                + "not ore and never match *. Remove * and unnamed ores are never vein-mined. A "
                + "metal missing from this list is never vein-mined. A level of -1 switches that "
                + "metal off. The levels climb with the biomes on purpose: each one arrives after "
                + "you have mined that metal by hand for a while, never before you have seen it. "
                + "The level compared is the one you have earned, the big number on the skills "
                + "page. A bonus from gear, food or an effect makes each blow harder but does not "
                + "open a metal early.");

            LevelsPerExtraChunk = cfg.Bind("Unlocks", "LevelsPerExtraChunk", 10,
                "At the unlock level a swing takes ONE extra chunk beside the one you hit. Every "
                + "this many Pickaxes levels above it, one more, up to MaxExtraChunks. So with "
                + "the defaults copper gives 1 extra chunk at 20, 2 at 30, 3 at 40 and 4 from 50. "
                + "0 hands out MaxExtraChunks the moment a metal unlocks, which makes the level "
                + "after the unlock worth nothing to this mod.");

            MaxExtraChunks = cfg.Bind("Unlocks", "MaxExtraChunks", 4,
                "The most chunks one swing can take beyond the ones vanilla hit, across every "
                + "deposit that swing touched. This is the constraint that keeps a deposit a job "
                + "rather than a click: a copper deposit is dozens of chunks, and four extra a "
                + "swing still leaves it several minutes of work. Each extra chunk takes one of "
                + "the blows the swing landed on the chunks you hit, so a chunk that needs three "
                + "hits by hand still needs three. Chunks the game drops by itself when their "
                + "support breaks do not count. 0 turns vein mining off without turning the mod "
                + "off.");

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

            LeaveBuried = cfg.Bind("Unlocks", "LeaveBuried", true,
                "Leave chunks whose middle is still under the ground alone. Silver veins and big "
                + "copper deposits sit mostly below the surface, and in vanilla you dig down to "
                + "reach them. Without this the vein would pull ore out of rock you have not "
                + "uncovered yet, and digging would stop being part of mining. It is the middle "
                + "and not the tip that counts because the game drops a chunk's ore at its "
                + "middle, so a chunk judged by its tip would put its ore inside the hillside.");

            // The three costs below share one unit, and it is the blow, not the swing. A swing
            // pays once however many chunks it struck - a fractured deposit often shows two or
            // three damage numbers a swing - so what one chunk cost by hand is the swing's cost
            // divided by the chunks it struck, and that is what an extra chunk pays at 1. The
            // first version charged a whole swing per extra chunk and so wore the pickaxe two or
            // three times as fast per blow along the vein as by hand, against the promise below.
            DurabilityPerChunk = cfg.Bind("Cost", "DurabilityPerChunk", 1f,
                "Pickaxe wear for each extra chunk, as a fraction of what one blow costs you by "
                + "hand. A swing wears the pickaxe once however many chunks it strikes, so one "
                + "blow's share is that wear divided by the chunks the swing struck. 1 means a "
                + "deposit costs the same pickaxe whichever way you mine it and vein mining buys "
                + "time, never durability. Below 1 is the setting that makes it a discount. The "
                + "swing stops taking chunks when the pickaxe reaches zero.");

            StaminaPerChunk = cfg.Bind("Cost", "StaminaPerChunk", 0f,
                "Stamina for each extra chunk, as a fraction of what one blow costs you by hand "
                + "(a swing's stamina, after your skill and gear have lowered it, divided by the "
                + "chunks the swing struck). 0 by default, because at 1 a swing that strikes one "
                + "chunk and takes four more costs five swings of stamina and you stop to breathe "
                + "after two or three - the time vein mining saves would go straight back into "
                + "waiting. Raise it if the durability cost alone feels too cheap. The swing "
                + "stops taking chunks when you cannot pay for the next one.");

            ExtraChunksTrainSkill = cfg.Bind("Cost", "ExtraChunksTrainSkill", false,
                "Whether the extra chunks also raise Pickaxes, at the rate mining them by hand "
                + "would have (a swing raises it once, so each chunk it struck is worth a share). "
                + "Off, so the skill counts swings, as it does in vanilla. The cost of that is "
                + "real: a deposit mined along the vein teaches less than one mined chunk by "
                + "chunk. On would make every unlock speed up the climb to the next one, and the "
                + "mod would turn into a way to level Pickaxes rather than a reward for having "
                + "done it.");

            AnnounceUnlocks = cfg.Bind("Display", "AnnounceUnlocks", true,
                "Say so in the middle of the screen when a Pickaxes level-up opens a metal's "
                + "veins. Without it the only way to find out is to notice that a swing took "
                + "more than it should have.");

            // Not synced by intent - see the plugin. A diagnostic flag is personal, and a
            // host turning on someone else's logging is not a thing anybody asked for.
            Verbose = cfg.Bind("Display", "Verbose", false,
                "Write one line per vein-mined swing to BepInEx/LogOutput.log: which deposit, "
                + "what it counted as, how many chunks, and what stopped it if it stopped early. "
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

        /// <summary>
        /// How many chunks beyond the struck ones a swing may take, for a metal unlocked at
        /// <paramref name="unlock"/>, at Pickaxes <paramref name="level"/>.
        /// </summary>
        internal static int ExtraChunks(float level, int unlock)
        {
            if (unlock < 0) return 0;

            int max = MaxExtraChunks.Value;
            if (max <= 0) return 0;

            if (level < unlock) return 0;

            int step = LevelsPerExtraChunk.Value;
            if (step <= 0) return max;

            int extra = 1 + (int)((level - unlock) / step);
            return extra < max ? extra : max;
        }

        // ---------------------------------------------------------------- Deposits

        private static string _depositsRaw;
        private static Dictionary<string, string> _deposits;

        /// <summary>Bumped whenever either string changes, so cached classifications know.</summary>
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
