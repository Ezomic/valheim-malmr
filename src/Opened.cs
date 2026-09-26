using System;
using System.Collections.Generic;
using UnityEngine;

namespace Malmr
{
    /// <summary>
    /// The unlock, said out loud: once, when a metal opens, whichever half of it landed second.
    ///
    /// <b>Why this is not simply the level-up any more.</b> The first version announced on
    /// Player.OnSkillLevelup, when the level reached matched a metal's. With the boss half that
    /// is only right when the boss came first. A player who reaches Pickaxes 30 before beating
    /// the Elder would be told copper was open when it was not, and then told
    /// nothing on the kill that actually opened it. So the message follows the metal's state,
    /// not either event: it fires when the whole Gate goes from shut to open.
    ///
    /// <b>How the kill is seen.</b> With Vandi, a kill is recorded with SetGlobalKey on whichever
    /// machine owned the boss, and the server passes the new key list to every client. There is
    /// no event for "a key changed" to hook on the client - RPC_GlobalKeys clears the list and
    /// adds every key back - so this looks, once a second, from the plugin's Update. Without
    /// Vandi the count is the character's own tally, which Game.RPC_RegisterKill bumps the moment
    /// the boss dies, and the same look finds it. Either way a handful of dictionary reads, and a
    /// second is well inside the time a player spends looking at a dead boss. The level-up still
    /// calls in directly, so a metal opened by the level is announced on the swing that earned
    /// it, beside vanilla's own skill line, as before.
    ///
    /// <b>What keeps it to one message.</b> Only a change from shut to open speaks, measured
    /// against what this same character saw a moment ago. The first look after logging in, after
    /// changing character or world, or after the rule itself changed takes the picture in
    /// silence: a metal already open when you arrive is not news, and neither is a host's table
    /// replacing your own when you join, which would otherwise open or shut half the list at
    /// once. Shutting is always silent. Nothing is saved - this picture is the one thing about an
    /// unlock the mod keeps, and it lives in memory - and the one repeat that can happen is
    /// honest: the skill a death takes can drop a metal back below its level, and earning that
    /// level again opens it again.
    ///
    /// The message names the vein mode key. An open metal does nothing until vein mode is on,
    /// so the unlock is the one moment the player is sure to be told how to use it.
    ///
    /// The rebroadcast of the whole key list that every SetGlobalKey causes does not look like a
    /// change here, because a whole RPC runs inside one frame and this never looks mid-frame.
    /// </summary>
    internal static class Opened
    {
        private const float Interval = 1f;

        private static float _next;

        /// <summary>The character the picture below is of. 0 means no picture yet.</summary>
        private static long _player;

        private static ZNetScene _scene;

        /// <summary>
        /// The parts of the rule the picture was taken under. A change to any of them is a new
        /// rule rather than a metal opening, so it is taken in silence.
        /// </summary>
        private static int _revision = -1;
        private static int _bossKills = -1;

        private static readonly Dictionary<string, bool> Was =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>From the plugin's Update. Cheap on every frame but one a second.</summary>
        internal static void Tick()
        {
            if (Time.time < _next) return;
            _next = Time.time + Interval;

            Check();
        }

        private static bool _failed;

        /// <summary>
        /// Looks at every metal now and says which have just opened. Also called straight from
        /// the Pickaxes level-up, so the message lands with it rather than up to a second later.
        ///
        /// Caught here rather than by the callers, because one of them is a postfix inside
        /// vanilla's RaiseSkill: an exception escaping it would surface as a skill that failed
        /// to level. A failure costs the message and nothing else - the swing asks Gate itself.
        /// </summary>
        internal static void Check()
        {
            try
            {
                Look();
            }
            catch (Exception error)
            {
                // Once, not once a second for the rest of the session.
                if (_failed) return;
                _failed = true;
                MalmrPlugin.Log.LogWarning("Could not check which metals are open, so the unlock "
                    + "message may not come. Swinging is unaffected. Said once per session: "
                    + error);
            }
        }

        private static void Look()
        {
            Player player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null || ZoneSystem.instance == null) return;

            // The profile's id, not the Player object: a respawn is a new Player with the same id,
            // and a death that drops a metal below its level should shut it silently rather than
            // start a new picture.
            long id = player.GetPlayerID();
            if (id == 0L) return;

            float level = Vein.EarnedLevel(player);

            var now = new List<Gate>();
            foreach (string entry in MalmrConfig.UnlockTable().Keys)
            {
                // Not a metal, so there is nothing to call it on screen, and the first ore it
                // finds would have to be named by a swing that has not happened.
                if (entry == MalmrConfig.AnyMetal) continue;

                now.Add(Gate.For(entry, level));
            }

            // Read after the gates, because asking them is what parses a changed rule and moves
            // the revision.
            bool fresh = id != _player
                         || ZNetScene.instance != _scene
                         || MalmrConfig.Revision != _revision
                         || MalmrConfig.BossKills.Value != _bossKills;

            var opened = new List<Gate>();

            foreach (Gate gate in now)
            {
                bool was;
                if (!fresh && Was.TryGetValue(gate.Entry, out was) && !was && gate.Open)
                    opened.Add(gate);
            }

            Was.Clear();
            foreach (Gate gate in now) Was[gate.Entry] = gate.Open;

            _player = id;
            _scene = ZNetScene.instance;
            _revision = MalmrConfig.Revision;
            _bossKills = MalmrConfig.BossKills.Value;

            if (opened.Count == 0) return;

            // Always logged, whatever Verbose says: it happens once per metal, and it is the line
            // that answers "why does vein mode fill a bar on this rock and not on that one".
            foreach (Gate gate in opened)
                MalmrPlugin.Log.LogInfo(gate.Entry + " veins open: Pickaxes " + (int)gate.Level
                    + " of " + gate.Unlock
                    + (gate.Boss != null
                        ? ", " + gate.Boss + " killed " + gate.Kills + " of " + gate.KillsNeeded
                          + " time(s) " + (Bosses.ThroughVandi ? "through Vandi" : "as the game counts it")
                        : ", no boss needed")
                    + ".");

            if (!MalmrConfig.Enabled.Value || !MalmrConfig.AnnounceUnlocks.Value) return;

            player.Message(MessageHud.MessageType.Center, Message(opened));
        }

        /// <summary>
        /// "Copper veins open to you now: Pickaxes 30 and The Elder beaten at one star through
        /// Vandi." and then how to use it. What opened each one is said, because the boss half
        /// of that sentence is where the player learns which count applies - at one star through
        /// Vandi, or beaten by you without it - and a rule nobody is told is a rule that reads as
        /// a bug the first time it differs from a friend's.
        ///
        /// Metals that opened for the same reason share a line, which is the usual case: one
        /// level-up or one boss kill. Several reasons at once - a skill set by hand, or a host's
        /// table arriving mid-session - get a line each rather than one sentence nobody can parse.
        /// </summary>
        private static string Message(List<Gate> opened)
        {
            var reasons = new List<string>();
            var nouns = new Dictionary<string, List<string>>();

            foreach (Gate gate in opened)
            {
                string reason = gate.Earned();

                List<string> list;
                if (!nouns.TryGetValue(reason, out list))
                {
                    nouns[reason] = list = new List<string>();
                    reasons.Add(reason);
                }

                list.Add(Deposits.Nouns(gate.Entry));
            }

            var text = new System.Text.StringBuilder();

            foreach (string reason in reasons)
            {
                List<string> list = nouns[reason];

                string things = list.Count == 1
                    ? list[0]
                    : string.Join(", ", list.GetRange(0, list.Count - 1).ToArray())
                      + " and " + list[list.Count - 1];

                text.Append(things).Append(" open to you now: ").Append(reason).Append(".\n");
            }

            // The key is named, because the message is the one moment a player learns this mod
            // exists: an open metal does nothing at all until vein mode is switched on.
            text.Append("Tap ").Append(KeyName(MalmrConfig.VeinToggleKey.Value))
                .Append(" with a pickaxe out to mine a whole deposit");

            return text.ToString();
        }

        /// <summary>
        /// A KeyCode as a person reads it: LeftAlt as Left Alt. Only the word breaks; the name is
        /// Unity's own, which is also what the player typed into the cfg.
        /// </summary>
        internal static string KeyName(KeyCode key)
        {
            string raw = key.ToString();
            var text = new System.Text.StringBuilder(raw.Length + 4);

            for (int i = 0; i < raw.Length; i++)
            {
                if (i > 0 && char.IsUpper(raw[i]) && !char.IsUpper(raw[i - 1])) text.Append(' ');
                text.Append(raw[i]);
            }

            return text.ToString();
        }
    }
}
