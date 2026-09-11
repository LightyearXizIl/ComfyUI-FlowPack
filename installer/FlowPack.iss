#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

#define MyAppName "ComfyUI FlowPack"
#define MyAppPublisher "LightyearXizIl"
#define MyAppExeName "ComfyUI.FlowPack.exe"

[Setup]
AppId={{B203065A-7E82-4F0A-B3B3-43CC5E1C5BC6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\ComfyUI FlowPack
DisableDirPage=no
UsePreviousAppDir=yes
UsePreviousLanguage=yes
DefaultGroupName=ComfyUI FlowPack
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=ComfyUI-FlowPack-{#MyAppVersion}-Setup
SetupIconFile=..\src\FlowPack.App\Assets\FlowPack.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Windows installer

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl,Languages\ChineseSimplified.isl"

[CustomMessages]
DesktopIcon=Create a desktop shortcut
OtherTasks=Additional tasks:

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:OtherTasks}"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\app\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\publish\worker\*"; DestDir: "{app}\worker"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ComfyUI FlowPack"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\ComfyUI FlowPack"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 ComfyUI FlowPack"; Flags: nowait postinstall skipifsilent
