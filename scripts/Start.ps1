param([switch]$NoBrowser,[switch]$SkipBuild)
. "$PSScriptRoot/Common.ps1"
# Retira solo el proceso de la API anterior, identificado por PID y hora de inicio.
$pidFile=Join-Path $LocalDir 'api.json'
if (Test-Path $pidFile) {
    $saved=Get-Content $pidFile -Raw | ConvertFrom-Json
    $running=Get-Process -Id $saved.Id -ErrorAction SilentlyContinue
    if ($running -and $running.StartTime.ToUniversalTime().Ticks.ToString() -eq $saved.StartTicks) { Stop-Process -Id $saved.Id }
    Remove-Item -LiteralPath $pidFile
}
docker info --format '{{.ServerVersion}}' | Out-Null
Assert-Exit 'Docker Desktop: comprueba que esté iniciado'
if ($SkipBuild) { docker compose up -d --wait --wait-timeout 180 } else { docker compose up -d --build --wait --wait-timeout 180 }
Assert-Exit 'Inicio de contenedores'
if (!(Test-Path (Join-Path $LocalDir 'legacy-admin-imported'))) {
    $env:SQLCMDPASSWORD=$Settings.SqlPassword
    docker compose cp database/import-legacy-admin.sql sqlserver:/tmp/import-legacy-admin.sql
    Assert-Exit 'Preparación de migración del administrador'
    docker compose exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -i /tmp/import-legacy-admin.sql
    Assert-Exit 'Conservación del administrador anterior'
    'Importación completada' | Set-Content (Join-Path $LocalDir 'legacy-admin-imported')
}
$r=Invoke-WebRequest http://127.0.0.1:5080 -UseBasicParsing -TimeoutSec 15
if ($r.StatusCode -ne 200) { throw 'El frontend no responde.' }
Write-Host 'EduMonitor está listo: http://127.0.0.1:5080'
Write-Host 'Credenciales: abre Ver acceso.cmd o .local/ACCESO.txt'
if (!$NoBrowser) { Start-Process 'http://127.0.0.1:5080' }
