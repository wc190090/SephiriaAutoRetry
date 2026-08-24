# Sephiria 自动重试

《Sephiria》的 BepInEx 5 本层自动重试 Mod。角色真正死亡后，从当前楼层入口检查点恢复。

## v0.2.0 功能

- 支持单人游戏。
- 支持“仅房主安装”的联机游戏，客人保持原版即可加入。
- 全员死亡后由房主统一读取楼层 TMP 检查点并恢复所有仍在线的玩家。
- 不发送自定义网络消息，不要求客人安装相同 Mod 或匹配协议版本。
- 通关、剧情结算、主动放弃、断线、玩家中途加入、存档人数/GUID 不匹配、检查点无效等情况会安全退回原版死亡结算。

自动重试会跳过原版死亡结算界面，因此不会执行该界面里的死亡次数增加、蓝宝石结算和 TMP 删除逻辑。恢复时还会跳过“全新一局”专用的初始物品与药水补发，避免背包出现重复物品。

## 联机设置

房主必须在 Steam 中给游戏添加启动参数：

```text
-allow_rejoin
```

路径：Steam 库 → 右键《Sephiria》→ 属性 → 通用 → 启动选项。

- 只有房主需要安装本 Mod。
- 客人不需要安装 Mod，也不需要添加启动参数。
- 每次更改启动参数后，应完全退出游戏再重新启动；游戏会在进程启动时缓存该参数。
- 房主没有添加参数时，联机模式不会自动重试，而是保留原版结算。

## 安装

从 [GitHub 最新版](https://github.com/wc190090/SephiriaAutoRetry/releases/latest) 或 [Gitee 镜像](https://gitee.com/wc190092/SephiriaAutoRetry/releases) 下载：

- 推荐：运行离线安装器；它会自动寻找游戏目录、安装/保留 BepInEx、备份旧版插件并校验 DLL。
- 手动：把 ZIP 直接解压到游戏根目录。手动安装前需已有 BepInEx 5 x64。

最终 DLL 路径应为：

```text
Sephiria/BepInEx/plugins/SephiriaAutoRetry/SephiriaAutoRetry.dll
```

首次启动后可编辑：

```text
Sephiria/BepInEx/config/local.sephiria.autoretry.cfg
```

将 `Enabled = false` 可临时关闭自动重试。

## 安全边界

联机重试前，房主会核对：

- 当前确实是房主服务器，且至少两名玩家在线；
- 所有在线角色均已真正死亡；
- 房主启用了 `-allow_rejoin`；
- TMP、`RunStarted` 和 `LastFloorGuid` 有效；
- 当前连接数与 `SavedPlayerCount` 完全一致；
- 每名在线玩家的 GUID 都唯一对应检查点中的存档槽。

任何一项不满足，Mod 都不会尝试“猜测”恢复关系，而会继续执行原版死亡结算。

## 兼容性

当前按 Sephiria 1.0.30、Steam build `24838233`、Windows x64 Mono 构建并静态核对。兼容程序集：

```text
Assembly-CSharp.dll SHA-256
C3939A7D431F1362DACA655938480569E11F501F7D0820A989873765555A7C39
```

游戏更新导致关键方法或字段变化时，运行时兼容性检查会安全禁用 Mod，不阻止原版结算。联机逻辑已经完成代码和程序集级核对，但游戏更新后仍建议先进行一次 2–4 人实机测试。

## 构建与许可

构建方法见 [BUILDING.md](BUILDING.md)。项目采用 [MIT License](LICENSE)，仓库不包含游戏程序集或 BepInEx 二进制。
