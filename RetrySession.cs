using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace SephiriaAutoRetry;

internal static class RetrySession
{
    private const string TmpSuffix = "TMP";
    private const string FloorEntranceSpawnPoint = "FLOORSTARTING";

    private static bool active;
    private static bool checkpointReloaded;
    private static string protectedProfile = "";
    private static string protectedTmpFile = "";
    private static string checkpointFloorGuid = "";
    private static int generation;
    private static bool allowOriginalGameOverOnce;
    private static bool allowOriginalRpcGameOverOnce;
    private static bool multiplayerRetry;
    private static int expectedPlayerRestarts;
    private static readonly HashSet<int> CompletedPlayerRestarts = new HashSet<int>();

    internal static bool IsActive => active;

    internal static bool ConsumeOriginalGameOverBypass()
    {
        if (!allowOriginalGameOverOnce)
        {
            return false;
        }

        allowOriginalGameOverOnce = false;
        return true;
    }

    internal static bool ConsumeOriginalRpcGameOverBypass()
    {
        if (!allowOriginalRpcGameOverOnce)
        {
            return false;
        }

        allowOriginalRpcGameOverOnce = false;
        return true;
    }

    internal static bool TryGetSinglePlayerCheckpoint(
        PlayerSpawner spawner,
        out HorayNetworkManager networkManager,
        out string profile,
        out string floorGuid,
        out string reason)
    {
        networkManager = null;
        profile = "";
        floorGuid = "";

        if (!CanStartRetry(out reason))
        {
            return false;
        }

        if (spawner == null || !NetworkClient.active || !NetworkServer.active || !spawner.isServer || !spawner.isLocalPlayer)
        {
            reason = "当前不是本地单人主机。";
            return false;
        }

        if (PlayerSpawner.MultiplayerList == null || PlayerSpawner.MultiplayerList.Count != 1 ||
            PlayerSpawner.MultiplayerList[0] == null || PlayerSpawner.MultiplayerList[0] != spawner)
        {
            reason = "检测到联机或玩家列表状态不符合单人模式。";
            return false;
        }

        PlayerAvatar avatar = spawner.PlayerAvatar;
        if (avatar == null || !avatar.IsDead || !avatar.dieIsGameOver.IsTrue())
        {
            reason = "本地角色没有处于会导致游戏结束的死亡状态。";
            return false;
        }

        return TryGetCheckpointCore(out networkManager, out profile, out floorGuid, out reason);
    }

    internal static bool TryGetMultiplayerCheckpoint(
        PlayerSpawner rpcSender,
        out HorayNetworkManager networkManager,
        out string profile,
        out string floorGuid,
        out int playerCount,
        out string reason)
    {
        networkManager = null;
        profile = "";
        floorGuid = "";
        playerCount = 0;

        if (!CanStartRetry(out reason))
        {
            return false;
        }

        if (!NetworkServer.active || !NetworkClient.active || rpcSender == null || !rpcSender.isServer)
        {
            reason = "联机重试只能由同时运行客户端的房主服务器发起。";
            return false;
        }

        if (!HorayNetworkManager.AllowRejoin)
        {
            reason = "房主未使用 -allow_rejoin，联机 TMP 不包含可靠的楼层检查点。";
            return false;
        }

        List<PlayerSpawner> connectedPlayers = new List<PlayerSpawner>();
        foreach (KeyValuePair<int, NetworkConnectionToClient> pair in NetworkServer.connections)
        {
            NetworkConnectionToClient connection = pair.Value;
            if (connection == null || connection.identity == null ||
                !connection.identity.TryGetComponent(out PlayerSpawner connectedSpawner) || connectedSpawner == null)
            {
                reason = "存在尚未完成玩家初始化的网络连接。";
                return false;
            }

            connectedPlayers.Add(connectedSpawner);
        }

        playerCount = connectedPlayers.Count;
        if (playerCount < 2 || PlayerSpawner.MultiplayerList == null || PlayerSpawner.MultiplayerList.Count != playerCount)
        {
            reason = "在线连接与玩家列表不一致，或当前不是多人游戏。";
            return false;
        }

        bool senderFound = false;
        bool localHostFound = false;
        foreach (PlayerSpawner connectedSpawner in connectedPlayers)
        {
            senderFound |= connectedSpawner == rpcSender;
            localHostFound |= connectedSpawner.isLocalPlayer && connectedSpawner.isServer;

            PlayerAvatar avatar = connectedSpawner.PlayerAvatar;
            if (avatar == null || !avatar.IsDead || !avatar.dieIsGameOver.IsTrue())
            {
                reason = "并非所有在线玩家都处于会导致游戏结束的死亡状态。";
                return false;
            }

            bool listed = false;
            foreach (PlayerSpawner listedSpawner in PlayerSpawner.MultiplayerList)
            {
                if (listedSpawner == connectedSpawner)
                {
                    listed = true;
                    break;
                }
            }

            if (!listed)
            {
                reason = "网络连接中的玩家不在原版 MultiplayerList 中。";
                return false;
            }
        }

        if (!senderFound || !localHostFound)
        {
            reason = "找不到发起结算的玩家或本地房主玩家。";
            return false;
        }

        if (!TryGetCheckpointCore(out networkManager, out profile, out floorGuid, out reason))
        {
            return false;
        }

        SaveData run = SaveManager.CurrentRun;
        int savedPlayerCount = run.GetInt(PlayerSpawner.SavedPlayerCountKey, 0);
        if (savedPlayerCount != playerCount)
        {
            reason = $"检查点玩家数为 {savedPlayerCount}，当前在线玩家数为 {playerCount}；可能发生过断线或中途加入。";
            return false;
        }

        HashSet<int> occupiedSaveSlots = new HashSet<int>();
        foreach (PlayerSpawner connectedSpawner in connectedPlayers)
        {
            int saveSlot = connectedSpawner.currentPlayerIdxForSave;
            string playerGuid = connectedSpawner.playerGuid;
            if (saveSlot < 0 || saveSlot >= savedPlayerCount || !occupiedSaveSlots.Add(saveSlot) ||
                string.IsNullOrWhiteSpace(playerGuid) ||
                !string.Equals(run.GetString($"Player{saveSlot}Guid", ""), playerGuid, StringComparison.Ordinal))
            {
                reason = "至少一名在线玩家无法与 TMP 中的唯一玩家存档槽对应。";
                return false;
            }
        }

        reason = null;
        return true;
    }

    private static bool CanStartRetry(out string reason)
    {
        if (Plugin.Enabled == null || !Plugin.Enabled.Value)
        {
            reason = "配置中已关闭自动重试。";
            return false;
        }

        if (active)
        {
            reason = "自动重试已经在进行中。";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool TryGetCheckpointCore(
        out HorayNetworkManager networkManager,
        out string profile,
        out string floorGuid,
        out string reason)
    {
        networkManager = null;
        profile = "";
        floorGuid = "";

        DungeonManager dungeon = DungeonManager.Instance;
        if (dungeon == null || dungeon.victoryType != 0 || dungeon.isGiveUpRun)
        {
            reason = "本次结算是通关、剧情结束或主动放弃，不属于真正死亡。";
            return false;
        }

        SaveData run = SaveManager.CurrentRun;
        if (run == null || !run.enableSave || !run.GetBool("RunStarted", fallback: false))
        {
            reason = "当前没有可保存的已开始地牢会话。";
            return false;
        }

        floorGuid = run.GetString("LastFloorGuid", "");
        if (string.IsNullOrWhiteSpace(floorGuid) || !dungeon.generatedFloors.ContainsKey(floorGuid))
        {
            reason = "当前存档没有有效的 LastFloorGuid 本层检查点。";
            return false;
        }

        string tmpFile = run.BindedFileName;
        if (string.IsNullOrWhiteSpace(tmpFile) || !tmpFile.EndsWith(TmpSuffix, StringComparison.OrdinalIgnoreCase) ||
            !SaveData.Exists(tmpFile))
        {
            reason = "当前 TMP 检查点文件不存在或名称无效。";
            return false;
        }

        profile = tmpFile.Substring(0, tmpFile.Length - TmpSuffix.Length);
        string selectedProfile;
        try
        {
            selectedProfile = OptionsBinding.Instance.Options.GetString("SelectedProfile", SaveManager.defaultSlotName);
        }
        catch (Exception ex)
        {
            reason = "无法读取当前存档槽：" + ex.Message;
            return false;
        }

        if (!string.Equals(profile, selectedProfile, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"TMP 所属存档槽 {profile} 与当前所选槽 {selectedProfile} 不一致。";
            return false;
        }

        networkManager = NetworkManager.singleton as HorayNetworkManager;
        if (networkManager == null)
        {
            reason = "当前网络管理器不是 HorayNetworkManager。";
            return false;
        }

        try
        {
            if ((bool)Plugin.RestartingField.GetValue(networkManager))
            {
                reason = "原版重开流程已经在运行。";
                return false;
            }
        }
        catch (Exception ex)
        {
            reason = "无法读取原版重开状态：" + ex.Message;
            return false;
        }

        reason = null;
        return true;
    }

    internal static void Begin(string profile, string floorGuid, int playerCount, bool multiplayer)
    {
        generation++;
        active = true;
        checkpointReloaded = false;
        protectedProfile = profile;
        protectedTmpFile = profile + TmpSuffix;
        checkpointFloorGuid = floorGuid;
        multiplayerRetry = multiplayer;
        expectedPlayerRestarts = Math.Max(1, playerCount);
        CompletedPlayerRestarts.Clear();
        string mode = multiplayer ? $"联机房主（{playerCount} 名玩家）" : "单人";
        Plugin.Log?.LogInfo($"角色死亡，开始{mode}自动重试：存档槽={profile}，LastFloorGuid={floorGuid}。");
        Plugin.StartProtectionTimeout(generation);
    }

    internal static bool TryMovePlayersToCheckpointEntrance(out string reason)
    {
        reason = null;
        if (!active || string.IsNullOrWhiteSpace(checkpointFloorGuid))
        {
            reason = "自动重试检查点状态已失效。";
            return false;
        }

        FloorGenerator floor = FloorGenerator.FindByGuid(checkpointFloorGuid);
        if (floor == null || !floor.GenerateSuccess)
        {
            reason = "死亡楼层当前未完成生成，无法在重开前固定入口坐标。";
            return false;
        }

        AreaSpawnPointProp entrance = floor.FindSpawnPoint(FloorEntranceSpawnPoint);
        if (entrance == null)
        {
            reason = $"死亡楼层缺少 {FloorEntranceSpawnPoint} 入口点。";
            return false;
        }

        if (PlayerSpawner.MultiplayerList == null || PlayerSpawner.MultiplayerList.Count != expectedPlayerRestarts)
        {
            reason = "重开前玩家列表数量发生变化。";
            return false;
        }

        List<PlayerAvatar> players = new List<PlayerAvatar>(expectedPlayerRestarts);
        foreach (PlayerSpawner spawner in PlayerSpawner.MultiplayerList)
        {
            if (spawner == null || spawner.PlayerAvatar == null)
            {
                reason = "重开前至少一名玩家对象已失效。";
                return false;
            }

            players.Add(spawner.PlayerAvatar);
        }

        Vector3 position = entrance.SpawnPoint;
        if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z) ||
            float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z))
        {
            reason = "死亡楼层入口坐标无效。";
            return false;
        }

        foreach (PlayerAvatar player in players)
        {
            // Player network objects survive RestartGame(). Move the server transform first so
            // newly generated room reveal and boss trigger checks cannot observe the death position.
            // ReqSetPosition then mirrors the same quarantine position to an authority-owning client.
            player.transform.position = position;
            player.ReqSetPosition(position, teleport: true);
        }

        Plugin.Log?.LogInfo(
            $"重开前已将 {players.Count} 名玩家固定到本层入口 {position}，避免旧死亡坐标触发地图揭示或 Boss 事件。");
        return true;
    }

    internal static bool ShouldBlockDelete(string fileName)
    {
        return active && string.Equals(fileName, protectedTmpFile, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldLoadInsteadOfCreate(string profile)
    {
        return active && string.Equals(profile, protectedProfile, StringComparison.OrdinalIgnoreCase);
    }

    internal static void MarkCheckpointReloaded()
    {
        checkpointReloaded = true;
    }

    internal static bool ShouldSuppressFreshRunItems()
    {
        return active && checkpointReloaded;
    }

    internal static void MarkNewGameLoaded()
    {
        if (!active)
        {
            return;
        }

        if (checkpointReloaded)
        {
            Plugin.Log?.LogInfo("本层检查点已重新载入，自动重试进入角色初始化阶段。");
        }
        else
        {
            Plugin.Log?.LogWarning("NewGame 已开始，但未确认 TMP 检查点成功载入。");
        }
    }

    internal static void CompleteAfterPlayerRestart(PlayerSpawner spawner)
    {
        if (!active)
        {
            return;
        }

        if (spawner != null)
        {
            CompletedPlayerRestarts.Add(spawner.GetInstanceID());
        }

        if (CompletedPlayerRestarts.Count < expectedPlayerRestarts)
        {
            Plugin.Log?.LogInfo($"已恢复 {CompletedPlayerRestarts.Count}/{expectedPlayerRestarts} 名玩家。");
            return;
        }

        string mode = multiplayerRetry ? "全部联机玩家" : "单人角色";
        Plugin.Log?.LogInfo($"{mode}已从本层入口检查点初始化完成。");
        Clear();
    }

    internal static void Cancel(string reason, bool logWarning = true)
    {
        if (!active)
        {
            return;
        }

        if (logWarning)
        {
            Plugin.Log?.LogWarning(reason);
        }

        Clear();
    }

    internal static void CancelIfGeneration(int expectedGeneration, string reason)
    {
        if (active && generation == expectedGeneration)
        {
            Cancel(reason);
        }
    }

    internal static void FallbackToOriginalClientGameOver(PlayerSpawner spawner)
    {
        Cancel("自动重试启动失败。", logWarning: false);
        if (spawner == null)
        {
            Plugin.Log?.LogError("无法恢复原版死亡结算：PlayerSpawner 已失效。");
            return;
        }

        allowOriginalGameOverOnce = true;
        try
        {
            spawner.ClientGameOver();
        }
        finally
        {
            allowOriginalGameOverOnce = false;
        }
    }

    internal static void FallbackToOriginalRpcGameOver(PlayerSpawner spawner)
    {
        Cancel("联机自动重试启动失败。", logWarning: false);
        if (spawner == null)
        {
            Plugin.Log?.LogError("无法恢复原版联机死亡结算：PlayerSpawner 已失效。");
            return;
        }

        allowOriginalRpcGameOverOnce = true;
        try
        {
            spawner.RpcGameOver();
        }
        finally
        {
            allowOriginalRpcGameOverOnce = false;
        }
    }

    private static void Clear()
    {
        active = false;
        checkpointReloaded = false;
        protectedProfile = "";
        protectedTmpFile = "";
        checkpointFloorGuid = "";
        multiplayerRetry = false;
        expectedPlayerRestarts = 0;
        CompletedPlayerRestarts.Clear();
    }
}
