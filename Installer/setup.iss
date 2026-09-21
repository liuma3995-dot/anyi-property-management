; ============================================================================
; 安怡物业管理系统 · Inno Setup 6 安装脚本（M8 D8-1 / T8-1-2）
; ----------------------------------------------------------------------------
; 口径来源：[M8-打包发布任务计划] §九 —— ①per-machine（{autopf}\PropertyManagement）
;   ②自启 = 登录启动项（Run 键）+ 隐藏窗口  ③内嵌 .NET Framework 4.8 离线安装器
;   ④卸载询问（默认保留数据 / 可选一并删除 + 二次确认）  ⑤本期不做代码签名（附 SHA256）
;   ⑥包名 <正式名>-Setup-<主.次.修订>.exe
; 安装布局（客户端按 Client\..\Server 解析并兜底拉起，见 BackendLauncher.FindServerExecutable）：
;   {app}\Client\PropertyManagement.Client.exe
;   {app}\Server\PropertyManagement.Server.exe
; 注意（D8-0 口径 1/2 红线）：AppId / 默认安装目录 / 数据目录 / 程序集名 / 可执行文件名均不得变更。
; ============================================================================

#define AppName "安怡物业管理系统"
#define AppPublisher "安怡物业"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
; 四位程序集版本（v1.1.1 起）：安装包自身 FileVersion 用它，展示版本仍用三位 AppVersion
#define AppVersionQuad AppVersion + ".0"
#define ClientBin "..\PropertyManagement.Client\bin\Release"
#define ServerBin "..\PropertyManagement.Server\bin\Release"
#define RedistDir "redist"

[Setup]
AppId={{2B7DE9A1-0005-4A5B-9C05-123456789ABC}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
; 安装包自身的版本资源（v1.1.1 补齐）：原先仅由 Inno 默认写入产品名/产品版本，FileVersion 为空
VersionInfoVersion={#AppVersionQuad}
VersionInfoProductVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} 安装程序
VersionInfoCopyright=Copyright (C) 2026 {#AppPublisher}
DefaultDirName={autopf}\PropertyManagement
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Inno 6 默认禁用欢迎页；此处启用以呈现品牌大图与产品全称
DisableWelcomePage=no
AllowNoIcons=yes
OutputDir=..\output
OutputBaseFilename=安怡物业管理系统-Setup-{#AppVersion}
OutputManifestFile=..\output\安怡物业管理系统-Setup-{#AppVersion}-文件清单.txt
Compression=lzma2/max
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
SetupIconFile=..\PropertyManagement.Client\Assets\app.ico
UninstallDisplayIcon={app}\Client\PropertyManagement.Client.exe
UninstallDisplayName={#AppName} {#AppVersion}
; 品牌图（T8-1-2 接入 docs\prototypes\品牌logo\brand\ 资产，由 build-setup.ps1 生成 BMP 到 build\）
WizardImageFile=build\wizard-image.bmp
WizardSmallImageFile=build\wizard-small.bmp
; 品牌图以 2 倍分辨率生成，交由 Inno 按目标 DPI 缩放（缩小而非放大）以保证文字锐利
WizardImageStretch=yes

[Languages]
Name: "chinese"; MessagesFile: "compiler:Default.isl"

[Messages]
; Inno Setup 6 未内置简体中文语言包：以英文 Default.isl 为基底，覆盖主要页面文案
; 说明：占位符须与 Default.isl 一致（页面标题用 %1，正文/说明用 [name]、[name/ver]、[mb]）
; ── 安装向导：标题与窗口 ──
SetupAppTitle=安装
SetupWindowTitle=安装 - %1
SetupLdrStartupMessage=即将在你的电脑上安装 %1。是否继续？
AdminPrivilegesRequired=安装本程序需要以管理员身份登录。
ExitSetupTitle=退出安装
ExitSetupMessage=安装尚未完成。现在退出将不会安装本软件。%n%n确定要退出吗？
; ── 安装向导：欢迎页 ──
WelcomeLabel1=欢迎使用 [name] 安装向导
WelcomeLabel2=即将在你的电脑上安装 [name/ver]。
ClickNext=点击「下一步」继续，或点击「取消」退出安装。
; ── 安装向导：选择安装位置 ──
WizardSelectDir=选择安装位置
SelectDirDesc=[name] 将被安装到以下文件夹。
SelectDirLabel3=安装程序将把 [name] 安装到以下文件夹。
SelectDirBrowseLabel=点击「下一步」继续；如需更改安装位置，请点击「浏览」。
DiskSpaceMBLabel=至少需要 [mb] MB 可用磁盘空间。
; ── 安装向导：附加任务 ──
WizardSelectTasks=选择附加任务
SelectTasksLabel2=请选择安装 [name] 期间要执行的附加任务，然后点击「下一步」。
; ── 安装向导：准备安装 ──
WizardReady=准备安装
ReadyLabel1=安装程序已准备好开始安装 [name]。
ReadyLabel2a=点击「安装」开始；如需检查或更改设置，请点击「上一步」。
ReadyLabel2b=点击「安装」开始安装。
ReadyMemoDir=安装位置：
ReadyMemoGroup=开始菜单文件夹：
PreparingDesc=正在准备安装 [name]，请稍候…
; ── 安装向导：安装中 / 完成 ──
WizardInstalling=正在安装
InstallingLabel=正在安装 [name]，请稍候…
FinishedHeadingLabel=[name] 安装完成
FinishedLabel=安装程序已完成 [name] 的安装。%n%n点击「完成」退出。
FinishedLabelNoIcons=安装程序已完成 [name] 的安装。%n%n点击「完成」退出。
; ── 卸载向导（英文提示一并中文化） ──
UninstallAppTitle=卸载
UninstallAppFullTitle=卸载 %1
UninstallStatusLabel=正在从你的电脑上移除 %1，请稍候…
WizardUninstalling=卸载状态
StatusUninstalling=正在卸载 %1…
ConfirmUninstall=确定要完全卸载 %1 及其所有组件吗？
UninstalledAll=%1 已成功从你的电脑上移除。
UninstalledMost=%1 卸载完成。%n%n部分元素未能删除，可手动清理。
UninstalledAndNeedsRestart=要完成 %1 的卸载，需要重启电脑。%n%n是否现在重启？
UninstallAppRunningError=检测到 %1 正在运行。%n%n请先关闭它的所有实例，然后点击「确定」继续，或点击「取消」退出。
; ── 按钮 ──
ButtonNext=下一步(&N) >
ButtonBack=< 上一步(&B)
ButtonInstall=安装(&I)
ButtonCancel=取消
ButtonFinish=完成(&F)
ButtonBrowse=浏览(&B)…
; 向导内的“浏览”按钮与选文件夹对话框（Inno 使用 ButtonWizardBrowse，而非 ButtonBrowse）
ButtonWizardBrowse=浏览(&W)…
BrowseDialogTitle=选择文件夹
BrowseDialogLabel=在下面的列表中选择文件夹，然后点击「确定」。
ButtonYes=是(&Y)
ButtonNo=否(&N)

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："

[Files]
; ① 整树打包（Release 产物：全部依赖 DLL + exe.config + NLog.config），递归含子目录
Source: "{#ClientBin}\*"; DestDir: "{app}\Client"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ServerBin}\*"; DestDir: "{app}\Server"; Flags: ignoreversion recursesubdirs createallsubdirs
; ② 自启载体：隐藏窗口拉起脚本（Run 键指向 wscript + 本脚本，避免控制台窗口闪现）
Source: "launch-server-hidden.vbs"; DestDir: "{app}\Server"; Flags: ignoreversion
; ③ .NET Framework 4.8 离线安装器（T8-1-1 落资源，按 §九 第 9 项不纳入版本控制）
Source: "{#RedistDir}\ndp48-x86-x64-allos-enu.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Dirs]
; 运行期数据目录（DZ-3：%ProgramData%\PropertyManagement），预建并授权普通用户可写
Name: "{commonappdata}\PropertyManagement"; Permissions: users-modify
Name: "{commonappdata}\PropertyManagement\data"; Permissions: users-modify
Name: "{commonappdata}\PropertyManagement\logs"; Permissions: users-modify
Name: "{commonappdata}\PropertyManagement\backups"; Permissions: users-modify
Name: "{commonappdata}\PropertyManagement\exports"; Permissions: users-modify

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Client\PropertyManagement.Client.exe"; WorkingDir: "{app}\Client"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Client\PropertyManagement.Client.exe"; WorkingDir: "{app}\Client"; Tasks: desktopicon

[Registry]
; 登录自启（§九 第 2 项）：HKLM Run 键（per-machine 口径）→ wscript 隐藏窗口拉起后端；
; 卸载时由 uninsdeletevalue 删除该值。用 HKLM 而非 HKCU：与 per-machine 安装形态一致，
; 且避免「admin 安装模式下写按用户区域」的告警。
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "AnYiPropertyServer"; ValueData: "wscript.exe //B ""{app}\Server\launch-server-hidden.vbs"""; \
    Flags: uninsdeletevalue

[Run]
; .NET Framework 4.8：缺失（Release < 528040）时内嵌静默安装；已满足则跳过（P-04）
Filename: "{tmp}\ndp48-x86-x64-allos-enu.exe"; Parameters: "/q /norestart"; \
    StatusMsg: "正在安装 .NET Framework 4.8 运行时（可能需要几分钟）…"; \
    Check: NeedsDotNet48; Flags: waituntilterminated
Filename: "{app}\Client\PropertyManagement.Client.exe"; Description: "启动 {#AppName}"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
; 卸载前先停服（RunOnceId 保证只执行一次）
Filename: "{cmd}"; Parameters: "/C taskkill /F /IM PropertyManagement.Server.exe /T"; Flags: runhidden; RunOnceId: "StopServer"
Filename: "{cmd}"; Parameters: "/C taskkill /F /IM PropertyManagement.Client.exe /T"; Flags: runhidden; RunOnceId: "StopClient"

[Code]
const
  DotNet48ReleaseThreshold = 528040;

var
  DeleteUserData: Boolean;

{ .NET Framework 4.8 检测（T8-1-2 / P-04）：注册表 Release ≥ 528040 视为已满足 }
function QueryDotNetRelease(out Release: Cardinal): Boolean;
begin
  if IsWin64 then
    Result := RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
  else
    Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release);
end;

function NeedsDotNet48(): Boolean;
var
  Release: Cardinal;
begin
  if not QueryDotNetRelease(Release) then
  begin
    Result := True;
    Exit;
  end;
  Result := Release < DotNet48ReleaseThreshold;
end;

{ 按镜像名结束进程；返回是否确实结束过（用于升级停服提示） }
function KillProcessByName(const ImageName: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'),
    '/C taskkill /F /IM "' + ImageName + '" /T',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  { taskkill 在进程不存在时返回非 0，这里以「命令成功执行」为准，不据此判定业务结果 }
end;

function IsProcessRunning(const ImageName: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'),
    '/C tasklist /FI "IMAGENAME eq ' + ImageName + '" | find /I "' + ImageName + '" > nul',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

{ 升级安装前置：停止运行中的程序，避免 exe/dll 被占用导致覆盖失败（T8-2-3） }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Stopped: Boolean;
begin
  Result := '';
  NeedsRestart := False;

  Stopped := False;
  if IsProcessRunning('PropertyManagement.Client.exe') then
  begin
    KillProcessByName('PropertyManagement.Client.exe');
    Stopped := True;
  end;
  if IsProcessRunning('PropertyManagement.Server.exe') then
  begin
    KillProcessByName('PropertyManagement.Server.exe');
    Stopped := True;
  end;

  { 静默安装（自动升级）不弹窗，只在交互安装时提示 }
  if Stopped and (not WizardSilent) then
    MsgBox('检测到安怡物业管理系统正在运行，安装程序已自动停止相关进程以完成升级。' + #13#10 +
           '你的数据不受影响；安装完成后请重新启动客户端。', mbInformation, MB_OK);
end;

{ 卸载询问（§九 第 5 项）：默认「保留数据」；选「一并删除」需二次确认 }
function InitializeUninstall(): Boolean;
begin
  Result := True;
  DeleteUserData := False;

  { 静默卸载按默认口径处理：保留数据（/SUPPRESSMSGBOXES 不会抑制 [Code] 的 MsgBox） }
  if UninstallSilent then
    Exit;

  if MsgBox('是否同时删除本机的运行数据？' + #13#10 + #13#10 +
            '数据目录：' + ExpandConstant('{commonappdata}') + '\PropertyManagement' + #13#10 +
            '（包含数据库、日志、备份、导出文件）' + #13#10 + #13#10 +
            '默认选择「否」，保留数据以便将来恢复或升级。',
            mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDNO then
  begin
    Exit;
  end;

  if MsgBox('再次确认：将永久删除上述数据目录及其中的全部记录，删除后无法恢复。' + #13#10 + #13#10 +
            '确定要一并删除吗？',
            mbError, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DeleteUserData := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    (* 卸载清理客户端本地缓存（负责人 2026-09-21 裁定）：
       %LocalAppData%\PropertyManagement 存有 session.json（登录态）/ prefs.json（记住账号）/ crash.log。
       此前卸载不清理，重装后只要本地会话未过期，客户端会跳过登录页直接进入上次登录态，故随卸载一并删除。
       口径：只清当前 Windows 用户的本地缓存；共享数据目录 commonappdata 仍按上面的询问处理
       （默认保留，选择「一并删除」时连同清理）。 *)
    DelTree(ExpandConstant('{localappdata}\PropertyManagement'), True, True, True);

    if DeleteUserData then
      DelTree(ExpandConstant('{commonappdata}\PropertyManagement'), True, True, True);
  end;
end;
