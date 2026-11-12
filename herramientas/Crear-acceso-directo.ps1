# Ejecutar desde la carpeta extraída del Lanzador.
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Nombre = 'Lanzador Switch',
    [string]$Icono
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Nombre) -or $Nombre.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {
    throw 'El nombre del acceso directo debe ser un nombre de archivo de Windows.'
}

$ejecutable = Get-Item -LiteralPath (Join-Path $PSScriptRoot 'Lanzador.exe')
$rutaIcono = $ejecutable.FullName
if ($Icono) {
    $archivoIcono = Get-Item -LiteralPath $Icono
    if ($archivoIcono.PSIsContainer -or $archivoIcono.Extension -ne '.ico') {
        throw 'Selecciona un archivo .ico para el acceso directo.'
    }
    $rutaIcono = $archivoIcono.FullName
}

$escritorio = [Environment]::GetFolderPath('DesktopDirectory')
if (!$escritorio) { throw 'No se encontró la carpeta del Escritorio.' }
$ruta = Join-Path $escritorio "$Nombre.lnk"
$shell = New-Object -ComObject WScript.Shell
$acceso = $shell.CreateShortcut($ruta)
if ((Test-Path -LiteralPath $ruta) -and [IO.Path]::GetFileName($acceso.TargetPath) -ne 'Lanzador.exe') {
    throw "Ya existe un acceso directo a otro programa con el nombre $Nombre."
}
$acceso.TargetPath = $ejecutable.FullName
$acceso.WorkingDirectory = $ejecutable.DirectoryName
$acceso.Arguments = ''
$acceso.IconLocation = "$rutaIcono,0"
$acceso.Save()
Get-Item -LiteralPath $ruta
