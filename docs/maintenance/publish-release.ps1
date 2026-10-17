# ==============================================================================
# publish-release.ps1 - Crea un release en GitHub y sube el .exe del updater
# ==============================================================================
# Uso:
#   powershell -NoProfile -ExecutionPolicy Bypass -File publish-release.ps1 `
#     -Version 1.1.5 `
#     -Repo "williamconejo67-ai/ArdillaUpdaterIA" `
#     -ExePath "...\publish\updater-publish-1.1.5\ArdillaUpdaterIA.exe"
#
#   Para el clon:
#     -Repo "williamconejo67-ai/ArdillaUpdater" -ExePath "...\ArdillaUpdater.exe"
#
# Requisitos:
#   - Token GITHUB_TOKEN en el .env del uploader (ArdillaUpdaterClon\ardilla-updater-uploader\.env)
#   - El nombre del asset debe coincidir con AppConfig.UpdaterAssetName de la app:
#       IA   -> ArdillaUpdaterIA.exe
#       Clon -> ArdillaUpdater.exe
#   (el script usa el nombre real del archivo en -ExePath, así que solo hay que
#    pasar el .exe correcto)
# ==============================================================================

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Repo,
    [Parameter(Mandatory = $true)][string]$ExePath,
    [string]$Notes = ''
)

$ErrorActionPreference = 'Stop'

# ---- 1) Token (nunca se imprime) --------------------------------------------
$envPath = 'E:\Respaldo 7.10.2026\Ardilla ia\ArdillaUpdaterClon\ardilla-updater-uploader\.env'
if (-not (Test-Path $envPath)) { throw "No se encontró el .env con el token: $envPath" }
$token = (((Get-Content $envPath) | Where-Object { $_ -like 'GITHUB_TOKEN=*' }) -replace '^GITHUB_TOKEN=', '').Trim()
if ([string]::IsNullOrWhiteSpace($token)) { throw "GITHUB_TOKEN vacío en $envPath" }

# ---- 2) Parámetros -----------------------------------------------------------
if (-not (Test-Path $ExePath)) { throw "No existe el ejecutable: $ExePath" }
$assetName = (Get-Item $ExePath).Name
$tag = "v$Version"
$exeItem = Get-Item $ExePath
$localSha = (Get-FileHash -Path $ExePath -Algorithm SHA256).Hash.ToLower()

$headers = @{
    Authorization          = "Bearer $token"
    'X-GitHub-Api-Version' = '2022-11-28'
    Accept                 = 'application/vnd.github+json'
    'User-Agent'           = 'ArdillaUpdater-ReleaseScript'
}

# Notas por defecto si no se pasan (texto genérico; se pueden sobreescribir con -Notes)
if ([string]::IsNullOrWhiteSpace($Notes)) {
    $Notes = "Actualización automática $Version`n`nAsset: $assetName`nSHA256: $localSha"
}

# ---- 3) Verificar que el tag local existe (recomendado) ----------------------
try {
    $localTag = git tag -l $tag
    if (-not $localTag) { Write-Output "AVISO: el tag $tag no existe localmente; se creará apuntando al commit actual del remoto." }
}
catch { }

# ---- 4) Crear release (si ya existe, informar y salir) -----------------------
try {
    $release = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$Repo/releases" `
        -Headers $headers `
        -Body (@{ tag_name = $tag; name = $Version; body = $Notes; draft = $false; prerelease = $false } | ConvertTo-Json -Depth 5) `
        -ContentType 'application/json'
}
catch {
    if ($_.Exception.Response.StatusCode.value__ -eq 422) {
        Write-Output "EL_RELEASE_YA_EXISTE: $tag en $Repo"
        exit 0
    }
    throw
}

Write-Output "RELEASE_ID=$($release.id)"
Write-Output "RELEASE_URL=$($release.html_url)"

# ---- 5) Subir el asset -------------------------------------------------------
$uploadUrl = ($release.upload_url -replace '\{.*', '')
$bytes = [System.IO.File]::ReadAllBytes($ExePath)
$asset = Invoke-RestMethod -Method Post -Uri "$uploadUrl`?name=$assetName" `
    -Headers $headers -Body $bytes -ContentType 'application/x-msdownload'

Write-Output "ASSET_ID=$($asset.id)"
Write-Output "ASSET_NAME=$($asset.name)"
Write-Output "ASSET_SIZE=$($asset.size)"
Write-Output "LOCAL_SIZE=$($exeItem.Length)"
Write-Output "LOCAL_SHA256=$localSha"
Write-Output "ASSET_URL=$($asset.browser_download_url)"

# ---- 6) Comprobación rápida de tamaño ---------------------------------------
if ($asset.size -eq $exeItem.Length) {
    Write-Output "TAMANO_OK=True"
}
else {
    Write-Output "TAMANO_OK=False (revisar la subida)"
    exit 1
}
