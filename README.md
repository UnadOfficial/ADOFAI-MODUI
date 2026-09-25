# ADOFAI-MODUI

现代化的《冰与火之舞》（A Dance of Fire and Ice, ADOFAI）开源 Mod 管理器 —— 基于 **WinUI 3** 的 Windows 桌面客户端。

对接 [Tuforums](https://tuforums.com) 公共 Mod 目录，兼容 **Unity Mod Manager（UMM）** 的 `Mods/` 目录与 `Info.json` 规范，提供浏览、搜索、安装、更新、禁用/启用与卸载的完整生命周期管理。

## 功能

- **游戏路径自动定位**：读取注册表（`HKCU\Software\Valve\Steam\SteamPath` 等）→ 解析 `libraryfolders.vdf` 枚举全部 Steam 库 → 校验 `A Dance of Fire and Ice.exe` 与 `*_Data` 目录（Steam AppID `977950`）。支持手动选择目录并校验。
- **Tuforums Mod 目录**：分页拉取 `https://api.tuforums.com/v2/mods`（列表）与 `/v2/mods/{slug}`（详情/版本历史），带磁盘缓存与离线回退；客户端搜索 / 标签 / 状态过滤。
- **下载**：GitHub Release 标签页自动解析为直链资产（限流时回退 `expanded_assets` HTML 解析）；流式下载、断点续传（Range）、失败自动重试、实时进度与速度、可取消。
- **安装 / 更新**：Zip-Slip 防护；自动剥除压缩包多层嵌套目录；staging 原子替换；**更新时自动保留用户配置**（`Settings.xml`、`config.json`、`config/`、`*.cfg`/`*.ini` 等新包中不存在的文件）；失败自动回滚。
- **启停与卸载**：通过 `Info.json` ↔ `Info.json.disabled` 重命名实现禁用/启用（UMM 本身无原生禁用持久化，此为约定式实现）；卸载前先移入备份区（`%LOCALAPPDATA%\ADOFAI.ModManager\trash`）。
- **并发防锁**：安装/更新/卸载前检测游戏进程 `A Dance of Fire and Ice.exe` 是否运行，运行时弹窗提醒，防止 DLL 占用错误；同 Mod 串行、全局并行下载上限可配置。
- **版本比对**：兼容 SemVer 与 UMM 四段版本号（`1.2.3.4`）、`v` 前缀与预发布段，本地 `Info.json` 与远端最新版比对，标记“可更新”。
- **Fluent UI**：WinUI 3 + Mica 背景 + NavigationView，Mod 卡片（图标、版本徽章、标签彩条、行内进度条）、详情对话框（版本历史、依赖）、下载页与设置页。

## 技术栈

| 项 | 选型 |
| --- | --- |
| UI | WinUI 3（Windows App SDK 1.5+），非打包部署（`WindowsPackageType=None`）+ `WindowsAppSDKSelfContained` |
| 架构 | MVVM（`CommunityToolkit.Mvvm` 源生成器） |
| DI | `Microsoft.Extensions.DependencyInjection` |
| 网络 | `HttpClientFactory` 具名客户端；全异步 + `CancellationToken` + `IProgress<T>` |
| JSON | `System.Text.Json`（宽容反序列化） |
| 测试 | xUnit（核心层跨平台可测，CI 用 windows-latest） |

## 目录结构

```
ADOFAI-MODUI/
├─ ADOFAI.ModManager.sln
├─ src/
│  ├─ ADOFAI.ModManager.Core/          # 领域层（net8.0，无 UI 依赖，可跨平台测试）
│  │  ├─ Models/                       # TUF DTO / 本地 Mod / 游戏定位 / 应用状态
│  │  ├─ Helpers/                      # UmVersion / VdfParser / PathGuard / ZipNesting / Retry
│  │  ├─ Services/                     # Tuforums 客户端、Steam 定位、下载、安装、生命周期…
│  │  └─ ViewModels/                   # 主页面 / 卡片 / 详情 / 设置 / 下载
│  └─ ADOFAI.ModManager/               # WinUI 3 桌面应用（net8.0-windows10.0.19041.0）
│     ├─ App.xaml(.cs)                 # DI 组合根
│     ├─ MainWindow.xaml(.cs)          # Mica + NavigationView 壳
│     ├─ Views/                        # 浏览 / 我的 Mod / 下载 / 设置
│     └─ Controls/ · Converters/ · Services/Ui/ · Strings/zh-CN/
├─ tests/ADOFAI.ModManager.Core.Tests/ # 单元测试 + 真实 API golden fixtures
├─ tools/typecheck/                    # 非 Windows 主机上的应用层类型检查工程
└─ scripts/verify.sh                   # 一键验证（测试 + 构建/类型检查）
```

## 构建与运行

要求：Windows 10 1809+ / Windows 11，.NET 8 SDK（含 Windows 目标包）。

```powershell
dotnet build ADOFAI.ModManager.sln -c Release
dotnet test tests/ADOFAI.ModManager.Core.Tests -c Release
dotnet run --project src/ADOFAI.ModManager -c Release
```

非打包模式下运行需要 Windows App SDK 运行时：本仓库已设置 `WindowsAppSDKSelfContained=true`（运行时会随构建产物自包含），无需单独安装 WinAppRuntime。

### 一键验证

```bash
./scripts/verify.sh
```

- **任意平台**：构建核心层并运行单元测试（75 个用例）。
- **Windows**：额外完整构建 WinUI 应用（含 XAML 编译，即 `x:Bind` 路径、资源键与事件绑定校验）。
- **Linux / macOS**：`XamlCompiler` 只能在 Windows 上运行（它会 P/Invoke `kernel32.dll`），因此改为用 `tools/typecheck` 对应用层做编译期类型检查——它引用真实的 WinAppSDK 引用程序集，可校验全部 WinUI API 用法，但不校验 XAML 标记本身。详见 `tools/typecheck/README.md`。

仓库根目录的 `global.json` 将 SDK 固定为 8.0（`rollForward: latestFeature`）：.NET 10 SDK 会因 `win10-*` 这类版本化 RID 无法识别而中断 WinAppSDK 还原。

## 使用说明

1. 首次启动进入 **设置** 页，点击“自动定位”或“浏览…”选择游戏根目录（校验通过后会持久化）。
2. **浏览 Mod** 页拉取 Tuforums 目录，支持搜索、标签与状态筛选；卡片上的按钮随状态变化（安装 / 更新 / 启用 / 卸载 / 打开目录）。
3. 安装与更新自动处理下载、解压、嵌套剥层与配置保留；下载可在卡片内实时取消。
4. **我的 Mod** 页管理本地 Mods 目录（含未收录 Mod 与损坏 Mod 的识别），支持“导入本地 zip”安装。
5. **下载与任务** 页查看进行中的任务与安装历史。

## 已知边界与说明

- **禁用机制**：Unity Mod Manager 自身不提供持久化的禁用状态，本工具采用社区通行的重命名约定（`Info.json → Info.json.disabled`）。个别 UMM 分支若忽略该约定，被“禁用”的 Mod 仍可能被加载；此时请直接卸载。
- **GitHub 限流**：未认证 GitHub API 为 60 次/小时；解析结果会缓存 12 小时，限流时自动回退 HTML 解析，最终兜底为浏览器手动下载 + “导入本地 zip”。
- **仅支持 `.zip`** 分发包（rar/7z 不在内）。
- 本工具不安装 Unity Mod Manager 本体，请先按社区教程完成 UMM 安装（游戏目录下应存在 `Mods/` 文件夹）。
- Mod 目录以 `Info.json` 的 `Id` 为准做去重与匹配；与远端的自动匹配优先级为：用户关联 > `Id == slug` > `DisplayName == Name`。
- **验证边界**：核心层（服务 / ViewModel / 工具类）已在 Linux 上编译并通过全部单元测试；WinUI 应用层的 C# 已对照真实 WinAppSDK 元数据完成类型检查。但 XAML 标记的编译（`x:Bind` 路径、资源键、事件绑定）依赖 Windows-only 的 `XamlCompiler`，因此界面层需在 Windows 上构建后方可视为最终验证——CI 的 `app` 任务（`windows-latest`）覆盖这一步。

## 免责声明

本项目与 7th Beat Games、Tuforums 无官方关联；请仅安装可信来源的 Mod。使用本工具造成的任何游戏存档或数据问题请自行承担风险。

## License

Apache-2.0，见 [LICENSE](LICENSE)。
