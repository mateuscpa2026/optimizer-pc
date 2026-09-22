<#
    Gera os entregaveis do Optimizer PC a partir do codigo-fonte.

    Saidas (pasta dist):
      publish\                  publicacao self-contained win-x64 (nao exige .NET instalado)
      OptimizerPC-Setup.exe     instalador (Inno Setup 6)
      OptimizerPC-Portable.zip  versao portatil

    Uso:
      powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
      powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1 -SkipTests
      powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1 -Iscc "D:\Inno Setup 6\ISCC.exe"

    Observacao: o Inno Setup 6 nao acompanha o repositorio. Sem ele o aplicativo e a
    versao portatil ainda sao gerados; a etapa do instalador precisa do compilador.
    O ISCC.exe e procurado nas pastas padrao de instalacao; use -Iscc para apontar
    outro caminho. Use uma copia integra: o Inno confere as proprias DLLs contra a
    assinatura do fabricante e recusa (corretamente) qualquer copia alterada. Para
    verificar uma instalacao, compare o SHA-256 de ISCmplr.dll com o valor gravado em
    ISCmplr.dll.issig. Se as duas copias nao baterem, baixe o Inno Setup 6 em
    https://jrsoftware.org/isdl.php.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$Iscc = '',
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host "ERRO: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }

function Find-Iscc {
    $candidates = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }

    return ''
}

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$publish = Join-Path $dist 'publish'
$staging = Join-Path $dist 'OptimizerPC-Portable'
$zip = Join-Path $dist 'OptimizerPC-Portable.zip'

$dotnet = Join-Path $HOME '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $command) { throw 'SDK do .NET 8 nao encontrado. Instale em https://dotnet.microsoft.com/download/dotnet/8.0' }
    $dotnet = $command.Source
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null

if ($SkipTests) {
    Write-Host '== 1/4 Compilando a solucao ==' -ForegroundColor Cyan
    & $dotnet build (Join-Path $root 'OptimizerPC.sln') -c $Configuration --nologo -v q
} else {
    Write-Host '== 1/4 Compilando e executando os testes ==' -ForegroundColor Cyan
    & $dotnet test (Join-Path $root 'tests\OptimizerPC.Tests\OptimizerPC.Tests.csproj') -c $Configuration --nologo -v q
}
if ($LASTEXITCODE -ne 0) { throw 'A compilacao ou os testes falharam.' }

Write-Host '== 2/4 Publicando o aplicativo ==' -ForegroundColor Cyan
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
& $dotnet publish (Join-Path $root 'src\OptimizerPC.App\OptimizerPC.App.csproj') -c $Configuration -r $RuntimeIdentifier --self-contained true --nologo -o $publish
if ($LASTEXITCODE -ne 0) { throw 'A publicacao do aplicativo falhou.' }

Write-Host '== 3/4 Compilando o instalador ==' -ForegroundColor Cyan
if ([string]::IsNullOrWhiteSpace($Iscc)) { $Iscc = Find-Iscc }
if (Test-Path $Iscc) {
    & $Iscc (Join-Path $root 'installer\OptimizerPC.iss')
    if ($LASTEXITCODE -ne 0) { throw 'A compilacao do instalador falhou.' }
} else {
    Write-Warning 'ISCC.exe nao encontrado. O instalador nao foi gerado. Baixe o Inno Setup 6 em https://jrsoftware.org/isdl.php e informe o caminho com -Iscc.'
}

Write-Host '== 4/4 Gerando o pacote portatil ==' -ForegroundColor Cyan
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
Copy-Item $publish $staging -Recurse
Copy-Item (Join-Path $root 'installer\portable-LEIA-ME.txt') (Join-Path $staging 'LEIA-ME.txt')
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $staging -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $staging -Recurse -Force

Write-Host ''
Write-Host 'Entregaveis gerados em dist:' -ForegroundColor Green
foreach ($path in @((Join-Path $publish 'OptimizerPC.exe'), (Join-Path $dist 'OptimizerPC-Setup.exe'), $zip)) {
    if (Test-Path $path) {
        $item = Get-Item $path
        $hash = (Get-FileHash $path -Algorithm SHA256).Hash.Substring(0, 16)
        Write-Host ("  {0,-26} {1,9:N1} MB   SHA256 {2}..." -f $item.Name, ($item.Length / 1MB), $hash)
    }
}
