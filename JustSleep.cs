﻿using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace JustSleep
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    public class JustSleep : BaseUnityPlugin
    {
        private const string pluginID = "shudnal.JustSleep";
        private const string pluginName = "JustSleep";
        private const string pluginVersion = "1.0.5";

        private Harmony harmony;

        private static JustSleep instance;

        private static ConfigEntry<bool> modEnabled;

        private static ConfigEntry<bool> sleepingInNotOwnedBed;

        private static ConfigEntry<bool> sleepingWhileResting;
        private static ConfigEntry<int> sleepingWhileRestingSeconds;

        private static float restingTimer = 0f;
        private static bool isSittingSleeping;

        private void Awake()
        {
            harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), pluginID);

            instance = this;

            ConfigInit();

            Game.isModded = true;
        }

        private void FixedUpdate()
        {
            if (Player.m_localPlayer == null || Player.m_localPlayer.GetSEMan() == null)
                return;

            if (Player.m_localPlayer.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectResting))
                restingTimer += Time.fixedDeltaTime;
            else
                restingTimer = 0;

            if (isSittingSleeping && !Game.instance.m_sleeping && !CanSleep())
                SetSleepingWhileResting(sleeping: false);
        }

        private void OnDestroy()
        {
            Config.Save();
            harmony?.UnpatchSelf();
        }

        private void ConfigInit()
        {
            Config.Bind("General", "NexusID", 2561, "Nexus mod ID for updates");

            modEnabled = Config.Bind("General", "Enabled", defaultValue: true, "Enable the mod.");

            sleepingInNotOwnedBed = Config.Bind("Sleeping in not owned beds", "Enabled", defaultValue: true, "Enable sleeping in not owned beds.");
            
            sleepingWhileResting = Config.Bind("Sleeping while resting", "Enabled", defaultValue: true, "Enable option to sleep while Resting.");
            sleepingWhileRestingSeconds = Config.Bind("Sleeping while resting", "Seconds to stay resting", defaultValue: 20, "How many seconds should pass while resting for sleep in front of fireplace to be available");
        }

        private static bool CanSleep()
        {
            return Player.m_localPlayer != null && IsSleepingWhileRestingAvailable() && EnvMan.CanSleep() && !Player.m_localPlayer.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet) && !Player.m_localPlayer.IsSensed();
        }

        private static bool IsSleepingWhileRestingAvailable()
        {
            return modEnabled.Value && sleepingWhileResting.Value && restingTimer >= sleepingWhileRestingSeconds.Value && Player.m_localPlayer != null && (Player.m_localPlayer.IsSitting() || Player.m_localPlayer.IsAttached());
        }

        private static void SetSleepingWhileResting(bool sleeping)
        {
            isSittingSleeping = sleeping;
            Player.m_localPlayer.m_nview.GetZDO().Set(ZDOVars.s_inBed, isSittingSleeping);

            if (isSittingSleeping)
                Chat.instance.SetNpcText(Player.m_localPlayer.gameObject, Vector2.up, 20, 600, "", "$se_resting_start", false);
            else
                Chat.instance.ClearNpcText(Player.m_localPlayer.gameObject);
        }

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetHoverText))]
        private class Fireplace_GetHoverText_HoverTextWithSleepAction
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(ref string __result)
            {
                if (!IsSleepingWhileRestingAvailable())
                    return;

                if (Player.m_localPlayer.InBed())
                    return;

                if (!EnvMan.CanSleep())
                {
                    __result += Localization.instance.Localize("\n$msg_cantsleep");
                }
                else if (Player.m_localPlayer.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet))
                {
                    __result += Localization.instance.Localize("\n$msg_bedwet");
                }
                else if (Player.m_localPlayer.IsSensed())
                {
                    __result += Localization.instance.Localize("\n$msg_bedenemiesnearby");
                }
                else
                {
                    string altKey = !ZInput.IsNonClassicFunctionality() || !ZInput.IsGamepadActive() ? "$KEY_AltPlace" : "$KEY_JoyAltKeys";
                    __result += Localization.instance.Localize($"\n[<color=yellow><b>{altKey} + $KEY_Use</b></color>] $piece_bed_sleep");
                }
            }
        }

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Interact))]
        private class Fireplace_Interact_SleepAction
        {
            private static bool Prefix(Humanoid user, bool hold, bool alt)
            {
                if (!alt || hold || user != Player.m_localPlayer || !CanSleep())
                    return true;

                SetSleepingWhileResting(sleeping:true);
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private class Player_SetControls_StopSleeping
        {
            private static void Prefix(Player __instance, Vector3 movedir, bool attack, bool secondaryAttack, bool block, bool blockHold, bool jump, bool crouch)
            {
                if ((__instance.IsAttached() || __instance.InEmote()) && __instance.InBed() && !__instance.IsSleeping() && (movedir != Vector3.zero || attack || secondaryAttack || block || blockHold || jump || crouch) && __instance.GetDoodadController() == null)
                    SetSleepingWhileResting(sleeping: false);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetSleeping))]
        private class Player_SetSleeping_StopSleeping
        {
            private static void Prefix(Player __instance, bool sleep)
            {
                if (Player.m_localPlayer == __instance && isSittingSleeping && __instance.IsSleeping() && !sleep)
                {
                    SetSleepingWhileResting(sleeping: false);
                    if (__instance.IsSitting())
                        __instance.StopEmote();
                }
            }
        }

        [HarmonyPatch(typeof(Bed))]
        public static class BedPatches
        {
            private static Bed alternativeInteractingBed;

            [HarmonyPostfix]
            [HarmonyPatch(nameof(Bed.GetHoverText))]
            public static void GetHoverText(Bed __instance, ref string __result)
            {
                if (!modEnabled.Value)
                    return;

                if (!sleepingInNotOwnedBed.Value)
                    return;

                if (__result.Contains(Localization.instance.Localize("$piece_bed_sleep")))
                    return;

                if (!__instance.IsMine() || !__instance.IsCurrent())
                {
                    string altKey = !ZInput.IsNonClassicFunctionality() || !ZInput.IsGamepadActive() ? "$KEY_AltPlace" : "$KEY_JoyAltKeys";
                    __result += Localization.instance.Localize($"\n[<color=yellow><b>{altKey} + $KEY_Use</b></color>] $piece_bed_sleep");
                }
            }

            [HarmonyPrefix]
            [HarmonyPatch(nameof(Bed.Interact))]
            public static void InteractPrefix(Bed __instance, bool alt)
            {
                alternativeInteractingBed = modEnabled.Value && sleepingInNotOwnedBed.Value && alt ? __instance : null;
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(Bed.Interact))]
            public static void InteractPostfix()
            {
                alternativeInteractingBed = null;
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(Bed.GetOwner))]
            public static void GetOwner(Bed __instance, ref long __result)
            {
                if (__instance == alternativeInteractingBed)
                    __result = Game.instance.GetPlayerProfile().GetPlayerID();
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(Bed.IsCurrent))]
            public static void IsCurrent(Bed __instance, ref bool __result)
            {
                __result = __result || __instance == alternativeInteractingBed;
            }
        }
    }
}
