# STS2_Hotreload-switch

Slay the Spire 2 通用模组热重载（ModHotReload）。仓库：[github.com/sakikoTGW/STS2_Hotreload-switch](https://github.com/sakikoTGW/STS2_Hotreload-switch)

在**不重启游戏**的前提下监视 `mods/<任意模组>/` 下的 `*.dll`、`*.pck`、`*.json`，并对已加载模组执行热重载。适用于走官方 `ModManager` 管线的 STS2 模组（含 BaseLib 内容 mod）。

| 项目 | 说明 |
|------|------|
| 游戏 | [Slay the Spire 2](https://store.steampowered.com/app/2868840/)（Steam AppID `2868840`） |
| 运行时 | .NET 9 + Godot 4.5（与游戏一致） |
| 当前版本 | 见 `ModHotReloadCode/MainFile.cs` 中 `Version` |

## 快速安装（玩家）

1. 从 [Releases](https://github.com/sakikoTGW/STS2_Hotreload-switch/releases/latest) 下载 **ModHotReload-v1.7.0.zip**（或更新版本），或自行编译（见下）。
2. 将整个 `ModHotReload` 文件夹复制到游戏的 `mods/ModHotReload/`。
3. 双击仓库根目录的 **`Install.bat`**（或运行 `scripts/install.ps1`），会编译、部署并写入 `sts2.runtimeconfig.json` 的 **startupHooks**。
4. 用 **Steam 正常启动**游戏（无需改启动项），在模组列表启用 **Mod Hot Reload**。

首次拷贝 mod 且尚未写入 runtimeconfig 时，可能需要**完全退出游戏再进一次**。

## 从源码构建（开发者）

### 前置

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- 已安装 STS2（Steam）
- （可选）[Godot 4.5 mono](https://godotengine.org/) — 仅当需要重新导出 `ModHotReload.pck`

### 配置路径

任选其一：

1. **环境变量**（推荐）  
   ```powershell
   $env:STS2_PATH = "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2"
   ```
2. **本地 MSBuild 属性**  
   ```powershell
   Copy-Item local.props.example local.props
   # 编辑 local.props 中的 Sts2Path
   ```

`Sts2PathDiscovery.props` 会尝试从 Steam 注册表与常见库路径自动发现游戏目录。

### 编译与验证

```powershell
cd ModHotReload
.\scripts\verify-build.ps1      # build + 程序集冒烟（ModHotReloadVerify）
.\scripts\deploy-and-verify.ps1 # 同上 + runtimeconfig + 部署目录核对
.\scripts\audit-hotreload.ps1   # 静态审计 + 部署一致性
```

构建成功后会复制到 `$(Sts2Path)/mods/ModHotReload/`（可用 `-p:DeployToGame=false` 只产出到 `bin/`）。

游戏运行时若 DLL 被锁定，构建会写入 `*.pending`；退出游戏后运行 `scripts/apply-pending.ps1`。

### 解决方案结构

```
ModHotReload/                 # Godot 宿主 + Harmony 补丁（主 mod）
ModHotReload.Core/            # ALC 重定向、设置门控、runtimeconfig 安装
ModHotReload.StartupHook/     # .NET startup hook（Steam 直启）
ModHotReloadCode/             # C# 源码
tools/ModHotReloadVerify/     # 离线程序集 / Harmony 探针
scripts/                      # 安装、部署、集成测试
```

## 工作方式（概要）

| 步骤 | 说明 |
|------|------|
| 启动 | `StartupHook` 在其它 mod 之前加载 Core，按设置拦截 `TryLoadMod` |
| 监视 | `FileSystemWatcher` 监听各 mod 目录（跳过自身） |
| 卸载 | BaseLib `Unregister*`（若存在）+ 按 ModId / Harmony owner 全量 `UnpatchAll` |
| DLL | 可收集 ALC + 影子路径加载 |
| PCK | `LoadResourcePack(replaceFiles: true)` + 虚拟卸载注册表 |
| UI | 与原生模组列表启停对齐；`ModLifecycleCoordinator` 统一状态机 |

日志：游戏内 `` ` `` 控制台，或 `%APPDATA%\SlayTheSpire2\logs\godot.log`（搜 `[热重载]`、`[ITEST]`）。

## 兼容性 / 排错

| 现象 | 原因与处理 |
|------|------------|
| 勾选模组时刷屏 `reloadall`、Manosaba `duplicate key` | v1.6.6 及更早会在每次勾选时全量对齐 settings；请升到 **v1.6.7+** |
| BaseLib 一改就全 mod 重载、duplicate key | v1.7.0 默认 **不** 自动 reloadall；在 `config.json` 设 `cascadeReloadAllOnBaseLib: true` 可恢复旧行为 |
| `mods/BaseLib.pck` 在根目录、子目录无 PCK | v1.7.0+ 支持扁平 `mods/{id}.pck` 布局 |
| `[BetterModMenu] MissingMethodException … LoadedMods()` | BetterModMenu 与当前游戏版本不匹配；**更新或暂时禁用** BetterModMenu |
| 日志里 `ModHotReload, Version=1.0.0.0` | 未装全三个 DLL 或仍是旧 zip；用 Release 包并确认含 Core + StartupHook |
| `DevConsole 注册失败` | 无害；进主菜单后会再注册 |

安装包须含：`ModHotReload.dll`、`ModHotReload.Core.dll`、`ModHotReload.StartupHook.dll`、`ModHotReload.pck`。首次安装运行 `Install.bat` 后**完全退出再进游戏**一次。

### 与其它 GitHub mod 的兼容矩阵（概要）

| 类型 | 热重载支持度 | 说明 |
|------|-------------|------|
| 官方 `ModManager` + `manifest.json` | 高 | 标准 `mods/{id}/{id}.dll` 或扁平 PCK |
| BaseLib 内容 mod | 高（DLL/PCK） | 默认不再自动 `reloadall`；依赖方可用 `cascadeDependentsOnReload` |
| 仅 Harmony、无卸载钩子 | 中 | 重载会 `UnpatchAll` 再加载；与其它 mod 补丁顺序可能冲突 |
| 强依赖 Default ALC 的旧 DLL | 低 | 首次进 Default 后需**重启游戏** |
| 菜单/UI 仅 PCK | 高 | PCK 变更会按依赖顺序**重挂全部**已加载 PCK |
| 自研菜单 patch（BetterModMenu 等） | 视游戏版本 | API 不匹配时与热重载无关，需更新该 mod |

## 限制

- **不能**热重载 ModHotReload 自身；改本 mod 后请重启游戏。
- **战斗中改 DLL**：默认走 SL 管道（保存 → 主菜单 → 重载 → 继续），非边打边换。
- 已进入 Default ALC 的依赖需重启；Watcher 有 ~1.5s 节流合并。

## 切档 / 模式切换（v1.6.9+，持续维护至 v1.7.0）

`modmode on|off` 或 Vanilla/Modded 存档切换时会：

1. **等待** pending 重载队列与进行中的 reload 收尾（`WaitForQuiescence`）
2. **快照** 当前已加载 mod 列表 → `%LOCALAPPDATA%\STS2_ModHotReload\switch-snapshot.json`
3. 失败则 **按快照回滚** 启停状态
4. 关闭 mod 时：`OnModUnload` / `OnDisable` / `DisposeMod` / `Unregister` 静态钩子（若 mod 提供）+ 清理 `%LOCALAPPDATA%\STS2_ModHotReload\mods\{ModId}\cache\`

Mod 持久化数据请放在 `res://{ModId}/` 或上述 cache 目录；`%APPDATA%\SlayTheSpire2\{ModId}\` **不会自动删除**（仅日志提示）。

Harmony 与其它 mod：关键 UI/加载补丁优先打；冲突时请更新 BetterModMenu 等依赖旧 API 的 mod。

## 外置配置（v1.6.8+）

首次运行会在 `%LOCALAPPDATA%\STS2_ModHotReload\config.json` 生成默认配置。模板见仓库根目录 `config.example.json`。

| 字段 | 含义 |
|------|------|
| `schemaVersion` | 配置结构版本（兼容用） |
| `hotReloadEnabled` | 自动热重载总开关 |
| `fileWatchEnabled` | 是否监视 mods 目录 |
| `debounceSeconds` | 文件变更去抖（秒） |
| `minReloadIntervalSeconds` | 同一 mod 最短重载间隔 |
| `maxReloadRetries` | 单 mod 失败最大自动重试次数 |
| `retryBackoffSeconds` | 重试退避间隔 |
| `cascadeReloadAllOnBaseLib` | BaseLib 成功后是否 `reloadall`（默认 **false**，v1.7.0+） |
| `cascadeDependentsOnReload` | 是否级联重载 manifest 依赖方（默认 true） |

控制台：`hotreload on` / `hotreload off` / `hotreload status` / `hotreload reload-config`

## 脚本参考

| 脚本 | 用途 |
|------|------|
| `install.ps1` | 构建 + runtimeconfig + 验证 |
| `patch-runtimeconfig.ps1` | 仅写入 startupHooks |
| `apply-pending.ps1` | 游戏退出后应用 `.pending` |
| `push-mod-staging.ps1` | 运行时把 DLL 推入 staging 触发重载 |
| `run-integration-test.ps1` | 自动化集成测试（需本机 STS2） |
| `run-rien-combat-verify.ps1` | 可选：Rien mod 战斗冒烟 |

集成测试：创建 `%LOCALAPPDATA%\STS2_ModHotReload\run-itest.flag` 后启动游戏，或运行 `run-integration-test.ps1`。

## 许可证

[MIT](LICENSE) — 与 Slay the Spire 2 及 Mega Crit 无关的第三方工具；请遵守游戏与 Steam 用户协议。
