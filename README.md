# switc — Slay the Spire 2 Mod Hot Reload

在**不重启游戏**的前提下监视 `mods/<任意模组>/` 下的 `*.dll`、`*.pck`、`*.json`，并对已加载模组执行热重载。适用于走官方 `ModManager` 管线的 STS2 模组（含 BaseLib 内容 mod）。

| 项目 | 说明 |
|------|------|
| 游戏 | [Slay the Spire 2](https://store.steampowered.com/app/2868840/)（Steam AppID `2868840`） |
| 运行时 | .NET 9 + Godot 4.5（与游戏一致） |
| 当前版本 | 见 `ModHotReloadCode/MainFile.cs` 中 `Version` |

## 快速安装（玩家）

1. 从 [Releases](https://github.com/sakikoTGW/switc/releases) 下载构建产物，或自行编译（见下）。
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

## 限制

- **不能**热重载 ModHotReload 自身；改本 mod 后请重启游戏。
- **战斗中改 DLL**：默认走 SL 管道（保存 → 主菜单 → 重载 → 继续），非边打边换。
- 已进入 Default ALC 的依赖需重启；Watcher 有 ~1.5s 节流合并。

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
