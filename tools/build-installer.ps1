# Script para empacotar e compilar o instalador oficial do GoatDockFinder (x64)
param([switch]$Assinar, [string]$CertificadoThumbprint, [string]$SignToolPath, [string]$TimestampUrl)
$ErrorActionPreference = "Stop"

$signProperties = @()
if (-not $Assinar) { Write-Warning 'Build sem assinatura: o Smart App Control pode bloquear este instalador. Use -Assinar com certificado confiável para distribuição.' }
if ($Assinar) {
    $CertificadoThumbprint = ($CertificadoThumbprint -replace '\s', '').ToUpperInvariant()
    if ($CertificadoThumbprint -notmatch '^[0-9A-F]{40}$') { throw 'Informe o Thumbprint do certificado (40 caracteres hexadecimais).' }
    if (-not $SignToolPath -or -not (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) { throw 'Informe o caminho completo do signtool.exe do Windows SDK.' }
    $SignToolPath = (Resolve-Path -LiteralPath $SignToolPath).Path
    if ($SignToolPath -match '["&;<>%]' -or [IO.Path]::GetFileName($SignToolPath) -ne 'signtool.exe') { throw 'Caminho do SignTool inválido.' }
    if ($TimestampUrl -notmatch '^https?://[a-zA-Z0-9.-]+(/[a-zA-Z0-9/._-]*)?$') { throw 'Informe o timestamp RFC3161 da certificadora (URL HTTP/HTTPS sem parâmetros).' }
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificadoThumbprint" -ErrorAction Stop
    if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -lt (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) { throw 'Certificado sem chave privada acessível ou fora da validade.' }
    if ($certificate.PublicKey.Oid.Value -ne '1.2.840.113549.1.1.1') { throw 'Use certificado RSA para Smart App Control.' }
    if (-not ($certificate.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' })) { throw 'O certificado precisa da finalidade de assinatura de código.' }
    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    try { if (-not $chain.Build($certificate)) { throw 'O Windows não validou a cadeia de confiança do certificado.' } }
    finally { $chain.Dispose() }
    $signProperties = @('-p:GoatDockFinderSigning=true', "-p:CodeSigningThumbprint=$CertificadoThumbprint", "-p:CodeSigningTool=$SignToolPath", "-p:CodeSigningTimestamp=$TimestampUrl")
}

function Assinar-Publicacao([string]$pasta) {
    if (-not $Assinar) { return }
    foreach ($arquivo in (Get-ChildItem -LiteralPath $pasta -File -Recurse | Where-Object { $_.Extension -in @('.exe', '.dll') })) {
        $signature = Get-AuthenticodeSignature -LiteralPath $arquivo.FullName
        if ($signature.Status -eq 'NotSigned') {
            & $SignToolPath sign /sha1 $CertificadoThumbprint /s My /fd SHA256 /tr $TimestampUrl /td SHA256 $arquivo.FullName
            if ($LASTEXITCODE -ne 0) { throw "Falha na assinatura: $($arquivo.Name)" }
        }
        & $SignToolPath verify /pa $arquivo.FullName
        if ($LASTEXITCODE -ne 0) { throw "Assinatura inválida: $($arquivo.Name)" }
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Compilando GoatDockFinder e Gerando Instalador Oficial   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$rootDir = Split-Path -Parent $PSScriptRoot
$installerProj = Join-Path $rootDir "src\GoatDockFinder.Installer\GoatDockFinder.Installer.csproj"
$distDir = Join-Path $rootDir ("dist\release-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
$installerDistDir = Join-Path $distDir "installer"
$resourcesDir = Join-Path $rootDir "src\GoatDockFinder.Installer\Resources"

# Cada execução usa uma pasta nova e preserva os artefatos anteriores.
New-Item -ItemType Directory -Path $installerDistDir -Force | Out-Null
New-Item -ItemType Directory -Path $resourcesDir -Force | Out-Null

# 1 e 2. Publicar cada componente (win-x64) e compactar em dock.zip / finder.zip (embutidos no instalador)
$componentes = @(
    @{ Nome = 'GoatDock';   Zip = 'dock.zip' },
    @{ Nome = 'GoatFinder'; Zip = 'finder.zip' }
)
$passo = 1
foreach ($c in $componentes) {
    Write-Host "`n[$passo/4] Publicando $($c.Nome) para win-x64 e gerando $($c.Zip)..." -ForegroundColor Yellow
    $proj = Join-Path $rootDir "src\$($c.Nome)\$($c.Nome).csproj"
    $out = Join-Path $distDir $c.Nome
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    dotnet publish $proj -c Release -r win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --self-contained false -o $out @signProperties
    if ($LASTEXITCODE -ne 0) { Write-Error "Falha ao publicar $($c.Nome)." }

    Assinar-Publicacao $out

    $zip = Join-Path $resourcesDir $c.Zip
    if (Test-Path $zip) { Remove-Item -Path $zip -Force }
    Compress-Archive -Path "$out\*" -DestinationPath $zip -CompressionLevel Optimal
    $passo++
}
# 3. Publicar GoatDockFinder.Installer com dock.zip e finder.zip embutidos
Write-Host "`n[3/4] Compilando GoatDockFinder.Installer (Single-File)..." -ForegroundColor Yellow
dotnet publish $installerProj -c Release -r win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --self-contained true -o $installerDistDir @signProperties
if ($LASTEXITCODE -ne 0) {
    Write-Error "Falha ao compilar GoatDockFinder.Installer."
}

Assinar-Publicacao $installerDistDir

# 4. Mover executável final para dist/
$finalSetupExe = Join-Path $distDir "GoatDockFinder-Setup.exe"
Copy-Item (Join-Path $installerDistDir "GoatDockFinder-Setup.exe") $finalSetupExe -Force
$releaseDir = Join-Path $rootDir "release"
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
Copy-Item -LiteralPath $finalSetupExe -Destination (Join-Path $releaseDir "GoatDockFinder-Setup.exe") -Force
$finalSignature = Get-AuthenticodeSignature -LiteralPath $finalSetupExe
@{
    data = (Get-Date).ToString('o')
    instalador = 'release/GoatDockFinder-Setup.exe'
    tamanhoBytes = (Get-Item -LiteralPath $finalSetupExe).Length
    sha256 = (Get-FileHash -LiteralPath $finalSetupExe -Algorithm SHA256).Hash
    assinaturaSolicitada = [bool]$Assinar
    assinaturaStatus = $finalSignature.Status.ToString()
    instalacaoManualExecutada = $false
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $rootDir 'docs/installer-validation.json') -Encoding UTF8

Write-Host "`n[4/4] Instalador gerado com sucesso!" -ForegroundColor Green
$setupSize = (Get-Item $finalSetupExe).Length / 1MB
Write-Host "Local: $finalSetupExe" -ForegroundColor White
Write-Host ("Tamanho do Instalador: {0:N2} MB" -f $setupSize) -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan


