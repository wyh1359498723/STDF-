; STDF Analyzer — Inno Setup 6
; 先执行: dotnet publish (见 build-installer.ps1)

#define MyAppName      "STDF Analyzer"
#define MyAppExeName   "StdfAnalyzer.exe"
#define MyAppPublisher "HT-Tech"
#define MyAppVersion   "1.4.0"

[Setup]
AppId={{8F3E2B1A-6C7D-4E5F-9A0B-1C2D3E4F5A6B}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\bin\Installer
OutputBaseFilename=STDF_Analyzer_Setup_{#MyAppVersion}
; 压缩：zip 兼容性好；与 build-installer.ps1 的「多文件发布」配合，避免单一大 exe 嵌入导致 CRC 失败。
Compression=zip
SolidCompression=no
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

; Inno Setup 6.7+ 默认不再附带 ChineseSimplified.isl，向导使用英文。
; 若需中文界面，可将 ChineseSimplified.isl 放到本目录并把下行改为: MessagesFile: "ChineseSimplified.isl"
[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 递归打包 publish 目录（多文件自包含）；勿改回只打包单文件大 exe，易触发 “The setup files are corrupted”
Source: "..\bin\Publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
