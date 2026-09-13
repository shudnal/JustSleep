using BepInEx;
using ConditionalConfigSync;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace JustSleep
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    [BepInDependency("_shudnal.ConditionalConfigSync", "1.0.5")]
    public class JustSleep : BaseUnityPlugin
    {
        public const string pluginID = "shudnal.JustSleep";
        public const string pluginName = "JustSleep";
        public const string pluginVersion = "1.0.8";

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID)
        {
            DisplayName = pluginName,
            CurrentVersion = pluginVersion,
            MinimumRequiredVersion = pluginVersion,
            ModRequired = false
        };

        private Harmony harmony;

        private static JustSleep instance;

        private static ConfigEntry<bool> modEnabled;

        private static ConfigEntry<bool> sleepingInNotOwnedBed;

        private static ConfigEntry<bool> sleepingWhileResting;
        private static ConfigEntry<int> sleepingWhileRestingSeconds;

        private const float automaticSleepFocusSeconds = 20f;
        private const float sleepLookRightSeconds = 3f;
        private const float sleepLookLeftSeconds = 0.25f;
        private const float sleepLookTransitionToFarSeconds = 0.4f;
        private const float sleepLookTransitionToNearSeconds = 1.6f;
        private const float sleepTextUpdateInterval = 0.2f;

        private const string rightSleepLookTargetPath = "Visual/Armature/Hips/RightUpLeg/RightLeg/RightFoot/RightToeBase/RightToeBase_end";
        private const string leftSleepLookTargetPath = "Visual/Armature/Hips/LeftUpLeg/LeftLeg/LeftFoot/LeftToeBase/LeftToeBase_end";

        private static readonly int sleepingWhileRestingZdoKey = "shudnal.JustSleep.SleepingWhileResting".GetStableHashCode();
        private static readonly HashSet<Player> playersWithSleepText = new HashSet<Player>();

        private static float restingTimer = 0f;
        private static bool isSittingSleeping;

        private static Fireplace automaticSleepFireplace;
        private static float automaticSleepFocusTimer;
        private static float sleepTextUpdateTimer;

        private static CharacterAnimEvent sleepCharacterAnimEvent;
        private static Transform previousSleepLookTarget;
        private static Transform rightSleepLookTarget;
        private static Transform leftSleepLookTarget;
        private static Transform sleepLookTarget;
        private static bool sleepLookUsesRightTarget;
        private static bool sleepLookTransitioning;
        private static bool sleepLookTransitionToRight;
        private static float sleepLookTimer;
        private static float sleepLookTransitionTimer;
        private static Vector3 sleepLookTransitionStart;

        public static bool debugPreventAutomaticSleep;

        public static CanvasGroup screenBlackener;

        private void Awake()
        {
            harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), pluginID);

            instance = this;

            ConfigInit();

            Game.isModded = true;
        }

        private void FixedUpdate()
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.GetSEMan() == null)
            {
                ResetAutomaticSleepFocus();
                StopSleepLookAnimation();
                isSittingSleeping = false;
                playersWithSleepText.Clear();
                return;
            }

            UpdateSleepTexts(Time.fixedDeltaTime);

            if (player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectResting))
                restingTimer += Time.fixedDeltaTime;
            else
                restingTimer = 0f;

            UpdateAutomaticSleepFocus(Time.fixedDeltaTime);

            if (isSittingSleeping)
            {
                UpdateVanillaSleepRequest();
                UpdateSleepLookAnimation(Time.fixedDeltaTime);
            }

            if (isSittingSleeping && Game.instance != null && !Game.instance.m_sleeping && !CanSleep())
                SetSleepingWhileResting(sleeping: false);
        }

        private void Update()
        {
            UpdateScreenBlackener(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (isSittingSleeping && Player.m_localPlayer != null)
                SetSleepingWhileResting(sleeping: false);
            else
                StopSleepLookAnimation();

            ClearSleepTexts();
            Config.Save();
            harmony?.UnpatchSelf();
        }

        private ConfigEntry<T> BindConfig<T>(string group, string name, T defaultValue, string description)
        {
            return configSync.AddConfigEntry(Config, group, name, defaultValue, new ConfigDescription(description),
                ConfigSyncMode.Conditional, serverControlledByDefault: true).SourceConfig;
        }

        private void ConfigInit()
        {

            modEnabled = BindConfig("General", "Enabled", defaultValue: true, "Enable the mod.");

            sleepingInNotOwnedBed = BindConfig("Sleeping in not owned beds", "Enabled", defaultValue: true, "Enable sleeping in not owned beds.");
            
            sleepingWhileResting = BindConfig("Sleeping while resting", "Enabled", defaultValue: true, "Enable option to sleep while Resting.");
            sleepingWhileRestingSeconds = BindConfig("Sleeping while resting", "Seconds to stay resting", defaultValue: 20, "How many seconds should pass while resting for sleep in front of fireplace to be available");
        }

        private static bool CanSleep() => IsSleepingWhileRestingAvailable() && EnvMan.CanSleep() && !Player.m_localPlayer.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet) && !Player.m_localPlayer.IsSensed();

        private static bool IsSleepingWhileRestingAvailable() => sleepingWhileResting.Value && restingTimer >= sleepingWhileRestingSeconds.Value && PlayerInSleepPosition();

        private static bool PlayerInSleepPosition() => modEnabled.Value && Player.m_localPlayer != null && (Player.m_localPlayer.IsSitting() || Player.m_localPlayer.IsAttached());

        private static Fireplace GetHoveredFireplace()
        {
            GameObject hoverObject = Player.m_localPlayer?.GetHoverObject();
            return hoverObject != null ? hoverObject.GetComponentInParent<Fireplace>() : null;
        }

        private static void UpdateAutomaticSleepFocus(float dt)
        {
            if (isSittingSleeping || !CanSleep())
            {
                ResetAutomaticSleepFocus();
                return;
            }

            Fireplace hoveredFireplace = GetHoveredFireplace();
            if (hoveredFireplace == null || !hoveredFireplace.IsBurning())
            {
                ResetAutomaticSleepFocus();
                return;
            }

            if (automaticSleepFireplace != hoveredFireplace)
            {
                automaticSleepFireplace = hoveredFireplace;
                automaticSleepFocusTimer = 0f;
            }

            automaticSleepFocusTimer = Mathf.Min(automaticSleepFocusTimer + dt, automaticSleepFocusSeconds);

            if (automaticSleepFocusTimer >= automaticSleepFocusSeconds)
                SetSleepingWhileResting(sleeping: true);
        }

        private static void ResetAutomaticSleepFocus()
        {
            automaticSleepFireplace = null;
            automaticSleepFocusTimer = 0f;
        }

        private static float GetAutomaticSleepFocusProgress(Fireplace fireplace)
        {
            return automaticSleepFireplace == fireplace ? Mathf.Clamp01(automaticSleepFocusTimer / automaticSleepFocusSeconds) : 0f;
        }

        private static void SetSleepingWhileResting(bool sleeping)
        {
            Player player = Player.m_localPlayer;
            isSittingSleeping = sleeping;

            if (sleeping)
            {
                ResetAutomaticSleepFocus();
                StartSleepLookAnimation();
            }
            else
            {
                StopSleepLookAnimation();
            }

            if (player == null || player.m_nview == null || !player.m_nview.IsValid())
                return;

            ZDO zdo = player.m_nview.GetZDO();
            if (zdo == null)
                return;

            zdo.Set(sleepingWhileRestingZdoKey, sleeping);
            SetVanillaInBedState(zdo, sleeping && !debugPreventAutomaticSleep);
            sleepTextUpdateTimer = 0f;
        }

        private static void UpdateVanillaSleepRequest()
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.m_nview == null || !player.m_nview.IsValid())
                return;

            ZDO zdo = player.m_nview.GetZDO();
            if (zdo != null)
                SetVanillaInBedState(zdo, !debugPreventAutomaticSleep);
        }

        private static void SetVanillaInBedState(ZDO zdo, bool inBed)
        {
            if (zdo.GetBool(ZDOVars.s_inBed, defaultValue: false) != inBed)
                zdo.Set(ZDOVars.s_inBed, inBed);
        }

        private static void StartSleepLookAnimation()
        {
            if (sleepCharacterAnimEvent != null && rightSleepLookTarget != null && leftSleepLookTarget != null && sleepLookTarget != null)
                return;

            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            CharacterAnimEvent characterAnimEvent = player.GetComponentInChildren<CharacterAnimEvent>();
            Transform rightTarget = player.transform.Find(rightSleepLookTargetPath);
            Transform leftTarget = player.transform.Find(leftSleepLookTargetPath);
            if (characterAnimEvent == null || rightTarget == null || leftTarget == null)
                return;

            sleepCharacterAnimEvent = characterAnimEvent;
            previousSleepLookTarget = characterAnimEvent.m_lookAt;
            rightSleepLookTarget = rightTarget;
            leftSleepLookTarget = leftTarget;

            GameObject lookTargetObject = new GameObject("JustSleepLookTarget");
            sleepLookTarget = lookTargetObject.transform;
            sleepLookTarget.SetParent(player.transform, worldPositionStays: true);

            sleepLookUsesRightTarget = true;
            sleepLookTransitioning = false;
            sleepLookTransitionToRight = false;
            sleepLookTimer = 0f;
            sleepLookTransitionTimer = 0f;
            sleepLookTarget.position = GetSleepLookPosition(rightSleepLookTarget);
            sleepCharacterAnimEvent.m_lookAt = sleepLookTarget;
        }

        private static Vector3 GetSleepLookPosition(Transform source)
        {
            Player player = Player.m_localPlayer;
            return player != null && source != null ? Vector3.Lerp(source.position, player.transform.position, 0.5f) : Vector3.zero;
        }

        private static void UpdateSleepLookAnimation(float dt)
        {
            StartSleepLookAnimation();
            if (sleepCharacterAnimEvent == null || rightSleepLookTarget == null || leftSleepLookTarget == null || sleepLookTarget == null)
                return;

            if (sleepLookTransitioning)
            {
                sleepLookTransitionTimer += dt;
                float transitionDuration = sleepLookTransitionToRight ? sleepLookTransitionToNearSeconds : sleepLookTransitionToFarSeconds;
                float t = Mathf.Clamp01(sleepLookTransitionTimer / transitionDuration);
                float smoothT = t * t * (3f - 2f * t);
                Transform destination = sleepLookTransitionToRight ? rightSleepLookTarget : leftSleepLookTarget;
                sleepLookTarget.position = Vector3.Lerp(sleepLookTransitionStart, GetSleepLookPosition(destination), smoothT);

                if (t >= 1f)
                {
                    sleepLookUsesRightTarget = sleepLookTransitionToRight;
                    sleepLookTransitioning = false;
                    sleepLookTimer = 0f;
                    sleepLookTarget.position = GetSleepLookPosition(destination);
                }
                return;
            }

            Transform currentTarget = sleepLookUsesRightTarget ? rightSleepLookTarget : leftSleepLookTarget;
            sleepLookTarget.position = GetSleepLookPosition(currentTarget);

            sleepLookTimer += dt;
            float targetDuration = sleepLookUsesRightTarget ? sleepLookRightSeconds : sleepLookLeftSeconds;
            if (sleepLookTimer < targetDuration)
                return;

            sleepLookTransitioning = true;
            sleepLookTransitionToRight = !sleepLookUsesRightTarget;
            sleepLookTransitionTimer = 0f;
            sleepLookTransitionStart = sleepLookTarget.position;
        }

        private static void StopSleepLookAnimation()
        {
            if (sleepCharacterAnimEvent != null && sleepCharacterAnimEvent.m_lookAt == sleepLookTarget)
                sleepCharacterAnimEvent.m_lookAt = previousSleepLookTarget;

            if (sleepLookTarget != null)
                UnityEngine.Object.Destroy(sleepLookTarget.gameObject);

            sleepCharacterAnimEvent = null;
            previousSleepLookTarget = null;
            rightSleepLookTarget = null;
            leftSleepLookTarget = null;
            sleepLookTarget = null;
            sleepLookUsesRightTarget = false;
            sleepLookTransitioning = false;
            sleepLookTransitionToRight = false;
            sleepLookTimer = 0f;
            sleepLookTransitionTimer = 0f;
            sleepLookTransitionStart = Vector3.zero;
        }

        private static void UpdateSleepTexts(float dt)
        {
            sleepTextUpdateTimer -= dt;
            if (sleepTextUpdateTimer > 0f)
                return;

            sleepTextUpdateTimer = sleepTextUpdateInterval;

            if (Chat.instance == null || Hud.instance == null || Hud.instance.m_userHidden)
            {
                playersWithSleepText.Clear();
                return;
            }

            playersWithSleepText.RemoveWhere(player => !player);

            foreach (Player player in Player.s_players)
            {
                if (player == null || player.m_nview == null || !player.m_nview.IsValid())
                    continue;

                ZDO zdo = player.m_nview.GetZDO();
                bool sleeping = zdo != null && zdo.GetBool(sleepingWhileRestingZdoKey, defaultValue: false);
                bool textShown = playersWithSleepText.Contains(player);

                if (sleeping)
                {
                    if (Vector3.Distance(Player.m_localPlayer.transform.position, player.transform.position) > 20f)
                    {
                        playersWithSleepText.Remove(player);
                        continue;
                    }

                    if (!textShown || Chat.instance.FindNpcText(player.gameObject) == null)
                    {
                        Chat.instance.SetNpcText(player.gameObject, Vector3.up, 20f, 0f, "", "Zzzzz...", large: false);
                        playersWithSleepText.Add(player);
                    }
                }
                else if (textShown)
                {
                    Chat.instance.ClearNpcText(player.gameObject);
                    playersWithSleepText.Remove(player);
                }
            }
        }

        private static void ClearSleepTexts()
        {
            if (Chat.instance != null)
            {
                foreach (Player player in playersWithSleepText)
                {
                    if (player != null)
                        Chat.instance.ClearNpcText(player.gameObject);
                }
            }

            playersWithSleepText.Clear();
        }

        private static string FromPercent(double percent) => "<sup><alpha=#ff>▀▀▀▀▀▀▀▀▀▀<alpha=#ff></sup>".Insert(Mathf.Clamp(Mathf.RoundToInt((float)percent * 10), 0, 10) + 16, "<alpha=#33>");

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetHoverText))]
        private class Fireplace_GetHoverText_HoverTextWithSleepAction
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(Fireplace __instance, ref string __result)
            {
                if (!IsSleepingWhileRestingAvailable())
                {
                    if (PlayerInSleepPosition() && restingTimer > 0)
                        __result += $"\n{Localization.instance.Localize("$se_resting_start")}\n{FromPercent(restingTimer / sleepingWhileRestingSeconds.Value)}";
                    return;
                }

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

                    if (__instance.IsBurning())
                        __result += $"\n{FromPercent(GetAutomaticSleepFocusProgress(__instance))}";
                }
            }
        }

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Interact))]
        private class Fireplace_Interact_SleepAction
        {
            [HarmonyPriority(Priority.First)]
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
                if (Player.m_localPlayer == __instance && isSittingSleeping && (__instance.IsAttached() || __instance.InEmote()) && !__instance.IsSleeping() && (movedir != Vector3.zero || attack || secondaryAttack || block || blockHold || jump || crouch) && __instance.GetDoodadController() == null)
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

            [HarmonyFinalizer]
            [HarmonyPatch(nameof(Bed.Interact))]
            public static void InteractFinalizer()
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

        [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        public static class Hud_Awake_BlackPanelInit
        {
            private static void Postfix(Hud __instance)
            {
                playersWithSleepText.Clear();

                if (__instance.m_loadingScreen == null)
                    return;

                GameObject blocker = UnityEngine.Object.Instantiate(__instance.m_loadingScreen.gameObject, __instance.m_loadingScreen.transform.parent);
                blocker.name = "JustSleep_SleepingBlack";
                blocker.transform.SetSiblingIndex(0);

                blocker.transform.Find("Loading/TopFade")?.SetParent(blocker.transform);
                blocker.transform.Find("Loading/BottomFade")?.SetParent(blocker.transform);

                for (int i = blocker.transform.childCount - 1; i >= 0; i--)
                {
                    Transform child = blocker.transform.GetChild(i);
                    switch (child.name)
                    {
                        case "Loading":
                        case "Sleeping":
                        case "Teleporting":
                        case "Image":
                        case "Tip":
                        case "panel_separator":
                            UnityEngine.Object.Destroy(child.gameObject);
                            break;
                    }
                }

                screenBlackener = blocker.GetComponent<CanvasGroup>() ?? blocker.AddComponent<CanvasGroup>();
                screenBlackener.alpha = 0f;
                screenBlackener.interactable = false;
                screenBlackener.blocksRaycasts = false;
                screenBlackener.gameObject.SetActive(false);
            }
        }

        private static void UpdateScreenBlackener(float dt)
        {
            if (screenBlackener == null)
                return;

            if (isSittingSleeping)
            {
                screenBlackener.gameObject.SetActive(value: true);
                screenBlackener.alpha = Mathf.MoveTowards(screenBlackener.alpha, 0.96f, dt / 2f);
                return;
            }

            if (automaticSleepFireplace != null)
            {
                screenBlackener.gameObject.SetActive(value: true);
                float targetAlpha = Mathf.Clamp01(automaticSleepFocusTimer / automaticSleepFocusSeconds) * 0.96f;
                screenBlackener.alpha = Mathf.MoveTowards(screenBlackener.alpha, targetAlpha, dt);
                return;
            }

            screenBlackener.alpha = Mathf.MoveTowards(screenBlackener.alpha, 0f, dt / 2f);
            if (screenBlackener.alpha <= 0f)
                screenBlackener.gameObject.SetActive(value: false);
        }
    }
}
