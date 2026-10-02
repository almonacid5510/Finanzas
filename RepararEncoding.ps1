# Repara mojibake (UTF-8 reinterpretado como 1252 y reescrito como UTF-8).
# Estrategia: texto actual (UTF-8) -> bytes 1252 -> decodificar UTF-8 = original.
# Deteccion sin literales acentuados: buscamos el char U+00C3 ('A tilde') que
# es el marcador tipico del mojibake de tildes/enie en espanol.

$root = "c:\Users\juandalmonacidl\Documents\MisFinanzas\MisFinanzas\MisFinanzas"

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$latin1    = [System.Text.Encoding]::GetEncoding(1252)
$marcador  = [char]0x00C3   # 'A' con tilde -> aparece en todo mojibake de tildes

$archivos = Get-ChildItem $root -Recurse -Include *.cs, *.xaml -File |
    Where-Object { $_.FullName -notmatch '\\(obj|bin|publish)\\' -and $_.Name -notmatch '\.g\.cs$|\.g\.i\.cs$' }

$reparados = 0
foreach ($f in $archivos) {
    $texto = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    if ($texto.IndexOf($marcador) -lt 0) { continue }

    $bytesLatin1 = $latin1.GetBytes($texto)
    $original    = [System.Text.Encoding]::UTF8.GetString($bytesLatin1)

    [System.IO.File]::WriteAllText($f.FullName, $original, $utf8NoBom)
    $reparados++
    Write-Host ("REPARADO  " + $f.Name)
}

Write-Host ""
Write-Host ("Archivos reparados: " + $reparados)
