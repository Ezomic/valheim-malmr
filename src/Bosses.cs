using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace Malmr
{
    /// <summary>
    /// The boss half of an unlock: how many times the local player has killed a boss, as Vandi
    /// counts it, and what the game calls that boss.
    ///
    /// <b>Why Vandi and not the game's own record.</b> The game remembers a boss as a world key
    /// set the first time anybody kills it, and a player unique key for whoever stood near.
    /// Neither says who fought it, nor how many times, nor at how many stars. Vandi does: it
    /// credits each kill to the player who made the offering, and brings the boss back one star
    /// harder for each repeat kill by that player. So "beaten at one star" is Vandi's count
    /// reaching two, and that is a number only Vandi keeps. Hence the hard dependency - see the
    /// plugin.
    ///
    /// <b>Through VandiApi, never the key.</b> Vandi keeps the count in a global key whose layout
    /// is its own business. Reading that key here would work until Vandi renamed it, and then it
    /// would read zero for everybody and keep every metal shut without a word. VandiApi exists
    /// so that rename breaks a build instead.
    ///
    /// Every call into Vandi sits in its own never-inlined method inside a try. Both mods ship
    /// from one source tree and Core's gate compares builds, so a mismatch should not reach a
    /// player - but if one does, the JIT throws MissingMethodException when it compiles the
    /// method that names the missing member, and isolating that method is what lets the throw
    /// cost the boss half and not the class. Unreadable counts as not met: fail shut.
    /// </summary>
    internal static class Bosses
    {
        /// <summary>A count Vandi could not be asked for. Never met.</summary>
        internal const int Unreadable = -1;

        private static bool _warned;

        /// <summary>The local player's kills of that boss, or Unreadable.</summary>
        internal static int LocalKills(string bossKey)
        {
            if (string.IsNullOrEmpty(bossKey)) return 0;

            try
            {
                return ReadLocalKills(bossKey);
            }
            catch (Exception error)
            {
                Warn(error);
                return Unreadable;
            }
        }

        /// <summary>
        /// Whether Vandi records kills of that boss at all, or null when it could not be asked.
        /// A boss missing from Vandi's BossBiomes reads as zero kills forever, which looks
        /// exactly like a boss nobody has killed yet - this is how the log tells the two apart.
        /// </summary>
        internal static bool? Counted(string bossKey)
        {
            if (string.IsNullOrEmpty(bossKey)) return false;

            try
            {
                return ReadCounted(bossKey);
            }
            catch (Exception error)
            {
                Warn(error);
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ReadLocalKills(string bossKey)
        {
            return Vandi.VandiApi.LocalBossKills(bossKey);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ReadCounted(string bossKey)
        {
            return Vandi.VandiApi.CountsKillsOf(bossKey);
        }

        private static void Warn(Exception error)
        {
            if (_warned) return;
            _warned = true;

            MalmrPlugin.Log.LogWarning("Could not ask Vandi for a boss count, so every metal that "
                + "waits for a boss stays shut. Malmr and Vandi are probably from different "
                + "builds. Said once per session: " + error.Message);
        }

        // ---------------------------------------------------------------- names

        /// <summary>The scene the tables below were read from. A new world is a new ZNetScene.</summary>
        private static ZNetScene _scene;

        /// <summary>Defeat key to the name the player's own game gives the creature that sets it.</summary>
        private static readonly Dictionary<string, string> Names =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every creature the game marks as a boss, as prefab=defeatkey, for the log.</summary>
        private static readonly List<string> Marked = new List<string>();

        /// <summary>
        /// What the game calls the boss behind a defeat key - "The Elder" for defeated_gdking -
        /// or the key itself when no creature in this world sets it.
        /// </summary>
        internal static string DisplayName(string bossKey)
        {
            if (string.IsNullOrEmpty(bossKey)) return "";

            Scan();

            string name;
            return Names.TryGetValue(bossKey, out name) ? name : bossKey;
        }

        /// <summary>
        /// Every creature prefab that sets a defeat key when it dies, read once per world.
        ///
        /// Keyed by the defeat key rather than by prefab because the key is what the config
        /// names. Where several creatures set one key, a creature the game marks as a boss wins
        /// the name - a lesser creature can carry a boss's key, and "the boss of copper is a
        /// summoned aspect" is not what anybody means.
        /// </summary>
        private static void Scan()
        {
            if (_scene == ZNetScene.instance) return;

            _scene = ZNetScene.instance;
            Names.Clear();
            Marked.Clear();

            if (_scene == null) return;

            var namedByBoss = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (GameObject prefab in _scene.m_prefabs)
            {
                if (prefab == null) continue;

                Character character;
                if (!prefab.TryGetComponent(out character)) continue;

                string key = string.IsNullOrEmpty(character.m_defeatSetGlobalKey)
                    ? null
                    : character.m_defeatSetGlobalKey.ToLowerInvariant();

                if (character.m_boss) Marked.Add(prefab.name + "=" + (key ?? "(no key)"));
                if (key == null) continue;

                if (namedByBoss.Contains(key)) continue;
                if (Names.ContainsKey(key) && !character.m_boss) continue;

                Names[key] = Localised(character.m_name, prefab.name);
                if (character.m_boss) namedByBoss.Add(key);
            }

            Marked.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private static string Localised(string token, string fallback)
        {
            if (string.IsNullOrEmpty(token) || Localization.instance == null) return fallback;

            string text = Localization.instance.Localize(token);
            return string.IsNullOrEmpty(text) ? fallback : text;
        }

        // ---------------------------------------------------------------- the survey

        /// <summary>
        /// The holes in the boss half: a boss Vandi does not count, and a boss nothing in this
        /// world sets on death. Either one keeps its metals shut forever, and from the player's
        /// side both look exactly like a boss not killed yet, which is why they are said out
        /// loud. The console prints the same lines.
        /// </summary>
        internal static List<string> Holes()
        {
            var lines = new List<string>();
            if (MalmrConfig.BossKills.Value <= 0) return lines;

            Scan();

            foreach (KeyValuePair<string, List<string>> pair in MetalsByBoss())
            {
                string metals = string.Join(", ", pair.Value.ToArray());

                bool? counted = Counted(pair.Key);
                if (counted == false)
                    lines.Add(pair.Key + ": Vandi does not count kills of it, because it is not in "
                        + "Vandi's BossBiomes. " + metals + " can never open until it is, or until "
                        + "Bosses gives " + (pair.Value.Count == 1 ? "it" : "them") + " a boss Vandi counts.");

                if (_scene != null && !Names.ContainsKey(pair.Key))
                    lines.Add(pair.Key + ": no creature in this world sets it when it dies, so "
                        + metals + " can never open. Check the spelling against the list of bosses "
                        + "on world load.");
            }

            return lines;
        }

        /// <summary>For the world-load log block: which metal waits for whom, then the holes.</summary>
        internal static void Describe(StringBuilder text)
        {
            int need = MalmrConfig.BossKills.Value;

            if (need <= 0)
            {
                text.Append("\n   Bosses: BossKills is ").Append(need)
                    .Append(", so every metal opens on the Pickaxes level alone.");
                return;
            }

            Dictionary<string, List<string>> byBoss = MetalsByBoss();

            if (byBoss.Count == 0)
            {
                text.Append("\n   Bosses: no metal in Unlocks waits for one.");
            }
            else
            {
                text.Append("\n   Bosses, each killed ").Append(need).Append(" time(s) through Vandi: ");

                bool first = true;
                foreach (KeyValuePair<string, List<string>> pair in byBoss)
                {
                    if (!first) text.Append("; ");
                    first = false;

                    text.Append(string.Join(", ", pair.Value.ToArray())).Append(" on ")
                        .Append(pair.Key).Append(" (").Append(DisplayName(pair.Key)).Append(')');
                }
            }

            foreach (string hole in Holes()) text.Append("\n   ").Append(hole);

            // Every creature the game itself calls a boss, and the key it sets. Written so that
            // a boss this mod has never heard of is one log line away from being named in
            // Bosses: 1.0's Deep North has a boss of its own, and its defeat key is asset data
            // that nothing offline can read.
            text.Append("\n   Bosses this world has, with the key each sets when it dies: ")
                .Append(Marked.Count == 0 ? "none found" : string.Join(", ", Marked.ToArray()));
        }

        /// <summary>
        /// Boss key to the Unlocks entries waiting for it, in Unlocks order. Only entries that are
        /// in Unlocks: a Bosses pair for a metal nobody vein-mines, like the default's Obsidian,
        /// is a line ready for the day somebody adds it, not a hole.
        /// </summary>
        private static Dictionary<string, List<string>> MetalsByBoss()
        {
            var byBoss = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string entry in MalmrConfig.UnlockTable().Keys)
            {
                string key = MalmrConfig.BossFor(entry);
                if (key == null) continue;

                List<string> metals;
                if (!byBoss.TryGetValue(key, out metals)) byBoss[key] = metals = new List<string>();
                metals.Add(entry);
            }

            return byBoss;
        }
    }
}
