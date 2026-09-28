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
    /// Stone is the one default that is not a metal, since 2026-09-27. Nothing smelts it, so it
    /// is named as the dropped item itself, and it is the reason Classify now asks every ore
    /// for its metal first and reads drops by name only after - see there.
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
            /// <summary>The Unlocks entry it goes by, or null when it is not a vein at all.</summary>
            public string Entry;

            /// <summary>
            /// Every Unlocks entry it answers to, Entry among them. One for an ore deposit; more
            /// only for a rock read by the names of its drops, when two of them are named - and
            /// then every one of them has to be open before it vein mines, because a full bar
            /// hands out all of it. See Classify. Empty when it is not a vein.
            /// </summary>
            public readonly List<string> Entries = new List<string>();

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

        /// <summary>One smelting station as the scene holds it.</summary>
        private sealed class Station
        {
            public string Name;

            /// <summary>The fuel item's prefab name, or empty for a station that burns nothing.</summary>
            public string Fuel = "";

            /// <summary>What goes in and what comes out, in the asset's order.</summary>
            public readonly List<Conversion> Conversions = new List<Conversion>();
        }

        /// <summary>One line of a station's m_conversion, by prefab name.</summary>
        private sealed class Conversion
        {
            public Station Station;
            public string From;
            public string To;
        }

        private static readonly List<Station> Stations = new List<Station>();

        /// <summary>
        /// Dropped item to every conversion that takes it, one per station. A list, because two
        /// stations can take one item - a mod that adds an ore to both the smelter and its own
        /// forge - and the classification should see both rather than whichever loaded first.
        /// </summary>
        private static readonly Dictionary<string, List<Conversion>> TakenBy =
            new Dictionary<string, List<Conversion>>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<Conversion> None = new List<Conversion>();

        /// <summary>
        /// The stations whose inputs count as ore for the "*" entry. Depends on the Unlocks table,
        /// so it is rebuilt with it - see OreStations.
        /// </summary>
        private static readonly HashSet<Station> Ore = new HashSet<Station>();

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

            string prefab = PrefabName(rock);

            Kind kind;
            if (ByPrefab.TryGetValue(prefab, out kind)) return kind;

            kind = Classify(prefab, DropsOf(rock));
            ByPrefab[prefab] = kind;
            return kind;
        }

        /// <summary>
        /// A live deposit's prefab name, read off its ZDO and not off its GameObject.
        ///
        /// <b>Every live MineRock5 is called "___MineRock5 m_meshFilter".</b> Its Awake adds a
        /// MeshFilter to the deposit's own GameObject and names it that, and a component's name is
        /// its GameObject's name, so the deposit itself is renamed. Utils.GetPrefabName on one
        /// answers "___MineRock5" for a copper vein, a silver vein and every fractured boulder
        /// alike. The first Devkit run of the scenarios found it on 2026-09-28: `malmr progress
        /// rock4_copper_frac` answered "none" standing at the copper it had just put down, and
        /// the same command without a name printed prefab=___MineRock5. Worse, Of cached what a
        /// deposit is under that one shared name, so the first MineRock5 struck in a session
        /// decided what every other one was until the world changed - a silver vein after a
        /// copper one came out copper, opened at copper's level and called a copper vein on the
        /// bar, and a Deposits override named for a MineRock5 prefab never applied at all.
        ///
        /// The ZDO holds the hash of the name the object had when ZNetView.Awake ran, which is
        /// before the deposit's own Awake renames it: MineRock5.Awake reads that ZDO to register
        /// its messages, and Owner.Listen does the same right after it, so the bar could not work
        /// at all were it the other way round. ZNetScene turns the hash back into the prefab. The
        /// GameObject's own name is the fallback for a rock with no live ZDO, which nothing here
        /// classifies - every caller asks IsValid first.
        /// </summary>
        internal static string PrefabName(Component rock)
        {
            if (rock == null) return "";

            ZNetView nview;
            if (ZNetScene.instance != null && rock.TryGetComponent(out nview) && nview.IsValid())
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(nview.GetZDO().GetPrefab());
                if (prefab != null) return prefab.name;
            }

            return Utils.GetPrefabName(rock.gameObject);
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
        /// One deposit of this metal as the screen calls it: "Copper vein", or what Names says -
        /// "Giant brain" for Eitr by default. The bar reads "Copper vein 45%" off it.
        /// </summary>
        internal static string Noun(string metal)
        {
            string named = MalmrConfig.NameFor(metal);
            if (!string.IsNullOrEmpty(named)) return named;

            return DisplayName(metal) + " vein";
        }

        /// <summary>
        /// Several, for the messages: "Copper veins", "Giant brains". An s on the end, which is
        /// right for every default and for anything a player is likely to write in Names; a name
        /// that needs another plural is one nobody has asked for yet.
        /// </summary>
        internal static string Nouns(string metal)
        {
            return Noun(metal) + "s";
        }

        // ---------------------------------------------------------------- the Mistlands

        /// <summary>
        /// Whether this deposit must be mined by hand because of where it stands, whatever the
        /// player has opened: Robbin's call of 2026-09-26 that in the Mistlands only the giant
        /// brains vein mine, and copper, iron or anything else found there is mined the normal
        /// way. Stone joined the brains there on 2026-09-27, because the rule was about the ore
        /// scattered in the Mistlands and stone is stone everywhere. Which entries still vein
        /// mine there is the Mistlands config line.
        ///
        /// Asked by the swing, the bar and the console alike, per deposit, so the three cannot
        /// disagree about one rock. Never by the owner: the swinger decides, as it decides the
        /// rest of the gate, and the owner only counts what arrives.
        ///
        /// Every entry the deposit answers to has to be on the line, not only the one it goes
        /// by, for the reason Gate.For over a Kind gives: a full bar hands out all of it.
        ///
        /// Heightmap.FindBiome at the deposit, which is the map's biome at that spot. It compares
        /// X and Z only, so a deposit in a dungeon reads the surface biome above it; that is the
        /// biome the dungeon belongs to, which is the right answer here. None when the ground
        /// under the deposit is not loaded - which cannot be the case for a rock someone is
        /// standing at and swinging on - and None is never the Mistlands, so the answer there is
        /// the permissive one rather than a rock that refuses for no reason anybody could see.
        /// </summary>
        internal static bool HandOnly(Component rock, Kind kind)
        {
            if (rock == null || kind == null || kind.Entry == null) return false;
            if (BiomeOf(rock) != Heightmap.Biome.Mistlands) return false;

            foreach (string entry in kind.Entries)
                if (!MalmrConfig.MistlandsAllows(entry)) return true;

            return false;
        }

        internal static Heightmap.Biome BiomeOf(Component rock)
        {
            return rock == null ? Heightmap.Biome.None : Heightmap.FindBiome(rock.transform.position);
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

        /// <summary>
        /// How far a point is from the nearest chunk still standing, measured to that chunk's
        /// box, or from the deposit's pivot when no chunk stands.
        ///
        /// Not the pivot, because stone made that wrong. An ore deposit is a few metres across,
        /// so its pivot is never far from any face of it. The rocks stone made into veins on
        /// 2026-09-27 include cliffs and pillars, and a player at the face of one of those can
        /// stand well over ten metres from its pivot, where `malmr progress` would have read
        /// "none" at the rock the player was swinging at. Found in review the same day.
        ///
        /// A box is loose, and that is fine for finding the nearest rock but not for anything
        /// finer. A rock4 deposit's 130 chunk boxes overlap and reach well past its faces, so
        /// `malmr progress` reads distance=0.0 from a metre or two off the rock as readily as from
        /// inside it: the third scenario run on 2026-09-28 read 0.0 with the nearest face 2.0
        /// metres from the swing, out of reach. Whether a player stands in a rock, or can reach
        /// it, is Devkit's `put` and `swing` notes to answer, which measure the faces.
        /// </summary>
        internal static float Distance(Component rock, Vector3 from)
        {
            float best = float.MaxValue;

            foreach (Collider area in Areas(rock, false))
            {
                if (area == null || !area.enabled) continue;

                float squared = area.bounds.SqrDistance(from);
                if (squared < best) best = squared;
            }

            return best == float.MaxValue
                ? Vector3.Distance(from, rock.transform.position)
                : Mathf.Sqrt(best);
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
                // With what each one drops, which the console leaves out for length. Since stone
                // joined the table the question "why is this boulder not a stone vein" has an
                // answer only the running game holds - the rock drops something beside its stone -
                // and this block is the one place a person reads it.
                if (row.Kind == null || row.Kind.Entry == null)
                {
                    notVeins.Add(row.Kind != null && row.Kind.Why.Length > 0
                        ? row.Prefab + " (" + row.Kind.Why + ")"
                        : row.Prefab);
                    continue;
                }

                foreach (string entry in row.Kind.Entries) used.Add(entry);

                text.Append("\n   ").Append(row.Prefab.PadRight(26)).Append(' ')
                    .Append(row.Kind.Metal).Append(", ")
                    .Append(Opens(row.Kind.Entry));

                // A rock read by the names of two drops needs both open, so the line says what
                // the other one asks as well; the entry it goes by is only half the answer.
                foreach (string entry in row.Kind.Entries)
                    if (entry != row.Kind.Entry)
                        text.Append(", with ").Append(entry).Append(' ').Append(Opens(entry));

                text.Append(", ").Append(row.Chunks).Append(" chunk(s) - ")
                    .Append(row.Kind.Why);
            }

            if (notVeins.Count > 0)
                text.Append("\n   Not veins (no entry in Unlocks answers for what they drop): ")
                    .Append(string.Join(", ", notVeins.ToArray()));

            // The half of the check that finds a hole rather than a mistake. A metal in the
            // table that nothing resolved to is either not in this world or not being read,
            // and the second is the one to fix - so say how.
            foreach (var entry in MalmrConfig.UnlockTable().Keys)
            {
                if (entry == MalmrConfig.AnyMetal || used.Contains(entry)) continue;

                text.Append("\n   ").Append(entry).Append(": no deposit here resolves to it. "
                    + "If one should, either no station this world has makes it from what the "
                    + "deposit drops, or the deposit drops something besides it (see the not-veins "
                    + "list) - name it in Deposits as Prefab:").Append(entry).Append('.');
            }

            text.Append("\n   ").Append(SmeltingLine);

            // The deposit-level rule, so a player asking why a copper vein in the Mistlands
            // breaks by hand finds the answer in the same block as the rest.
            text.Append("\n   In the Mistlands only these vein mine, everything else there by hand: ")
                .Append(MalmrConfig.MistlandsText());

            // The boss half, in the same block and for the same reason: which metal waits for
            // whom, any boss that can never be met, and every boss the world has with the key it
            // sets. That last line is the offline-unreadable fact the Deep North's own boss needs.
            Bosses.Describe(text);

            MalmrPlugin.Log.LogInfo(text.ToString());
        }

        /// <summary>What one entry asks, for the survey: "from Pickaxes 30 and defeated_gdking x2", or "never".</summary>
        private static string Opens(string entry)
        {
            int level = MalmrConfig.LevelFor(entry);
            if (level < 0) return "never";

            string boss = MalmrConfig.BossFor(entry);
            return "from Pickaxes " + level
                   + (boss != null ? " and " + boss + " x" + MalmrConfig.BossKills.Value : "");
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

            bool changed = false;

            if (ZNetScene.instance != _scene)
            {
                _scene = ZNetScene.instance;
                ReadSmelters();
                ByPrefab.Clear();
                changed = true;
            }

            if (MalmrConfig.Revision != _revision)
            {
                _revision = MalmrConfig.Revision;
                ByPrefab.Clear();
                changed = true;
            }

            if (changed) OreStations();
        }

        /// <summary>
        /// Every conversion every smelting station in the scene performs. Kilns, the eitr
        /// refinery, the windmill and the spinning wheel are Smelters too, and are all read,
        /// because a named metal is looked up in all of them - a metal is a metal wherever it is
        /// made. Which of them may feed the "*" entry is a separate, narrower question, answered
        /// by OreStations.
        /// </summary>
        private static void ReadSmelters()
        {
            Stations.Clear();
            TakenBy.Clear();

            if (_scene == null) return;

            foreach (GameObject prefab in _scene.m_prefabs)
            {
                if (prefab == null) continue;

                Smelter smelter;
                if (!prefab.TryGetComponent(out smelter) || smelter.m_conversion == null) continue;

                var station = new Station { Name = prefab.name };

                // m_maxFuel first, because it is what the game itself asks: a Smelter with
                // m_maxFuel 0 runs without fuel whatever m_fuelItem says (Smelter's own
                // "m_maxFuel != 0 &&" guards), and the charcoal kiln is ripped at exactly that.
                // A leftover fuel item on such a station would otherwise match the smelter's coal
                // and make the kiln a furnace. Plain == null, not ?. - m_fuelItem is a Unity
                // object. An empty fuel is never "the same fuel" as anything.
                if (smelter.m_maxFuel > 0 && smelter.m_fuelItem != null)
                    station.Fuel = Utils.GetPrefabName(smelter.m_fuelItem.gameObject);

                foreach (Smelter.ItemConversion line in smelter.m_conversion)
                {
                    if (line == null || line.m_from == null || line.m_to == null) continue;

                    var conversion = new Conversion
                    {
                        Station = station,
                        From = Utils.GetPrefabName(line.m_from.gameObject),
                        To = Utils.GetPrefabName(line.m_to.gameObject),
                    };

                    station.Conversions.Add(conversion);

                    List<Conversion> takers;
                    if (!TakenBy.TryGetValue(conversion.From, out takers))
                        TakenBy[conversion.From] = takers = new List<Conversion>();
                    takers.Add(conversion);
                }

                if (station.Conversions.Count > 0) Stations.Add(station);
            }
        }

        /// <summary>Every conversion that takes a dropped item, or an empty list.</summary>
        private static List<Conversion> Taking(string item)
        {
            List<Conversion> takers;
            return TakenBy.TryGetValue(item, out takers) ? takers : None;
        }

        /// <summary>
        /// Which stations' inputs count as ore for the "*" entry, and the log line that says so.
        ///
        /// Not every Smelter is a furnace. The first version let "*" take anything any Smelter
        /// took and argued it was harmless because nothing a rock drops goes into a kiln or a
        /// refinery - which is an assumption about asset data, and the likeliest place it fails
        /// is the Mistlands: a giant's brain is mined with a pickaxe and what it drops goes into
        /// the eitr refinery. It would have become an "Eitr vein" at the * level, with no
        /// message, in a mod that opens metal by metal. A deposit that dropped wood would
        /// likewise have been a coal vein through the charcoal kiln.
        ///
        /// So a station counts as a furnace when it makes one of the entries named in Unlocks -
        /// the smelter, the blast furnace, whatever makes the Deep North's metal - or when it
        /// burns the same fuel as one that does. The second half is what keeps the promise to a
        /// mod ore: a mod that puts its ore in the vanilla smelter is covered by the first half,
        /// and one that ships its own coal-burning forge by the second. Neither half names a
        /// station or an item, so neither is a list to keep up to date; the table the player
        /// already edits is the only input. A mod ore at a station that fits neither is still
        /// one line away - name its metal in Unlocks and the named path finds it, because that
        /// path reads every station.
        ///
        /// Since 2026-09-26 the default table has no "*" and names Eitr, for the giant brains.
        /// So by default nothing here decides anything - it only labels the log line - and the
        /// eitr refinery counts as a furnace, because it makes a named entry. That is only ever
        /// felt by somebody who adds a "*" back: anything else that goes into the refinery would
        /// then be ore at the "*" level too. It is the rule doing what it says, and the fix, if
        /// anybody minds, is to name that thing rather than to special-case the refinery.
        /// </summary>
        private static void OreStations()
        {
            Ore.Clear();

            var fuels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Station station in Stations)
            {
                foreach (Conversion conversion in station.Conversions)
                {
                    if (MalmrConfig.MatchEntry(conversion.To) == null) continue;

                    Ore.Add(station);
                    if (station.Fuel.Length > 0) fuels.Add(station.Fuel);
                    break;
                }
            }

            foreach (Station station in Stations)
                if (station.Fuel.Length > 0 && fuels.Contains(station.Fuel)) Ore.Add(station);

            // One entry per station, its conversions as From>To, so the line answers both "was
            // this ore read at all" and "which station decided it". A scenario asserts on the
            // From>To tokens, so they stay spelled exactly that way.
            var text = new StringBuilder("Smelting stations here turn: ");

            if (Stations.Count == 0) text.Append("nothing (no Smelter found)");

            for (int i = 0; i < Stations.Count; i++)
            {
                Station station = Stations[i];
                if (i > 0) text.Append("; ");

                text.Append(station.Name)
                    .Append(Ore.Contains(station) ? "" : " (not a furnace, so never *)")
                    .Append(": ");

                for (int j = 0; j < station.Conversions.Count; j++)
                {
                    if (j > 0) text.Append(", ");
                    text.Append(station.Conversions[j].From).Append('>').Append(station.Conversions[j].To);
                }
            }

            SmeltingLine = text.ToString();
        }

        /// <summary>
        /// The rule, in order: the override if there is one; then the named metal the deposit's
        /// ore smelts into; then the deposit's drops by their own names; and last the "*" entry.
        ///
        /// <b>The named metal first, across every ore.</b> A drop some station takes is ore. The
        /// ores are tried heaviest first, each as the metal it smelts into at any station, and
        /// the first one whose metal is named in Unlocks decides the deposit. Heaviest first so
        /// that a deposit which mostly gives one ore is that ore, even if it sometimes gives
        /// another. And every ore is asked for its metal before any drop is read by its own
        /// name, which is what keeps a copper deposit copper however much stone it drops.
        ///
        /// That order is what stone needed, on 2026-09-27. Until then every drop was tried in
        /// weight order, ore or not, as its metal and then as itself, and the comment here leaned
        /// on no raw drop being in the table: "the stone every boulder also drops cannot make a
        /// copper vein into a stone one". With Stone an entry that stopped being true twice over.
        /// A copper deposit drops stone beside its ore, and wherever the stone weighs more it
        /// would have come out a stone vein, open at stone's level rather than copper's. Worse,
        /// as it was read then, tin, which Robbin left out on purpose ("tin doesnt need vein
        /// mining"), would have found nothing in its tin ore and fallen through to any stone its
        /// table carries, and vein mined at Pickaxes 20 through the back door. In 1.0 that road
        /// turned out not to exist: a tin rock is a single rock, neither a MineRock5 nor a
        /// MineRock, so it never reaches this method at all (the first scenario run, 2026-09-28).
        /// The danger stands for any deposit whose ore is not named. The first fix still read each
        /// ore as its metal and then as itself before moving on to the next, so a station that
        /// takes stone (a mod's crusher, say) would have made stone an ore, and a copper deposit
        /// whose stone outweighs its ore a stone vein again. Found in review the same day, and the
        /// reason the metal pass now finishes over every ore before anything is read by name.
        ///
        /// <b>Then the drops by name, and only when EVERY drop is named.</b> A deposit none of
        /// whose ore makes a named metal answers to the entries its drops are named by as
        /// themselves, stone for a boulder, and only when every drop it has is named. An ore is
        /// named by the metal it becomes, not by itself (the cfg says a drop is named as itself
        /// for what nothing smelts), so an ore that gets this far is a drop with no name and
        /// keeps the deposit out. That was written as the tin guard, and in 1.0 no tin rock gets
        /// this far; it guards any deposit whose ore is not named, a mod's included. The copper
        /// and iron scattered through the Mistlands answer the metal pass before they ever get
        /// here. It is also what keeps an unnamed drop out of a stone vein: a full bar hands out
        /// the whole deposit, so a rock that drops something else beside its stone would
        /// otherwise give that away to a player who opened stone. The Ashlands cliffs are the
        /// live case in 1.0, grausten beside their stone, and the world-load list shows them as
        /// not veins for exactly this reason. Obsidian was the example here until 2026-09-28,
        /// when the first scenario run showed an obsidian rock is no deposit Malmr reads.
        ///
        /// When more than one drop is named, the deposit answers to ALL of them. Kind.Entries
        /// holds each, and the swing, the bar and the console need every one open, level and boss
        /// (Gate.For over a Kind), and every one on the Mistlands line where that applies
        /// (HandOnly). The first version compared the levels alone and asked only the higher
        /// entry's boss, so Stone:60 beside Obsidian:50 would have handed obsidian out on Eikthyr
        /// to a player who had never beaten Moder, on a rock dropping both. Found in review on
        /// 2026-09-27; 1.0 turned out to have no such rock, and the rule is the same for the
        /// Ashlands cliffs' grausten and stone. It follows that one switched-off drop keeps the
        /// whole rock by hand: Stone:-1 shuts a cliff that drops grausten beside its stone,
        /// whatever Grausten says. That is on purpose, because the other way round, Grausten:-1 on
        /// the same cliff, is the case the rule exists for, and a rule that let a switched-off drop
        /// through could not tell the two apart. The name the deposit goes by is the strictest of
        /// its entries, switched off first and then the highest level, since that is the likeliest
        /// one to be holding it shut.
        ///
        /// <b>Then "*"</b>, for a drop a furnace takes (OreStations says which stations are
        /// furnaces), after the names, as it always came after a drop read as itself. A deposit
        /// none of the three answers is not a vein.
        ///
        /// Nothing here names Stone. Every step holds for any entry named by its drop, a mod's
        /// included, and no named metal can lose its deposit to one, because every named metal
        /// is asked before any drop is read by name.
        /// </summary>
        private static Kind Classify(string prefab, List<DropTable.DropData> drops)
        {
            string forced = MalmrConfig.DepositOverride(prefab);
            if (forced != null)
            {
                string entry = MalmrConfig.MatchEntry(forced);
                var named = new Kind
                {
                    Entry = entry,
                    Metal = entry ?? forced,
                    Why = entry != null
                        ? "named in Deposits"
                        : "named in Deposits as " + forced + ", which is not in Unlocks",
                };
                if (entry != null) named.Entries.Add(entry);
                return named;
            }

            if (drops.Count == 0)
                return new Kind { Why = "drops nothing" };

            // Each item once, heaviest first. A table can list one item twice with different
            // stacks, and the log line should not.
            var items = new List<string>();
            foreach (DropTable.DropData drop in drops)
            {
                string item = Utils.GetPrefabName(drop.m_item);
                if (!items.Contains(item)) items.Add(item);
            }

            string dropped = "drops " + string.Join(", ", items.ToArray());

            return AsMetal(items)
                   ?? ByName(items, dropped)
                   ?? AsAnyOre(items)
                   ?? new Kind { Why = dropped };
        }

        /// <summary>
        /// The first step of Classify: the heaviest ore whose metal, at any station, is named in
        /// Unlocks. Null when none is.
        /// </summary>
        private static Kind AsMetal(List<string> items)
        {
            foreach (string item in items)
            {
                foreach (Conversion conversion in Taking(item))
                {
                    string entry = MalmrConfig.MatchEntry(conversion.To);
                    if (entry == null) continue;

                    var kind = new Kind
                    {
                        Entry = entry, Metal = entry, Ore = item,
                        Why = "drops " + item + ", which smelts into " + conversion.To
                              + " at " + conversion.Station.Name,
                    };
                    kind.Entries.Add(entry);
                    return kind;
                }
            }

            return null;
        }

        /// <summary>
        /// The second step: every drop named in Unlocks as itself, and then every one of those
        /// entries is the deposit's, and it goes by the strictest. Null when any drop has no
        /// name - see Classify for why one is enough.
        /// </summary>
        private static Kind ByName(List<string> items, string dropped)
        {
            var kind = new Kind();

            foreach (string item in items)
            {
                string entry = MalmrConfig.MatchEntry(item);
                if (entry == null) return null;

                if (!kind.Entries.Contains(entry)) kind.Entries.Add(entry);

                if (kind.Entry == null || Strictness(entry) > Strictness(kind.Entry))
                {
                    kind.Entry = entry;
                    kind.Ore = item;
                }
            }

            kind.Metal = kind.Entry;
            kind.Why = items.Count == 1
                ? "drops only " + kind.Ore + ", named in Unlocks"
                : kind.Entries.Count == 1
                    ? dropped + ", all named in Unlocks as " + kind.Entry
                    : dropped + ", all named in Unlocks, so every one of "
                      + string.Join(", ", kind.Entries.ToArray()) + " has to be open; called "
                      + kind.Entry + ", the strictest";
            return kind;
        }

        /// <summary>The last step: the "*" entry, for the heaviest drop a furnace takes. Null without a "*".</summary>
        private static Kind AsAnyOre(List<string> items)
        {
            if (!MalmrConfig.UnlockTable().ContainsKey(MalmrConfig.AnyMetal)) return null;

            foreach (string item in items)
            {
                foreach (Conversion conversion in Taking(item))
                {
                    if (!Ore.Contains(conversion.Station)) continue;

                    var kind = new Kind
                    {
                        Entry = MalmrConfig.AnyMetal, Metal = conversion.To, Ore = item,
                        Why = "drops " + item + ", which smelts into " + conversion.To
                              + " at " + conversion.Station.Name
                              + " - not named, so the * level applies",
                    };
                    kind.Entries.Add(MalmrConfig.AnyMetal);
                    return kind;
                }
            }

            return null;
        }

        /// <summary>
        /// How hard an entry is to open, for the name ByName gives a deposit: its level, and
        /// switched off hardest of all. It picks the name and nothing else; the gate asks every
        /// entry.
        /// </summary>
        private static int Strictness(string entry)
        {
            int level = MalmrConfig.LevelFor(entry);
            return level < 0 ? int.MaxValue : level;
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
