using System;
using System.Collections.Generic;
using UnityEngine;

namespace Malmr
{
    /// <summary>
    /// A full bar, on the owner: every chunk still standing breaks, through the deposit's own
    /// damage path, a few a frame, top down.
    ///
    /// <b>The deposit's own path, so everything downstream is vanilla.</b> Each chunk gets one
    /// lethal blow sent to itself as RPC_Damage (MineRock5) or Hit (MineRock) - the message a
    /// pickaxe blow becomes. On the owner that routed call is handled on the spot, no network
    /// involved, and from there it is the game's own code: the health saved, the chunk hidden on
    /// every client, the break effect, the drops rolled from the deposit's table at the chunk's
    /// own position, the mine stats, the ward flash, and at the last chunk the deposit destroying
    /// itself. That last step is where Dvala's KeepVeins sits - a postfix on MineRock5's
    /// AllDestroyed - and it sees this break the same as the last blow by hand, so a vein inside
    /// a crypt it manages is kept for its restock.
    ///
    /// The blow is the one that filled the bar, with its damage replaced by the chunk's
    /// remaining health (and a hundredth, see Margin). Untyped damage (m_damage.m_damage) is the one kind no
    /// resistance touches, so the chunk always dies to it - the same trick the game's own
    /// support check uses. The tool tier, the attacker and the item's world level come from the
    /// real blow, which already passed the tier check to fill the bar. The number that floats
    /// up over each chunk is its remaining health, which is what it cost.
    ///
    /// Buried chunks are broken the same as the rest, because Robbin asked for everything, and
    /// their ore comes out: MineRock5 drops at the chunk's middle, under the ground, and
    /// ItemDrop's own TerrainCheck lifts anything more than half a metre below the surface to
    /// just above it on its next slow update.
    ///
    /// <b>Top down</b>, because the deposit's support check runs after every chunk that breaks
    /// and drops whatever that chunk was holding up. Breaking from the bottom would bring most of
    /// a deposit down in the first frame and undo the spreading below. From the top, a chunk
    /// only ever holds up chunks that are already gone.
    ///
    /// <b>A few a frame, not all at once.</b> Every chunk is a break effect with its sound, two
    /// or three item drops each with a rigidbody and a new ZDO, a broadcast to every client and
    /// a support check. Vanilla already survives that for a dozen chunks in one frame - a big
    /// support collapse does it - but vein mining would make that worst case the normal case,
    /// on every deposit, on whichever machine happens to own it, and a copper deposit is
    /// dozens of chunks. Six a frame is still well under a tenth of a second for any ore
    /// deposit the game ships, which reads as one break.
    ///
    /// <b>And a time budget, since stone.</b> On 2026-09-27 every rock whose drops are all
    /// stone became a vein, and the biggest of those are not boulders but pillars and cliffs,
    /// which may be hundreds of chunks. None of that can be counted offline; the world-load log
    /// gives every vein's chunk count. Size costs twice. More chunks, and each one dearer,
    /// because the support check MineRock5 runs after every chunk that breaks (UpdateSupport,
    /// read in the 1.0 assembly) boxes every chunk of the deposit three times over and, for
    /// each neighbour of the same deposit it finds, walks the deposit's whole chunk list - so
    /// its cost grows faster than the deposit does. Six of those a frame on a cliff could be a
    /// stutter on every frame of its break. So six is the ceiling and BudgetMs is the real
    /// limit: once a frame has spent that long breaking, the rest wait for the next frame, and
    /// one chunk always goes, so the biggest deposit still finishes. An ore deposit is expected
    /// to fit six inside the budget and break exactly as before; a big rock takes longer to
    /// come down from the top, which is honest about its size. While it does, the bar reads
    /// full and a blow on it adds nothing (Busy), the same as for an ore deposit's few frames.
    ///
    /// Every chunk still drops what it drops, so a big rock leaves a crowd of stone on the
    /// ground at once. It is the total a hand would have taken, only sooner, and the game has
    /// its own answer to a crowd: ItemDrop's slow update stacks neighbours of the same item
    /// together once more than 200 drops are loaded (AutoStackItems).
    ///
    /// <b>If it is cut off</b> - the owner leaves, the deposit unloads - the job simply stops.
    /// The bar is not cleared until every chunk is gone, so what is left reads 100% on whoever
    /// owns it next, and the next vein blow there breaks the rest. The chunks already broken
    /// only shrank the total, so the stored progress still counts - Ledger throws it away only
    /// when the total has grown.
    /// </summary>
    internal static class Collapse
    {
        /// <summary>
        /// Chunks broken per frame. A constant rather than a setting: no value of it is a
        /// gameplay choice, only a frame-time one, and the argument for six is above.
        /// </summary>
        private const int PerFrame = 6;

        /// <summary>
        /// Milliseconds of breaking a frame, per deposit, after which the rest wait for the next
        /// frame; the first chunk of a frame goes whatever it costs. A quarter of a frame at 60,
        /// so a big stone deposit slows its own break down rather than the game. Also a constant,
        /// for PerFrame's reason. See the class comment for why stone needed it.
        /// </summary>
        private const double BudgetMs = 4.0;

        /// <summary>
        /// Added to each chunk's remaining health in its lethal blow. The chunk dies to its exact
        /// health already - the deposit subtracts the same float it saved - but a hundredth is
        /// cheap insurance against a rounding path nobody has read, and the floating number shows
        /// one decimal, so it never changes what the player sees.
        /// </summary>
        private const float Margin = 0.01f;

        private sealed class Job
        {
            public Component Rock;
            public ZNetView View;
            public HitData Template;
            public string Prefab;

            /// <summary>Chunk indices still to break, highest first.</summary>
            public readonly List<int> Order = new List<int>();

            public int Next;
            public int Broken;
            public int Refused;
            public int Frames;
        }

        private static readonly List<Job> Jobs = new List<Job>();

        private static ZNetScene _scene;

        private static bool _warned;

        /// <summary>Whether this deposit is breaking right now, so a blow arriving mid-break is not counted twice.</summary>
        internal static bool Busy(ZNetView nview)
        {
            foreach (Job job in Jobs)
                if (job.View == nview) return true;
            return false;
        }

        internal static void Start(Component rock, ZNetView nview, HitData template)
        {
            if (rock == null || nview == null) return;

            // Jobs belong to the scene they started in. Taken here as well as in Tick, because
            // the first job of a world starts before Tick has seen that world at all.
            if (ZNetScene.instance != _scene)
            {
                _scene = ZNetScene.instance;
                Jobs.Clear();
            }

            if (Busy(nview)) return;

            var job = new Job
            {
                Rock = rock,
                View = nview,
                Template = template,
                Prefab = Utils.GetPrefabName(rock.gameObject),
            };

            Collider[] areas = Deposits.Areas(rock, true);
            Ledger.Reading reading = Ledger.Read(rock, nview);
            if (reading == null) return;

            for (int i = 0; i < reading.Health.Length && i < areas.Length; i++)
                if (reading.Health[i] > 0f && areas[i] != null) job.Order.Add(i);

            job.Order.Sort((a, b) => areas[b].bounds.center.y.CompareTo(areas[a].bounds.center.y));

            Jobs.Add(job);

            // The first frame's worth now, so the break lands on the blow that filled the bar
            // rather than a frame after it. Caught here as in Tick: a throw from inside the
            // deposit's own damage path would otherwise surface as the blow having been lost.
            try
            {
                Step(job);
            }
            catch (Exception error)
            {
                job.Next = job.Order.Count;
                Warn(error);
            }

            if (Done(job)) Finish(job);
        }

        /// <summary>From the plugin's Update: the next few chunks of every deposit that is breaking.</summary>
        internal static void Tick()
        {
            if (Jobs.Count == 0) return;

            // A new world is a new scene, and every job belongs to the old one.
            if (ZNetScene.instance != _scene)
            {
                _scene = ZNetScene.instance;
                Jobs.Clear();
                return;
            }

            for (int i = Jobs.Count - 1; i >= 0; i--)
            {
                Job job = Jobs[i];

                try
                {
                    Step(job);
                }
                catch (Exception error)
                {
                    job.Next = job.Order.Count;
                    Warn(error);
                }

                if (Done(job)) Finish(job);
            }
        }

        private static void Step(Job job)
        {
            job.Frames++;

            int budget = PerFrame;

            // A timestamp rather than a Stopwatch object: nothing allocated per frame, and
            // nothing held between frames that a scene change could leave stale.
            long started = System.Diagnostics.Stopwatch.GetTimestamp();

            while (budget > 0 && job.Next < job.Order.Count)
            {
                if (budget < PerFrame && Elapsed(started) >= BudgetMs) return;

                if (job.Rock == null || !job.View.IsValid() || !job.View.IsOwner())
                {
                    job.Next = job.Order.Count;
                    return;
                }

                int area = job.Order[job.Next++];

                // Re-read before each one. The support check inside the previous chunk's break
                // may already have taken this one, and a second lethal blow at a dead chunk is
                // "Already destroyed" in vanilla - harmless, but not worth a frame's budget.
                float[] health = Ledger.Health(job.Rock, job.View.GetZDO());
                if (area >= health.Length || health[area] <= 0f) continue;

                Collider[] areas = Deposits.Areas(job.Rock, true);
                Collider chunk = area < areas.Length ? areas[area] : null;

                HitData blow = job.Template.Clone();
                blow.m_damage = new HitData.DamageTypes();
                blow.m_damage.m_damage = health[area] + Margin;
                blow.m_hitCollider = null;
                blow.m_radius = 0f;
                blow.m_pushForce = 0f;

                // Each chunk's own middle. MineRock5 drops there anyway on the default
                // m_hitEffectAreaCenter; with it off, and on a MineRock, the drops and the effect
                // follow m_point, and the struck point would pile a whole deposit's ore in one spot.
                if (chunk != null) blow.m_point = chunk.bounds.center;

                job.View.InvokeRPC(job.Rock is MineRock5 ? "RPC_Damage" : "Hit", blow, area);

                budget--;

                // The last chunk destroys the deposit, and there is nothing left to read.
                if (!job.View.IsValid())
                {
                    job.Broken++;
                    job.Next = job.Order.Count;
                    return;
                }

                float[] after = Ledger.Health(job.Rock, job.View.GetZDO());
                if (area < after.Length && after[area] > 0f) job.Refused++;
                else job.Broken++;
            }
        }

        private static double Elapsed(long started)
        {
            return (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0
                   / System.Diagnostics.Stopwatch.Frequency;
        }

        private static bool Done(Job job)
        {
            return job.Next >= job.Order.Count;
        }

        private static void Finish(Job job)
        {
            Jobs.Remove(job);

            bool alive = job.Rock != null && job.View.IsValid();
            Ledger.Reading left = alive ? Ledger.Read(job.Rock, job.View) : null;

            // Cleared only when nothing is standing. That is Dvala keeping a finished vein for its
            // restock, and the bar must start empty when the chunks come back. With chunks still
            // up - a cut-off job, or blows the deposit refused - the bar stays full, so the next
            // vein blow finishes the job rather than the player losing it.
            //
            // Not the only thing that empties a restocked bar any more. A vein finished by hand
            // never reaches this line, and Ledger.Read now throws away progress stored against
            // a smaller total than the deposit has - see Ledger. This clear stays because it is
            // the tidy answer where Malmr did the breaking, and costs one write.
            if (left != null && left.Standing == 0) Ledger.SetProgress(job.View, 0f, left.Total);

            if (job.Refused > 0 || (left != null && left.Standing > 0 && job.View.IsOwner()))
            {
                if (!_warned)
                {
                    _warned = true;
                    MalmrPlugin.Log.LogWarning(job.Prefab + " did not break whole: "
                        + job.Refused + " chunk(s) survived a lethal blow and "
                        + (left != null ? left.Standing : 0) + " are still standing. Its bar "
                        + "stays full, so the next vein blow tries again. Most likely a game "
                        + "update renamed the deposit's damage message. Said once per session.");
                }
            }

            if (MalmrConfig.Verbose.Value)
                MalmrPlugin.Log.LogInfo(job.Prefab + " broke whole: " + job.Broken + " of "
                    + job.Order.Count + " chunk(s) over " + job.Frames + " frame(s)"
                    + (alive ? ", the deposit kept (" + (left != null ? left.Standing : 0) + " standing)" : "")
                    + ".");
        }

        private static void Warn(Exception error)
        {
            if (_warned) return;
            _warned = true;
            MalmrPlugin.Log.LogWarning("A deposit could not finish breaking. Its bar stays full, "
                + "so the next vein blow tries again. Said once per session: " + error);
        }
    }
}
