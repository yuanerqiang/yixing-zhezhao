; ============================================================
; 学习遮罩 - Inno Setup 打包脚本
; 生成标准的向导式安装程序（欢迎 → 安装位置 → 任务 → 安装 → 完成）
; 编译命令（管理员或普通用户均可）：
;   "C:\Users\yuan\AppData\Local\Programs\Inno Setup 6\ISCC.exe" 学习遮罩.iss
; ============================================================

#define MyAppName "学习遮罩"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "本地应用"
#define MyAppExeName "学习遮罩.exe"

[Setup]
; 应用唯一标识（AppId 请保持固定，升级安装时依赖它识别旧版本）
AppId={{8A3B2C1D-4E5F-4A6B-9C7D-8E9F0A1B2C3D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; 默认安装位置：当前用户的本地应用数据目录（无需管理员权限）
DefaultDirName={localappdata}\StudyMask
DefaultGroupName={#MyAppName}
; 允许用户修改安装目录
DisableDirPage=no
PrivilegesRequired=lowest
OutputDir=..\release
OutputBaseFilename=学习遮罩安装程序
SetupIconFile=..\assets\学习遮罩.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
; 卸载时删除目录内残留
UninstallFilesDir={app}\unins

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"
Name: "startmenuicon"; Description: "创建开始菜单快捷方式"; GroupDescription: "附加任务:"

[Files]
Source: "..\release\学习遮罩.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\release\使用说明.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon

; 安装完成后可选立即运行
[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行{#MyAppName}"; Flags: nowait postinstall skipifsilent

; 清理旧版（自写 WinForms 安装器）留下的残留文件与注册表项
[InstallDelete]
Type: files; Name: "{app}\卸载学习遮罩.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\StudyMask"; Flags: deletekey
