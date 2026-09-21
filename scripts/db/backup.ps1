param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "../../backups"),
    [int]$RetentionDays = 14
)
$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$envFile = Join-Path $projectRoot ".env"
$settings = @{}
Get-Content $envFile | Where-Object { $_ -match "^[A-Z0-9_]+=" } | ForEach-Object {
    $key, $value = $_.Split("=", 2)
    $settings[$key] = $value
}
$user = if ($settings.POSTGRES_USER) { $settings.POSTGRES_USER } else { "kekebeauty" }
$database = if ($settings.POSTGRES_DB) { $settings.POSTGRES_DB } else { "kekebeautyDb" }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$destination = Join-Path $OutputDirectory "kekebeauty-$timestamp.dump"
$process = Start-Process docker -ArgumentList @("exec", "kekebeauty-postgres", "pg_dump", "-U", $user, "-d", $database, "-Fc") -NoNewWindow -Wait -PassThru -RedirectStandardOutput $destination
if ($process.ExitCode -ne 0 -or !(Test-Path $destination) -or (Get-Item $destination).Length -eq 0) {
    throw "La sauvegarde PostgreSQL a échoué."
}
Get-ChildItem $OutputDirectory -Filter "kekebeauty-*.dump" -File |
    Where-Object LastWriteTimeUtc -lt (Get-Date).ToUniversalTime().AddDays(-$RetentionDays) |
    Remove-Item -Force
Write-Output $destination