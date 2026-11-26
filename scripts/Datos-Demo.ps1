# Datos de demostración para empezar a trabajar sin partir de cero: un docente de prueba asignado a los cursos de
# Software, nivel I, paralelo A (se crea el paralelo si falta). Es idempotente: puedes ejecutarlo varias veces.
# Los estudiantes y las notas NO se cargan aquí: salen de Moodle (ver datos-demo/LEEME.md).
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingPlainTextForPassword', '', Justification = 'Contraseña de demostración local.')]
param(
    [string]$Url = 'http://127.0.0.1:5080',
    [string]$DocenteEmail = 'docente.demo@edumonitor.local',
    [string]$DocentePassword = 'DemoDocente9!x'
)
. "$PSScriptRoot/Common.ps1"

function Api([string]$Metodo, [string]$Ruta, $Cuerpo = $null, [string]$Token = '') {
    $encabezados = @{}
    if ($Token) { $encabezados['Authorization'] = "Bearer $Token" }
    $p = @{ Uri = "$Url/api$Ruta"; Method = $Metodo; Headers = $encabezados; UseBasicParsing = $true }
    if ($null -ne $Cuerpo) {
        $p['Body'] = [Text.Encoding]::UTF8.GetBytes(($Cuerpo | ConvertTo-Json -Depth 6))
        $p['ContentType'] = 'application/json; charset=utf-8'
    }
    try { Invoke-RestMethod @p }
    catch {
        # Muestra el mensaje que devolvió el servidor, no solo «(409) Conflicto».
        $detalle = ''
        if ($_.Exception.Response) { $detalle = (New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() }
        throw "$Metodo $Ruta falló: $detalle"
    }
}
# Cédula ecuatoriana válida a partir de sus 9 primeros dígitos (módulo 10).
function Get-Cedula([string]$Nueve) {
    $suma = 0
    for ($i = 0; $i -lt 9; $i++) {
        $peso = if ($i % 2 -eq 0) { 2 } else { 1 }
        $producto = [int][string]$Nueve[$i] * $peso
        if ($producto -gt 9) { $producto -= 9 }
        $suma += $producto
    }
    return $Nueve + ((10 - ($suma % 10)) % 10)
}

try { Invoke-WebRequest "$Url/health" -UseBasicParsing -TimeoutSec 5 | Out-Null }
catch { throw 'EduMonitor no responde. Ejecuta primero Iniciar EduMonitor.cmd y espera a que esté listo.' }

Write-Host '==> Iniciando sesión como administrador' -ForegroundColor Cyan
$token = (Api 'POST' '/auth/login' @{ email = $Settings.AdminEmail; password = $Settings.AdminPassword }).token
if (!$token) { throw 'No se pudo iniciar sesión como administrador.' }

Write-Host '==> Docente de demostración' -ForegroundColor Cyan
# ForEach-Object { $_ } fuerza a PowerShell 5.1 a tratar la respuesta como una lista de objetos y no como un único arreglo.
$docente = Api 'GET' '/docentes' $null $token | ForEach-Object { $_ } | Where-Object { $_.email -eq $DocenteEmail } | Select-Object -First 1
if (!$docente) {
    $docente = Api 'POST' '/docentes' @{
        cedula = (Get-Cedula '180000001'); nombres = 'Docente'; apellidos = 'Demostración'; email = $DocenteEmail
        telefono = $null; tituloAcademico = 'Ingeniero'; carreras = @('SOF'); password = $DocentePassword
    } $token
    Write-Host "    Creado: $DocenteEmail"
} else { Write-Host "    Ya existía: $DocenteEmail" }

Write-Host '==> Paralelo A de Software, nivel I' -ForegroundColor Cyan
try { Api 'POST' '/paralelos' @{ carrera = 'SOF'; nivel = 1; letra = 'A'; malla = $null } $token | Out-Null; Write-Host '    Creado.' }
catch { Write-Host '    Ya existía (o no se pudo crear; se continúa).' }

Write-Host '==> Asignando el docente a sus cursos' -ForegroundColor Cyan
$cursos = Api 'GET' '/cursos' $null $token | ForEach-Object { $_ } | Where-Object { $_.paralelo -eq 'A' -and $_.materiaCodigo -like 'SOF-1*' }
foreach ($c in $cursos) {
    Api 'PUT' "/cursos/$($c.id)/docente" @{ teacherId = $docente.id } $token | Out-Null
    Write-Host "    $($c.materiaCodigo) $($c.materiaNombre)"
}

Write-Host ''
Write-Host 'Listo.' -ForegroundColor Green
Write-Host "  Docente: $DocenteEmail   Contraseña: $DocentePassword  (cámbiala si lo vas a compartir)"
Write-Host '  Siguiente paso: cargar estudiantes y notas desde Moodle. Ver datos-demo/LEEME.md'
