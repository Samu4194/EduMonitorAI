param([switch]$StopDatabase)
. "$PSScriptRoot/Common.ps1"
docker compose stop
Assert-Exit 'Detención de EduMonitor'
Write-Host 'Contenedores detenidos. Las bases de datos se conservan.'
