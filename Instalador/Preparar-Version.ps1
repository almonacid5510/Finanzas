param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = "1.9.0",
    [string]$Notas = "$PSScriptRoot/Novedades-1.9.0.md",
    [ValidatePattern('^$|^[A-Fa-f0-9]{40}$')][string]$HuellaCertificado = "",
    [switch]$Publicar,
    [switch]$SoloCelular
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
function Ejecutar([string]$Programa, [string[]]$Parametros) {
    & $Programa @Parametros
    if ($LASTEXITCODE -ne 0) { throw "$Programa terminó con código $LASTEXITCODE" }
}
Push-Location $repo
try {
    if (-not (Test-Path -LiteralPath $Notas)) { throw "No existe el archivo de novedades: $Notas" }
    $notasTexto = Get-Content -LiteralPath $Notas -Raw -Encoding UTF8
    # Una sola versión para ensamblado, interfaz del celular y metadatos del worker.
    $proyecto = Join-Path $repo "MisFinanzas/MisFinanzas.csproj"
    $xml = Get-Content -LiteralPath $proyecto -Raw -Encoding UTF8
    $xml = [regex]::Replace($xml, '<Version>.*?</Version>', "<Version>$Version</Version>")
    $xml = [regex]::Replace($xml, '<FileVersion>.*?</FileVersion>', "<FileVersion>$Version.0</FileVersion>")
    $xml = [regex]::Replace($xml, '<AssemblyVersion>.*?</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>")
    Set-Content -LiteralPath $proyecto -Value $xml -Encoding UTF8
    $versionMovil = Join-Path $repo "Celular/app/src/app/nucleo/version.ts"
    Set-Content -LiteralPath $versionMovil -Value "export const VERSION_APP = '$Version';" -Encoding UTF8
    $workerRuta = Join-Path $repo "Celular/app/ngsw-config.json"
    $worker = Get-Content -LiteralPath $workerRuta -Raw -Encoding UTF8 | ConvertFrom-Json
    $worker.appData.version = $Version
    $worker.appData.novedades = $notasTexto
    $worker | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $workerRuta -Encoding UTF8

    if (-not $SoloCelular) {
        Ejecutar "dotnet" @("tool", "restore")
        $compilado = Join-Path $repo "MisFinanzas/publish/$Version"
        $salida = Join-Path $repo "Instalador/Salida/Actualizaciones/$Version"
        if (Test-Path -LiteralPath $salida) { throw "Ya hay un paquete para $Version. Usa una versión nueva para no sustituir un lanzamiento existente." }
        Ejecutar "dotnet" @("publish", $proyecto, "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-o", $compilado)
        $parametros = @("vpk", "pack", "--packId", "MisFinanzas", "--packTitle", "Mis Finanzas", "--packAuthors", "Juan Almonacid", "--packVersion", $Version, "--runtime", "win-x64", "--channel", "win", "--packDir", $compilado, "--mainExe", "MisFinanzas.exe", "--icon", (Join-Path $repo "MisFinanzas/Recursos/app.ico"), "--releaseNotes", (Resolve-Path -LiteralPath $Notas).Path, "--delta", "None", "--outputDir", $salida)
        if ($HuellaCertificado) { $parametros += @("--signParams", "/sha1 $HuellaCertificado /fd SHA256 /tr https://timestamp.digicert.com /td SHA256") }
        Ejecutar "dotnet" $parametros
        $feed = Join-Path $repo "Celular/app/public/actualizaciones/windows"
        New-Item -ItemType Directory -Force -Path $feed | Out-Null
        Get-ChildItem -LiteralPath $salida -File | Where-Object { $_.Extension -in @('.json','.nupkg','.exe') -or $_.Name -eq 'RELEASES' } | Copy-Item -Destination $feed -Force
        # El plan gratuito de Firebase no permite publicar .exe (firebase.json los ignora): el instalador se ofrece en .zip
        Compress-Archive -LiteralPath (Join-Path $feed "MisFinanzas-win-Setup.exe") -DestinationPath (Join-Path $feed "MisFinanzas-Instalador.zip") -Force
        Write-Host "Instalador y paquetes preparados en $salida"
    }
    Push-Location (Join-Path $repo "Celular/app")
    try { Ejecutar "npm.cmd" @("ci"); Ejecutar "npm.cmd" @("run", "test:caja"); Ejecutar "npm.cmd" @("run", "build") }
    finally { Pop-Location }
    Write-Host "Versión $Version preparada. No se publicó nada todavía."
    if ($Publicar) {
        $indicePc = Join-Path $repo "Celular/app/dist/mis-finanzas/browser/actualizaciones/windows/releases.win.json"
        if (-not (Test-Path -LiteralPath $indicePc)) { throw "Falta el canal de actualizaciones de PC en la salida. Prepara primero un lanzamiento completo para evitar retirarlo de hosting." }
        Push-Location (Join-Path $repo "Celular")
        try { Ejecutar "npx.cmd" @("--yes", "--package=firebase-tools@15.34.0", "firebase", "deploy", "--only", "hosting,firestore:rules", "--project", "mis-finanzas-4ea0b") }
        finally { Pop-Location }
    }
}
finally { Pop-Location }
