param(
    # Ollama: usa el Ollama instalado en Windows (aprovecha la tarjeta gráfica).
    # Docker: levanta Ollama en un contenedor (solo CPU, no instala nada en Windows).
    # Nube: guarda la clave gratuita de Google Gemini para usar IA en la nube.
    [ValidateSet('Ollama', 'Docker', 'Nube')][string]$Modo = 'Ollama',
    [string]$Modelo = 'qwen2.5:3b',
    [string]$Compose = 'compose.express.yaml'
)
# Prepara el agente académico (Sprint 3). Es opcional: sin IA el sistema redacta las recomendaciones con plantillas.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Common.ps1"
Set-Location $ProjectRoot

switch ($Modo) {
    'Ollama' {
        if (!(Get-Command ollama -ErrorAction SilentlyContinue)) {
            Write-Host 'No se encontró Ollama en este equipo.' -ForegroundColor Yellow
            Write-Host 'Descárgalo desde https://ollama.com/download, instálalo y vuelve a ejecutar este script.'
            Write-Host 'Otra opción sin instalar nada: .\scripts\Preparar-IA.ps1 -Modo Docker'
            exit 1
        }
        Write-Host "==> Descargando el modelo $Modelo (solo la primera vez)" -ForegroundColor Cyan
        & ollama pull $Modelo
        if ($LASTEXITCODE -ne 0) { throw "No se pudo descargar el modelo $Modelo." }
        # Variante para el chat del asistente: el mismo modelo con ventana de contexto de 8192 (Ollama usa 4096 por defecto).
        Write-Host '==> Creando la variante del chat (edumonitor-asistente)' -ForegroundColor Cyan
        & ollama create edumonitor-asistente -f (Join-Path $PSScriptRoot 'ia/Modelfile.asistente')
        if ($LASTEXITCODE -ne 0) { Write-Host 'No se pudo crear la variante del chat; el asistente usará el modelo normal.' -ForegroundColor Yellow }
    }
    'Docker' {
        Write-Host '==> Levantando Ollama en Docker' -ForegroundColor Cyan
        docker compose -f $Compose --profile ia-local up -d ollama
        Assert-Exit 'Levantar Ollama'
        Write-Host "==> Descargando el modelo $Modelo (solo la primera vez)" -ForegroundColor Cyan
        docker compose -f $Compose exec ollama ollama pull $Modelo
        Assert-Exit 'Descargar el modelo'
    }
    'Nube' {
        Write-Host 'Crea una clave gratuita en https://aistudio.google.com/apikey y pégala aquí (no se muestra al escribir).'
        $segura = Read-Host 'Clave de API de Gemini' -AsSecureString
        $clave = [System.Net.NetworkCredential]::new('', $segura).Password
        if ([string]::IsNullOrWhiteSpace($clave)) { throw 'No se escribió ninguna clave.' }
        New-Item -ItemType Directory -Force -Path (Join-Path $ProjectRoot '.local') | Out-Null
        # La carpeta .local está en .gitignore: la clave nunca se sube al repositorio.
        [System.IO.File]::WriteAllLines((Join-Path $ProjectRoot '.local/agente.env'), @("Agente__Nube__ApiKey=$clave"), (New-Object System.Text.UTF8Encoding $false))
        Write-Host '==> Reiniciando el servicio de seguimiento' -ForegroundColor Cyan
        docker compose -f $Compose up -d --force-recreate seguimiento
        Assert-Exit 'Reiniciar seguimiento'
    }
}
Write-Host 'Listo. Abre un curso, entra a «Seguimiento» y revisa qué motores de IA aparecen disponibles.' -ForegroundColor Green
