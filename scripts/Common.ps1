$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path $PSScriptRoot -Parent
Set-Location $ProjectRoot
$env:DOTNET_ROOT = Join-Path $ProjectRoot '.dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_HOME = Join-Path $ProjectRoot '.cli'
$env:NUGET_PACKAGES = Join-Path $ProjectRoot '.nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$Dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
if (!(Test-Path $Dotnet)) { $installed = Get-Command dotnet -ErrorAction SilentlyContinue; if ($installed) { $Dotnet=$installed.Source } }
$LocalDir = Join-Path $ProjectRoot '.local'
New-Item -ItemType Directory -Force $LocalDir | Out-Null
$SettingsPath = Join-Path $LocalDir 'settings.json'
function New-Secret { $bytes=New-Object byte[] 32; $rng=[System.Security.Cryptography.RandomNumberGenerator]::Create(); $rng.GetBytes($bytes); $rng.Dispose(); return [Convert]::ToBase64String($bytes) }
if (!(Test-Path $SettingsPath)) {
    $settings = @{ JwtKey=(New-Secret); SqlPassword=('Sql9!'+(New-Secret)); AdminEmail='admin@edumonitor.local'; AdminPassword=('Edu9!'+(New-Secret).Substring(0,18)) }
    $settings | ConvertTo-Json | Set-Content -LiteralPath $SettingsPath -Encoding UTF8
    @('EduMonitor AI - acceso local', "Correo: $($settings.AdminEmail)", "Contraseña: $($settings.AdminPassword)", '', 'Conserva este archivo en privado.') | Set-Content -LiteralPath (Join-Path $LocalDir 'ACCESO.txt') -Encoding UTF8
}
$Settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
foreach ($name in @('UsuariosPassword','MateriasPassword','EstudiantesPassword')) {
    if (!$Settings.PSObject.Properties[$name]) { $Settings | Add-Member -NotePropertyName $name -NotePropertyValue ('Db9!'+(New-Secret)) }
}
$Settings | ConvertTo-Json | Set-Content -LiteralPath $SettingsPath -Encoding UTF8
$env:Jwt__Key=$Settings.JwtKey
$env:Seed__AdminEmail=$Settings.AdminEmail
$env:Seed__AdminPassword=$Settings.AdminPassword
$env:MSSQL_SA_PASSWORD=$Settings.SqlPassword
$env:JWT_KEY=$Settings.JwtKey
$env:ADMIN_EMAIL=$Settings.AdminEmail
$env:ADMIN_PASSWORD=$Settings.AdminPassword
$env:USUARIOS_PASSWORD=$Settings.UsuariosPassword
$env:MATERIAS_PASSWORD=$Settings.MateriasPassword
$env:ESTUDIANTES_PASSWORD=$Settings.EstudiantesPassword
$env:ConnectionStrings__Default="Server=127.0.0.1,14333;Database=EduMonitor_Usuarios;User Id=sa;Password=$($Settings.SqlPassword);TrustServerCertificate=True;Encrypt=True"
$lines=@(
    "MSSQL_SA_PASSWORD=$($Settings.SqlPassword)"
    "JWT_KEY=$($Settings.JwtKey)"
    "ADMIN_EMAIL=$($Settings.AdminEmail)"
    "ADMIN_PASSWORD=$($Settings.AdminPassword)"
    "USUARIOS_PASSWORD=$($Settings.UsuariosPassword)"
    "MATERIAS_PASSWORD=$($Settings.MateriasPassword)"
    "ESTUDIANTES_PASSWORD=$($Settings.EstudiantesPassword)"
)
[System.IO.File]::WriteAllLines((Join-Path $ProjectRoot '.env'),$lines,(New-Object System.Text.UTF8Encoding $false))
function Assert-Exit([string]$Step) { if ($LASTEXITCODE -ne 0) { throw "$Step falló (código $LASTEXITCODE)." } }
