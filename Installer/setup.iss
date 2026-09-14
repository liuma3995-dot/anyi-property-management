; 安怡物业管理系统 安装脚本（M0 骨架，M8 完善）
#define AppName "安怡物业管理系统"
#define AppVersion "0.1.0"
#define AppPublisher "安怡物业"
#define ClientExe "..\PropertyManagement.Client\bin\Release\PropertyManagement.Client.exe"
#define ServerExe "..\PropertyManagement.Server\bin\Release\PropertyManagement.Server.exe"

[Setup]
AppId={{2B7DE9A1-0005-4A5B-9C05-123456789ABC}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\PropertyManagement
DefaultGroupName={#AppName}
OutputDir=..\output
OutputBaseFilename=安怡物业管理系统-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: {#ClientExe}; DestDir: {app}; Flags: ignoreversion
Source: {#ServerExe}; DestDir: {app}; Flags: ignoreversion
; TODO(M8)：.NET Framework 4.8 离线安装器、SQLite 原生库、后端服务自启（Windows 服务或启动项）

[Run]
Filename: "{app}\{#ServerExe}"; Description: "启动后端服务"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#ClientExe}"; Description: "启动客户端"; Flags: nowait postinstall skipifsilent
