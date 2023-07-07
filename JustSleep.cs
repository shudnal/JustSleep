using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
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
        private const string pluginVersion = "1.0.0";

        private Harmony _harmony;

        public static ManualLogSource logger;

        private static ConfigEntry<bool> modEnabled;

        internal void Awake()
        {
            logger = Logger;

            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");

            _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), pluginID);

        }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        [HarmonyPatch(typeof(Bed))]
        public static class BedPatch
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(Bed.GetHoverText))]
            [HarmonyPriority(Priority.Low)]
            public static void GetHoverText(Bed __instance, ref string __result)
            {
                if (!modEnabled.Value)
                    return;

                if (__result.Contains(Localization.instance.Localize("$piece_bed_sleep")))
                {
                    return;
                }

                if (!__instance.IsMine() || !__instance.IsCurrent())
                {
                    string textJustSleep = "";
                    if (!ZInput.IsAlternative1Functionality() || !ZInput.IsGamepadActive())
                        {
                        textJustSleep = "\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $piece_bed_sleep";
                    }
                    else
                    {
                        textJustSleep = "\n[<color=yellow><b>$KEY_JoyAltKeys + $KEY_Use</b></color>] $piece_bed_sleep";
                    }
                    __result += Localization.instance.Localize(textJustSleep);
                }

                return;
                
            }

            [HarmonyPrefix]
            [HarmonyPatch(nameof(Bed.Interact))]
            [HarmonyPriority(Priority.High)]
            public static bool Interact(Bed __instance, ref bool __result, ref Humanoid human, ref bool repeat, ref bool alt)
            {
                
                if (!modEnabled.Value)
                    return true;

                if (!alt)
                    return true;

                __result = false;
                if (repeat)
                    return false;

                ZNetView m_nview = (ZNetView)AccessTools.Field(typeof(Bed), "m_nview").GetValue(__instance);
                if (m_nview == null || !m_nview.IsValid() || m_nview.GetZDO() == null)
                    return false;

                Player human2 = human as Player;

                if (!EnvMan.instance.CanSleep())
                {
                    ((Character)human).Message(MessageHud.MessageType.Center, "$msg_cantsleep");
                    return false;
                }

                if (!__instance.CheckEnemies(human2))
                    return false;

                if (!__instance.CheckExposure(human2))
                    return false;

                if (!__instance.CheckFire(human2))
                    return false;

                if (!__instance.CheckWet(human2))
                    return false;

                ((Character)human).AttachStart(__instance.m_spawnPoint, ((Component)human).gameObject, true, true, false, "attach_bed", new Vector3(0.0f, 0.5f, 0.0f));
                return false;

            }

        }
    }
}
