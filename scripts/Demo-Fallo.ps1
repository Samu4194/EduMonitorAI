param([Parameter(Mandatory=$true)][ValidateSet('stop','start','status')][string]$Action)
. "$PSScriptRoot/Common.ps1"
if ($Action -eq 'status') { docker compose ps } else { docker compose $Action estudiantes }
Assert-Exit 'Demostración de fallo'
