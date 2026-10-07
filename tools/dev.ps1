param(
    [switch]$Dock,
    [switch]$Finder,
    [switch]$Both,
    [switch]$Stop,
    [switch]$Release
)

# Uso: .\tools\dev.ps1 -Dock | -Finder | -Both  (-Release opcional)  |  -Stop
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$config = if ($Release) { 'Release' } else { 'Debug' }
$framework = 'net10.0-windows10.0.19041.0'

if ($Stop) {
    # A AppBar do Finder é liberada pelo Windows quando o processo termina.
    foreach ($name in 'GoatDock', 'GoatFinder') { Get-Process $name -ErrorAction SilentlyContinue | Stop-Process }
    return
}

if ($Both) { $Dock = $true; $Finder = $true }
if (-not ($Dock -or $Finder)) { throw 'Informe -Dock, -Finder ou -Both (ou -Stop).' }

$targets = @()
if ($Dock) { $targets += 'GoatDock' }
if ($Finder) { $targets += 'GoatFinder' }

# Compila um por vez: o segundo build só recompila o que mudou, e evita dois "dotnet run" disputando os mesmos arquivos.
foreach ($name in $targets) {
    dotnet build (Join-Path $root "src\$name\$name.csproj") -c $config --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar $name." }
}

foreach ($name in $targets) {
    $exe = Join-Path $root "src\$name\bin\$config\$framework\$name.exe"
    if (-not (Test-Path $exe)) { throw "Executável não encontrado: $exe" }
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
    Write-Host "Iniciado: $name"
}
