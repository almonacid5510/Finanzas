; ============================================================
;  Instalador de Mis Finanzas (WPF / .NET 8)
;  Genera un instalador .exe tipo asistente (Next-Next-Finish)
;  con Inno Setup 6.
; ============================================================

#define MyAppName "Mis Finanzas"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Juan Almonacid"
#define MyAppCopyright "© 2026 Juan Almonacid"
#define MyAppExeName "MisFinanzas.exe"

; Carpeta donde 'dotnet publish' dejo la app (self-contained)
#define PublishDir "..\MisFinanzas\publish"

[Setup]
AppId={{B3F1B0A0-9C4E-4E2A-9B10-MISFINANZAS01}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright={#MyAppCopyright}
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright={#MyAppCopyright}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; El instalador de salida se llama asi y se guarda en la carpeta Salida
OutputDir=Salida
OutputBaseFilename=MisFinanzas_Setup_{#MyAppVersion}
SetupIconFile=..\MisFinanzas\Recursos\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; La app es de 64 bits (win-x64)
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos adicionales:"; Flags: unchecked

[Files]
; Copia TODO el contenido de publish\ (exe + runtime .NET + dependencias)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Ofrecer ejecutar la app al terminar la instalacion
Filename: "{app}\{#MyAppExeName}"; Description: "Ejecutar {#MyAppName} ahora"; Flags: nowait postinstall skipifsilent
