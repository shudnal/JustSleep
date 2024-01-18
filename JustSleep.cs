using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace JustSleep
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    public class JustSleep : BaseUnityPlugin
    {
        private const string pluginID = "shudnal.JustSleep";
        private const string pluginName = "JustSleep";
        private const string pluginVersion = "1.0.2";

        private readonly Harmony harmony = new Harmony(pluginID);

        private static ConfigEntry<bool> modEnabled;

        internal void Awake()
        {
            harmony.PatchAll();
            
            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");
        }

        private void OnDestroy() => harmony?.UnpatchSelf();

        [HarmonyPatch(typeof(Bed))]
        public static class BedPatch
        {
            private static Bed alternativeInteractingBed;

            [HarmonyPostfix]
            [HarmonyPatch(nameof(Bed.GetHoverText))]
            public static void GetHoverText(Bed __instance, ref string __result)
            {
                if (!modEnabled.Value)
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
                alternativeInteractingBed = modEnabled.Value && alt ? __instance : null;
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
