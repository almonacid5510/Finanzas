# ============================================================
#  Inserta un encabezado de autoria en los archivos fuente
#  del proyecto (.cs y .xaml). Idempotente: no duplica.
#  Autor de la firma: Juan Almonacid
# ============================================================

$root  = "c:\Users\juandalmonacidl\Documents\MisFinanzas\MisFinanzas\MisFinanzas"
$autor = "Juan Almonacid"
$anio  = "2026"
$proyecto = "Mis Finanzas"
$marca = "Autoria:" # texto que usamos para detectar si ya existe el encabezado

$archivos = Get-ChildItem $root -Recurse -Include *.cs, *.xaml -File |
    Where-Object { $_.FullName -notmatch '\\(obj|bin|publish)\\' -and $_.Name -notmatch '\.g\.cs$|\.g\.i\.cs$' }

$modificados = 0
foreach ($f in $archivos) {
    $contenido = Get-Content -LiteralPath $f.FullName -Raw

    # Si ya tiene la firma, lo saltamos (idempotente)
    if ($contenido -match [regex]::Escape($marca)) { continue }

    if ($f.Extension -eq ".cs") {
        $header = @"
// ============================================================
//  Proyecto: $proyecto
//  $marca $autor
//  Copyright (c) $anio $autor. Todos los derechos reservados.
// ============================================================

"@
        $nuevo = $header + $contenido
    }
    else {
        # .xaml -> comentario XML. Seguro porque estos archivos no llevan <?xml ?>.
        $header = @"
<!--
    Proyecto: $proyecto
    $marca $autor
    Copyright (c) $anio $autor. Todos los derechos reservados.
-->
"@
        $nuevo = $header + "`r`n" + $contenido
    }

    Set-Content -LiteralPath $f.FullName -Value $nuevo -NoNewline -Encoding UTF8
    $modificados++
    Write-Host "OK  $($f.FullName.Replace($root+'\',''))"
}

Write-Host ""
Write-Host "Archivos con encabezado agregado: $modificados"
