using System;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.TitleScreen;
using UnityEngine;

namespace KingdomCharters
{
    [BepInPlugin(Id, "Kingdom Charters", "0.1.4")]
    [BepInDependency("sfk.dailycaravan", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string Id = "gkj.sfk.kingdomcharters";
        internal static Plugin Instance;
        internal CharterPicker Picker;
        internal Charter Pending;
        internal CharterState Run;
        internal string RunId;
        internal bool Starting;

        internal static bool DailyCaravan => Chainloader.PluginInfos.ContainsKey("sfk.dailycaravan");

        internal static bool Merchant => Instance.Run?.choice == Charter.Merchant &&
            MainManager.Instance != null && !MainManager.Instance.IsChallenge() && !DailyCaravan;

        internal void UnsupportedMaterials() =>
            Logger.LogWarning("This kingdom has no distinct stored wood and stone equivalents. Supply charter suspended.");

        private Harmony patches;

        private void Awake()
        {
            Instance = this;
            Picker = gameObject.AddComponent<CharterPicker>();
            gameObject.AddComponent<DebtHud>();
            patches = new Harmony(Id);
            try
            {
                patches.PatchAll(typeof(Plugin).Assembly);
                Logger.LogInfo("Kingdom Charters ready.");
            }
            catch
            {
                patches.UnpatchSelf();
                throw;
            }
        }

        private static readonly AccessTools.FieldRef<TitleScreenHeroSelectionManager, bool> TransitionStarted =
            AccessTools.FieldRefAccess<TitleScreenHeroSelectionManager, bool>("m_StartedSceneTransition");

        internal void StartRun(TitleScreenHeroSelectionManager screen, Charter choice)
        {
            Pending = choice;
            Starting = true;
            try { screen.Play(); }
            catch { Pending = Charter.Standard; throw; }
            finally
            {
                Starting = false;
                if (screen != null && !TransitionStarted(screen)) Pending = Charter.Standard;
            }
        }

        private void OnDestroy()
        {
            if (Picker != null) Destroy(Picker);
            patches?.UnpatchSelf();
            Instance = null;
        }


    }

    [HarmonyPatch(typeof(TitleScreenHeroSelectionManager), nameof(TitleScreenHeroSelectionManager.Play))]
    internal static class ChooseBeforePlay
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(TitleScreenHeroSelectionManager __instance, bool __runOriginal, bool ___m_StartedSceneTransition,
            CanvasGroup ___totalCanvasGroup, GameObject ___playButton)
        {
            var mod = Plugin.Instance;
            if (!__runOriginal || mod.Starting || ___m_StartedSceneTransition || MainManager.Instance.IsChallenge()) return true;
            if (!mod.Picker.IsOpen) mod.Picker.Open(__instance, ___totalCanvasGroup, ___playButton);
            return false;
        }
    }

    [HarmonyPatch(typeof(TitleScreenHeroSelectionManager), nameof(TitleScreenHeroSelectionManager.Back))]
    internal static class ModalBack
    {
        private static bool Prefix(ref bool __result)
        {
            var picker = Plugin.Instance.Picker;
            if (!picker.IsOpen && !picker.HandledBackThisFrame) return true;
            picker.Back();
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(TitleScreenHeroSelectionManager), nameof(TitleScreenHeroSelectionManager.AddCurseButton))]
    [HarmonyPatch(new Type[] { typeof(UnityEngine.InputSystem.InputAction.CallbackContext) })]
    internal static class ModalCurseUp
    {
        private static bool Prefix() => !Plugin.Instance.Picker.IsOpen;
    }

    [HarmonyPatch(typeof(TitleScreenHeroSelectionManager), nameof(TitleScreenHeroSelectionManager.RemoveCurseButton))]
    internal static class ModalCurseDown
    {
        private static bool Prefix() => !Plugin.Instance.Picker.IsOpen;
    }
}
