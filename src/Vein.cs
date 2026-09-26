using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Malmr
{
    /// <summary>
    /// One pickaxe swing, and the chunks it takes beyond the ones it hit.
    ///
    /// <b>Where the decision is made, and why nowhere else will do.</b> Skills live in the
    /// player profile, not on any ZDO, so the only machine that knows this player's Pickaxes
    /// level is the one swinging. The deposit may be owned by somebody else entirely - whoever
    /// was nearest when it loaded, or the server - and that owner has no way to ask. So the
    /// extra chunks are chosen here, on the attacker's machine, and sent down the exact road a
    /// vanilla swing uses: IDestructible.Damage with a HitData whose m_hitCollider names the
    /// chunk, which MineRock5 and MineRock turn into an area index and an RPC to the owner.
    /// The owner applies it the way it applies any hit - resistance, tool tier, health, the
    /// support check, the drops at the chunk's own position, the ward flash - and every other
    /// client learns of it from the owner's broadcast, as they would have for a hit by hand.
    ///
    /// Nothing claims ownership. A claim-and-write would race the owner and lose chunks or
    /// double drops; the RPC is the owner doing its own write, which cannot race. It also means
    /// the owner needs no mod at all.
    ///
    /// <b>How one swing is seen whole.</b> A vanilla pickaxe hits several chunks in one swing -
    /// Attack.AddHitPoint keeps one hit point per COLLIDER on a MineRock5 or MineRock when
    /// m_pickaxeSpecial is set, and DoMeleeAttack calls Damage once for each. Extending the vein
    /// from inside Damage would extend it once per struck chunk and could pick a chunk the same
    /// swing is about to hit. So the swing is bracketed instead: a prefix on the attack opens
    /// it, a prefix on each deposit's Damage records what was struck, and the postfix on the
    /// attack does the extending once, knowing every chunk the swing reached by itself.
    ///
    /// <b>What a vein is.</b> For a MineRock5 - copper, silver, the fractured boulders - it is
    /// the deposit's live chunks, walked outward from the struck ones through chunks that touch,
    /// nearest first. Touching rather than simply nearest, so the walk follows the ore and does
    /// not jump a gap to a separate cluster. For a MineRock - tin and the smaller rocks - it is
    /// the same walk over its handful of areas. Every chunk of a deposit drops the same table,
    /// so the walk decides the ORDER chunks come off, never how much ore there is.
    /// </summary>
    internal static class Vein
    {
        /// <summary>
        /// How far apart two chunks' boxes may sit and still count as touching. Fractured chunks
        /// share faces, so their bounding boxes overlap outright; this is slack for rounding and
        /// for a chunk that sits a hair proud of its neighbour, not a reach. A constant rather
        /// than a setting because no value of it is a gameplay choice - the per-swing cap is.
        /// </summary>
        private const float Gap = 0.3f;

        private sealed class Struck
        {
            public Component Rock;
            public IDestructible Target;
            public readonly List<Collider> Colliders = new List<Collider>();

            /// <summary>
            /// Recorded at the moment of the hit, not read later. On a machine that owns the
            /// deposit the RPC is handled inside the same call - ZRoutedRpc dispatches a call to
            /// itself synchronously - so a chunk this swing kills is deactivated before the
            /// attack returns, and a deactivated collider reports empty bounds at the world
            /// origin. That would start the walk from the wrong end of the map.
            /// </summary>
            public readonly List<Bounds> Bounds = new List<Bounds>();

            /// <summary>
            /// Every blow this swing landed on this deposit, one per struck chunk, in the order
            /// the swing dealt them. The extra chunks take them in turn - see Record for why
            /// not the strongest.
            /// </summary>
            public readonly List<HitData> Hits = new List<HitData>();
        }

        /// <summary>
        /// Durability on the swung pickaxe when the bracket opened, so Close can read what the
        /// swing itself cost rather than recomputing it. See Close.
        /// </summary>
        private static float _durabilityBefore;

        /// <summary>True between the attack's prefix and postfix, for a local pickaxe swing only.</summary>
        private static bool _open;

        /// <summary>True while this class is calling Damage itself, so it does not record its own hits.</summary>
        private static bool _applying;

        private static readonly List<Struck> Swing = new List<Struck>();

        private static bool _warned;

        internal static void Open(Humanoid character, ItemDrop.ItemData weapon)
        {
            Swing.Clear();

            // Everything a swing needs to qualify is known here, so a sword, a thrall's
            // pickaxe or another player's swing costs one compare and nothing else. Every
            // Humanoid runs its attacks through the same Attack class, so this line is what
            // keeps the whole mod to the person at the keyboard.
            _open = MalmrConfig.Enabled.Value
                    && character != null
                    && character == Player.m_localPlayer
                    && weapon != null
                    && weapon.m_shared != null
                    && weapon.m_shared.m_skillType == Skills.SkillType.Pickaxes;

            if (_open) _durabilityBefore = weapon.m_durability;
        }

        /// <summary>
        /// A hit on a deposit, before the deposit sees it. A clone is kept because the caller's
        /// HitData is the one that goes on to the owner.
        /// </summary>
        internal static void Record(Component rock, HitData hit)
        {
            if (!_open || _applying) return;
            if (rock == null || hit == null) return;

            // Only a hit that names its chunk. MineRock5 has a second branch for a hit with a
            // radius or no collider, which picks chunks by an overlap sphere of its own - and
            // the one place the game uses it on a swing's behalf is the fractured boulder
            // Destructible spawns when a whole rock breaks. Destructible.Destroy hands the new
            // deposit the hit it was given, and by then that hit has been through the RPC:
            // ZRoutedRpc serialises even a call to itself, and m_hitCollider is not among the
            // fields HitData writes, so it arrives naming no chunk at all. There is nothing to
            // walk from, so the vein begins on the next swing, at a chunk the player actually
            // aimed at.
            if (hit.m_hitCollider == null || hit.m_radius > 0f) return;

            Struck struck = null;
            foreach (Struck candidate in Swing)
                if (candidate.Rock == rock) { struck = candidate; break; }

            if (struck == null)
            {
                IDestructible target = rock as IDestructible;
                if (target == null) return;

                struck = new Struck { Rock = rock, Target = target };
                Swing.Add(struck);
            }

            // One blow per chunk, which is also how the swing deals them: with m_pickaxeSpecial
            // Attack keeps one hit point per collider, so a second hit on the same chunk in one
            // swing does not happen in vanilla. Keeping the lists aligned is what lets Close
            // count blows by counting colliders.
            if (struck.Colliders.Contains(hit.m_hitCollider)) return;

            struck.Colliders.Add(hit.m_hitCollider);
            struck.Bounds.Add(hit.m_hitCollider.bounds);

            // Every blow, not the strongest. The first version kept the largest, on the idea
            // that the split between struck chunks made the others smaller - but the split is
            // the same for every chunk the swing hit (num5 /= list.Count * 0.75f in
            // DoMeleeAttack), and what differs between them is only the dice:
            // GetRandomSkillFactor is rolled again for each hit point inside the loop. So the
            // largest was the best of several rolls - at Pickaxes 50 with three chunks struck,
            // about 0.78 of a blow against 0.70 by hand - and a chunk just over two blows' worth
            // of health came off in two swings along the vein where it takes three by hand.
            // Each struck blow is a fair roll, so the extra chunks take them in turn and none
            // is chosen for its size.
            struck.Hits.Add(hit.Clone());
        }

        internal static void Close(Attack attack, Humanoid character, ItemDrop.ItemData weapon)
        {
            if (!_open) return;
            _open = false;

            if (Swing.Count == 0) return;

            try
            {
                Player player = character as Player;
                if (player == null || player != Player.m_localPlayer) return;

                // A swing-wide budget, not a per-deposit one. A swing that clips two deposits at
                // once would otherwise take twice the cap, and the cap is a promise about a
                // swing.
                int budget = MalmrConfig.MaxExtraChunks.Value;

                // What one blow cost by hand, which is what each extra chunk pays. A swing pays
                // ONCE - one durability drain, one stamina cost, one skill raise - however many
                // chunks it struck, and a fractured deposit often shows two or three damage
                // numbers a swing. Charging each extra chunk a whole swing, as the first version
                // did, made the vein-mined half of a deposit wear the pickaxe two or three times
                // as fast per blow as the half mined by hand, against the promise that a deposit
                // costs the same pickaxe either way. So the swing's cost is shared across every
                // chunk it landed on, across every deposit, and an extra chunk pays one share.
                // With one chunk struck the share is a whole swing, as before.
                int landed = 0;
                foreach (Struck struck in Swing) landed += struck.Hits.Count;

                var cost = new Cost { Share = landed > 0 ? 1f / landed : 1f };

                // The wear is read off the pickaxe rather than recomputed. Attack takes
                // m_useDurabilityDrain * Game.m_durabilityRate on a melee swing and a flat
                // 1 * m_durabilityRate on an area attack, the drain is asset data, and a mod
                // that changes wear changes it here too - the difference across the bracket is
                // what this swing actually cost, whichever of those applied.
                if (weapon != null && weapon.m_shared != null && weapon.m_shared.m_useDurability)
                    cost.WearPerBlow = Mathf.Max(0f, _durabilityBefore - weapon.m_durability)
                                       * cost.Share;

                foreach (Struck struck in Swing)
                {
                    if (budget <= 0) break;

                    // Caught per deposit. This runs inside the game's own attack, and an
                    // exception escaping here would surface as a vanilla swing misbehaving -
                    // a failure in the vein should cost the vein, never the swing.
                    try
                    {
                        budget -= Extend(struck, player, weapon, attack, budget, cost);
                    }
                    catch (Exception error)
                    {
                        if (!_warned)
                        {
                            _warned = true;
                            MalmrPlugin.Log.LogWarning("A vein could not be followed, and the "
                                + "swing went ahead as vanilla. Said once per session: "
                                + error);
                        }
                    }
                }
            }
            finally
            {
                Swing.Clear();
            }
        }

        /// <summary>From the attack's finalizer: whatever happened, the next swing starts clean.</summary>
        internal static void Reset()
        {
            _open = false;
            _applying = false;
            Swing.Clear();
        }

        /// <summary>What one extra chunk pays, worked out once per swing in Close.</summary>
        private sealed class Cost
        {
            /// <summary>1 over the number of chunks the swing struck, across every deposit.</summary>
            public float Share = 1f;

            /// <summary>The swing's own durability loss times Share. 0 on a pickaxe that does not wear.</summary>
            public float WearPerBlow;
        }

        /// <summary>
        /// The level every unlock is read against: the Pickaxes level the character has earned,
        /// without what gear, food or a status effect adds on top.
        ///
        /// The earned level because it is the one the game shows. The skills page prints it as
        /// the big number with any bonus in a separate "+2" beside it, and the level-up message
        /// Announce rides on carries it too. The first version unlocked on the buffed level and
        /// announced on the earned one, so a character with a standing bonus - Rist's Quick
        /// study capstone is +2 to every skill and never wears off - had copper veins at 18 in
        /// silence and was told about them at 20. One level for both, and the one the player can
        /// read off their own screen.
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

        /// <summary>
        /// The extra chunks for one deposit this swing touched. Returns how many it took.
        /// </summary>
        private static int Extend(Struck struck, Player player, ItemDrop.ItemData weapon,
                                  Attack attack, int budget, Cost cost)
        {
            Component rock = struck.Rock;
            if (rock == null || struck.Hits.Count == 0) return 0;

            HitData first = struck.Hits[0];

            ZNetView nview = rock.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return 0;

            Deposits.Kind kind = Deposits.Of(rock);
            if (kind == null || kind.Entry == null)
            {
                Quiet(rock, "is not a vein: " + (kind == null ? "unreadable" : kind.Why));
                return 0;
            }

            int unlock = MalmrConfig.LevelFor(kind.Entry);

            // The earned level, not the buffed one the blow was rolled with - see EarnedLevel.
            // A bonus still makes each blow harder, as it does in vanilla; it does not open a
            // metal early.
            float level = EarnedLevel(player);

            int allowed = MalmrConfig.ExtraChunks(level, unlock);
            if (allowed <= 0)
            {
                Quiet(rock, kind.Metal + " opens at Pickaxes " + unlock + ", you are at " + level);
                return 0;
            }

            if (allowed > budget) allowed = budget;

            // Checked here as well as by the owner, because the owner's refusal still costs the
            // hit: a pickaxe too weak for the rock would otherwise spend durability on every
            // extra chunk just to be told "too hard" once per chunk. Vanilla already said it
            // once, for the chunk that was struck.
            if (!first.CheckToolTier(Deposits.MinToolTier(rock))) return 0;

            List<Collider> live = LiveChunks(rock, struck.Colliders);

            // The whole vein in walking order, not just the first `allowed` of it, because a
            // chunk can come off between being chosen and being struck. See the loop.
            List<Collider> order = Walk(struck.Bounds, live, first.m_point, live.Count);

            int done = 0;
            int fell = 0;
            string stoppedBy = null;
            float swingStamina = -1f;

            foreach (Collider chunk in order)
            {
                if (done >= allowed) break;

                // Re-checked before every chunk, because the previous extra hit can change the
                // deposit under the list. When this machine owns it the whole hit runs inside
                // Damage below - ZRoutedRpc handles a call to itself inline - and a chunk that
                // breaks there runs CheckSupport, which breaks every chunk it was holding up and
                // deactivates them through RPC_SetAreaHealth, also inline. A chunk the walk
                // picked can therefore already be ore on the ground. Striking it anyway reached
                // DamageArea's "Already destroyed" and did nothing, but still took a share of
                // wear and one of the swing's slots. So a fallen chunk is skipped for free and
                // the next one along takes its place: the game dropped it by itself, as it would
                // have for a hit by hand. And a deposit whose last chunk broke has been handed to
                // ZNetScene.Destroy, which nulls its ZDO - MineRock5.Damage then returns at once,
                // and MineRock.Damage would throw on the null ZDO.
                //
                // What this cannot catch is the same collapse on a deposit someone else owns,
                // which reaches this machine a round trip later. That hit is spent on nothing,
                // the same as a vanilla swing at a chunk that has just fallen.
                if (!nview.IsValid())
                {
                    stoppedBy = "the deposit is gone";
                    break;
                }

                if (chunk == null || !chunk.gameObject.activeInHierarchy)
                {
                    fell++;
                    continue;
                }

                // Checked before the chunk, charged after it. A pickaxe at zero cannot swing in
                // vanilla - Humanoid refuses the attack - so it cannot take a chunk here either.
                if (weapon.m_shared.m_useDurability && weapon.m_durability <= 0f)
                {
                    stoppedBy = "the pickaxe is worn out";
                    break;
                }

                float stamina = 0f;
                if (MalmrConfig.StaminaPerChunk.Value > 0f)
                {
                    if (swingStamina < 0f) swingStamina = SwingStamina(attack);
                    stamina = swingStamina * cost.Share * MalmrConfig.StaminaPerChunk.Value;

                    if (stamina > 0f && !player.HaveStamina(stamina))
                    {
                        stoppedBy = "out of stamina";
                        break;
                    }
                }

                // A struck blow, aimed at this chunk, the struck blows taken in turn. The point
                // moves to the chunk because MineRock drops its ore just in front of
                // hit.m_point, and MineRock5 uses it for the hit effect when a deposit is not
                // set to use the chunk's own centre; the point nearest the first blow keeps the
                // ore on the side the player is standing.
                HitData hit = struck.Hits[done % struck.Hits.Count].Clone();
                hit.m_hitCollider = chunk;
                hit.m_point = chunk.bounds.ClosestPoint(first.m_point);

                _applying = true;
                try
                {
                    struck.Target.Damage(hit);
                }
                finally
                {
                    _applying = false;
                }

                done++;

                // One blow's share of what the swing cost, scaled by the setting - see Close.
                // Clamped at zero because vanilla's own is not, and a negative durability is a
                // number no repair or tooltip expects.
                if (weapon.m_shared.m_useDurability && cost.WearPerBlow > 0f)
                {
                    weapon.m_durability = Mathf.Max(0f, weapon.m_durability
                        - cost.WearPerBlow * MalmrConfig.DurabilityPerChunk.Value);
                }

                if (stamina > 0f) player.UseStamina(stamina);

                // Vanilla raises the skill once per swing, whatever it struck, so a blow is
                // worth one share of a raise here too.
                if (MalmrConfig.ExtraChunksTrainSkill.Value)
                    player.RaiseSkill(Skills.SkillType.Pickaxes, hit.m_skillRaiseAmount * cost.Share);
            }

            if (MalmrConfig.Verbose.Value)
            {
                MalmrPlugin.Log.LogInfo(Utils.GetPrefabName(rock.gameObject) + " (" + kind.Metal
                    + ", Pickaxes " + level + " against " + unlock + "): " + done + " of "
                    + allowed + " extra chunk(s) at " + cost.Share.ToString("0.##")
                    + " of a swing each, " + live.Count + " live beside the "
                    + struck.Colliders.Count + " struck"
                    + (fell > 0 ? ", " + fell + " fell on their own first" : "")
                    + (stoppedBy != null ? " - stopped, " + stoppedBy : "")
                    + (done < allowed && stoppedBy == null
                        ? " - the vein ran out of reachable chunks" : ""));
            }

            return done;
        }

        /// <summary>
        /// This deposit's chunks that are still standing, were not already struck by this
        /// swing, and are not buried.
        /// </summary>
        private static List<Collider> LiveChunks(Component rock, List<Collider> struck)
        {
            var live = new List<Collider>();

            foreach (Collider chunk in Deposits.Areas(rock, false))
            {
                if (chunk == null || !chunk.enabled) continue;
                if (struck.Contains(chunk)) continue;

                // A trigger is in the area list too, if a deposit has one, and no swing can
                // reach it - the attack's casts ignore triggers. Striking one here would be a
                // chunk the player could never have hit by hand.
                if (chunk.isTrigger) continue;

                if (MalmrConfig.LeaveBuried.Value && Buried(chunk.bounds.center)) continue;

                live.Add(chunk);
            }

            return live;
        }

        /// <summary>
        /// Whether a point is under the terrain. ZoneSystem's own ground probe, which casts
        /// down from far above against the terrain layer only - so inside a dungeon, hung
        /// thousands of metres above its entrance, it finds the surface far below and every
        /// chunk counts as uncovered, which is right: nobody digs in a crypt.
        /// </summary>
        private static bool Buried(Vector3 point)
        {
            if (ZoneSystem.instance == null) return false;

            float ground;
            return ZoneSystem.instance.GetGroundHeight(point, out ground) && point.y < ground;
        }

        /// <summary>
        /// Outward from the struck chunks, a ring at a time. Each ring is every remaining chunk
        /// that touches the ring before it, taken nearest to the blow first, until the count is
        /// met or nothing more touches.
        /// </summary>
        private static List<Collider> Walk(List<Bounds> seeds, List<Collider> live,
                                           Vector3 origin, int count)
        {
            var picked = new List<Collider>();
            var left = new List<Collider>(live);
            var frontier = new List<Bounds>(seeds);
            var ring = new List<Collider>();

            while (picked.Count < count && left.Count > 0 && frontier.Count > 0)
            {
                ring.Clear();

                foreach (Collider chunk in left)
                {
                    Bounds box = chunk.bounds;

                    foreach (Bounds edge in frontier)
                    {
                        Bounds grown = edge;
                        grown.Expand(Gap * 2f);
                        if (!grown.Intersects(box)) continue;

                        ring.Add(chunk);
                        break;
                    }
                }

                if (ring.Count == 0) break;

                ring.Sort((a, b) => (a.bounds.center - origin).sqrMagnitude
                                    .CompareTo((b.bounds.center - origin).sqrMagnitude));

                frontier = new List<Bounds>();

                foreach (Collider chunk in ring)
                {
                    if (picked.Count >= count) break;

                    picked.Add(chunk);
                    left.Remove(chunk);
                    frontier.Add(chunk.bounds);
                }
            }

            return picked;
        }

        // ---------------------------------------------------------------- stamina

        private static MethodInfo _attackStamina;
        private static bool _staminaBound;

        /// <summary>
        /// What one swing of this attack costs, from the game's own private GetAttackStamina,
        /// so skill, gear and status effects lower it for the extra chunks exactly as they
        /// lower it for the swing.
        ///
        /// Bound lazily inside a try, never in a static field initialiser: a reflection bind
        /// that throws at type-init poisons every patch the class carries. If the method has
        /// gone, the extra chunks are free of stamina and the log says so once - which is the
        /// default setting anyway.
        /// </summary>
        private static float SwingStamina(Attack attack)
        {
            if (!_staminaBound)
            {
                _staminaBound = true;
                try
                {
                    _attackStamina = AccessTools.Method(typeof(Attack), "GetAttackStamina");
                }
                catch (Exception)
                {
                    _attackStamina = null;
                }

                if (_attackStamina == null)
                    MalmrPlugin.Log.LogWarning("Attack.GetAttackStamina is gone, so "
                        + "StaminaPerChunk cannot be charged and extra chunks cost no stamina.");
            }

            if (_attackStamina == null || attack == null) return 0f;

            try
            {
                return (float)_attackStamina.Invoke(attack, null);
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        // ---------------------------------------------------------------- logging

        private static readonly HashSet<string> Said = new HashSet<string>();

        /// <summary>One Verbose line per deposit kind and reason, not one per swing.</summary>
        private static void Quiet(Component rock, string why)
        {
            if (!MalmrConfig.Verbose.Value) return;

            string line = Utils.GetPrefabName(rock.gameObject) + " " + why;
            if (!Said.Add(line)) return;

            MalmrPlugin.Log.LogInfo(line);
        }
    }
}
