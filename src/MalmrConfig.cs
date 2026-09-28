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
    /// The strings below are parsed on demand and re-parsed only when their text changes,
    /// because Core can rewrite them live when a host's values arrive, and so can a config
    /// manager. A parse cached once at load would keep the joining player on their own table
    /// for the whole evening while the log said the host's was in force.
    ///
    /// <b>What went on 2026-09-26.</b> The first mechanic took a few extra chunks a swing, and
    /// six settings belonged to it: the cap, the growth per ten levels, the buried-chunk rule and
    /// three per-chunk costs. Robbin replaced the mechanic with the bar that fills until the whole
    /// deposit breaks, and all six went with it rather than staying as keys nothing reads. No
    /// Malmr cfg existed on any machine yet, so nobody carries a stale one.
    ///
    /// <b>And the defaults that moved the same evening</b>, after the bar: the levels came down
    /// to copper 30 up to bloodgold 80, tin and the "*" entry left the table, the giant brains
    /// joined it at 60, and the Mistlands line and the Names line arrived. Still no cfg on any
    /// machine - the test profile had never run - so again nothing stale is out there.
    ///
    /// <b>And stone, on 2026-09-27</b>, Robbin's "also add stone". Three defaults moved:
    /// Stone:20 in Unlocks, the step below copper on his ten-a-step table; Stone on
    /// defeated_eikthyr in Bosses, the Meadows boss, by the same rule that pairs every other
    /// entry with the boss of its biome; and Stone beside Eitr on the Mistlands line. The real
    /// work was in Deposits.Classify, which had to stop a copper deposit coming out as stone
    /// (and a tin one, it was thought then; in 1.0 no tin rock reaches Classify, see Unlocks
    /// below). Checked again that day: no Malmr cfg on any profile, so no stale one to edit. A
    /// review the same day changed what the Unlocks text promises for a rock with two named
    /// drops: both entries have to be open, not the higher level of the two.
    /// </summary>
    internal static class MalmrConfig
    {
        internal static ConfigEntry<bool> Enabled;

        internal static ConfigEntry<string> Unlocks;
        internal static ConfigEntry<string> Deposits;
        internal static ConfigEntry<string> Mistlands;
        internal static ConfigEntry<string> Names;

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
            // The levels are Robbin's, 2026-09-26, the second table of that day: copper 30 and
            // ten more per step up to bloodgold at 80, with the Mistlands' giant brains at 60 in
            // the step between silver and flametal. Taking a whole deposit in one go is a big
            // thing to hand out, so each one has been mined by hand for a while first.
            //
            // Tin is not in it, his words: "tin doesnt need vein mining". Tin comes in small
            // rocks by the shore, a few swings each, and there is no deposit worth a bar.
            //
            // Naming it would do nothing anyway, and the same goes for obsidian. In 1.0 a tin
            // rock and an obsidian rock are each a single rock, neither a MineRock5 nor a
            // MineRock, and Malmr hooks only MineRock5.Damage and MineRock.Damage. The first
            // scenario run found it on 2026-09-28: neither prefab is in the world-load list of
            // deposits, which holds everything carrying either component, and Devkit's `hurt`
            // called a MineRock_Tin "not a MineRock5 or a MineRock". Until then the text below
            // promised that Tin:20 and Obsidian:50 would add them.
            //
            // The "*" entry went with the same change, so only what is named here vein mines.
            // The code for it stays, because a mod's ore is exactly what it is for, and the text
            // below says how to put it back.
            //
            // Eitr is the brains' entry. A giant brain drops soft tissue, the eitr refinery turns
            // soft tissue into refined eitr, and the named path in Deposits asks every station
            // what a drop becomes - so naming Eitr is what makes a brain a vein, the same way
            // naming Copper makes a copper deposit one. Names below is what puts "Giant brain"
            // on the screen instead of the refinery's product.
            //
            // Stone is the other way round, since 2026-09-27: nothing smelts it, so it is named
            // as the item that drops, and at 20, the step below copper, because stone is the
            // first thing anybody mines. Ore deposits drop stone too, so naming it could have
            // made a copper deposit into a stone vein. Deposits.Classify is what stops that,
            // and the text below says the rule in the player's words.
            Unlocks = cfg.Bind("Unlocks", "Unlocks",
                "Stone:20, Copper:30, Iron:40, Silver:50, Eitr:60, Flametal:70, Gold:80",
                "The Pickaxes level at which each metal's deposits, and stone, can be vein "
                + "mined. Comma separated Name:Level pairs. Only what is named here vein mines; "
                + "everything else is mined by hand. The name is the smelted metal, the one that "
                + "comes OUT of the smelter, so Iron covers muddy scrap piles and anything else "
                + "whose drop smelts into iron. A trailing \"New\" is ignored when names are "
                + "compared, so "
                + "Flametal covers both the old meteorite flametal and the Ashlands one. Names "
                + "are prefab names, not what the game shows: Gold is the Deep North metal the "
                + "game calls Bloodgold, and Eitr is the Mistlands' giant brains, because what "
                + "they drop becomes refined eitr in the eitr refinery (Names sets what the "
                + "screen calls them). Tin and obsidian are not here, and naming them does "
                + "nothing: in Valheim 1.0 a tin rock and an obsidian rock are each a single "
                + "rock, not a deposit of chunks, and Malmr does not touch them, so they are "
                + "mined by hand whatever this line says. A name can also be the dropped item "
                + "itself, for what nothing smelts. Stone is one: it covers the plain rocks and "
                + "boulders, the ones whose every drop is stone. A deposit that drops ore is "
                + "never a stone vein, even though ore deposits drop stone as well: it is its "
                + "ore's vein, or nobody's if that ore is not named here. A rock that drops "
                + "anything else beside its stone is not a stone vein either, unless that is "
                + "named here too, and then "
                + "BOTH have to be open to you, each with its own level and its own boss, and "
                + "both on the Mistlands line for a rock that stands there, because a full bar "
                + "hands out everything the rock drops. So a name for a dropped item covers a "
                + "rock only while every other drop on that rock is named here as well, at a "
                + "level that is not -1: one drop left out or switched off keeps the whole rock "
                + "mined by hand. A * entry, for example *:80, would give every other ore a "
                + "level, meaning a drop that goes into a furnace: a station that makes one of the "
                + "entries named here, or burns the same fuel as one that does. That is how an "
                + "ore added by another mod could join without a new build; naming that ore "
                + "here is the better way, because it gets its own level. A level of -1 "
                + "switches that entry off. The level compared is the one you have earned, the "
                + "big number on the skills page. A bonus from gear, food or an effect makes "
                + "each blow harder but does not open a metal early.");

            // A name table as the override, not the rule. The rule is the drops: a deposit is
            // whatever metal its drop table smelts into, read from the running game. This is
            // only for a deposit the rule cannot read correctly - one whose ore no smelting
            // station takes, or one a player simply wants to class differently.
            Deposits = cfg.Bind("Unlocks", "Deposits", "",
                "Overrides, as Prefab:Name pairs, comma separated, e.g. "
                + "\"rock4_copper_frac:none, goldvein_frac:Gold\". The prefab is the "
                + "deposit's own name as the log lists it on world load, and the Name is looked "
                + "up in Unlocks like any other. Empty is the normal state: every deposit is "
                + "classed by what it drops, and the log says what each one came out as. Use a "
                + "Name that is not in Unlocks (for example none) to rule a deposit out. Only a "
                + "deposit in that list can be overridden. A rock that is not in it, like tin "
                + "or obsidian in 1.0, is one Malmr does not touch, and naming it here does "
                + "nothing.");

            // Robbin's answer of 2026-09-26 to "how does it work with the iron and copper you
            // can find in the Mistlands": it doesn't. In the Mistlands only the giant brains vein
            // mine, and copper, iron or any other ore found there is mined the normal way, even
            // when that metal is long open to you. A line rather than a rule in code, so a
            // server that sees it differently changes a word instead of waiting for a build.
            //
            // Stone is on it too since 2026-09-27. The line exists to keep the copper and iron
            // scattered through the Mistlands mined by hand, and stone was never what it was
            // about: a Mistlands boulder is stone like any other, so it vein mines there as it
            // does everywhere else.
            //
            // Decided by the deposit's own biome, Heightmap.FindBiome at its position - the
            // ground's biome, the same one the map shows. That reads X and Z only, so a deposit
            // inside a dungeon reads the biome on the surface above it: iron in a Mistlands
            // crypt would be Mistlands iron. Nothing vanilla puts a deposit there, and it would
            // be the right answer if something did.
            Mistlands = cfg.Bind("Unlocks", "Mistlands", "Eitr, Stone",
                "Which Unlocks entries still vein mine when the deposit stands in the Mistlands, "
                + "comma separated. Everything else found in the Mistlands, like copper or iron, "
                + "is mined the normal way there even when that metal is open to you, and the "
                + "top left of the screen says so once per deposit. The default is Eitr, the "
                + "giant brains, which are the Mistlands' own vein, and Stone: this line is here "
                + "to keep the copper and iron scattered through the Mistlands mined by hand, "
                + "and stone is stone everywhere. A rock that answers to two entries, like one "
                + "dropping two things named in Unlocks, needs both here. "
                + "* lets every open entry vein mine there too. "
                + "Empty means nothing vein mines in the Mistlands. The biome is the one on the "
                + "map at the deposit's spot; everywhere else this line does nothing.");

            // What the screen calls an entry's deposits. A metal is named off its item, in the
            // player's own language - "Copper vein" - and that is right for every metal. It is
            // wrong for exactly one default: the brains' entry is Eitr, whose item the game
            // calls refined eitr, and "Refined eitr vein 45%" over a giant brain would be a
            // puzzle. So the default names that one, and the line is there for a mod's ore that
            // wants the same.
            Names = cfg.Bind("Unlocks", "Names", "Eitr:Giant brain",
                "What the screen calls one deposit of an Unlocks entry, as Name:Text pairs, "
                + "comma separated. It shows on the vein bar (\"Giant brain 45%\") and, with an s "
                + "added, in the messages (\"Giant brains open to you now\"). An entry not listed "
                + "is called after its metal as the game names it, plus vein: \"Copper vein\".");

            // The second half of an unlock, Robbin's rule of 2026-09-26: the level says you have
            // mined the metal by hand for a while, the boss says you have gone back to the fight
            // that opened its biome and won it again at one star. Keyed by metal like Unlocks
            // and matched the same way, so the two lines read side by side and a mod ore named
            // in one can be named in the other. A separate string rather than a third field on
            // each Unlocks pair: "Copper:50:defeated_gdking" would have turned a line people
            // edit into one they have to count colons in.
            //
            // Defeat keys rather than creature names. It is the name both counts can be reached
            // by: Vandi files its count under the key, and the key leads to the creature whose
            // name the game's own kill tally is kept under (see Bosses). Gold is on Fader, not on
            // a Deep North boss of its own: Vandi and Utangard both pair defeated_fader with the
            // Deep North, and a key Vandi does not count can never be met - a metal on it would
            // stay shut forever and look exactly like one that was merely waiting. That is a
            // fallback, not a choice: the world-load log lists every boss with the key it sets,
            // and the Frozen King's is read there the first time somebody looks.
            //
            // Stone is on Eikthyr, 2026-09-27, by that same rule: the boulders it covers stand
            // in every biome, but stone is the Meadows' material, the first a pickaxe ever
            // takes, and Eikthyr is the Meadows' boss. Vandi's BossBiomes has defeated_eikthyr
            // by default, so the one-star kill can be counted.
            Bosses = cfg.Bind("Unlocks", "Bosses",
                "Stone:defeated_eikthyr, Copper:defeated_gdking, Iron:defeated_bonemass, "
                + "Silver:defeated_dragon, Obsidian:defeated_dragon, Eitr:defeated_queen, "
                + "Flametal:defeated_fader, Gold:defeated_fader",
                "The boss each Unlocks entry also waits for, as Name:bosskey pairs, comma "
                + "separated. An entry opens when BOTH hold: your Pickaxes level has reached its "
                + "Unlocks level, and you have beaten its boss (see BossKills for how that is "
                + "counted, with Vandi and without it). The boss is the one whose biome the ore "
                + "comes from: Eikthyr for stone, the Elder for copper, Bonemass for iron, Moder "
                + "for silver, the Queen for the giant brains (Eitr), Fader for "
                + "flametal. Gold, the "
                + "Deep North's bloodgold, is on Fader too until the Deep North's own boss has "
                + "been read from a world: the log lists every boss in the world with its key "
                + "when a world loads. The key is the boss's defeat key, the one the game sets "
                + "when it dies. Names match Unlocks the same way, so Flametal covers both "
                + "flametals. An entry not in this list needs no boss, only the level. A pair "
                + "whose name is not in Unlocks does nothing. The default's Obsidian pair is one, "
                + "and adding Obsidian to Unlocks would not wake it: in 1.0 obsidian rocks are "
                + "single rocks that Malmr does not touch. With "
                + "Vandi installed, only a boss in Vandi's BossBiomes can ever be met, and the log "
                + "says so when a world loads.");

            // Robbin, 2026-09-26: Vandi is a soft dependency, and "still boss kill but no star
            // since vanilla doesnt provide star". So the one number means two things depending
            // on what is installed, and the text says both, because the same cfg can sit on a
            // machine with Vandi and one without.
            //
            // The Call to Arms line is there because the per-name kill record only loads from a
            // character saved at Version.Player.CallToArms (42) or later - LoadPlayerFromDisk
            // skips it below that - so a veteran's old Elder kill reads as none, and "you have
            // not beaten the Elder" would otherwise read as a bug to exactly the players with
            // the oldest characters. Found in review on 2026-09-26.
            BossKills = cfg.Bind("Unlocks", "BossKills", 2,
                "How many times you must have killed an entry's boss. With Vandi installed it is "
                + "Vandi's count: kills of a boss you summoned yourself at its altar. 2 is the "
                + "one-star kill, because Vandi brings a boss back one star harder for every "
                + "repeat kill, so your first kill is the boss as the game ships it and your "
                + "second is the boss at one star. Vandi counts kills, not stars, so with its "
                + "HarderBosses off the second kill is a plain boss and still counts. Without "
                + "Vandi there are no stars to ask for, and this number is not used: one kill of "
                + "the boss by this character, as the game itself counts kills, opens it. The "
                + "game only started keeping that count per creature with its Call to Arms "
                + "update, so a boss this character beat before then is not in it and has to be "
                + "beaten again. On both, 0 switches the boss half off and every entry opens on "
                + "the Pickaxes level alone.");

            // A KeyCode, so Core leaves it with the player whatever the host runs - keys are one
            // of the two types its sync exempts. It is still declared Local in the plugin, which
            // says the same thing where somebody reading the sync list will look for it.
            //
            // Left Alt because Robbin asked for Alt. Two other mods in this suite read it: Jafna's
            // height hold is Left Alt with a hoe out, and Taum's follow toggle is Alt held with E
            // on a boar. This only listens with a pickaxe out, which keeps it off Jafna's, and a
            // tap with any other key pressed inside it, E included, does not count, which keeps it
            // off Taum's. Alt+Tab is the third, in both directions, and VeinMode says how.
            VeinToggleKey = cfg.Bind("Controls", "VeinToggleKey", KeyCode.LeftAlt,
                "Tap this with a pickaxe out to switch vein mining on, and tap it again to switch "
                + "it off. It stays on across swings, tool changes and deposits until you tap it "
                + "again; a small gold Vein above the crosshair says it is on while a pickaxe is out. "
                + "While it is on, your pickaxe blows on a deposit of an open metal fill a bar "
                + "instead of breaking chunks, and when the bar is full the whole deposit breaks "
                + "at once. A tap is a short press and release of this key on its own, with the "
                + "game in front the whole time: holding it, pressing any other key during it (E on "
                + "a boar, say), or Alt+Tab, out of the game or back into it, does not switch "
                + "anything. With any other tool in hand the key does nothing here. None switches "
                + "the key off.");

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
        /// Bumped whenever Unlocks, Deposits or Bosses changes, so cached classifications know, and
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

        // ---------------------------------------------------------------- Mistlands

        private static string _mistlandsRaw;
        private static List<string> _mistlands;

        /// <summary>
        /// Whether an Unlocks entry may vein mine a deposit standing in the Mistlands. Matched
        /// the way MatchEntry matches, exact and then without a trailing "New", so the two lines
        /// may spell a metal differently and still meet. A "*" in the line lets everything.
        ///
        /// No revision bump when it changes, unlike the three strings above: nothing is cached
        /// against it. The swing, the bar and the console ask it fresh for each deposit.
        /// </summary>
        internal static bool MistlandsAllows(string entry)
        {
            string raw = Mistlands.Value ?? "";
            if (_mistlands == null || raw != _mistlandsRaw)
            {
                _mistlandsRaw = raw;
                _mistlands = new List<string>();

                foreach (string part in raw.Split(','))
                {
                    string name = part.Trim();
                    if (name.Length > 0) _mistlands.Add(name);
                }
            }

            if (string.IsNullOrEmpty(entry)) return false;

            foreach (string name in _mistlands)
            {
                if (name == AnyMetal) return true;
                if (string.Equals(name, entry, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(WithoutNew(name), WithoutNew(entry), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>The Mistlands line as it stands, for the console and the log.</summary>
        internal static string MistlandsText()
        {
            string raw = (Mistlands.Value ?? "").Trim();
            return raw.Length == 0 ? "(none)" : raw.Replace(" ", "");
        }

        // ---------------------------------------------------------------- Names

        private static string _namesRaw;
        private static Dictionary<string, string> _names;

        /// <summary>
        /// The screen name for one deposit of this entry or metal, "Giant brain", or null when
        /// the Names line does not mention it. Matched like the rest, so FlametalNew finds a
        /// Flametal pair.
        /// </summary>
        internal static string NameFor(string entry)
        {
            string raw = Names.Value ?? "";
            if (_names == null || raw != _namesRaw)
            {
                _namesRaw = raw;
                _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var pair in Pairs(raw))
                    _names[pair.Key] = pair.Value;
            }

            if (string.IsNullOrEmpty(entry)) return null;

            string text;
            if (_names.TryGetValue(entry, out text)) return text;

            string bare = WithoutNew(entry);
            foreach (KeyValuePair<string, string> pair in _names)
                if (string.Equals(WithoutNew(pair.Key), bare, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;

            return null;
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
