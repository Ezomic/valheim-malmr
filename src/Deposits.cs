using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Malmr
{
    /// <summary>
    /// What metal a deposit is, read off the running game rather than off a list.
    ///
    /// A deposit is a MineRock5 (the many-chunk veins and fractured boulders) or a MineRock (the
    /// smaller rocks with a handful of areas). Both carry a DropTable, and every chunk that
    /// breaks drops one roll of it. So the honest answer to "is this copper" is "does what it
    /// drops smelt into copper", and the game already keeps that fact: every smelting station's
    /// Smelter.m_conversion lists what goes in and what comes out. That is the same seam Stow
    /// uses to group ores, and it has the same payoff - an ore another mod adds, with a station
    /// that smelts it, is recognised the first time anybody swings at it, and nothing here has
    /// to be rebuilt.
    ///
    /// The name table in the config is kept, but as the override and not the rule. It is for a
    /// deposit the drops cannot class: one whose ore no Smelter takes, or one somebody wants
    /// classed differently. A list was the obvious first design and it was rejected because it
    /// is only as good as the day it was written - the manifest names seven or eight vanilla
    /// deposit prefabs, and the manifest lists what exists on disk, not what loads.
    ///
    /// Both lookups are asset data. None of it can be read offline, so the first time a world
    /// is up this writes the whole table to the log once: every deposit prefab in ZNetScene,
    /// what it came out as and why, and every Unlocks entry that nothing resolved to. That log
    /// block is the only place the classification is proved, and it is written whether or not
    /// Verbose is on.
    /// </summary>
    internal static class Deposits
    {
        /// <summary>What one deposit prefab counts as.</summary>
        internal sealed class Kind
        {
            /// <summary>The Unlocks entry it answers to, or null when it is not a vein at all.</summary>
            public string Entry;

            /// <summary>What to call it: the entry's own name, or for "*" the metal it smelts into.</summary>
            public string Metal;

            /// <summary>The dropped item it was read from. Empty for an override.</summary>
            public string Ore = "";

            /// <summary>How it was decided, in words, for the log and the console.</summary>
            public string Why = "";
        }

        /// <summary>One deposit prefab as the survey found it, for the console.</summary>
        internal sealed class Row
        {
            public string Prefab;
            public int Chunks;
            public Kind Kind;
        }

        /// <summary>The scene the maps below were built for. A new world is a new ZNetScene.</summary>
        private static ZNetScene _scene;

        /// <summary>The config revision the cache below was built against.</summary>
        private static int _revision = -1;

        /// <summary>Ore prefab name to metal prefab name, from every Smelter in the scene.</summary>
        private static readonly Dictionary<string, string> SmeltsInto =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, Kind> ByPrefab =
            new Dictionary<string, Kind>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The scene the survey was last written for, so it is written once per world.</summary>
        private static ZNetScene _surveyed;

        internal static readonly List<Row> Survey = new List<Row>();

        internal static string SmeltingLine { get; private set; }

        /// <summary>What a struck deposit counts as. Never null for a live rock.</summary>
        internal static Kind Of(Component rock)
        {
            if (rock == null) return null;

            Refresh();

            string prefab = Utils.GetPrefabName(rock.gameObject);

            Kind kind;
            if (ByPrefab.TryGetValue(prefab, out kind)) return kind;

            kind = Classify(prefab, DropsOf(rock));
            ByPrefab[prefab] = kind;
            return kind;
        }

        /// <summary>
        /// A metal's name as the player's own game spells it. The Unlocks entry is a prefab name
        /// and reads like one; the item it names carries a localised name, and that is what
        /// belongs on screen.
        ///
        /// The "New" spelling is tried first because it is the CURRENT one where both exist:
        /// FlametalNew is the flametal Ashlands smelts, and plain Flametal is the older item.
        /// For every other metal the New lookup simply finds nothing.
        /// </summary>
        internal static string DisplayName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            if (ObjectDB.instance == null || Localization.instance == null) return name;

            GameObject prefab = ObjectDB.instance.GetItemPrefab(name + "New");
            if (prefab == null) prefab = ObjectDB.instance.GetItemPrefab(name);
            if (prefab == null) return name;

            ItemDrop item;
            if (!prefab.TryGetComponent(out item) || item.m_itemData == null
                || item.m_itemData.m_shared == null) return name;

            string localised = Localization.instance.Localize(item.m_itemData.m_shared.m_name);
            return string.IsNullOrEmpty(localised) ? name : localised;
        }

        /// <summary>
        /// The chunks a deposit is made of, the same way the game counts them.
        ///
        /// Mirrors each class's own Awake/Start exactly, because the area index the RPC carries
        /// is a position in THAT list: MineRock5 takes every collider under itself, MineRock
        /// takes the colliders under m_areaRoot when it has one. Active only, which is also
        /// the vanilla call - and it is what makes this the LIVE chunks, because MineRock5's
        /// UpdateMesh and MineRock's RPC_Hide both deactivate a chunk's GameObject the moment
        /// its health reaches zero, on every client, from the broadcast the owner sends.
        /// </summary>
        internal static Collider[] Areas(Component rock, bool includeDead)
        {
            MineRock5 vein = rock as MineRock5;
            if (vein != null) return vein.GetComponentsInChildren<Collider>(includeDead);

            MineRock small = rock as MineRock;
            if (small == null) return new Collider[0];

            return small.m_areaRoot != null
                ? small.m_areaRoot.GetComponentsInChildren<Collider>(includeDead)
                : small.GetComponentsInChildren<Collider>(includeDead);
        }

        internal static int MinToolTier(Component rock)
        {
            MineRock5 vein = rock as MineRock5;
            if (vein != null) return vein.m_minToolTier;

            MineRock small = rock as MineRock;
            return small != null ? small.m_minToolTier : 0;
        }

        // ---------------------------------------------------------------- the survey

        /// <summary>
        /// Once per world, from the plugin's Update: write every deposit and what it counts as.
        ///
        /// Waits for the local player rather than for ZNetScene alone, because the other mods in
        /// this suite register their prefabs from their own Update and retry until the scene
        /// takes them - so the scene is complete a while after it exists, and a survey taken at
        /// Awake would miss a mod's ore on exactly the machine where somebody was checking for
        /// it. A dedicated server never has a local player and never surveys, which is right:
        /// nothing on a server swings a pickaxe.
        /// </summary>
        internal static void SurveyTick()
        {
            if (ZNetScene.instance == null || _surveyed == ZNetScene.instance) return;
            if (Player.m_localPlayer == null) return;
            if (ObjectDB.instance == null || ObjectDB.instance.m_items.Count == 0) return;

            _surveyed = ZNetScene.instance;

            try
            {
                Write();
            }
            catch (Exception error)
            {
                // A survey that throws costs the log block and nothing else. Classification is
                // lazy and per deposit, so swinging still works.
                MalmrPlugin.Log.LogWarning("Could not write the deposit table: " + error.Message);
            }
        }

        /// <summary>The survey, rebuilt against the current config. Also what the console reads.</summary>
        internal static void Rebuild()
        {
            Refresh();
            Survey.Clear();

            if (ZNetScene.instance == null) return;

            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;

                Component rock = Rock(prefab);
                if (rock == null) continue;

                string name = prefab.name;

                Kind kind;
                if (!ByPrefab.TryGetValue(name, out kind))
                {
                    kind = Classify(name, DropsOf(rock));
                    ByPrefab[name] = kind;
                }

                Survey.Add(new Row
                {
                    Prefab = name,
                    Chunks = Areas(rock, true).Length,
                    Kind = kind,
                });
            }

            Survey.Sort((a, b) => string.Compare(a.Prefab, b.Prefab, StringComparison.OrdinalIgnoreCase));
        }

        private static void Write()
        {
            Rebuild();

            var text = new StringBuilder();
            text.Append("Deposits in this world, and what each one counts as for vein mining:");

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var notVeins = new List<string>();

            foreach (Row row in Survey)
            {
                if (row.Kind == null || row.Kind.Entry == null)
                {
                    notVeins.Add(row.Prefab);
                    continue;
                }

                used.Add(row.Kind.Entry);

                int level = MalmrConfig.LevelFor(row.Kind.Entry);

                text.Append("\n   ").Append(row.Prefab.PadRight(26)).Append(' ')
                    .Append(row.Kind.Metal).Append(", ")
                    .Append(level < 0 ? "never" : "from Pickaxes " + level)
                    .Append(", ").Append(row.Chunks).Append(" chunk(s) - ")
                    .Append(row.Kind.Why);
            }

            if (notVeins.Count > 0)
                text.Append("\n   Not veins (nothing they drop is in Unlocks): ")
                    .Append(string.Join(", ", notVeins.ToArray()));

            // The half of the check that finds a hole rather than a mistake. A metal in the
            // table that nothing resolved to is either not in this world or not being read,
            // and the second is the one to fix - so say how.
            foreach (var entry in MalmrConfig.UnlockTable().Keys)
            {
                if (entry == MalmrConfig.AnyMetal || used.Contains(entry)) continue;

                text.Append("\n   ").Append(entry).Append(": no deposit here resolves to it. "
                    + "If one should, its drop is not smelted by any station this world has - "
                    + "name it in Deposits as Prefab:").Append(entry).Append('.');
            }

            text.Append("\n   ").Append(SmeltingLine);

            MalmrPlugin.Log.LogInfo(text.ToString());
        }

        // ---------------------------------------------------------------- classification

        /// <summary>
        /// Drops the maps when the world or the config under them has changed. Asks the config
        /// to parse first, because the parse is what moves its revision.
        /// </summary>
        private static void Refresh()
        {
            MalmrConfig.UnlockTable();
            MalmrConfig.DepositOverride("");

            if (ZNetScene.instance != _scene)
            {
                _scene = ZNetScene.instance;
                ReadSmelters();
                ByPrefab.Clear();
            }

            if (MalmrConfig.Revision != _revision)
            {
                _revision = MalmrConfig.Revision;
                ByPrefab.Clear();
            }
        }

        /// <summary>
        /// Every conversion every smelting station in the scene performs. Kilns, the eitr
        /// refinery and the windmill are Smelters too, and are read as well - harmless, because
        /// nothing a rock drops goes into any of them, and filtering them out by name would be
        /// one more list to keep up to date.
        /// </summary>
        private static void ReadSmelters()
        {
            SmeltsInto.Clear();

            var pairs = new List<string>();

            if (_scene != null)
            {
                foreach (GameObject prefab in _scene.m_prefabs)
                {
                    if (prefab == null) continue;

                    Smelter smelter;
                    if (!prefab.TryGetComponent(out smelter) || smelter.m_conversion == null) continue;

                    foreach (Smelter.ItemConversion conversion in smelter.m_conversion)
                    {
                        if (conversion == null || conversion.m_from == null || conversion.m_to == null)
                            continue;

                        string from = Utils.GetPrefabName(conversion.m_from.gameObject);
                        string to = Utils.GetPrefabName(conversion.m_to.gameObject);

                        if (SmeltsInto.ContainsKey(from)) continue;

                        SmeltsInto[from] = to;
                        pairs.Add(from + ">" + to);
                    }
                }
            }

            SmeltingLine = "Smelting stations here turn: "
                + (pairs.Count == 0 ? "nothing (no Smelter found)" : string.Join(", ", pairs.ToArray()));
        }

        /// <summary>
        /// The rule, in order: the override if there is one; then the drops, heaviest first,
        /// each tried as the metal it smelts into and then as itself; then the "*" entry for any
        /// drop that smelts into something; and otherwise not a vein.
        ///
        /// Heaviest drop first so that a deposit which mostly gives one thing is that thing,
        /// even if it sometimes gives another. Only drops that match an entry count toward it,
        /// so the stone every boulder also drops cannot make a copper vein into a stone one.
        /// </summary>
        private static Kind Classify(string prefab, List<DropTable.DropData> drops)
        {
            string forced = MalmrConfig.DepositOverride(prefab);
            if (forced != null)
            {
                string entry = MalmrConfig.MatchEntry(forced);
                return new Kind
                {
                    Entry = entry,
                    Metal = entry ?? forced,
                    Why = entry != null
                        ? "named in Deposits"
                        : "named in Deposits as " + forced + ", which is not in Unlocks",
                };
            }

            if (drops.Count == 0)
                return new Kind { Why = "drops nothing" };

            foreach (DropTable.DropData drop in drops)
            {
                string item = Utils.GetPrefabName(drop.m_item);

                string metal;
                if (SmeltsInto.TryGetValue(item, out metal))
                {
                    string entry = MalmrConfig.MatchEntry(metal);
                    if (entry != null)
                        return new Kind
                        {
                            Entry = entry, Metal = entry, Ore = item,
                            Why = "drops " + item + ", which smelts into " + metal,
                        };
                }

                string itself = MalmrConfig.MatchEntry(item);
                if (itself != null)
                    return new Kind
                    {
                        Entry = itself, Metal = itself, Ore = item,
                        Why = "drops " + item + ", named in Unlocks",
                    };
            }

            if (MalmrConfig.UnlockTable().ContainsKey(MalmrConfig.AnyMetal))
            {
                foreach (DropTable.DropData drop in drops)
                {
                    string item = Utils.GetPrefabName(drop.m_item);

                    string metal;
                    if (!SmeltsInto.TryGetValue(item, out metal)) continue;

                    return new Kind
                    {
                        Entry = MalmrConfig.AnyMetal, Metal = metal, Ore = item,
                        Why = "drops " + item + ", which smelts into " + metal
                              + " - not named, so the * level applies",
                    };
                }
            }

            var names = new List<string>();
            foreach (DropTable.DropData drop in drops) names.Add(Utils.GetPrefabName(drop.m_item));

            return new Kind { Why = "drops " + string.Join(", ", names.ToArray()) };
        }

        /// <summary>A deposit's drops with an item, heaviest first, ties in table order.</summary>
        private static List<DropTable.DropData> DropsOf(Component rock)
        {
            DropTable table = null;

            MineRock5 vein = rock as MineRock5;
            if (vein != null) table = vein.m_dropItems;

            MineRock small = rock as MineRock;
            if (small != null) table = small.m_dropItems;

            var drops = new List<DropTable.DropData>();
            if (table == null || table.m_drops == null) return drops;

            foreach (DropTable.DropData drop in table.m_drops)
                if (drop.m_item != null) drops.Add(drop);

            // Insertion rather than List.Sort, because List.Sort is not stable and a tie
            // should keep the order the asset author wrote.
            for (int i = 1; i < drops.Count; i++)
            {
                DropTable.DropData moving = drops[i];
                int j = i - 1;
                while (j >= 0 && drops[j].m_weight < moving.m_weight)
                {
                    drops[j + 1] = drops[j];
                    j--;
                }
                drops[j + 1] = moving;
            }

            return drops;
        }

        private static Component Rock(GameObject prefab)
        {
            MineRock5 vein;
            if (prefab.TryGetComponent(out vein)) return vein;

            MineRock small;
            if (prefab.TryGetComponent(out small)) return small;

            return null;
        }
    }
}
