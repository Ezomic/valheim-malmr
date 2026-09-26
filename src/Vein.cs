using System;
using System.Collections.Generic;
using UnityEngine;

namespace Malmr
{
    /// <summary>
    /// One pickaxe blow on a deposit, on the machine that swung it: vanilla's, or the bar's.
    ///
    /// <b>The mechanic, since 2026-09-26.</b> Robbin's words: you switch vein mining on with
    /// Alt, you keep mining a deposit, a bar fills to 100 until you have done enough damage for
    /// the whole deposit, and then it breaks all at once. So with vein mode on and the metal
    /// open, a blow on any chunk does not damage that chunk. It is sent to the deposit's owner
    /// as Malmr_Vein1 instead of RPC_Damage, and the owner puts what vanilla would have dealt
    /// into the bar - see Owner. It replaced the first mechanic, a few extra chunks taken beside
    /// the struck one on every swing, always on, with each extra chunk charged a share of the
    /// swing's wear.
    ///
    /// <b>Why the costs need no code now.</b> A swing is still a swing. Attack charges its
    /// stamina when the swing starts, its durability once when it lands on anything, and raises
    /// the skill once - none of that is in the deposit's Damage, which is the only thing this
    /// replaces. So a deposit costs the swings it takes, and its damage is exactly the damage
    /// those swings would have done by hand. The old per-chunk share of wear and stamina went
    /// with the old mechanic.
    ///
    /// Roughly the same swings as by hand, not exactly, and the difference runs both ways. By
    /// hand, the last blow on a chunk wastes whatever it deals past the chunk's health, and the
    /// bar wastes nothing. By hand, a chunk whose support you broke falls for free, and the bar
    /// charges every chunk's health, buried ones included. Which wins depends on the deposit.
    ///
    /// <b>Decided here because only this machine can.</b> Skills live in the player profile,
    /// not on any ZDO, and so does the game's own boss kill tally that the gate reads without
    /// Vandi, so whether a metal is open for this player is known only where the player is. The
    /// owner never re-checks it; it has no way to. The Mistlands rule is decided here too, for
    /// the same one-place reason, though the owner could have read that one.
    /// </summary>
    internal static class Vein
    {
        private static bool _warned;

        /// <summary>
        /// A blow arriving at a deposit's public Damage, before the deposit sees it. True lets
        /// vanilla have it; false means it went into the bar.
        ///
        /// Everything that keeps this to the player's own pickaxe is read off the blow itself:
        /// the attacker is the local player, the skill is Pickaxes, and it names the chunk it
        /// struck. Attack builds every swing's HitData that way, and nothing else in the game
        /// does. A blow with a radius, or naming no chunk, is the fractured-deposit spawn -
        /// Destructible hands the new rock the hit that broke the whole one, after the network
        /// has stripped its collider - and that goes to vanilla, which spreads it over whatever
        /// chunks the sphere finds.
        /// </summary>
        internal static bool Intercept(Component rock, HitData hit)
        {
            try
            {
                return !Divert(rock, hit);
            }
            catch (Exception error)
            {
                // A failure here must cost the bar, never the blow. Falling through to vanilla
                // means the swing still breaks rock the ordinary way.
                if (!_warned)
                {
                    _warned = true;
                    MalmrPlugin.Log.LogWarning("A vein mining blow could not be sent, and it "
                        + "went to the deposit the vanilla way. Said once per session: " + error);
                }

                return true;
            }
        }

        private static bool Divert(Component rock, HitData hit)
        {
            if (!MalmrConfig.Enabled.Value || !VeinMode.On) return false;
            if (rock == null || hit == null) return false;
            if (hit.m_hitCollider == null || hit.m_radius > 0f) return false;
            if (hit.m_skill != Skills.SkillType.Pickaxes) return false;

            Player player = Player.m_localPlayer;
            if (player == null || hit.m_attacker != player.GetZDOID()) return false;

            ZNetView nview;
            if (!rock.TryGetComponent(out nview)) return false;

            // A deposit whose ZDO is gone, swallowed rather than handed on. It happens inside the
            // swing that fills a bar: the owner breaks the deposit on the spot, and the same
            // swing's next chunk arrives at a rock that no longer exists. Vanilla's MineRock5
            // returns at once there, but MineRock.Damage does not check and throws on the null
            // ZDO, which would abort the rest of the swing - the wear and the skill with it.
            if (!nview.IsValid()) return true;

            Deposits.Kind kind = Deposits.Of(rock);
            if (kind == null || kind.Entry == null) return false;

            // Where it stands before what you have earned: a copper vein in the Mistlands is
            // mined by hand however open copper is, and "you need Pickaxes 30" would be the
            // wrong thing to tell a player looking at one. See Deposits.HandOnly.
            if (Deposits.HandOnly(rock, kind.Entry))
            {
                Tell(nview, kind, Deposits.Nouns(kind.Metal) + " in the Mistlands are mined by hand",
                     "is mined by hand in the Mistlands (the Mistlands line)");
                return false;
            }

            // The earned level, not the buffed one the blow was rolled with - see EarnedLevel. A
            // bonus still makes each blow harder, as it does in vanilla; it does not open a metal
            // early.
            Gate gate = Gate.For(kind.Entry, EarnedLevel(player));
            if (!gate.Open)
            {
                Tell(nview, kind, gate.Off
                        ? Deposits.Nouns(kind.Metal) + " cannot be vein mined here"
                        : Deposits.Nouns(kind.Metal) + " need " + gate.Needs(),
                     "is shut for vein mining: " + gate.Why());
                return false;
            }

            // The chunk as the deposit numbers it, which is what the owner indexes by. Both
            // deposit shapes build their area list from their colliders once, while every chunk
            // is still active, and Areas(true) is that list at any later time - dead chunks are
            // only deactivated, never removed.
            int area = Array.IndexOf(Deposits.Areas(rock, true), hit.m_hitCollider);
            if (area < 0) return false;

            Focus.Struck(rock);

            // To the owner, the road RPC_Damage takes. When this machine is the owner the call is
            // handled on the spot, inside this swing, before Damage would have returned.
            nview.InvokeRPC(Owner.Rpc, hit, area);

            if (MalmrConfig.Verbose.Value)
                MalmrPlugin.Log.LogInfo("Sent a vein mining blow on "
                    + Utils.GetPrefabName(rock.gameObject) + " (" + kind.Metal + ") chunk " + area
                    + " to " + (nview.IsOwner() ? "this machine" : "peer " + nview.GetZDO().GetOwner())
                    + ".");

            return true;
        }

        // ---------------------------------------------------------------- the shut metal

        /// <summary>Deposits already told about this world, by ZDO.</summary>
        private static readonly HashSet<ZDOID> Told = new HashSet<ZDOID>();

        private static ZNetScene _toldIn;

        /// <summary>
        /// Vein mode on, and this deposit is not one it takes: the blow goes to vanilla, and once
        /// per deposit the player is told why - "Iron veins need Pickaxes 40 and Bonemass beaten
        /// at one star through Vandi", or "Copper veins in the Mistlands are mined by hand".
        /// Once per deposit rather than once per metal because it is the rock in front of you
        /// that did not do what you expected, and once rather than every swing because a player
        /// who has read it is mining by hand on purpose.
        ///
        /// Top left, not centre. The centre is where the unlock message lives, and a crypt full
        /// of scrap piles with iron still shut would stack the same line there over and over.
        /// </summary>
        private static void Tell(ZNetView nview, Deposits.Kind kind, string onScreen, string forLog)
        {
            if (_toldIn != ZNetScene.instance)
            {
                _toldIn = ZNetScene.instance;
                Told.Clear();
            }

            if (!Told.Add(nview.GetZDO().m_uid)) return;

            Player player = Player.m_localPlayer;
            if (player == null) return;

            player.Message(MessageHud.MessageType.TopLeft, onScreen);

            if (MalmrConfig.Verbose.Value)
                MalmrPlugin.Log.LogInfo(kind.Metal + " " + forLog);
        }

        // ---------------------------------------------------------------- the level

        /// <summary>
        /// The level every unlock is read against: the Pickaxes level the character has earned,
        /// without what gear, food or a status effect adds on top.
        ///
        /// The earned level because it is the one the game shows. The skills page prints it as
        /// the big number with any bonus in a separate "+2" beside it, and the level-up message
        /// Announce rides on carries it too. The first version unlocked on the buffed level and
        /// announced on the earned one, so a character with a standing bonus - Rist's Quick
        /// study capstone is +2 to every skill and never wears off - had copper veins two levels
        /// early in silence and was told about them at the level. One level for both, and the
        /// one the player can read off their own screen.
        ///
        /// Read through GetSkillList because Skills.GetSkill is private and creates an entry as
        /// a side effect; a skill never used yet has no entry, and that is level 0.
        /// </summary>
        internal static float EarnedLevel(Player player)
        {
            if (player == null) return 0f;

            Skills skills = player.GetSkills();
            if (skills == null) return 0f;

            foreach (Skills.Skill skill in skills.GetSkillList())
            {
                if (skill == null || skill.m_info == null) continue;
                if (skill.m_info.m_skill == Skills.SkillType.Pickaxes) return Mathf.Floor(skill.m_level);
            }

            return 0f;
        }
    }
}
