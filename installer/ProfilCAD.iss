; Profil CAD kurulum betiği (Inno Setup 6)
; GitHub Actions tarafından derlenir:  iscc /DAppVersion=0.2.5 /DSourceDir=..\publish installer\ProfilCAD.iss

#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

#define AppName "Profil CAD"
#define AppExe "ProfilCAD.exe"

[Setup]
AppId={{6A1F2C77-3B7E-4F0D-9C55-8E2B7A4D1C90}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Harun Şimşek
DefaultDirName={autopf}\Profil CAD
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\output
OutputBaseFilename=ProfilCAD-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
ChangesAssociations=yes

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "assocdxf"; Description: "DXF dosyalarını Profil CAD ile aç"; GroupDescription: "Dosya ilişkilendirme:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.dxf\OpenWithProgids"; ValueType: string; ValueName: "ProfilCAD.dxf"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assocdxf
Root: HKA; Subkey: "Software\Classes\ProfilCAD.dxf"; ValueType: string; ValueName: ""; ValueData: "DXF Çizimi (Profil CAD)"; Flags: uninsdeletekey; Tasks: assocdxf
Root: HKA; Subkey: "Software\Classes\ProfilCAD.dxf\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: assocdxf
Root: HKA; Subkey: "Software\Classes\ProfilCAD.dxf\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: assocdxf
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".dxf"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".dwg"; ValueData: ""

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
