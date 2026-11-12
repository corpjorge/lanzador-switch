<p align="center"><img src="docs/icono.png" alt="Icono de Lanzador Switch" width="112"></p>

# Lanzador Switch

Lanzador estilo Big Picture para Windows que abre tus juegos de Switch con **Eden**, **Ryujinx**, **Citron Neo** o **Suyu**, todo con el mando: compatibilidad de cada juego, trucos en español según la versión que usas, actualización de los emuladores con respaldo, y mandos configurados una sola vez para todos los emuladores (hasta 8 jugadores).

![Inicio: el juego con foco en grande y las filas de carátulas](docs/capturas/inicio.jpg)

## Qué hace

- **Biblioteca** con carátulas y fondos, filas de «Continuar jugando» y Favoritos, filtros y página de cada juego.
- **Elegir emulador** en cada partida, con tu calificación y las listas de compatibilidad de Ryubing, Citron Neo y yuzu.
- **Trucos** de switch-cheats-db y tinfoil.io para la versión exacta del juego (por Build ID), con nombres en español (traducción opcional con Claude Code o Codex) y el botón de cada truco. Durante el juego, **F7** muestra los trucos activos sin quitarle el foco al emulador.
- **Emuladores:** muestra la versión instalada y la publicada de Eden, Ryujinx (Ryubing Canary), Citron Neo y Suyu, y los instala o actualiza en su sitio verificando la descarga, con respaldo para volver atrás. Nunca toca las carpetas `user` ni `portable`.
- **Avisa de las actualizaciones** al abrirlo: del propio lanzador (se actualiza solo con su instalador) y de los emuladores instalados; eliges cuáles actualizar.
- **Controles:** eliges el mando de cada jugador en el lanzador y se escribe en Eden, Citron, Suyu y Ryujinx. Con los **mandos automáticos**, al abrir un juego los mandos encendidos ocupan los primeros jugadores, así siempre juega el que tienes en la mano.
- **Convertir NSZ a NSP:** descomprime tus juegos `.nsz`, verifica el resultado y te deja enviar el original a la papelera. Sin programas externos ni claves.
- **Volver al lanzador** desde el juego manteniendo Vista + LB + RB (Xbox), Create + L1 + R1 (PS5) o − + L + R (Pro Controller).

## Descarga e instalación

1. Descarga `LanzadorSwitch-<versión>-win-x64-instalador.exe` de la [última versión](https://github.com/corpjorge/lanzador-switch/releases/latest).
2. Opcional: comprueba que llegó completo comparando su hash con `SHA256SUMS.txt`:
   ```powershell
   Get-FileHash .\LanzadorSwitch-1.2.0-win-x64-instalador.exe -Algorithm SHA256
   ```
3. Ábrelo. No está firmado, así que Windows SmartScreen puede mostrar «Windows protegió su PC»: pulsa **Más información → Ejecutar de todas formas**. No pide permisos de administrador.
4. Elige la carpeta. Por defecto es `C:\Switch\Lanzador`: el lanzador busca Eden, Ryujinx, Citron Neo y Suyu en la carpeta que la contiene (`C:\Switch`), y ahí instala los que le pidas. Si ya tienes tus emuladores en otra carpeta, por ejemplo `D:\Juegos\Switch`, instala en `D:\Juegos\Switch\Lanzador`.
5. Al terminar queda en el menú Inicio (y en el escritorio, si lo marcas).

No necesita instalar .NET ni nada más. Guarda su configuración en la carpeta `datos`, junto al ejecutable, por eso no se instala en `Archivos de programa`. Se quita desde **Configuración de Windows → Aplicaciones**; al desinstalar pregunta si también borras tus datos.

**Actualizaciones:** cuando publiques o salga una versión nueva, el lanzador la detecta al abrirse y, si aceptas, descarga el instalador, verifica su sha256, se actualiza y se vuelve a abrir. Tus datos no se tocan.

**Versión portable:** el mismo contenido está en `LanzadorSwitch-<versión>-win-x64.zip`. Descomprímelo donde tienes los emuladores (queda `C:\Switch\Lanzador\Lanzador.exe`) y abre `Lanzador.exe`. Si después lo actualizas desde el propio lanzador, el instalador se aplica sobre esa misma carpeta.

## Requisitos

- Windows 10 u 11 de 64 bits.
- Al menos un emulador: Eden, Ryujinx (Ryubing), Citron Neo o Suyu. Los cuatro se pueden instalar desde la sección Emuladores.
- Tus juegos. Para las claves y el firmware, puedes indicar una carpeta con tus archivos o conectar una instalación aparte de [Firma](https://github.com/corpjorge/firma).
- Un mando Xbox, PlayStation, Pro Controller o compatible (también se maneja con el teclado).
- Opcional: [Claude Code](https://claude.com/claude-code) o Codex para traducir los nombres de los trucos.

## Uso básico

| Mando | Teclado | Qué hace |
|---|---|---|
| LB / RB | Q / E | Cambiar de sección: Inicio, Biblioteca, Emuladores, Controles |
| A | Enter | Jugar (pregunta con qué emulador; el preferido va primero) |
| X | T | Trucos del juego |
| Y | O | Página del juego / opciones |
| ☰ | M | Menú: configuración, convertir NSZ a NSP, buscar actualizaciones, volver a buscar juegos, pantalla completa, salir |
| B | Esc | Volver |
| — | F7 | Durante el juego: mostrar u ocultar los trucos activos |

La primera vez te pregunta si permites descargar carátulas, trucos y listas de compatibilidad: para eso envía el identificador de tus juegos a tinfoil y GitHub, y nada más tuyo. Se cambia cuando quieras en **☰ → Configuración → Internet**. La traducción con IA viene desactivada. Detalles en la [política de privacidad](POLITICA-DE-FIRMA.md#privacidad).

Revisa también **☰ → Configuración**: las rutas de los emuladores y las carpetas de juegos se detectan solas, pero puedes cambiarlas. La guía completa, con cómo funciona cada parte, está en [LEEME.md](LEEME.md).

## Cómo se ve

| | |
|---|---|
| ![Intro](docs/capturas/intro.jpg)<br>**Intro** al abrir, con su sonido (se salta con cualquier botón). | ![Elegir emulador](docs/capturas/elegir-emulador.jpg)<br>**A** pregunta con qué emulador; el preferido va primero. |
| ![Página del juego](docs/capturas/pagina-juego.jpg)<br>**Página del juego**: emulador con LB / RB, trucos, calificación, versión y archivo. | ![Trucos](docs/capturas/trucos.jpg)<br>**Trucos** de tu versión, en español; Vista apaga todos o enciende los de antes. |
| ![Biblioteca](docs/capturas/biblioteca.jpg)<br>**Biblioteca** en cuadrícula, con filtros (Y). | ![Emuladores](docs/capturas/emuladores.jpg)<br>**Emuladores**: versión instalada y publicada, actualización y respaldo. |
| ![Controles](docs/capturas/controles.jpg)<br>**Controles** para hasta 8 jugadores, escritos en todos los emuladores. | ![Abriendo juego](docs/capturas/abriendo-juego.jpg)<br>Al abrir un juego: **cómo volver** al lanzador con cada mando. |

<p align="center"><img src="docs/capturas/ventana-trucos.jpg" alt="Ventana flotante de trucos" width="320"><br><b>F7</b> durante el juego: ventana flotante con los trucos activos y sus botones.</p>

## Compilar

Necesitas Windows y el [SDK de .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build Lanzador.slnx -c Release
dotnet run --project src\Lanzador.Pruebas         # pruebas del núcleo (leen tu instalación, no la modifican)
dotnet publish src\Lanzador.App -c Release -o app  # el lanzador en app\Lanzador.exe
pwsh herramientas\empaquetar.ps1                   # instalador, ZIP portable y SHA256SUMS.txt en publicar\
```

El instalador se compila con [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`); sin él, `empaquetar.ps1` arma solo el ZIP.

**Publicar una versión:** sube `<Version>` en `src\Lanzador.App\Lanzador.App.csproj`, crea el tag `vX.Y.Z` y un release de GitHub con ese tag que lleve el instalador, el ZIP y `SHA256SUMS.txt`. Los lanzadores instalados comparan el tag del último release con su versión y ofrecen actualizarse con el instalador `…-instalador.exe` de ese release.

El código está en `src\Lanzador.Core` (núcleo), `src\Lanzador.App` (interfaz WPF) y `src\Lanzador.Pruebas` (pruebas).

Los paquetes de cada versión los arma GitHub Actions desde este repositorio ([`compilar.yml`](.github/workflows/compilar.yml)).

## Política de firma de código (Code signing policy)

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org). La firma está pendiente de aprobación: hasta entonces los paquetes no van firmados. Qué se firma, quién lo aprueba y a qué servicios se conecta el lanzador: [POLITICA-DE-FIRMA.md](POLITICA-DE-FIRMA.md).

## Licencia

[MIT](LICENSE). Los componentes de terceros (SDL3, ZstdSharp, fuentes Sora, Lexend y JetBrains Mono, .NET) tienen sus propias licencias: ver [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Este proyecto no está afiliado a Nintendo ni a los proyectos de los emuladores. Nintendo Switch es una marca de Nintendo. Las carátulas que aparecen en las capturas son de sus dueños: el lanzador las descarga de tinfoil.media en tu equipo y no se distribuyen con él.
