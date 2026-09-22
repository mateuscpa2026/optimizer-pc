; ---------------------------------------------------------------------------
; Optimizer PC - script do instalador (Inno Setup 6)
;
; Compile com: tools\Build-Release.ps1
;   ou: "C:\Users\<usuario>\tools\InnoSetup6\ISCC.exe" installer\OptimizerPC.iss
;
; Requisitos:
;   - dist\publish com a publicacao self-contained (win-x64) do aplicativo;
;   - installer\wizard-large.bmp (gerado por tools\Generate-Assets.ps1).
;
; O aplicativo e instalado com o manifesto asInvoker: ele NAO inicia elevado.
; A elevacao e pedida sob demanda, apenas nas funcoes que realmente precisam.
; O instalador requer administrador para gravar em Arquivos de Programas, mas
; o usuario pode escolher "somente para mim" e instalar sem privilegios.
; ---------------------------------------------------------------------------

#define AppName "Optimizer PC"
#define AppTagline "Otimize. Limpe. Desempenhe."
#define AppVersion "1.0"
#define AppPublisher "Optimizer PC"
#define AppExeName "OptimizerPC.exe"
#define SourceDir "..\dist\publish"

[Setup]
; AppId identifica o aplicativo nas atualizacoes e na desinstalacao. Nao altere.
AppId={{09AE0CF2-3095-42EF-85BE-7D6D68CD63D7}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} - {#AppTagline}
VersionInfoVersion=1.0.0.0

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
AllowNoIcons=yes
DisableProgramGroupPage=yes

OutputDir=..\dist
OutputBaseFilename=OptimizerPC-Setup
SetupIconFile=..\src\OptimizerPC.App\Assets\OptimizerPC.ico
WizardStyle=modern
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp

Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
