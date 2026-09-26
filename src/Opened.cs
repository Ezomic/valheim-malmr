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
    /// is only right when the boss came first. A player who reaches Pickaxes 20 before beating
    /// the Elder at one star would be told copper was open when it was not, and then told
    /// nothing on the kill that actually opened it. So the message follows the metal's state,
    /// not either event: it fires when the whole Gate goes from shut to open.
    ///
    /// <b>How the kill is seen.</b> Vandi records a kill with SetGlobalKey on whichever machine
    /// owned the boss, and the server passes the new key list to every client. There is no event
    /// for "a key changed" to hook on the client - RPC_GlobalKeys clears the list and adds every
    /// key back - so this looks, once a second, from the plugin's Update. That is a handful of
    /// dictionary reads, and a second is well inside the time a player spends looking at a dead
    /// boss. The level-up still calls in directly, so a metal opened by the level is announced on
    /// the swing that earned it, beside vanilla's own skill line, as before.
    ///
    /// <b>What keeps it to one message.</b> Only a change from shut to open speaks, measured
    /// against what this same character saw a moment ago. The first look after logging in, after
    /// changing character or world, or after the rule itself changed takes the picture in
    /// silence: a metal already open when you arrive is not news, and neither is a host's table
    /// replacing your own when you join, which would otherwise open or shut half the list at
    /// once. Shutting is always silent. Nothing is saved, so the mod still writes nothing to the
    /// character or the world, and the one repeat that can happen is honest: the skill a death
    /// takes can drop a metal back below its level, and earning that level again opens it again.
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
        private static bool _capped;

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
            bool capped = MalmrConfig.MaxExtraChunks.Value > 0;

            bool fresh = id != _player
                         || ZNetScene.instance != _scene
                         || MalmrConfig.Revision != _revision
                         || MalmrConfig.BossKills.Value != _bossKills
                         || capped != _capped;

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
            _capped = capped;

            if (opened.Count == 0) return;

            // Always logged, whatever Verbose says: it happens once per metal, and it is the line
            // that answers "why did my pickaxe start doing that".
            foreach (Gate gate in opened)
                MalmrPlugin.Log.LogInfo(gate.Entry + " veins open: Pickaxes " + (int)gate.Level
                    + " of " + gate.Unlock
                    + (gate.Boss != null
                        ? ", " + gate.Boss + " killed " + gate.Kills + " of " + gate.KillsNeeded + " time(s)"
                        : ", no boss needed")
                    + ".");

            if (!MalmrConfig.Enabled.Value || !MalmrConfig.AnnounceUnlocks.Value) return;

            var names = new List<string>();
            foreach (Gate gate in opened) names.Add(Deposits.DisplayName(gate.Entry));

            string metals = names.Count == 1
                ? names[0]
                : string.Join(", ", names.GetRange(0, names.Count - 1).ToArray())
                  + " and " + names[names.Count - 1];

            player.Message(MessageHud.MessageType.Center, "Your pickaxe follows " + metals + " veins now");
        }
    }
}
