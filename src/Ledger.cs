using System;
using UnityEngine;

namespace Malmr
{
    /// <summary>
    /// A deposit's numbers, read off its ZDO: the health of every chunk, what the bar has
    /// stored, and the two together as a percentage.
    ///
    /// <b>The bar is the sum of the chunks, as the deposit keeps them.</b> Robbin's rule is that
    /// 100% is enough damage for the whole deposit, buried chunks included, so the total is the
    /// CURRENT health of every chunk still standing. Nothing is stored for it: the deposit
    /// already keeps each chunk's health, and a hand-mined chunk leaves the sum on its own. So a
    /// friend swinging at the same rock by hand makes the percentage climb without anybody
    /// writing anything, which is what he asked for.
    ///
    /// Read from the ZDO on purpose, not from the deposit's own fields. MineRock5 keeps a copy
    /// of each chunk's health in memory, but only the owner's is current - every other client
    /// refreshes it on a ten second repeat (CheckForUpdate) or when a chunk dies. The ZDO is
    /// what the owner writes on every blow and what the network carries, so reading it is how
    /// a player who does not own the rock sees the same bar as the one who does.
    ///
    /// The two deposit shapes store health differently, and both are mirrored exactly:
    ///
    ///  - MineRock5 (copper, silver, the fractured boulders): one string, ZDOVars.s_health, a
    ///    base64 ZPackage of a count and one float per chunk - SaveHealth. A deposit nobody has
    ///    hit has no string at all, and every chunk is at the health Awake gave it, world level
    ///    included. A string shorter than the chunk list leaves the rest at that too, which is
    ///    what LoadHealth does.
    ///  - MineRock (tin and the small rocks): one float per chunk, "Health" + index, defaulting
    ///    to GetHealth() - RPC_Hit and AllDestroyed.
    ///
    /// The stored progress is Malmr's own float on the same ZDO. Only the owner writes it, from
    /// Owner, and it rides the ZDO everywhere the deposit goes: a logout, a friend finishing the
    /// job, a server restart.
    ///
    /// <b>Beside it, the total it was measured against</b>, and a bar whose deposit has grown
    /// since reads empty. In play the total only ever shrinks - a bar blow takes nothing off a
    /// chunk, and a blow by hand takes health off one - so a total above the one stored means
    /// the chunks came back. That is Dvala restocking a vein it kept: its KeepVeins holds on to
    /// a deposit emptied inside a crypt it manages, and thirty days later writes every chunk
    /// back to full health and nothing else. The bar used to be cleared only by Collapse, when
    /// Malmr's own break took the last chunk, so a vein finished by hand after some bar blows,
    /// or a break cut off by the owner leaving, kept its old progress into the restock: the
    /// fresh vein showed 80% before anyone touched it and broke whole on its first vein blow.
    /// Found in review on 2026-09-26. A world level change raises an untouched chunk's health
    /// the same way and empties the bar too, which is fair - the deposit got harder.
    ///
    /// Decided in Read rather than cleared in a write, because only the owner may write and the
    /// owner may not be looking when the chunks come back. Every client reads the same two
    /// floats and reaches the same answer, and the owner's next blow writes a fresh pair.
    /// </summary>
    internal static class Ledger
    {
        /// <summary>
        /// The ZDO key the bar's damage is stored under. Its name is permanent in the same way a
        /// prefab name is: a saved world carries the hash, and renaming it would zero every
        /// half-mined deposit in every world without a word.
        /// </summary>
        internal const string ProgressName = "malmr_vein_progress";

        /// <summary>
        /// The total the progress was last written against. Permanent for the same reason. A
        /// progress with no total beside it was written by a build before this one - only a test
        /// profile can hold one - and is trusted as it stands.
        /// </summary>
        internal const string TotalName = "malmr_vein_total";

        /// <summary>
        /// How far the total may rise before the bar counts as stale. Both sides are the same
        /// float sum over the same ZDO floats in the same order, so an unchanged deposit reads
        /// back exactly; this only absorbs rounding, and a regrown chunk is worth far more.
        /// </summary>
        private const float Regrown = 0.01f;

        private static int _progressKey, _totalKey;
        private static bool _keyed;

        internal static int ProgressKey
        {
            get
            {
                Key();
                return _progressKey;
            }
        }

        internal static int TotalKey
        {
            get
            {
                Key();
                return _totalKey;
            }
        }

        /// <summary>
        /// Hashed on first use rather than in a static initialiser. It cannot throw, but the
        /// house rule is that nothing runs at type-init in a class the patches reach.
        /// </summary>
        private static void Key()
        {
            if (_keyed) return;

            _progressKey = ProgressName.GetStableHashCode();
            _totalKey = TotalName.GetStableHashCode();
            _keyed = true;
        }

        /// <summary>One look at a deposit.</summary>
        internal sealed class Reading
        {
            /// <summary>Damage the bar holds. Zero when what was stored is stale - see the class comment.</summary>
            public float Progress;

            /// <summary>
            /// Whether progress was stored and thrown away because the deposit has grown since.
            /// Only for the console: a scenario or a player asking why a bar went back to zero.
            /// </summary>
            public bool Stale;

            /// <summary>Summed current health of every chunk still standing, buried or not.</summary>
            public float Total;

            public int Standing;
            public int Chunks;

            /// <summary>Health per chunk, in the deposit's own area order.</summary>
            public float[] Health;

            /// <summary>0 to 1. A deposit with nothing standing reads full, never divided by zero.</summary>
            public float Fraction
            {
                get { return Total <= 0f ? 1f : Mathf.Clamp01(Progress / Total); }
            }

            /// <summary>
            /// Whole percent, floored, so 100 is shown only once the bar has actually reached the
            /// total - rounding up would print 100% over a deposit that is still standing.
            /// </summary>
            public int Percent
            {
                get { return Mathf.Clamp(Mathf.FloorToInt(Fraction * 100f), 0, 100); }
            }
        }

        /// <summary>The deposit's numbers now, or null when it has no live ZDO.</summary>
        internal static Reading Read(Component rock, ZNetView nview)
        {
            if (rock == null || nview == null || !nview.IsValid()) return null;

            ZDO zdo = nview.GetZDO();
            float[] health = Health(rock, zdo);

            var reading = new Reading
            {
                Health = health,
                Chunks = health.Length,
            };

            foreach (float h in health)
            {
                if (h <= 0f) continue;
                reading.Total += h;
                reading.Standing++;
            }

            // The total first, because whether the stored progress still counts depends on it.
            // There is deliberately no other way to read the progress: a second reader that
            // skipped this check would bring the restock bug back through whichever caller used
            // it, which is why the old bare Progress(nview) is gone.
            float stored = Mathf.Max(0f, zdo.GetFloat(ProgressKey, 0f));
            float against = zdo.GetFloat(TotalKey, -1f);

            if (stored > 0f && against >= 0f && reading.Total > against + Regrown)
                reading.Stale = true;
            else
                reading.Progress = stored;

            return reading;
        }

        /// <summary>
        /// Owner only: see Owner for why nobody else ever writes this. The total goes with it on
        /// every write, so the next Read can tell whether the deposit has grown back since.
        /// </summary>
        internal static void SetProgress(ZNetView nview, float value, float total)
        {
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;

            ZDO zdo = nview.GetZDO();
            zdo.Set(ProgressKey, Mathf.Max(0f, value));
            zdo.Set(TotalKey, Mathf.Max(0f, total));
        }

        /// <summary>Every chunk's health, in the order the deposit numbers its areas.</summary>
        internal static float[] Health(Component rock, ZDO zdo)
        {
            int chunks = Deposits.Areas(rock, true).Length;
            var health = new float[chunks];
            float full = FullHealth(rock);

            for (int i = 0; i < chunks; i++) health[i] = full;
            if (zdo == null) return health;

            MineRock5 vein = rock as MineRock5;
            if (vein != null)
            {
                string saved = zdo.GetString(ZDOVars.s_health);
                if (saved.Length == 0) return health;

                try
                {
                    var package = new ZPackage(Convert.FromBase64String(saved));
                    int count = package.ReadInt();

                    for (int i = 0; i < count; i++)
                    {
                        float value = package.ReadSingle();
                        if (i < chunks) health[i] = value;
                    }
                }
                catch (Exception)
                {
                    // A string that does not parse is one LoadHealth would throw on too, so the
                    // deposit itself is broken. Reading it as untouched is the answer that keeps
                    // the bar from inventing progress.
                }

                return health;
            }

            if (rock is MineRock)
            {
                for (int i = 0; i < chunks; i++)
                    health[i] = zdo.GetFloat("Health" + i, full);
            }

            return health;
        }

        /// <summary>
        /// What one chunk starts at in this world, the expression the game itself uses. World
        /// level included: MineRock5.Awake seeds every area with it and MineRock.GetHealth
        /// returns it, and a bar measured against the base value would fill early on a harder
        /// world.
        /// </summary>
        internal static float FullHealth(Component rock)
        {
            MineRock small = rock as MineRock;
            if (small != null) return small.GetHealth();

            MineRock5 vein = rock as MineRock5;
            if (vein == null) return 0f;

            float multiplier = Game.instance != null ? Game.instance.m_worldLevelMineHPMultiplier : 0f;
            return vein.m_health + Game.m_worldLevel * vein.m_health * multiplier;
        }

        /// <summary>The deposit's resistances - what DamageArea and RPC_Hit apply to a blow.</summary>
        internal static HitData.DamageModifiers Modifiers(Component rock)
        {
            MineRock5 vein = rock as MineRock5;
            if (vein != null) return vein.m_damageModifiers;

            MineRock small = rock as MineRock;
            return small != null ? small.m_damageModifiers : default(HitData.DamageModifiers);
        }
    }
}
