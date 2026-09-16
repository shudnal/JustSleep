using BepInEx;
using ConditionalConfigSync;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;

namespace JustSleep
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    [BepInDependency("_shudnal.ConditionalConfigSync", "1.0.5")]
    public class JustSleep : BaseUnityPlugin
    {
        public const string pluginID = "shudnal.JustSleep";
        public const string pluginName = "JustSleep";
        public const string pluginVersion = "1.0.10";

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

        private static ConfigEntry<bool> sleepHotkeyOverride;
        private static ConfigEntry<KeyboardShortcut> sleepHotkey;
        private static ConfigEntry<bool> claimHotkeyOverride;
        private static ConfigEntry<KeyboardShortcut> claimHotkey;

        private static bool sanitizingHotkeys;

        private enum HotkeyAction
        {
            None,
            Sleep,
            Claim
        }

        private enum BedAction
        {
            None,
            Claim,
            SetSpawn,
            Sleep
        }

        private static HotkeyAction activeHotkeyAction;

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

        private ConfigEntry<T> BindClientConfig<T>(string group, string name, T defaultValue, string description)
        {
            return configSync.AddConfigEntry(Config, group, name, defaultValue, new ConfigDescription(description),
                ConfigSyncMode.Conditional, serverControlledByDefault: false).SourceConfig;
        }

        private void ConfigInit()
        {
            modEnabled = BindConfig("General", "Enabled", defaultValue: true, "Enable the mod.");

            sleepingInNotOwnedBed = BindConfig("Sleeping in not owned beds", "Enabled", defaultValue: true, "Enable sleeping in not owned beds.");

            sleepingWhileResting = BindConfig("Sleeping while resting", "Enabled", defaultValue: true, "Enable option to sleep while Resting.");
            sleepingWhileRestingSeconds = BindConfig("Sleeping while resting", "Seconds to stay resting", defaultValue: 20, "How many seconds should pass while resting for sleep in front of fireplace to be available");

            sleepHotkeyOverride = BindClientConfig("Hotkey - Sleep", "Override default hotkey", defaultValue: false, "Replace the default Sleep interaction hotkey with the configured hotkey.");
            sleepHotkey = BindClientConfig("Hotkey - Sleep", "Hotkey", new KeyboardShortcut(KeyCode.E, KeyCode.LeftShift), "Hotkey used for Sleep when the default hotkey override is enabled.");

            claimHotkeyOverride = BindClientConfig("Hotkey - Claim", "Override default hotkey", defaultValue: false, "Replace the default Claim interaction hotkey with the configured hotkey.");
            claimHotkey = BindClientConfig("Hotkey - Claim", "Hotkey", new KeyboardShortcut(KeyCode.E), "Hotkey used for Claim when the default hotkey override is enabled.");

            sleepHotkey.SettingChanged += (_, __) => SanitizeHotkey(sleepHotkey);
            claimHotkey.SettingChanged += (_, __) => SanitizeHotkey(claimHotkey);

            SanitizeHotkey(sleepHotkey);
            SanitizeHotkey(claimHotkey);
        }

        private static void SanitizeHotkey(ConfigEntry<KeyboardShortcut> hotkeyConfig)
        {
            if (sanitizingHotkeys || hotkeyConfig == null)
                return;

            KeyboardShortcut original = hotkeyConfig.Value;
            KeyboardShortcut sanitized = GetSanitizedHotkey(original);
            if (original.Equals(sanitized))
                return;

            sanitizingHotkeys = true;
            try
            {
                hotkeyConfig.Value = sanitized;
                instance?.Logger.LogWarning($"Sanitized hotkey data on {hotkeyConfig.Definition}: {sanitized}.");
            }
            finally
            {
                sanitizingHotkeys = false;
            }
        }

        private static KeyboardShortcut GetSanitizedHotkey(KeyboardShortcut shortcut)
        {
            List<KeyCode> keys = new List<KeyCode> { shortcut.MainKey };
            keys.AddRange(shortcut.Modifiers);
            keys = keys.Where(IsHotkeyKeyValid).Distinct().ToList();

            KeyCode mainKey = keys.Contains(shortcut.MainKey) && !IsModifier(shortcut.MainKey)
                ? shortcut.MainKey
                : keys.FirstOrDefault(key => !IsModifier(key));

            if (mainKey == KeyCode.None)
                return KeyboardShortcut.Empty;

            KeyCode[] modifiers = keys
                .Where(key => key != mainKey && IsModifier(key))
                .OrderBy(GetModifierSortOrder)
                .ThenBy(key => (int)key)
                .ToArray();

            return new KeyboardShortcut(mainKey, modifiers);
        }

        private static bool IsHotkeyKeyValid(KeyCode key)
        {
            return key != KeyCode.None &&
                   ZInput.IsKeyCodeValid(key) &&
                   key != KeyCode.Mouse0 &&
                   key != KeyCode.Mouse1;
        }

        private static bool IsModifier(KeyCode key)
        {
            return key == KeyCode.AltGr ||
                   key == KeyCode.LeftAlt ||
                   key == KeyCode.RightAlt ||
                   key == KeyCode.LeftShift ||
                   key == KeyCode.RightShift ||
                   key == KeyCode.LeftControl ||
                   key == KeyCode.RightControl ||
                   key == KeyCode.LeftApple ||
                   key == KeyCode.RightApple ||
                   key == KeyCode.LeftCommand ||
                   key == KeyCode.RightCommand ||
                   key == KeyCode.LeftWindows ||
                   key == KeyCode.RightWindows;
        }

        private static int GetModifierSortOrder(KeyCode key)
        {
            if (key == KeyCode.LeftControl || key == KeyCode.RightControl)
                return 0;
            if (key == KeyCode.LeftAlt || key == KeyCode.RightAlt || key == KeyCode.AltGr)
                return 1;
            if (key == KeyCode.LeftShift || key == KeyCode.RightShift)
                return 2;
            return 3;
        }

        private static string FormatHotkey(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None)
                return "None";

            IEnumerable<KeyCode> modifiers = shortcut.Modifiers
                .Where(IsModifier)
                .Distinct()
                .OrderBy(GetModifierSortOrder)
                .ThenBy(key => (int)key);

            return string.Join(" + ", modifiers
                .Select(GetKeyDisplayName)
                .Concat(new[] { GetKeyDisplayName(shortcut.MainKey) }));
        }

        private static string GetKeyDisplayName(KeyCode key)
        {
            string displayName = ZInput.KeyCodeToDisplayName(key);
            return string.IsNullOrEmpty(displayName) ? key.ToString() : displayName;
        }

        private static bool IsShortcutDown(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None || !ZInput.GetKeyDown(shortcut.MainKey))
                return false;

            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (!ZInput.GetKey(modifier))
                    return false;
            }

            return true;
        }

        private static string GetAlternativeUseHotkeyText()
        {
            string altKey = !ZInput.IsNonClassicFunctionality() || !ZInput.IsGamepadActive() ? "$KEY_AltPlace" : "$KEY_JoyAltKeys";
            return Localization.instance.Localize($"{altKey} + $KEY_Use");
        }

        private static string GetSleepHotkeyText(bool defaultUsesAlternativeAction)
        {
            if (sleepHotkeyOverride.Value)
                return FormatHotkey(sleepHotkey.Value);

            return defaultUsesAlternativeAction
                ? GetAlternativeUseHotkeyText()
                : Localization.instance.Localize("$KEY_Use");
        }

        private static string GetClaimHotkeyText()
        {
            return claimHotkeyOverride.Value
                ? FormatHotkey(claimHotkey.Value)
                : Localization.instance.Localize("$KEY_Use");
        }

        private static string GetActionHoverLine(string hotkey, string action)
        {
            return Localization.instance.Localize($"[<color=yellow><b>{hotkey}</b></color>] {action}");
        }

        private static void CheckCustomHotkeys(Player player)
        {
            if (!modEnabled.Value || player == null || player != Player.m_localPlayer || Hud.InRadial())
                return;

            bool sleepPressed = sleepHotkeyOverride.Value && IsShortcutDown(sleepHotkey.Value);
            bool claimPressed = claimHotkeyOverride.Value && IsShortcutDown(claimHotkey.Value);
            if (!sleepPressed && !claimPressed)
                return;

            GameObject hoverObject = player.GetHoverObject();
            if (hoverObject == null)
                return;

            Bed bed = hoverObject.GetComponentInParent<Bed>();
            if (bed != null)
            {
                GetBedActions(bed, out BedAction firstAction, out BedAction secondAction);

                if (sleepPressed && HasBedAction(firstAction, secondAction, BedAction.Sleep))
                {
                    InvokeCustomInteraction(player, hoverObject, HotkeyAction.Sleep, alt: true);
                    return;
                }

                if (claimPressed && HasBedClaimAction(firstAction, secondAction))
                    InvokeCustomInteraction(player, hoverObject, HotkeyAction.Claim, alt: false);

                return;
            }

            if (sleepPressed && hoverObject.GetComponentInParent<Fireplace>() != null && CanSleep())
                InvokeCustomInteraction(player, hoverObject, HotkeyAction.Sleep, alt: true);
        }

        private static void InvokeCustomInteraction(Player player, GameObject hoverObject, HotkeyAction action, bool alt)
        {
            activeHotkeyAction = action;
            try
            {
                player.Interact(hoverObject, false, alt);
            }
            finally
            {
                activeHotkeyAction = HotkeyAction.None;
            }
        }

        private static bool IsBedUnclaimed(Bed bed)
        {
            return bed != null && string.IsNullOrEmpty(bed.GetOwnerName());
        }

        private static void GetBedActions(Bed bed, out BedAction firstAction, out BedAction secondAction)
        {
            firstAction = BedAction.None;
            secondAction = BedAction.None;

            if (bed == null)
                return;

            bool unclaimed = IsBedUnclaimed(bed);
            bool isMine = !unclaimed && bed.IsMine();
            bool isCurrent = isMine && bed.IsCurrent();

            if (unclaimed)
            {
                firstAction = BedAction.Claim;
                if (sleepingInNotOwnedBed.Value)
                    secondAction = BedAction.Sleep;
                return;
            }

            if (isMine && !isCurrent)
            {
                firstAction = BedAction.SetSpawn;
                if (sleepingInNotOwnedBed.Value)
                    secondAction = BedAction.Sleep;
                return;
            }

            if (isCurrent || sleepingInNotOwnedBed.Value)
                firstAction = BedAction.Sleep;
        }

        private static bool HasBedAction(BedAction firstAction, BedAction secondAction, BedAction action)
        {
            return firstAction == action || secondAction == action;
        }

        private static bool HasBedClaimAction(BedAction firstAction, BedAction secondAction)
        {
            return HasBedAction(firstAction, secondAction, BedAction.Claim) ||
                   HasBedAction(firstAction, secondAction, BedAction.SetSpawn);
        }

        private static bool BedSleepUsesAlternativeAction(Bed bed)
        {
            return bed == null || !bed.IsMine() || !bed.IsCurrent();
        }

        private static string GetBedActionLocalization(BedAction action)
        {
            switch (action)
            {
                case BedAction.Claim:
                    return "$piece_bed_claim";
                case BedAction.SetSpawn:
                    return "$piece_bed_setspawn";
                case BedAction.Sleep:
                    return "$piece_bed_sleep";
                default:
                    return string.Empty;
            }
        }

        private static string GetBedActionHotkeyText(Bed bed, BedAction action)
        {
            return action == BedAction.Sleep
                ? GetSleepHotkeyText(BedSleepUsesAlternativeAction(bed))
                : GetClaimHotkeyText();
        }

        private static void AppendBedAction(StringBuilder builder, Bed bed, BedAction action)
        {
            if (action == BedAction.None)
                return;

            if (builder.Length > 0)
                builder.Append('\n');

            builder.Append(GetActionHoverLine(GetBedActionHotkeyText(bed, action), GetBedActionLocalization(action)));
        }

        private static bool ShouldSuppressNativeBedInteraction(Bed bed, bool repeat, bool alt)
        {
            if (!modEnabled.Value || bed == null || repeat || activeHotkeyAction != HotkeyAction.None)
                return false;

            GetBedActions(bed, out BedAction firstAction, out BedAction secondAction);
            bool sleepAvailable = HasBedAction(firstAction, secondAction, BedAction.Sleep);
            bool claimAvailable = HasBedClaimAction(firstAction, secondAction);

            // A custom shortcut can overlap Use/AltPlace. Suppress the native interaction first
            // so the configured action is the only one performed in this frame.
            if (sleepAvailable && sleepHotkeyOverride.Value && IsShortcutDown(sleepHotkey.Value))
                return true;

            if (claimAvailable && claimHotkeyOverride.Value && IsShortcutDown(claimHotkey.Value))
                return true;

            if (sleepAvailable && sleepHotkeyOverride.Value && alt == BedSleepUsesAlternativeAction(bed))
                return true;

            return claimAvailable && claimHotkeyOverride.Value && !alt;
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
                    __result += "\n" + GetActionHoverLine(GetSleepHotkeyText(defaultUsesAlternativeAction: true), "$piece_bed_sleep");
                }
            }
        }

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Interact))]
        private class Fireplace_Interact_SleepAction
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Humanoid user, bool hold, bool alt)
            {
                if (hold || user != Player.m_localPlayer || !CanSleep())
                    return true;

                if (activeHotkeyAction == HotkeyAction.Sleep)
                {
                    SetSleepingWhileResting(sleeping: true);
                    return false;
                }

                if (sleepHotkeyOverride.Value && IsShortcutDown(sleepHotkey.Value))
                    return false;

                if (!alt)
                    return true;

                if (sleepHotkeyOverride.Value)
                    return false;

                SetSleepingWhileResting(sleeping: true);
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        private static class Player_Update_CustomHotkeys
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = instructions.ToList();
                MethodInfo inRadial = AccessTools.Method(typeof(Hud), nameof(Hud.InRadial));
                MethodInfo getButtonUp = AccessTools.Method(typeof(ZInput), nameof(ZInput.GetButtonUp), new[] { typeof(string) });
                MethodInfo checkCustomHotkeys = AccessTools.Method(typeof(JustSleep), nameof(CheckCustomHotkeys));

                CodeMatcher matcher = new CodeMatcher(codes)
                    .MatchForward(false,
                        new CodeMatch(instruction => instruction.Calls(inRadial)),
                        new CodeMatch(instruction => instruction.opcode.FlowControl == FlowControl.Cond_Branch),
                        new CodeMatch(OpCodes.Ldstr, "JoyHide"),
                        new CodeMatch(instruction => instruction.Calls(getButtonUp)));

                if (!matcher.IsValid)
                {
                    instance?.Logger.LogError("Failed to inject custom bed hotkey handling into Player.Update.");
                    return codes;
                }

                CodeInstruction loadPlayer = new CodeInstruction(OpCodes.Ldarg_0);
                loadPlayer.labels.AddRange(matcher.Instruction.labels);
                matcher.Instruction.labels.Clear();
                loadPlayer.blocks.AddRange(matcher.Instruction.blocks);
                matcher.Instruction.blocks.Clear();

                matcher.Insert(
                    loadPlayer,
                    new CodeInstruction(OpCodes.Call, checkCustomHotkeys));

                return matcher.InstructionEnumeration();
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
            private static readonly StringBuilder sb = new StringBuilder();
            private static Bed alternativeInteractingBed;

            [HarmonyPostfix]
            [HarmonyPatch(nameof(Bed.GetHoverText))]
            public static void GetHoverText(Bed __instance, ref string __result)
            {
                if (!modEnabled.Value)
                    return;

                GetBedActions(__instance, out BedAction firstAction, out BedAction secondAction);

                sb.Clear();
                sb.Append(GetBedHeader(__instance));
                AppendBedAction(sb, __instance, firstAction);
                AppendBedAction(sb, __instance, secondAction);

                __result = sb.ToString();
            }

            private static string GetBedHeader(Bed bed)
            {
                string ownerName = bed.GetOwnerName();
                return ownerName == ""
                    ? Localization.instance.Localize("$piece_bed_unclaimed")
                    : Localization.instance.Localize(ownerName + "'s $piece_bed");
            }

            [HarmonyPrefix]
            [HarmonyPatch(nameof(Bed.Interact))]
            public static bool InteractPrefix(Bed __instance, bool repeat, bool alt)
            {
                alternativeInteractingBed = null;

                if (ShouldSuppressNativeBedInteraction(__instance, repeat, alt))
                    return false;

                GetBedActions(__instance, out BedAction firstAction, out BedAction secondAction);
                bool sleepAvailable = HasBedAction(firstAction, secondAction, BedAction.Sleep);
                alternativeInteractingBed = alt && sleepAvailable && BedSleepUsesAlternativeAction(__instance) ? __instance : null;
                return true;
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
