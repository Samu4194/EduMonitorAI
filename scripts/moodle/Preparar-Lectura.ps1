param([string]$MoodleLocal = (Join-Path $PSScriptRoot '../../../MoodleLocal'))
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$docker = (Get-Command docker.exe -ErrorAction SilentlyContinue).Source
if (!$docker) { $docker = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe' }
$OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$token = Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot 'preparar-lectura.php') |
    & $docker compose -p moodlelocal -f (Join-Path $MoodleLocal 'compose.yaml') exec -T -u www-data web php
if ($LASTEXITCODE -ne 0 -or "$token" -notmatch '^[0-9a-fA-F]{32,128}$') { throw 'No se pudo preparar el servicio REST de lectura.' }
$dir = Join-Path $root '.local'
[IO.Directory]::CreateDirectory($dir) | Out-Null
[IO.File]::WriteAllText((Join-Path $dir 'moodle.env'), "Moodle__Token=$token", $OutputEncoding)
Write-Host 'Token de lectura configurado en .local/moodle.env.'
