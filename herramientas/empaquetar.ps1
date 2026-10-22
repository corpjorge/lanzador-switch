# Empaqueta una versión del lanzador: ZIP portable autocontenido (no necesita .NET instalado) y SHA256SUMS.txt.
# Uso: pwsh herramientas\empaquetar.ps1  →  publicar\LanzadorSwitch-<versión>-win-x64.zip
# Conviene ejecutarlo sobre un árbol limpio (git status sin cambios), para que el paquete sea el commit publicado.
param([string]$Salida = (Join-Path $PSScriptRoot '..\publicar'))

$ErrorActionPreference = 'Stop'
$raiz = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$proyecto = Join-Path $raiz 'src\Lanzador.App\Lanzador.App.csproj'
$version = ([xml](Get-Content $proyecto -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No encuentro <Version> en $proyecto" }

$nombre = "LanzadorSwitch-$version-win-x64"
$Salida = [IO.Path]::GetFullPath($Salida)
$carpeta = Join-Path $Salida 'Lanzador'
if (Test-Path $Salida) { Remove-Item $Salida -Recurse -Force }

dotnet publish $proyecto -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o $carpeta
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló ($LASTEXITCODE)" }
Copy-Item (Join-Path $raiz 'LICENSE'), (Join-Path $raiz 'THIRD-PARTY-NOTICES.md'), (Join-Path $raiz 'LEEME.md') $carpeta

# El ZIP lleva la carpeta Lanzador: al descomprimirlo en C:\Switch queda C:\Switch\Lanzador\Lanzador.exe,
# y el lanzador busca y propone los emuladores en C:\Switch.
$zip = Join-Path $Salida "$nombre.zip"
Compress-Archive -Path $carpeta -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $Salida 'SHA256SUMS.txt'), "$hash  $nombre.zip`n")

Get-ChildItem $carpeta -Recurse -File | ForEach-Object { '  {0,12:N0}  {1}' -f $_.Length, $_.FullName.Substring($carpeta.Length + 1) }
"$zip ({0:N1} MB)" -f ((Get-Item $zip).Length / 1MB)
"sha256 $hash"
