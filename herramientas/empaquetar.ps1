# Empaqueta una versión del lanzador: ZIP portable autocontenido (no necesita .NET instalado) y SHA256SUMS.txt.
# Uso: pwsh herramientas\empaquetar.ps1  →  publicar\LanzadorSwitch-<versión>-win-x64.zip
# Conviene ejecutarlo sobre un árbol limpio (git status sin cambios), para que el paquete sea el commit publicado.
#
# Por fases, para firmar en medio (lo hace .github\workflows\compilar.yml):
#   -Fase Publicar   compila en publicar\Lanzador
#   -Fase Comprimir  arma el ZIP y SHA256SUMS.txt con lo que haya en publicar\Lanzador (ya firmado)
#   -Fase Todo       las dos seguidas, sin firmar (lo predeterminado)
param(
    [ValidateSet('Todo', 'Publicar', 'Comprimir')][string]$Fase = 'Todo',
    [string]$Salida = (Join-Path $PSScriptRoot '..\publicar')
)

$ErrorActionPreference = 'Stop'
$raiz = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$proyecto = Join-Path $raiz 'src\Lanzador.App\Lanzador.App.csproj'
$version = ([xml](Get-Content $proyecto -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No encuentro <Version> en $proyecto" }

$nombre = "LanzadorSwitch-$version-win-x64"
$Salida = [IO.Path]::GetFullPath($Salida)
$carpeta = Join-Path $Salida 'Lanzador'
$zip = Join-Path $Salida "$nombre.zip"
$sumas = Join-Path $Salida 'SHA256SUMS.txt'

# En GitHub Actions, la versión y el nombre del paquete quedan como salidas del paso.
if ($env:GITHUB_OUTPUT) { "version=$version`nnombre=$nombre" | Add-Content $env:GITHUB_OUTPUT }

if ($Fase -in 'Todo', 'Publicar') {
    if (Test-Path $Salida) { Remove-Item $Salida -Recurse -Force }
    dotnet publish $proyecto -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none -o $carpeta
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló ($LASTEXITCODE)" }
    Copy-Item (Join-Path $raiz 'LICENSE'), (Join-Path $raiz 'THIRD-PARTY-NOTICES.md'), (Join-Path $raiz 'LEEME.md') $carpeta
}

if ($Fase -in 'Todo', 'Comprimir') {
    if (-not (Test-Path (Join-Path $carpeta 'Lanzador.exe'))) { throw "No está $carpeta\Lanzador.exe: ejecuta antes -Fase Publicar" }
    Remove-Item $zip, $sumas -ErrorAction SilentlyContinue
    # El ZIP lleva la carpeta Lanzador: al descomprimirlo en C:\Switch queda C:\Switch\Lanzador\Lanzador.exe,
    # y el lanzador busca y propone los emuladores en C:\Switch.
    Compress-Archive -Path $carpeta -DestinationPath $zip -CompressionLevel Optimal
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($sumas, "$hash  $nombre.zip`n")
}

Get-ChildItem $carpeta -Recurse -File | ForEach-Object { '  {0,12:N0}  {1}' -f $_.Length, $_.FullName.Substring($carpeta.Length + 1) }
if (Test-Path $zip) {
    "$zip ({0:N1} MB)" -f ((Get-Item $zip).Length / 1MB)
    "sha256 $hash"
}
