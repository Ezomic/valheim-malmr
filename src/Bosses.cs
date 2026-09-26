using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Malmr
{
    /// <summary>
    /// The boss half of an unlock: how many times the local player has killed a boss, and what
    /// the game calls that boss. Counted one of two ways, decided once at load by whether Vandi
    /// is installed.
    ///
    /// <b>Two counts, since Robbin's call of 2026-09-26.</b> Vandi is a soft dependency now:
    /// "make vandi a soft dependency of malmr but recommend it in the readme", and without it
    /// "still boss kill but no star since vanilla doesnt provide star". So:
    ///
    ///  - With Vandi, the count is Vandi's: kills of a boss this player summoned at its altar,
    ///    and BossKills of them (2 by default) is the one-star kill, because Vandi brings a boss
    ///    back one star harder per repeat kill. Only Vandi keeps who beat what at how many stars.
    ///  - Without Vandi, the count is the game's own tally of this character's kills, and one
    ///    kill is enough. There are no stars to ask for, and asking for a second plain kill would
    ///    be a grind the rule never meant.
    ///
    /// Not a fallback between the two while running. With Vandi installed and its answer
    /// unreadable - two builds that disagree - the count reads Unreadable and the metal stays
    /// shut. Dropping to the game's count there would open it on one kill, which is the generous
    /// version a Vandi server chose not to be.
    ///
    /// <b>The game's tally.</b> Every character carries per-creature kill counts in its profile,
    /// <c>PlayerProfile.m_playerStats[0].m_enemyStats[0]</c>, keyed by the creature's m_name
    /// token ("$enemy_gdking"). It is written live: when a creature dies, its owner's
    /// Character.OnDeath sends RPC_RegisterKill to every player marked on its ZDO as having hit
    /// it, and Game.RPC_RegisterKill calls PlayerProfile.IncrementStatEnemy on the spot - no
    /// save, no respawn in between. That is the difference from m_uniques, which CLAUDE.md warns
    /// never holds a boss killed this session: the defeat key waits in a static queue until the
    /// next Player.Start. So this reads the tally, never the uniques. "Marked as having hit it"
    /// is Character.ApplyDamage writing the attacker's name to the ZDO, so the tally credits
    /// everyone who landed a blow, not only the last one, and a character that only watched
    /// gets nothing. The tally is the character's and not the world's: a boss killed in any
    /// world counts, in every world.
    ///
    /// Two holes in it, both found in review on 2026-09-26 and both left as they are, said in
    /// the README instead:
    ///
    ///  - It only goes back to the Call to Arms update. LoadPlayerFromDisk reads the per-name
    ///    record only from a character saved at Version.Player.CallToArms (42) or later, so a
    ///    boss a veteran character beat before that is not in it and must be beaten again. The
    ///    record that does go back further is m_uniques, which is ruled out above.
    ///  - It does not care how the boss got there. IncrementStatEnemy writes index 0 whatever
    ///    its cheated flag says - the flag only gates the achievement copies - so a boss spawned
    ///    with devcommands in a local world counts once the character walks onto a server.
    ///    Keeping a record of Malmr's own that skipped cheated kills would miss every kill made
    ///    before Malmr was installed, which is a bigger hole than the one it closes, and a
    ///    character that has run devcommands already carries the mark Dyrr reads at the door
    ///    and could raise Pickaxes the same way.
    ///
    /// The token comes off the creatures themselves: the world-load survey already walks every
    /// Character prefab for its defeat key, and the same walk records its m_name. A boss key
    /// therefore leads to the boss prefab, and the prefab to the name its kills are filed under.
    /// </summary>
    internal static class Bosses
    {
        /// <summary>A count that could not be read. Never met.</summary>
        internal const int Unreadable = -1;

        /// <summary>
        /// Whether counts come from Vandi. The plugin asks BepInEx once, at load, and Vandi loads
        /// before Malmr when it is there at all (the soft dependency's one guarantee), so the
        /// answer cannot change during a session.
        /// </summary>
        internal static bool ThroughVandi
        {
            get { return MalmrPlugin.VandiPresent; }
        }

        /// <summary>
        /// How many kills open a metal: BossKills through Vandi, one without it. Only asked for
        /// a metal that has a boss, and BossFor already answers "no boss" when BossKills is 0,
        /// so 0 switches the boss half off on both roads.
        /// </summary>
        internal static int KillsNeeded
        {
            get { return ThroughVandi ? MalmrConfig.BossKills.Value : 1; }
        }

        private static bool _vandiWarned, _gameWarned;

        /// <summary>The local player's kills of that boss, by whichever count applies, or Unreadable.</summary>
        internal static int LocalKills(string bossKey)
        {
            if (string.IsNullOrEmpty(bossKey)) return 0;
            return ThroughVandi ? VandiKills(bossKey) : GameKills(bossKey);
        }

        /// <summary>
        /// Whether that boss can be counted at all, or null when it could not be asked. Through
        /// Vandi, whether Vandi records it - a key missing from its BossBiomes reads as zero
        /// kills forever, which looks exactly like a boss nobody has killed yet. Without Vandi,
        /// whether any creature in this world files kills under a name the key leads to.
        /// </summary>
        internal static bool? Counted(string bossKey)
        {
            if (string.IsNullOrEmpty(bossKey)) return false;

            if (!ThroughVandi)
            {
                Scan();
                return Tokens.ContainsKey(bossKey);
            }

            try
            {
                return VandiBridge.CountsKillsOf(bossKey);
            }
            catch (Exception error)
            {
                WarnVandi(error);
                return null;
            }
        }

        private static int VandiKills(string bossKey)
        {
            try
            {
                return VandiBridge.LocalBossKills(bossKey);
            }
            catch (Exception error)
            {
                WarnVandi(error);
                return Unreadable;
            }
        }

        /// <summary>
        /// The local character's kills of every creature that sets this key, from the profile's
        /// raw tally. Summed over the names, because two boss prefabs can set one key - nothing
        /// vanilla does, but a mod's variant would - and a kill of either is a kill of that boss.
        /// </summary>
        private static int GameKills(string bossKey)
        {
            try
            {
                Scan();

                List<string> tokens;
                if (!Tokens.TryGetValue(bossKey, out tokens)) return 0;

                // Plain == null, not ?. - Game is a Unity object.
                Game game = Game.instance;
                if (game == null) return 0;

                PlayerProfile profile = game.GetPlayerProfile();
                if (profile == null || profile.m_playerStats == null || profile.m_playerStats.Length == 0)
                    return 0;

                PlayerProfile.PlayerStats raw = profile.m_playerStats[0];
                if (raw == null || raw.m_enemyStats == null || raw.m_enemyStats.Length == 0
                    || raw.m_enemyStats[0] == null) return 0;

                float total = 0f;
                foreach (string token in tokens)
                {
                    float kills;
                    if (raw.m_enemyStats[0].TryGetValue(token, out kills)) total += kills;
                }

                return Mathf.Max(0, Mathf.FloorToInt(total + 0.001f));
            }
            catch (Exception error)
            {
                if (!_gameWarned)
                {
                    _gameWarned = true;
                    MalmrPlugin.Log.LogWarning("Could not read this character's boss kills from "
                        + "the game, so every metal that waits for a boss stays shut. Probably a "
                        + "game update moved the kill tally. Said once per session: " + error.Message);
                }

                return Unreadable;
            }
        }

        private static void WarnVandi(Exception error)
        {
            if (_vandiWarned) return;
            _vandiWarned = true;

            MalmrPlugin.Log.LogWarning("Could not ask Vandi for a boss count, so every metal that "
                + "waits for a boss stays shut. Malmr and Vandi are probably from different "
                + "builds. Said once per session: " + error.Message);
        }

        /// <summary>
        /// The boss half in words, as the player reads it on screen: "The Elder beaten at one
        /// star through Vandi", or "The Elder beaten by you" without it. Used both for what is
        /// missing and for what opened a metal, so the two always say the same thing, and it is
        /// what tells the player which count applies.
        ///
        /// Said in stars through Vandi, because stars are what the player sees on the boss: kill
        /// number N is the boss at N-1 stars. That is only a translation of BossKills, never a
        /// second rule - a host who sets BossKills 3 is asking for the two-star kill, and that is
        /// what the line says.
        /// </summary>
        internal static string Phrase(string bossKey)
        {
            string boss = DisplayName(bossKey);

            if (!ThroughVandi) return boss + " beaten by you";

            int stars = KillsNeeded - 1;
            if (stars <= 0) return boss + " beaten through Vandi";
            return boss + " beaten at " + (stars == 1 ? "one star" : stars + " stars") + " through Vandi";
        }

        /// <summary>One token for the console and the scenarios: which count applies.</summary>
        internal static string CountName
        {
            get { return ThroughVandi ? "vandi" : "game"; }
        }

        // ---------------------------------------------------------------- names

        /// <summary>The scene the tables below were read from. A new world is a new ZNetScene.</summary>
        private static ZNetScene _scene;

        /// <summary>Defeat key to the name the player's own game gives the creature that sets it.</summary>
        private static readonly Dictionary<string, string> Names =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Defeat key to the m_name tokens the game's kill tally files that boss under. The
        /// bosses' own when any creature marked m_boss sets the key, and only otherwise the
        /// lesser creatures that do: a creature carrying a boss's key is not the boss.
        /// </summary>
        private static readonly Dictionary<string, List<string>> Tokens =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

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

        /// <summary>The tally names a boss key leads to, joined for the log. Empty when none.</summary>
        private static string TokenText(string bossKey)
        {
            List<string> tokens;
            return Tokens.TryGetValue(bossKey, out tokens) ? string.Join("+", tokens.ToArray()) : "";
        }

        /// <summary>
        /// Every creature prefab that sets a defeat key when it dies, read once per world: its
        /// name on screen, and the token its kills are counted under.
        /// </summary>
        private static void Scan()
        {
            if (_scene == ZNetScene.instance) return;

            _scene = ZNetScene.instance;
            Names.Clear();
            Tokens.Clear();
            Marked.Clear();

            if (_scene == null) return;

            // Per key, the bosses that set it and the rest that do, in prefab order. Decided
            // after the walk, so the answer does not depend on which of them ZNetScene lists
            // first.
            var bosses = new Dictionary<string, List<Character>>(StringComparer.OrdinalIgnoreCase);
            var others = new Dictionary<string, List<Character>>(StringComparer.OrdinalIgnoreCase);
            var prefabOf = new Dictionary<Character, string>();

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

                Dictionary<string, List<Character>> into = character.m_boss ? bosses : others;

                List<Character> list;
                if (!into.TryGetValue(key, out list)) into[key] = list = new List<Character>();
                list.Add(character);
                prefabOf[character] = prefab.name;
            }

            var keys = new HashSet<string>(bosses.Keys, StringComparer.OrdinalIgnoreCase);
            keys.UnionWith(others.Keys);

            foreach (string key in keys)
            {
                List<Character> use;
                if (!bosses.TryGetValue(key, out use)) use = others[key];

                Names[key] = Localised(use[0].m_name, prefabOf[use[0]]);

                var tokens = new List<string>();
                foreach (Character character in use)
                    if (!string.IsNullOrEmpty(character.m_name) && !tokens.Contains(character.m_name))
                        tokens.Add(character.m_name);

                if (tokens.Count > 0) Tokens[key] = tokens;
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

                if (ThroughVandi && Counted(pair.Key) == false)
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
                text.Append(ThroughVandi
                    ? "\n   Bosses, each killed " + KillsNeeded + " time(s) through Vandi: "
                    : "\n   Bosses, each killed once by the character, as the game's own kill "
                      + "tally counts it (Vandi is not installed): ");

                bool first = true;
                foreach (KeyValuePair<string, List<string>> pair in byBoss)
                {
                    if (!first) text.Append("; ");
                    first = false;

                    text.Append(string.Join(", ", pair.Value.ToArray())).Append(" on ")
                        .Append(pair.Key).Append(" (").Append(DisplayName(pair.Key));

                    // The tally name only where it is what gets read. It is the one fact the
                    // game-count road hangs on, and the only place a wrong one would show.
                    if (!ThroughVandi)
                    {
                        string tokens = TokenText(pair.Key);
                        text.Append(", counted as ").Append(tokens.Length > 0 ? tokens : "nothing");
                    }

                    text.Append(')');
                }
            }

            foreach (string hole in Holes()) text.Append("\n   ").Append(hole);

            // Every creature the game itself calls a boss, and the key it sets. Written so that
            // a boss this mod has never heard of is one log line away from being named in
            // Bosses: 1.0's Deep North has a boss of its own, and its defeat key is asset data
            // that nothing offline can read. Bloodgold waits on Fader until it is read here.
            text.Append("\n   Bosses this world has, with the key each sets when it dies: ")
                .Append(Marked.Count == 0 ? "none found" : string.Join(", ", Marked.ToArray()));
        }

        /// <summary>
        /// Boss key to the Unlocks entries waiting for it, in Unlocks order. Only entries that are
        /// in Unlocks: a Bosses pair for something nobody vein-mines, like the default's Obsidian,
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
