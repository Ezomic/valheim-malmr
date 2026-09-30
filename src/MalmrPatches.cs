using System;
using HarmonyLib;
using UnityEngine;

namespace Malmr
{
    /// <summary>
    /// Where a blow meets a deposit: the public Damage each deposit shape offers, which Attack
    /// calls once per chunk a swing struck and which turns the chunk into an area index and a
    /// message to the owner. Prefixes, so the HitData is seen exactly as the swing built it and
    /// the blow can go into the bar instead - see Vein.
    ///
    /// Until 2026-09-26 these only recorded what a swing struck, and a bracket around Attack took
    /// the extra chunks once the swing was over. The bar needs no bracket. Every fact it needs is
    /// on the blow, and the swing's own costs are charged by Attack whether or not the deposit's
    /// Damage runs.
    /// </summary>
    internal static class DamagePatches
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage))]
        private static bool Vein5(MineRock5 __instance, HitData hit)
        {
            return Vein.Intercept(__instance, hit);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage))]
        private static bool VeinSmall(MineRock __instance, HitData hit)
        {
            return Vein.Intercept(__instance, hit);
        }
    }

    /// <summary>
    /// Every deposit learns Malmr_Vein1 where it learns its own messages: MineRock5 in Awake,
    /// MineRock in Start. Postfixes, so the deposit's own registration has run and a failure of
    /// Malmr's can only cost the bar. See Owner.Listen.
    /// </summary>
    internal static class ListenPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MineRock5), "Awake")]
        private static void Vein5(MineRock5 __instance)
        {
            Guard("MineRock5.Awake", __instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MineRock), "Start")]
        private static void VeinSmall(MineRock __instance)
        {
            Guard("MineRock.Start", __instance);
        }

        private static bool _warned;

        private static void Guard(string where, Component rock)
        {
            try
            {
                Owner.Listen(rock);
            }
            catch (Exception error)
            {
                if (_warned) return;
                _warned = true;
                MalmrPlugin.Log.LogWarning("Could not register vein mining on a deposit in " + where
                    + ", so this machine cannot fill the bar of a deposit it owns. Said once per "
                    + "session: " + error);
            }
        }
    }

    /// <summary>
    /// The level-up half of the unlock message. The message itself, and the boss-kill half, are
    /// Opened's - see there for why the message follows the metal's state rather than this event.
    ///
    /// On Player.OnSkillLevelup, which Skills.RaiseSkill calls once for every level gained,
    /// after the level has moved and right before it shows vanilla's own "skill improved" line.
    /// Calling Opened from here rather than waiting for its once-a-second look is what puts a
    /// metal opened by the level beside the level-up it is about, on the swing that earned it.
    /// The level Opened reads is the earned one, the same the swing reads (Vein.EarnedLevel):
    /// the first version unlocked on the buffed level and announced on this one, which put the
    /// two a standing bonus apart - two levels, for anyone with Rist's Quick study capstone.
    /// </summary>
    internal static class Announce
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), nameof(Player.OnSkillLevelup))]
        private static void LevelUp(Player __instance, Skills.SkillType skill)
        {
            if (__instance == null || __instance != Player.m_localPlayer) return;
            if (skill != Skills.SkillType.Pickaxes) return;

            Opened.Check();
        }
    }
}
