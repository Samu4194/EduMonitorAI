param([switch]$OnlyFunctional)
. "$PSScriptRoot/Common.ps1"
try { Invoke-WebRequest http://127.0.0.1:5080/health -UseBasicParsing -TimeoutSec 5 | Out-Null }
catch { throw 'Inicia EduMonitor y espera que sus contenedores estén saludables antes de ejecutar las pruebas.' }
$env:EDUMONITOR_URL='http://127.0.0.1:5080'
$env:EDUMONITOR_ROOT=$ProjectRoot
$env:EDUMONITOR_TEST_SQL="Server=127.0.0.1,14333;Database=master;User Id=sa;Password=$($Settings.SqlPassword);TrustServerCertificate=True;Encrypt=True"
$testArgs=@('test','tests/EduMonitor.IntegrationTests','--logger','trx;LogFileName=microservices.trx','--results-directory','TestResults')
if ($OnlyFunctional) { $testArgs+=@('--filter','Category!=Availability') }
& $Dotnet @testArgs
Assert-Exit 'Pruebas de integración'
