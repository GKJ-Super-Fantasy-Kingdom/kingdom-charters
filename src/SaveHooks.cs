using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Tavern;
using UnityEngine;

namespace KingdomCharters
{
    internal static class SaveHooks
    {
        // Unity ignores extra fields when constructing GameData. Its own payload and hash stay intact.
        [Serializable]
        private sealed class Snapshot
        {
#pragma warning disable CS0649
            public string runID;
            public string hash;
            public CharterState kingdomChartersV1;
#pragma warning restore CS0649
        }

        private static readonly ConditionalWeakTable<GameData, CharterState> Checkpoints =
            new ConditionalWeakTable<GameData, CharterState>();
        private static bool IsNormalSave(string name) => name != null &&
            name.EndsWith("_game", StringComparison.Ordinal) &&
            !name.EndsWith("_challenge_game", StringComparison.Ordinal);

        // Both native load paths call this managed overload. Associate the extension
        // with the object made from those exact bytes, before the tavern clears its hash.
        [HarmonyPatch(typeof(JsonUtility), nameof(JsonUtility.FromJson), typeof(string), typeof(Type))]
        private static class Read
        {
            private static void Postfix(string __0, object __result)
            {
                if (!(__result is GameData data) || string.IsNullOrEmpty(__0) ||
                    !__0.Contains("\"kingdomChartersV1\"")) return;
                try
                {
                    var saved = JsonConvert.DeserializeObject<Snapshot>(__0);
                    if (saved?.kingdomChartersV1?.Valid == true && saved.runID == data.runID && saved.hash == data.hash)
                        Checkpoints.GetValue(data, _ => saved.kingdomChartersV1);
                }
                catch (JsonException) { /* An invalid extension uses standard rules. */ }
            }
        }

        private static CharterState Restore(GameData data) =>
            data != null && Checkpoints.TryGetValue(data, out var saved) ? saved : null;

        [HarmonyPatch(typeof(GameManager), nameof(GameManager.ResetScene))]
        private static class Restart
        {
            private static void Postfix(bool __runOriginal)
            {
                var mod = Plugin.Instance;
                if (__runOriginal && !MainManager.Instance.IsChallenge() && GameDataManager.Instance.IsFirstLoad())
                    mod.Pending = mod.Run?.choice ?? Charter.Standard;
            }
        }

        [HarmonyPatch(typeof(MainManager), nameof(MainManager.LoadTitleScene))]
        private static class LeaveRun
        {
            private static void Postfix() => Plugin.Instance.Pending = Charter.Standard;
        }

        [HarmonyPatch(typeof(GameDataManager), nameof(GameDataManager.Load))]
        private static class LoadRun
        {
            private static void Prefix(GameData ___m_GameData, out CharterState __state)
            {
                __state = Restore(___m_GameData);
                Plugin.Instance.Run = null; // Restoring inventory must never pay a debt.
                Plugin.Instance.RunId = null;
            }

            private static void Postfix(GameDataManager __instance, CharterState __state)
            {
                var mod = Plugin.Instance;
                var pending = mod.Pending;
                mod.Pending = Charter.Standard;
                if (MainManager.Instance.IsChallenge()) return;
                mod.RunId = __instance.GetRunID();
                if (__instance.IsFirstLoad())
                    mod.Run = pending == Charter.Standard ? null : CharterState.Start(pending);
                else
                    mod.Run = __state;
                Supplies.BindRun();
            }
        }

        [HarmonyPatch(typeof(TavernSaveManager), "Load")]
        private static class TavernLoad
        {
            private static void Postfix(TavernSaveManager __instance)
            {
                var data = __instance.GetGameData();
                // Tavern.Load clears the hash after LoadGameData returns.
                Plugin.Instance.RunId = data?.runID;
                Plugin.Instance.Run = MainManager.Instance.IsChallenge() ? null : Restore(data);
            }
        }

        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save), typeof(string), typeof(string))]
        private static class Write
        {
            private static void Prefix(string file, ref string jsonString)
            {
                var mod = Plugin.Instance;
                if (mod.Run == null || string.IsNullOrEmpty(mod.RunId) || !IsNormalSave(file) || string.IsNullOrEmpty(jsonString)) return;
                if (JsonUtility.FromJson<Snapshot>(jsonString).runID != mod.RunId) return;
                int end = jsonString.LastIndexOf('}');
                if (end >= 0) jsonString = jsonString.Insert(end, ",\"kingdomChartersV1\":" + JsonUtility.ToJson(mod.Run));
            }
        }
    }
}
