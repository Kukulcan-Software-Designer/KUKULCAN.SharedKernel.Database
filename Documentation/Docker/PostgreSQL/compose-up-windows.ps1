# KUKULCAN.SharedKernel.Database Docker Compose deployment helper for Windows PowerShell.
# Run from Documentation\Docker\PostgreSQL or set KUKULCAN_POSTGRES_ENV_FILE.

$ErrorActionPreference = 'Stop'

function Fail([string]$Message, [int]$Code = 1) {
    Write-Host "[ERROR] $Message" -ForegroundColor Red
    exit $Code
}

function EnvValue([string]$Name, [string]$DefaultValue) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) { return $DefaultValue }
    return $value
}

function LoadDotEnv([string]$Path) {
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith('#')) { continue }
        $separator = $trimmed.IndexOf('=')
        if ($separator -lt 1) { continue }
        $values[$trimmed.Substring(0, $separator).Trim()] = $trimmed.Substring($separator + 1).Trim()
    }
    return $values
}

function ContainerExists([string]$Name) {
    & docker container inspect $Name *> $null
    return $LASTEXITCODE -eq 0
}

function ContainerRunning([string]$Name) {
    return ((& docker inspect $Name -f '{{.State.Running}}').Trim() -eq 'true')
}

function ContainerEnvValue([string]$Name, [string]$Key, [string]$DefaultValue) {
    $lines = @(& docker inspect $Name -f '{{range .Config.Env}}{{println .}}{{end}}')
    foreach ($line in $lines) {
        $separator = $line.IndexOf('=')
        if ($separator -gt 0 -and $line.Substring(0, $separator) -eq $Key) {
            return $line.Substring($separator + 1)
        }
    }
    return $DefaultValue
}

function ContainerOnNetwork([string]$Network, [string]$Container) {
    $names = @(& docker network inspect $Network --format '{{range .Containers}}{{.Name}}{{"\n"}}{{end}}')
    return $names -contains $Container
}

function EnsureNetwork() {
    & docker network inspect $script:NetworkName *> $null
    if ($LASTEXITCODE -eq 0) { return }
    Write-Host "[INFO] Creating Docker network $script:NetworkName."
    & docker network create $script:NetworkName *> $null
    if ($LASTEXITCODE -ne 0) { Fail "Docker network creation failed (exit code $LASTEXITCODE)." $LASTEXITCODE }
}

function EnsureNetworkMembership([string]$Container) {
    if (ContainerOnNetwork $script:NetworkName $Container) { return }
    Write-Host "[INFO] Connecting $Container to $script:NetworkName."
    & docker network connect $script:NetworkName $Container
    if ($LASTEXITCODE -ne 0) { Fail "Docker network connection failed (exit code $LASTEXITCODE)." $LASTEXITCODE }
}

function EnsureService([string]$Service, [string]$Container) {
    if (ContainerExists $Container) {
        EnsureNetworkMembership $Container
        if (ContainerRunning $Container) {
            Write-Host "[INFO] Using existing running container $Container."
        }
        else {
            Write-Host "[INFO] Starting existing container $Container."
            & docker start $Container *> $null
            if ($LASTEXITCODE -ne 0) { Fail "Failed to start container $Container (exit code $LASTEXITCODE)." $LASTEXITCODE }
        }
        return
    }

    Write-Host "[INFO] Container $Container does not exist; creating service $Service with Compose."
    & docker compose --env-file $script:EnvFile -f $script:ComposeFile up -d --no-deps $Service
    if ($LASTEXITCODE -ne 0) { Fail "Docker Compose failed to create service $Service (exit code $LASTEXITCODE)." $LASTEXITCODE }
}

function New-TemporaryJwtSecret() {
    $bytes = New-Object byte[] 48
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return [Convert]::ToBase64String($bytes)
}

function EnsureI18nService() {
    if (ContainerExists $script:I18nContainer) {
        EnsureService 'kukulcan-i18n' $script:I18nContainer
        return
    }

    if ([string]::IsNullOrWhiteSpace($env:KUKULCAN_I18N_JWT_SECRET)) {
        $env:KUKULCAN_I18N_JWT_SECRET = New-TemporaryJwtSecret
        Write-Host '[INFO] Generated a temporary local JWT secret for kukulcan-i18n.'
    }

    EnsureService 'kukulcan-i18n' $script:I18nContainer
}

function WaitForPostgreSql() {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        & docker exec $script:PostgresContainer pg_isready -U $script:PostgresUser -d $script:PostgresDb *> $null
        if ($LASTEXITCODE -eq 0) {
            Write-Host '[INFO] PostgreSQL is ready.'
            return
        }
        Start-Sleep -Seconds 2
    }

    & docker logs $script:PostgresContainer 2>&1
    Fail 'PostgreSQL did not become ready.'
}

function WaitForHttp([string]$Name, [string]$Url) {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            Invoke-WebRequest -Uri $Url -Method Get -UseBasicParsing -TimeoutSec 2 | Out-Null
            Write-Host "[INFO] $Name is UP."
            return
        }
        catch { Start-Sleep -Seconds 2 }
    }

    & docker logs $script:I18nContainer 2>&1
    Fail "$Name did not become available."
}

$script:ScriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:ComposeFile = Join-Path $script:ScriptDirectory 'compose.yml'
$script:EnvFile = EnvValue 'KUKULCAN_POSTGRES_ENV_FILE' (Join-Path $script:ScriptDirectory '.env')

if (-not (Test-Path -LiteralPath $script:ComposeFile)) { Fail "compose.yml was not found under $script:ScriptDirectory." }
if (-not (Test-Path -LiteralPath $script:EnvFile)) { Fail '.env was not found. Create it from the local Docker configuration.' }
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { Fail 'Docker CLI is not available in PATH.' }

& docker info *> $null
if ($LASTEXITCODE -ne 0) { Fail 'Docker Engine/Desktop is not running or is not accessible.' }

$envValues = LoadDotEnv $script:EnvFile
$script:NetworkName = EnvValue 'KUKULCAN_I18N_NETWORK_NAME' 'kukulcan-local'
$script:I18nContainer = EnvValue 'KUKULCAN_I18N_CONTAINER_NAME' 'kukulcan-i18n'
$script:PostgresContainer = if ($envValues.ContainsKey('POSTGRES_HOST')) { $envValues['POSTGRES_HOST'] } else { 'mypostgres' }
$script:PostgresDb = if ($envValues.ContainsKey('POSTGRES_DB')) { $envValues['POSTGRES_DB'] } else { 'Atlas' }
$script:PostgresUser = if ($envValues.ContainsKey('POSTGRES_USER')) { $envValues['POSTGRES_USER'] } else { 'postgres' }
$script:HttpPort = EnvValue 'KUKULCAN_I18N_HTTP_PORT' '8080'

EnsureNetwork

if (ContainerExists $script:PostgresContainer) {
    $script:PostgresDb = ContainerEnvValue $script:PostgresContainer 'POSTGRES_DB' $script:PostgresDb
    $script:PostgresUser = ContainerEnvValue $script:PostgresContainer 'POSTGRES_USER' $script:PostgresUser
    $fallbackPassword = if ($envValues.ContainsKey('POSTGRES_PASSWORD')) { $envValues['POSTGRES_PASSWORD'] } else { '' }
    $postgresPassword = ContainerEnvValue $script:PostgresContainer 'POSTGRES_PASSWORD' $fallbackPassword
    $env:POSTGRES_HOST = $script:PostgresContainer
    $env:POSTGRES_DB = $script:PostgresDb
    $env:POSTGRES_USER = $script:PostgresUser
    $env:POSTGRES_PASSWORD = $postgresPassword
}

EnsureService 'mypostgres' $script:PostgresContainer
EnsureNetworkMembership $script:PostgresContainer
WaitForPostgreSql

EnsureI18nService
EnsureNetworkMembership $script:I18nContainer

WaitForHttp 'i18n liveness' "http://127.0.0.1:$script:HttpPort/health/live"
WaitForHttp 'i18n readiness' "http://127.0.0.1:$script:HttpPort/health/ready"

Write-Host '[100%] Docker Compose deployment is ready.'
Write-Host ("      PostgreSQL: {0} / {1}" -f $script:PostgresContainer, $script:PostgresDb)
Write-Host ("      i18n:       {0}" -f $script:I18nContainer)
Write-Host ("      API:        http://127.0.0.1:{0}" -f $script:HttpPort)
