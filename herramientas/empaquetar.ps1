# Empaqueta una versión del lanzador, autocontenida (no necesita .NET instalado):
#   - LanzadorSwitch-<versión>-win-x64-instalador.exe   instalador (Inno Setup 6); es también lo que descarga el
#                                                        lanzador para actualizarse solo
#   - LanzadorSwitch-<versión>-win-x64.zip               el mismo contenido, portable
#   - SHA256SUMS.txt                                     los sha256 de los dos
# Uso: pwsh herramientas\empaquetar.ps1  →  publicar\
# Conviene ejecutarlo sobre un árbol limpio (git status sin cambios), para que el paquete sea el commit publicado.
#
# Por fases, para firmar en medio (lo hace .github\workflows\compilar.yml):
#   -Fase Publicar   compila en publicar\Lanzador
#   -Fase Comprimir  arma el instalador, el ZIP y SHA256SUMS.txt con lo que haya en publicar\Lanzador (ya firmado)
#   -Fase Todo       las dos seguidas, sin firmar (lo predeterminado)
#
# El instalador necesita Inno Setup 6 (winget install JRSoftware.InnoSetup). Sin él, en tu equipo solo se arma el ZIP
# (con un aviso); con -InstaladorObligatorio, o en GitHub Actions, falla.
param(
    [ValidateSet('Todo', 'Publicar', 'Comprimir')][string]$Fase = 'Todo',
    [string]$Salida = (Join-Path $PSScriptRoot '..\publicar'),
    [switch]$InstaladorObligatorio
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
$instalador = Join-Path $Salida "$nombre-instalador.exe"
$sumas = Join-Path $Salida 'SHA256SUMS.txt'

# En GitHub Actions, la versión y el nombre del paquete quedan como salidas del paso.
if ($env:GITHUB_OUTPUT) { "version=$version`nnombre=$nombre" | Add-Content $env:GITHUB_OUTPUT }

function Buscar-Iscc {
    $candidatos = @(
        $env:ISCC,
        (Get-Command iscc -ErrorAction SilentlyContinue).Source,
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $candidatos | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

if ($Fase -in 'Todo', 'Publicar') {
    if (Test-Path $Salida) { Remove-Item $Salida -Recurse -Force }
    dotnet publish $proyecto -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none -o $carpeta
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló ($LASTEXITCODE)" }
    Copy-Item (Join-Path $raiz 'LICENSE'), (Join-Path $raiz 'THIRD-PARTY-NOTICES.md'), (Join-Path $raiz 'LEEME.md'),
        (Join-Path $PSScriptRoot 'Crear-acceso-directo.ps1') $carpeta
}

$hashes = @()
if ($Fase -in 'Todo', 'Comprimir') {
    if (-not (Test-Path (Join-Path $carpeta 'Lanzador.exe'))) { throw "No está $carpeta\Lanzador.exe: ejecuta antes -Fase Publicar" }
    Remove-Item $zip, $instalador, $sumas -ErrorAction SilentlyContinue

    # Instalador: instala en la carpeta que elijas (C:\Switch\Lanzador por defecto) y crea los accesos directos.
    $iscc = Buscar-Iscc
    if ($iscc) {
        & $iscc /Qp "/DVersion=$version" "/DOrigen=$carpeta" "/DSalida=$Salida" (Join-Path $raiz 'instalador\LanzadorSwitch.iss')
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $instalador)) { throw "Inno Setup falló ($LASTEXITCODE)" }
    }
    elseif ($InstaladorObligatorio -or $env:GITHUB_ACTIONS) {
        throw 'No encuentro Inno Setup 6 (ISCC.exe): instálalo con «winget install JRSoftware.InnoSetup» o indica su ruta en $env:ISCC.'
    }
    else {
        Write-Warning 'No encuentro Inno Setup 6 (ISCC.exe): se arma solo el ZIP. Instálalo con «winget install JRSoftware.InnoSetup» para tener el instalador.'
    }

    # El ZIP lleva la carpeta Lanzador: al descomprimirlo en C:\Switch queda C:\Switch\Lanzador\Lanzador.exe,
    # y el lanzador busca y propone los emuladores en C:\Switch.
    Compress-Archive -Path $carpeta -DestinationPath $zip -CompressionLevel Optimal

    $hashes = @($instalador, $zip) | Where-Object { Test-Path $_ } | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $_ -Leaf)
    }
    [IO.File]::WriteAllText($sumas, (($hashes -join "`n") + "`n"))
}

Get-ChildItem $carpeta -Recurse -File | ForEach-Object { '  {0,12:N0}  {1}' -f $_.Length, $_.FullName.Substring($carpeta.Length + 1) }
foreach ($paquete in $instalador, $zip) {
    if (Test-Path $paquete) { '{0} ({1:N1} MB)' -f $paquete, ((Get-Item $paquete).Length / 1MB) }
}
$hashes | ForEach-Object { "sha256 $_" }
