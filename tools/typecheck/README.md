# tools/typecheck —— 非 Windows 主机上的应用层类型检查

## 为什么需要它

`src/ADOFAI.ModManager` 是 WinUI 3 应用，其 XAML 编译由 `XamlCompiler` 完成，
而该工具**只能在 Windows 上运行**（在 Linux 上实测会因 P/Invoke `kernel32.dll` 失败）：

```
Xaml Internal Error error WMC9999: Unable to load shared library 'kernel32.dll'
```

因此在 Linux/macOS 上无法直接构建应用工程，应用层的 C# 代码也就得不到编译期校验。

本工程用「真实 WinAppSDK 引用程序集 + 手写 XAML 生成成员桩」的方式补上这块校验：

| 校验内容 | 本工程 | Windows 完整构建 |
| --- | :---: | :---: |
| 应用层 C#（WinUI API / 枚举 / 事件签名 / using） | ✅ | ✅ |
| 核心层（领域模型、服务、ViewModel） | ✅ | ✅ |
| XAML 语法、`x:Bind` 路径、资源键、事件绑定名 | ❌ | ✅ |

## 用法

```bash
dotnet build tools/typecheck/Typecheck.csproj -c Release
```

（该工程通过 `Directory.Build.targets` 把默认 `Build` 目标收敛到 `Compile`，
以避免触发 WinAppSDK 中依赖 Visual Studio 组件的 Windows-only 打包任务。）

或直接运行仓库根的 `scripts/verify.sh`（会自动区分当前平台）。

## 维护

`Stubs.cs` 里手写了 XAML 代码生成器会产出的成员：

- 每个 `x:Class` 的 `InitializeComponent()`
- 每个 `x:Name` 元素的字段（含类型）

**当 XAML 新增 `x:Name` 或新增带 `x:Class` 的 XAML 文件时，需要同步更新 `Stubs.cs`**，
否则类型检查会出现 `CS0103: 不存在名称 "InitializeComponent"` 之类的“假错误”。

本工程刻意不加入 `ADOFAI.ModManager.sln`，以免影响正常构建与 CI。
