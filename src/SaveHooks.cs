using System;
using HarmonyLib;
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

        private static Snapshot read;
        private static bool IsNormalSave(string name) => name != null &&
            name.EndsWith("_game", StringComparison.Ordinal) &&
            !name.EndsWith("_challenge_game", StringComparison.Ordinal);

        [HarmonyPatch]
        private static class Read
        {
            // SaveManager also has a generic Load<T> overload with the same parameter types.
            [HarmonyTargetMethod]
            private static System.Reflection.MethodBase TargetMethod() =>
                AccessTools.FirstMethod(typeof(SaveManager), m => m.Name == "Load" &&
                    !m.IsGenericMethod && m.ReturnType == typeof(string));

            private static void Postfix(string file, string __result)
            {
                if (!IsNormalSave(file)) return;
                read = null;
                if (string.IsNullOrEmpty(__result) || !__result.Contains("\"kingdomChartersV1\"")) return;
                try { read = JsonUtility.FromJson<Snapshot>(__result); }
                catch (ArgumentException) { /* The game may retry with its backup. */ }
            }
        }

        private static CharterState Restore(GameData data) =>
            data != null && read != null && read.runID == data.runID && read.hash == data.hash &&
            read.kingdomChartersV1?.Valid == true ? read.kingdomChartersV1 : null;

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
                Supplies.ResetBalance();
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
                Plugin.Instance.Run = !MainManager.Instance.IsChallenge() && data != null &&
                    read?.runID == data.runID && read.kingdomChartersV1?.Valid == true
                    ? read.kingdomChartersV1 : null;
            }
        }

        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save), typeof(string), typeof(string))]
        private static class Write
        {
            private static void Prefix(string file, ref string jsonString)
            {
                var mod = Plugin.Instance;
                if (mod.Run == null || !IsNormalSave(file) || string.IsNullOrEmpty(jsonString)) return;
                if (JsonUtility.FromJson<Snapshot>(jsonString).runID != mod.RunId) return;
                int end = jsonString.LastIndexOf('}');
                if (end >= 0) jsonString = jsonString.Insert(end, ",\"kingdomChartersV1\":" + JsonUtility.ToJson(mod.Run));
            }
        }
    }
}
