param([switch]$NoBrowser)
# Levanta EduMonitor con SQL Server instalado en Windows (compose.express.yaml).
# Comprueba Docker y SQL Server, crea las bases si faltan y arranca los seis contenedores.
. "$PSScriptRoot/Common.ps1"
$compose = 'compose.express.yaml'
$sqlService = 'MSSQLSERVER'
$databases = 'EduMonitor_Usuarios', 'EduMonitor_Materias', 'EduMonitor_Estudiantes', 'EduMonitor_Evaluaciones', 'EduMonitor_Seguimiento'

function Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

# 1. Docker Desktop
# Con Docker apagado, "docker info" escribe un error; con ErrorActionPreference=Stop eso cortaba el script. Se prueba sin detenerse.
function Test-Docker {
    $previo = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { docker info --format '{{.ServerVersion}}' 2>$null | Out-Null; return ($LASTEXITCODE -eq 0) }
    finally { $ErrorActionPreference = $previo }
}
Step 'Comprobando Docker Desktop'
if (!(Test-Docker)) {
    $exe = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
    if (!(Test-Path $exe)) { throw 'Docker Desktop no esta instalado.' }
    Write-Host 'Iniciando Docker Desktop, espera un momento...'
    Start-Process $exe
    $deadline = (Get-Date).AddMinutes(3)
    do { Start-Sleep 5 } until ((Test-Docker) -or (Get-Date) -gt $deadline)
    if (!(Test-Docker)) { throw 'Docker Desktop no estuvo listo en 3 minutos.' }
}

# 2. SQL Server local
Step "Comprobando SQL Server ($sqlService)"
$svc = Get-Service $sqlService -ErrorAction SilentlyContinue
if (!$svc) { throw "No existe el servicio $sqlService. Instala SQL Server, o borra .local/usar-express.txt para usar la base en Docker (lo normal)." }
if ($svc.Status -ne 'Running') {
    try { Start-Service $sqlService; $svc.WaitForStatus('Running', '00:01:00') }
    catch { throw "No se pudo iniciar $sqlService. Abre PowerShell como administrador y ejecuta: Start-Service $sqlService" }
}
if (!(Get-NetTCPConnection -LocalPort 1433 -State Listen -ErrorAction SilentlyContinue)) {
    throw 'SQL Server no escucha en el puerto 1433. Activa TCP/IP y fija el puerto 1433 en SQL Server Configuration Manager, y reinicia el servicio.'
}

# 3. Bases de datos y logins (el script es idempotente)
$sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
if ($sqlcmd) {
    $missing = $false
    foreach ($db in $databases) {
        $r = sqlcmd -S . -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name='$db'" 2>$null
        if (($r | Select-Object -First 1) -ne '1') { $missing = $true }
    }
    if ($missing) {
        Step 'Creando bases y logins de EduMonitor'
        $initFile = Join-Path $LocalDir 'initialize-express.sql'
        $sql = Get-Content (Join-Path $ProjectRoot 'database/initialize.sql') -Raw
        $sql = $sql.Replace('$(USUARIOS_PASSWORD)', $Settings.UsuariosPassword).Replace('$(MATERIAS_PASSWORD)', $Settings.MateriasPassword).Replace('$(ESTUDIANTES_PASSWORD)', $Settings.EstudiantesPassword).Replace('$(EVALUACIONES_PASSWORD)', $Settings.EvaluacionesPassword).Replace('$(SEGUIMIENTO_PASSWORD)', $Settings.SeguimientoPassword)
        $sql | Set-Content -LiteralPath $initFile -Encoding UTF8
        sqlcmd -S . -E -C -b -i $initFile | Out-Null
        Assert-Exit 'Creacion de las bases de datos'
    }
} else {
    Write-Host 'Aviso: sqlcmd no esta disponible; no se comprobaron las bases de datos.' -ForegroundColor Yellow
}

# 4. Contenedor SQL de la instalacion anterior (si quedo encendido, ocupa recursos y no se usa)
$old = docker ps --filter 'name=edumonitorai-sqlserver' --format '{{.Names}}'
if ($old) { Step 'Deteniendo el SQL Server de Docker (no se usa en esta configuracion)'; docker stop $old | Out-Null }

# 5. Contenedores
Step 'Iniciando contenedores (la primera vez puede tardar varios minutos)'
docker compose -f $compose up -d --build --wait --wait-timeout 180
Assert-Exit 'Inicio de contenedores'

# 6. Verificacion
$r = Invoke-WebRequest http://127.0.0.1:5080 -UseBasicParsing -TimeoutSec 15
if ($r.StatusCode -ne 200) { throw 'El frontend no responde.' }
docker compose -f $compose ps
Write-Host ''
Write-Host 'EduMonitor esta listo: http://127.0.0.1:5080' -ForegroundColor Green
Write-Host 'Credenciales: abre "Ver acceso.cmd" o .local/ACCESO.txt'
if (!$NoBrowser) { Start-Process 'http://127.0.0.1:5080' }
