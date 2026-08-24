using System;
using HarmonyLib;

namespace SephiriaAutoRetry;

[HarmonyPatch(typeof(PlayerSpawner), nameof(PlayerSpawner.ClientGameOver))]
internal static class ClientGameOverPatch
{
    [HarmonyPrefix]
    private static bool Prefix(PlayerSpawner __instance)
    {
        if (RetrySession.ConsumeOriginalGameOverBypass())
        {
            return true;
        }

        if (RetrySession.IsActive)
        {
            Plugin.Log?.LogInfo("自动重试已在进行，忽略重复的客户端死亡结算。");
            return false;
        }

        try
        {
            if (!RetrySession.TryGetSinglePlayerCheckpoint(
                    __instance,
                    out HorayNetworkManager networkManager,
                    out string profile,
                    out string floorGuid,
                    out string reason))
            {
                Plugin.Log?.LogInfo("本次结算不自动重试：" + reason);
                return true;
            }

            RetrySession.Begin(profile, floorGuid, playerCount: 1, multiplayer: false);
            if (!Plugin.StartRestartWhenSaveIsIdle(networkManager, __instance, multiplayer: false))
            {
                RetrySession.Cancel("无法启动重开协程，恢复原版死亡结算。", logWarning: false);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            RetrySession.Cancel("自动重试前置检查或启动失败。", logWarning: false);
            Plugin.Log?.LogError("自动重试前置检查或启动失败，恢复原版死亡结算：" + ex);
            return true;
        }
    }
}

[HarmonyPatch(typeof(PlayerSpawner), nameof(PlayerSpawner.RpcGameOver))]
internal static class MultiplayerRpcGameOverPatch
{
    [HarmonyPrefix]
    private static bool Prefix(PlayerSpawner __instance)
    {
        if (RetrySession.ConsumeOriginalRpcGameOverBypass())
        {
            return true;
        }

        if (RetrySession.IsActive)
        {
            Plugin.Log?.LogInfo("联机自动重试已在进行，阻止重复发送死亡结算 RPC。");
            return false;
        }

        try
        {
            if (!RetrySession.TryGetMultiplayerCheckpoint(
                    __instance,
                    out HorayNetworkManager networkManager,
                    out string profile,
                    out string floorGuid,
                    out int playerCount,
                    out string reason))
            {
                Plugin.Log?.LogInfo("本次服务器结算不自动重试：" + reason);
                return true;
            }

            RetrySession.Begin(profile, floorGuid, playerCount, multiplayer: true);
            if (!Plugin.StartRestartWhenSaveIsIdle(networkManager, __instance, multiplayer: true))
            {
                RetrySession.Cancel("无法启动联机重开协程，恢复原版死亡结算。", logWarning: false);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            RetrySession.Cancel("联机自动重试前置检查或启动失败。", logWarning: false);
            Plugin.Log?.LogError("联机自动重试失败，恢复原版死亡结算：" + ex);
            return true;
        }
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteFile), new[] { typeof(string), typeof(bool) })]
internal static class ProtectTmpDeletePatch
{
    [HarmonyPrefix]
    private static bool Prefix(string fileName)
    {
        if (!RetrySession.ShouldBlockDelete(fileName))
        {
            return true;
        }

        Plugin.Log?.LogInfo($"自动重试期间阻止删除检查点及其备份：{fileName}.sav。");
        return false;
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.CreateNewTMP), new[] { typeof(string) })]
internal static class ReloadTmpInsteadOfCreatePatch
{
    [HarmonyPrefix]
    private static bool Prefix(string fileName)
    {
        if (!RetrySession.ShouldLoadInsteadOfCreate(fileName))
        {
            return true;
        }

        try
        {
            if (SaveManager.LoadTMP(fileName))
            {
                RetrySession.MarkCheckpointReloaded();
                Plugin.Log?.LogInfo($"已用 LoadTMP(\"{fileName}\") 替代 CreateNewTMP()。");
                return false;
            }

            Plugin.Log?.LogError("TMP 检查点读取失败，将解除保护并让原版创建新的 TMP，避免重开流程卡死。");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError("读取 TMP 检查点时发生异常，将退回原版建档流程：" + ex);
        }

        RetrySession.Cancel("TMP 检查点读取失败。", logWarning: false);
        return true;
    }
}

[HarmonyPatch(typeof(HorayNetworkManager), nameof(HorayNetworkManager.NewGame))]
internal static class NewGamePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        RetrySession.MarkNewGameLoaded();
    }
}

[HarmonyPatch(typeof(GridInventory), nameof(GridInventory.RestockStartingItem), new[] { typeof(int) })]
internal static class SuppressStartingItemRestockPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        if (!RetrySession.ShouldSuppressFreshRunItems())
        {
            return true;
        }

        Plugin.Log?.LogInfo("已跳过新局初始物品补发，保留检查点中的原始背包状态。");
        return false;
    }
}

[HarmonyPatch(typeof(PlayerSpawner), nameof(PlayerSpawner.RestartNewGame), new[] { typeof(int) })]
internal static class RestorePlayerFromCheckpointPatch
{
    private readonly struct PatchState
    {
        internal PatchState(PlayerLocalDataStorage storage, sbyte startingPotionCount)
        {
            Storage = storage;
            StartingPotionCount = startingPotionCount;
            Suppressed = true;
        }

        internal PlayerLocalDataStorage Storage { get; }
        internal sbyte StartingPotionCount { get; }
        internal bool Suppressed { get; }
    }

    [HarmonyPrefix]
    private static void Prefix(PlayerSpawner __instance, out PatchState __state)
    {
        __state = default;
        if (!RetrySession.ShouldSuppressFreshRunItems() || __instance == null || __instance.LocalDataStorage == null)
        {
            return;
        }

        PlayerLocalDataStorage storage = __instance.LocalDataStorage;
        __state = new PatchState(storage, storage.startingPotionLV1Count);
        storage.startingPotionLV1Count = 0;
        Plugin.Log?.LogInfo("已临时抑制新局初始药水补发，保留检查点中的药水数量。");
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(PlayerSpawner __instance, Exception __exception, PatchState __state)
    {
        if (__state.Suppressed && __state.Storage != null)
        {
            __state.Storage.startingPotionLV1Count = __state.StartingPotionCount;
        }

        if (__exception == null)
        {
            RetrySession.CompleteAfterPlayerRestart(__instance);
        }

        return __exception;
    }
}
