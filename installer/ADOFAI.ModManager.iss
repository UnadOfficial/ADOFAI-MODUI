; =============================================================================
;  ADOFAI Mod 管理器 —— Inno Setup 安装脚本
;
;  需要 Inno Setup 6.3+（用到了 ArchitecturesAllowed=x64compatible）。
;  本文件为 UTF-8 with BOM —— Inno Setup 6 依赖 BOM 判定编码，请勿去掉。
;
;  由 .github/workflows/release.yml 调用：
;      iscc installer\ADOFAI.ModManager.iss /DMyAppVersion=1.2.3 /DMyAppVersionNumeric=1.2.3.0
;
;  路径约定（相对本文件所在目录 installer/）：
;      ..\dist\app            已 publish 的应用文件（工作流把 artifact 下载到这里）
;      ..\dist                安装包输出目录（OutputDir 相对于 SourceDir，即本目录）
;      ..\assets\app.ico      安装程序图标
;      ..\LICENSE             Apache-2.0 许可全文
;      ChineseSimplified.isl  简体中文语言文件（可选，缺失时只编译英文界面）
; =============================================================================

#define MyAppName      "ADOFAI Mod 管理器"
#define MyAppNameEn    "ADOFAI Mod Manager"
#define MyAppPublisher "ADOFAI-MODUI Contributors"
#define MyAppURL       "https://github.com/UnadOfficial/ADOFAI-MODUI"
#define MyAppExeName   "ADOFAI.ModManager.exe"
#define MyAppId        "{{DB500054-8743-45D7-898E-7E28162C1004}"

; ---- 可由命令行 /D 覆盖（本地直接编译时用下面的默认值） ----------------------
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef MyAppVersionNumeric
  #define MyAppVersionNumeric "0.0.0.0"
#endif
#ifndef AppPayloadDir
  #define AppPayloadDir "..\dist\app"
#endif
#ifndef OutDir
  #define OutDir "..\dist"
#endif

; 可选资源：存在才引用，避免本地缺少文件时编译失败
#define ZhIslPath   SourcePath + "\ChineseSimplified.isl"
#define IconPath    SourcePath + "\..\assets\app.ico"
#define LicensePath SourcePath + "\..\LICENSE"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyAppVersionNumeric}
VersionInfoDescription={#MyAppNameEn} Setup

DefaultDirName={autopf}\{#MyAppNameEn}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes

; 默认「仅为我安装」，无需管理员；用户仍可在向导首屏切换为全机器安装
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

OutputDir={#OutDir}
OutputBaseFilename=ADOFAI.ModManager-{#MyAppVersion}-win-x64-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

; 升级时通过重启管理器检测被占用的文件（例如程序正在运行）
CloseApplications=yes
RestartApplications=no

#if FileExists(IconPath)
SetupIconFile={#IconPath}
#endif

#if FileExists(LicensePath)
LicenseFile={#LicensePath}
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
#if FileExists(ZhIslPath)
Name: "chinesesimplified"; MessagesFile: "{#ZhIslPath}"
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 已 publish 的自包含产物整体铺开，不做筛选
Source: "{#AppPayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "{#MyAppNameEn}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
