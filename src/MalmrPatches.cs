using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Malmr
{
    /// <summary>
    /// The swing bracket. Both attack shapes a pickaxe could use, because which one a given
    /// pickaxe uses is asset data: every vanilla pickaxe swings as a melee attack, but a
    /// pickaxe with an area attack would reach MineRock5 through DoAreaAttack instead, and a
    /// mod could ship one.
    ///
    /// Returned as a list from a target method rather than stacked attributes. Core found out
    /// the hard way that Harmony merges stacked [HarmonyPatch] attributes into ONE target, so
    /// two of them patch one method and say nothing about the other. A missing method is
    /// logged rather than thrown, so a rename in a game update costs the one shape it renamed.
    /// </summary>
    [HarmonyPatch]
    internal static class SwingPatch
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> Targets()
        {
            var found = new List<MethodBase>();

            foreach (string name in new[] { "DoMeleeAttack", "DoAreaAttack" })
            {
                MethodInfo method = AccessTools.Method(typeof(Attack), name);
                if (method != null) found.Add(method);
                else MalmrPlugin.Log.LogWarning("Attack." + name + " is gone, so a pickaxe "
                    + "swinging that way will not follow veins.");
            }

            return found;
        }

        [HarmonyPrefix]
        private static void Open(Humanoid ___m_character, ItemDrop.ItemData ___m_weapon)
        {
            Vein.Open(___m_character, ___m_weapon);
        }

        [HarmonyPostfix]
        private static void Close(Attack __instance, Humanoid ___m_character,
                                  ItemDrop.ItemData ___m_weapon)
        {
            Vein.Close(__instance, ___m_character, ___m_weapon);
        }

        /// <summary>
        /// Runs whether or not the attack threw. A swing that throws halfway never reaches the
        /// postfix, and leaving the bracket open would make the next hit on a deposit by some
        /// other path look like part of a swing.
        /// </summary>
        [HarmonyFinalizer]
        private static Exception Reset(Exception __exception)
        {
            Vein.Reset();
            return __exception;
        }
    }

    /// <summary>
    /// What each swing struck, read on the way in to the deposit's own Damage - the public
    /// method Attack calls, and the one that turns a collider into an area index. Prefixes, so
    /// the HitData is seen exactly as the swing built it.
    /// </summary>
    internal static class DamagePatches
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage))]
        private static void Vein5(MineRock5 __instance, HitData hit)
        {
            Vein.Record(__instance, hit);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage))]
        private static void VeinSmall(MineRock __instance, HitData hit)
        {
            Vein.Record(__instance, hit);
        }
    }

    /// <summary>
    /// The unlock, said out loud.
    ///
    /// On Player.OnSkillLevelup, which Skills.RaiseSkill calls once for every level gained,
    /// with the new level, right before it shows vanilla's own "skill improved" line - so this
    /// lands beside the level-up it is about. The level it is handed is the earned one, without
    /// bonuses, and that is also the level the swing unlocks on (Vein.EarnedLevel), so the
    /// message arrives on the swing where the vein starts and never before or after it. The
    /// first version unlocked on the buffed level and announced on this one, which put the two
    /// a standing bonus apart - two levels, for anyone with Rist's Quick study capstone.
    /// </summary>
    internal static class Announce
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), nameof(Player.OnSkillLevelup))]
        private static void LevelUp(Player __instance, Skills.SkillType skill, float level)
        {
            if (__instance == null || __instance != Player.m_localPlayer) return;
            if (skill != Skills.SkillType.Pickaxes) return;
            if (!MalmrConfig.Enabled.Value || !MalmrConfig.AnnounceUnlocks.Value) return;
            if (MalmrConfig.MaxExtraChunks.Value <= 0) return;

            int reached = (int)level;
            var names = new List<string>();

            foreach (KeyValuePair<string, int> entry in MalmrConfig.UnlockTable())
            {
                if (entry.Key == MalmrConfig.AnyMetal) continue;
                if (entry.Value == reached) names.Add(Deposits.DisplayName(entry.Key));
            }

            if (names.Count == 0) return;

            string metals = names.Count == 1
                ? names[0]
                : string.Join(", ", names.GetRange(0, names.Count - 1).ToArray())
                  + " and " + names[names.Count - 1];

            __instance.Message(MessageHud.MessageType.Center,
                "Your pickaxe follows " + metals + " veins now");
        }
    }
}
