using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SephiriaAutoRetry;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "local.sephiria.autoretry";
    public const string PluginName = "Sephiria Auto Retry";
    public const string PluginVersion = "0.2.2";

    internal static ManualLogSource Log { get; private set; }
    internal static ConfigEntry<bool> Enabled { get; private set; }
    internal static FieldInfo RestartingField { get; private set; }
    internal static Plugin Instance { get; private set; }

    private Harmony harmony;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        Enabled = Config.Bind(
            "General",
            "Enabled",
            true,
            "真正死亡后自动读取本层入口检查点并重试。支持单人，以及启用 -allow_rejoin 的房主侧联机游戏。");

        if (!ValidateCompatibility(out string error))
        {
            Logger.LogError("兼容性检查失败，自动重试已安全禁用：" + error);
            return;
        }

        harmony = new Harmony(PluginGuid);
        try
        {
            harmony.PatchAll(typeof(Plugin).Assembly);
        }
        catch (Exception ex)
        {
            harmony.UnpatchSelf();
            Logger.LogError("Harmony 补丁安装失败，自动重试已安全禁用：" + ex);
            return;
        }

        Logger.LogInfo($"{PluginName} {PluginVersion} 已加载。");
    }

    private void OnDestroy()
    {
        RetrySession.Cancel("Mod 正在卸载。", logWarning: false);
        harmony?.UnpatchSelf();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    internal static void StartProtectionTimeout(int generation)
    {
        Plugin instance = Instance;
        if (instance != null)
        {
            instance.StartCoroutine(instance.ProtectionTimeout(generation));
        }
    }

    internal static bool StartRestartWhenSaveIsIdle(
        HorayNetworkManager networkManager,
        PlayerSpawner spawner,
        bool multiplayer)
    {
        Plugin instance = Instance;
        if (instance == null)
        {
            return false;
        }

        instance.StartCoroutine(instance.RestartWhenSaveIsIdle(networkManager, spawner, multiplayer));
        return true;
    }

    private IEnumerator RestartWhenSaveIsIdle(
        HorayNetworkManager networkManager,
        PlayerSpawner spawner,
        bool multiplayer)
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (SaveManager.IsSaving != SaveManager.ESaveState.None && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        if (SaveManager.IsSaving != SaveManager.ESaveState.None)
        {
            Logger.LogWarning("等待楼层存档完成超过 10 秒；将继续重开，并保留现有 TMP 与备份。");
        }

        try
        {
            if (networkManager == null)
            {
                throw new InvalidOperationException("重开前 HorayNetworkManager 已失效。");
            }

            networkManager.RestartGame(firstDeath: false, forceChapter: -1);
        }
        catch (Exception ex)
        {
            Logger.LogError("调用原版 RestartGame() 失败，恢复原版死亡结算：" + ex);
            if (multiplayer)
            {
                RetrySession.FallbackToOriginalRpcGameOver(spawner);
            }
            else
            {
                RetrySession.FallbackToOriginalClientGameOver(spawner);
            }
        }
    }

    private IEnumerator ProtectionTimeout(int generation)
    {
        yield return new WaitForSecondsRealtime(30f);
        RetrySession.CancelIfGeneration(
            generation,
            "重开流程超过 30 秒仍未完成角色初始化，已解除 TMP 保护以避免影响后续存档操作。");
    }

    private static bool ValidateCompatibility(out string error)
    {
        if (AccessTools.Method(typeof(PlayerSpawner), nameof(PlayerSpawner.ClientGameOver), Type.EmptyTypes) == null ||
            AccessTools.Method(typeof(PlayerSpawner), nameof(PlayerSpawner.RpcGameOver), Type.EmptyTypes) == null)
        {
            error = "找不到 PlayerSpawner 的死亡结算接口。";
            return false;
        }

        if (AccessTools.Method(typeof(PlayerSpawner), nameof(PlayerSpawner.RestartNewGame), new[] { typeof(int) }) == null ||
            AccessTools.Method(typeof(GridInventory), nameof(GridInventory.RestockStartingItem), new[] { typeof(int) }) == null)
        {
            error = "找不到角色重开或初始物品补发接口。";
            return false;
        }

        if (AccessTools.Method(typeof(SaveManager), nameof(SaveManager.DeleteFile), new[] { typeof(string), typeof(bool) }) == null ||
            AccessTools.Method(typeof(SaveManager), nameof(SaveManager.CreateNewTMP), new[] { typeof(string) }) == null ||
            AccessTools.Method(typeof(SaveManager), nameof(SaveManager.LoadTMP), new[] { typeof(string) }) == null)
        {
            error = "找不到所需的 SaveManager TMP 接口。";
            return false;
        }

        if (AccessTools.Method(typeof(HorayNetworkManager), nameof(HorayNetworkManager.RestartGame), new[] { typeof(bool), typeof(int) }) == null ||
            AccessTools.Method(typeof(HorayNetworkManager), nameof(HorayNetworkManager.NewGame), Type.EmptyTypes) == null)
        {
            error = "找不到 HorayNetworkManager 的重开接口。";
            return false;
        }

        RestartingField = AccessTools.Field(typeof(HorayNetworkManager), "restarting");
        if (RestartingField == null || RestartingField.FieldType != typeof(bool))
        {
            error = "找不到 HorayNetworkManager.restarting 状态。";
            return false;
        }

        if (AccessTools.Field(typeof(PlayerLocalDataStorage), "startingPotionLV1Count") == null ||
            AccessTools.Field(typeof(DungeonManager), "victoryType") == null ||
            AccessTools.Field(typeof(DungeonManager), "isGiveUpRun") == null ||
            AccessTools.Field(typeof(DungeonManager), "generatedFloors") == null)
        {
            error = "找不到死亡判定、楼层检查点或初始药水所需字段。";
            return false;
        }

        error = null;
        return true;
    }
}
