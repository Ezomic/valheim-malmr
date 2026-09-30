using System;
using UnityEngine;

namespace Malmr
{
    /// <summary>
    /// The deposit's owner, taking a vein mining blow: count it the way the deposit would have,
    /// add it to the bar on the deposit, and break the deposit when the bar is full.
    ///
    /// <b>Why the owner, and why by message.</b> The bar lives on the deposit's ZDO so that it
    /// survives a logout and a friend can finish the job. Only the owner may write a ZDO - a
    /// write by anybody else is silently discarded - and the obvious way round that, claiming
    /// ownership first, is a race: two players swinging at one rock would each claim, each add
    /// their blow to the number they last saw, and each write it back, and one blow in every
    /// such pair would vanish. So the swinging machine sends the blow and the owner does its own
    /// write, which cannot race because only one machine is doing it. It is the same road a
    /// vanilla pickaxe blow travels: MineRock5.Damage turns the hit into RPC_Damage and sends it
    /// to the owner, who applies it in DamageArea. Malmr_Vein1 is that message with a different
    /// ending.
    ///
    /// That is also why the mod is Requirement.Everyone - see the plugin. The owner might be
    /// anybody near the rock, or the server, and a machine without this handler drops the
    /// message with a warning in its log and the blow is lost.
    ///
    /// <b>What is counted is what vanilla would have dealt that chunk.</b> DamageArea and
    /// RPC_Hit are mirrored step for step, in their order: a chunk already gone takes nothing
    /// and says nothing; the deposit's resistances are applied; a pickaxe below the deposit's
    /// tool tier shows the game's own "too hard" and adds nothing; the damage number floats up
    /// where vanilla would put it; a blow of zero stops there; and then the deposit's own hit
    /// effect, the noise that wakes things nearby, and the mine-hit stat. The ward flash for a
    /// deposit that has one comes after all of that on every blow, landed or not, because
    /// RPC_Damage flashes whatever DamageArea answered. The only difference is the last line -
    /// the number goes into the bar instead of off the chunk.
    ///
    /// The wire version is in the name, the way Skra does it: a peer on another build has no
    /// handler for this hash and drops it, where a changed parameter list under an unchanged
    /// name would throw inside HandleRoutedRPC on that peer.
    /// </summary>
    internal static class Owner
    {
        internal const string Rpc = "Malmr_Vein1";

        private static bool _duplicateSaid;

        /// <summary>
        /// Called from MineRock5.Awake and MineRock.Start, the two places each deposit registers
        /// its own messages, under the same condition they do: a view with a ZDO. A placement
        /// ghost or a clone made with init suppressed has none and must not register.
        /// </summary>
        internal static void Listen(Component rock)
        {
            if (rock == null) return;

            ZNetView nview;
            if (!rock.TryGetComponent(out nview) || nview.GetZDO() == null) return;

            try
            {
                nview.Register<HitData, int>(Rpc, (sender, hit, area) => OnHit(rock, nview, sender, hit, area));
            }
            catch (ArgumentException)
            {
                // Register is m_functions.Add, and a second Add of one name throws. Awake and Start
                // run once per instance, so this is not expected; it is caught because an exception
                // here would escape the deposit's own Awake and take its chunks with it.
                if (_duplicateSaid) return;
                _duplicateSaid = true;
                MalmrPlugin.Log.LogWarning(Rpc + " was already registered on "
                    + Deposits.PrefabName(rock) + "; kept the first. Said once per session.");
            }
        }

        private static bool _failed;

        private static void OnHit(Component rock, ZNetView nview, long sender, HitData hit, int area)
        {
            try
            {
                Apply(rock, nview, sender, hit, area);
            }
            catch (Exception error)
            {
                // A failure here costs the blow, never the deposit: it is a handler inside the
                // routed RPC dispatch, and an exception escaping it lands in the middle of the
                // network loop.
                if (_failed) return;
                _failed = true;
                MalmrPlugin.Log.LogWarning("A vein mining blow could not be counted and was lost. "
                    + "Said once per session: " + error);
            }
        }

        private static void Apply(Component rock, ZNetView nview, long sender, HitData hit, int area)
        {
            // The same guard RPC_Damage opens with. Ownership can move while a message is in
            // flight, and the blow is then lost, exactly as a vanilla blow would be.
            if (rock == null || hit == null || nview == null || !nview.IsValid() || !nview.IsOwner()) return;

            // Switched off here but not on the sender - a host turned the mod off a moment ago,
            // or two machines disagree. The blow still happened, so it goes where a vanilla blow
            // goes: the deposit's own damage path, on this machine, at once.
            if (!MalmrConfig.Enabled.Value)
            {
                nview.InvokeRPC(rock is MineRock5 ? "RPC_Damage" : "Hit", hit, area);
                return;
            }

            HitData template;
            bool full = Count(rock, nview, sender, hit, area, out template);

            // After the count and before any break, in the order RPC_Damage has it: DamageArea,
            // then the flash, whatever DamageArea answered.
            Ward(rock, hit);

            if (full) Collapse.Start(rock, nview, template);
        }

        /// <summary>
        /// DamageArea's half of the mirror: everything from "which chunk" to the number going
        /// into the bar. True when the bar is now full and the deposit should break, with the
        /// blow as the swing built it for the break to reuse.
        /// </summary>
        private static bool Count(Component rock, ZNetView nview, long sender, HitData hit, int area,
                                  out HitData template)
        {
            template = null;

            // Mid-break, the blow has nothing left to add to. A vanilla blow at a chunk that is
            // already falling is spent on nothing too.
            if (Collapse.Busy(nview)) return false;

            Collider[] areas = Deposits.Areas(rock, true);
            if (area < 0 || area >= areas.Length)
            {
                // "Missing hit area" in vanilla, and logged there too.
                MalmrPlugin.Log.LogWarning("A vein mining blow named chunk " + area + " of "
                    + Deposits.PrefabName(rock) + ", which has " + areas.Length + ".");
                return false;
            }

            Ledger.Reading before = Ledger.Read(rock, nview);
            if (before == null) return false;

            // "Already destroyed": no damage, no text, no effect.
            if (before.Health[area] <= 0f) return false;

            // Kept before resistances touch it. The break reuses this blow as its template -
            // attacker, tool tier, direction - and needs it as the swing built it.
            template = hit.Clone();

            HitData.DamageModifier significant;
            hit.ApplyResistance(Ledger.Modifiers(rock), out significant);
            float damage = hit.GetTotalDamage();

            Vector3 point = EffectPoint(rock, areas[area], hit);

            if (!hit.CheckToolTier(Deposits.MinToolTier(rock)))
            {
                if (DamageText.instance != null)
                    DamageText.instance.ShowText(DamageText.TextType.TooHard, point, 0f);
                return false;
            }

            if (DamageText.instance != null)
                DamageText.instance.ShowText(significant, point, damage);

            if (damage <= 0f) return false;

            Effects(rock, hit, point);

            // Progress a restock made stale reads as zero here (see Ledger), so the first blow
            // on a regrown vein starts its bar again rather than finishing the old one.
            float progress = before.Progress + damage;
            bool full = progress >= before.Total;

            Ledger.SetProgress(nview, progress, before.Total);

            if (MalmrConfig.Verbose.Value)
                MalmrPlugin.Log.LogInfo(Deposits.PrefabName(rock) + ": +"
                    + damage.ToString("0.0") + " from peer " + sender + ", "
                    + progress.ToString("0.0") + " of " + before.Total.ToString("0.0")
                    + " across " + before.Standing + " chunk(s)"
                    + (before.Stale ? ", the old bar dropped because the deposit grew back" : "")
                    + (full ? " - full, breaking it" : ""));

            return full;
        }

        /// <summary>
        /// RPC_Damage's ward flash, on EVERY blow that reaches the owner, with "destroyed" false:
        /// a blow into the bar breaks nothing, and the break itself goes through RPC_Damage,
        /// which flashes again for each chunk it takes.
        ///
        /// Every blow, not only the ones that count. Vanilla calls DamageArea and then flashes
        /// whatever it answered - a chunk already gone, a pickaxe too weak, a blow of zero all
        /// flash - and PrivateArea.OnObjectDamaged is also what counts blows toward a ward's
        /// guards turning hostile (MonsterAI.OnPrivateAreaAttacked). The first version flashed
        /// only once a blow had landed, so in vein mode a too-weak pickaxe on a warded deposit
        /// raised nothing where the same swing by hand would. MineRock's RPC_Hit has no flash.
        /// </summary>
        private static void Ward(Component rock, HitData hit)
        {
            MineRock5 vein = rock as MineRock5;
            if (vein == null || !vein.m_triggerPrivateArea) return;

            Character attacker = hit.GetAttacker();
            if (attacker == null) return;

            PrivateArea.OnObjectDamaged(vein.transform.position, attacker, false);
        }

        /// <summary>
        /// Where the number floats and the effect plays - each deposit's own choice. MineRock5
        /// uses the chunk's middle when m_hitEffectAreaCenter is set and the blow's point when it
        /// is not; MineRock always uses the blow's point.
        /// </summary>
        private static Vector3 EffectPoint(Component rock, Collider chunk, HitData hit)
        {
            MineRock5 vein = rock as MineRock5;
            if (vein != null && vein.m_hitEffectAreaCenter && chunk != null) return chunk.bounds.center;
            return hit.m_point;
        }

        /// <summary>
        /// Everything DamageArea and RPC_Hit do after a blow lands and before the chunk is
        /// checked for death. The stat matters more than it looks: vanilla counts mine hits for
        /// a player on the machine that owns the rock, and anything reading those counters would
        /// otherwise see vein mining as no mining at all. The ward flash is not here: it belongs
        /// to every blow, landed or not - see Ward.
        /// </summary>
        private static void Effects(Component rock, HitData hit, Vector3 point)
        {
            Character attacker = hit.GetAttacker();

            // The ZDOID from the hit rather than attacker.GetZDOID(). RPC_Hit calls the latter on
            // a Character it never null-checks; an attacker not loaded on this machine would
            // throw there, and a mirror has no reason to copy that.
            ZDOID exclusive = hit.m_attacker;

            MineRock5 vein = rock as MineRock5;
            MineRock small = rock as MineRock;

            if (vein != null) vein.m_hitEffect.Create(point, Quaternion.identity, null, 1f, -1, exclusive);
            if (small != null) small.m_hitEffect.Create(point, Quaternion.identity, null, 1f, -1, exclusive);

            if (hit.m_hitType != HitData.HitType.CinderFire)
            {
                Player closest = Player.GetClosestPlayer(point, 10f);
                if (closest != null) closest.AddNoise(100f);
            }

            // MineRock's per-hit callback. Nothing Malmr classes as ore is known to use it, but
            // it is the deposit's own reaction to being hit and a mirror keeps it.
            if (small != null && small.m_onHit != null) small.m_onHit();

            Player player = attacker as Player;
            if (player == null || player != Player.m_localPlayer || Game.instance == null) return;

            bool cheated = player.GetInventory().CheatedDamagingItemEquipped();
            if (small != null) cheated &= !PlayerProfile.s_bypassCheatChecks;

            Game.instance.IncrementPlayerStat(PlayerStatType.MineHits, 1f, cheated);
        }
    }
}
