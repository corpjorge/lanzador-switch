# Lanzador Switch

Lanzador estilo Big Picture para abrir tus juegos con **Eden**, **Ryujinx**, **Citron Neo** o **Suyu**, ver su compatibilidad, manejar trucos en español según la versión del juego y mantener al día los emuladores.

**Para abrirlo:** `Lanzador.exe` (en la carpeta del ZIP descargado, o en `app\` si lo compilaste tú).

## Cómo se usa

Arriba están las secciones **Inicio · Biblioteca · Emuladores · Controles**. Se cambia de sección con **LB / RB** (Q / E en el teclado). B en cualquier sección vuelve a Inicio.

| Mando | Teclado | Inicio | Biblioteca | Página del juego | Trucos |
|---|---|---|---|---|---|
| A | Enter | Jugar: elige el emulador (el preferido va primero, así A + A lo abre) | Abrir la página del juego | Jugar con el emulador elegido / abrir la fila | Activar / desactivar |
| B | Esc | — | Volver a Inicio | Volver | Volver |
| X | T | Trucos | Trucos | Trucos | Volver a descargar los trucos |
| Y | O | Página del juego (más opciones) | Cambiar de filtro | Favorito sí / no | Traducir de nuevo |
| Vista | V | — | — | — | Apagar todos los trucos activos de un golpe, o volver a encender exactamente los que estaban (ni uno más) |
| ☰ (Menú) | M | Menú: configuración, recargar, pantalla completa, salir | Menú | Más (preferido, carpeta, tinfoil.io) | Aplicar cambios |
| LB / RB | Q / E | Sección | Sección | Cambiar de emulador | Cambiar de emulador |
| Cruceta / stick | Flechas | Moverse por las filas | Moverse por la cuadrícula | Moverse por las filas | Moverse; en la fila de versión, ← → cambia la versión |
| — | F11 | Pantalla completa / ventana | | | |

- **Inicio:** arriba, el juego con foco en grande (calificación, compatibilidad de Ryubing, favorito); debajo, las filas **Continuar jugando** (los últimos que abriste), **Favoritos** y **Todos los juegos**.
- **Biblioteca:** todos los juegos en cuadrícula, con los filtros Todos, Favoritos, Jugados, Jugables en Ryubing y Sin calificar.
- **Iconos de emuladores:** salen del ejecutable de cada uno y aparecen al elegir emulador, en la sección Emuladores y en la página del juego (si no está instalado, se ve su inicial).
- **Página del juego:** carátula grande, con qué emulador jugar (LB / RB), trucos activos para tu versión, tu calificación, la versión que usa cada emulador con su Build ID, y el archivo.

Funciona con mandos Xbox y con cualquier mando que Steam Input presente como Xbox.

**Cerrar el juego y volver al lanzador:** durante el juego, mantén la combinación **1,5 segundos**. Son los mismos botones en los tres mandos:

| Mando | Combinación |
|---|---|
| Xbox | Vista + LB + RB |
| PS5 (DualSense) | Create + L1 + R1 |
| Switch Pro Controller | − (Menos) + L + R |

- El lanzador pide al emulador que cierre como si pulsaras la X de su ventana.
- Si a los 4 segundos sigue abierto (por ejemplo, porque pregunta "¿seguro?"), lo cierra a la fuerza.
- La combinación se muestra en la pantalla negra al abrir un juego, en la página del juego y en el diálogo «¿Con qué emulador?».
- El PS5 y el Pro Controller se escuchan con Raw Input de Windows solo mientras hay un juego abierto: el lanzador recibe una copia de lo que manda el mando, sin abrirlo ni enviarle nada, así que el emulador sigue siendo su dueño. Funciona por cable y por Bluetooth. También con mandos de otras marcas para Switch por Bluetooth (PDP, Hori, PowerA, 8BitDo), que hablan como el Pro Controller aunque Windows los identifique con su propia marca. Los Joy-Con sueltos no están incluidos.
- Si con alguno no funciona, abre el lanzador con `LANZADOR_MANDOS_LOG=<archivo>`: anota qué mando detecta, su primer reporte y cuándo ve la combinación.

**Ver los trucos activos durante el juego:** pulsa **F7** en el teclado. Aparece una ventana arriba a la derecha con los trucos activos de ese juego (en ese emulador y para la versión que usa) y la combinación de botones de cada uno; F7 otra vez la oculta.
- No le quita el foco al emulador: el mando y el teclado siguen yendo al juego, y los clics la atraviesan.
- La tecla solo la toma el lanzador mientras hay un juego abierto. Se cambia o se desactiva en Configuración → Sonido y movimiento.
- Se ve sobre el emulador en pantalla completa sin bordes (lo normal en Eden y Ryujinx); no sobre la pantalla completa exclusiva.

La combinación evita Inicio + Menos, que en Eden detiene (L + Inicio + Menos) o reinicia (R + Inicio + Menos) la emulación y que también usa el truco "Reserva de poderes".

En la sección **Emuladores**: A actualiza o instala, Y abre las opciones (volver a la versión anterior, ver los cambios, abrir carpetas), X vuelve a buscar versiones y B regresa (o cancela una descarga en curso).

## Mandos en todos los emuladores (sección Controles)

Los mandos se configuran **en el lanzador** y se escriben en Eden, Citron, Suyu y Ryujinx. No hace falta abrir ningún emulador.

| Botón | Qué hace |
|---|---|
| ↑ ↓ | Elegir jugador (1 a 8) |
| ← → | Cambiar su mando: primero los conectados, después los que ya usaste aunque estén apagados (marcados «apagado»), o "Ninguno". Recorrerlos no le quita el mando a otro jugador |
| A | **Detectar:** pulsas un botón en el mando que quieres y queda asignado a ese jugador (sirve para distinguir dos mandos iguales) |
| Y | Tipo de mando: Pro Controller, Joy-Con dobles o sueltos, Portátil |
| X | **Aplicar a todos los emuladores** (tienen que estar cerrados; se guarda un respaldo) |
| ☰ | Más: empezar desde los mandos de un emulador, abrir un emulador para ajustar botones uno por uno |

**Hasta 8 jugadores**, como en Switch. Windows solo deja usar **4 mandos Xbox a la vez**, porque XInput admite cuatro; los emuladores tienen el mismo límite. Para los jugadores 5 a 8 usa mandos que no sean Xbox: Pro Controller, Joy-Con o PlayStation.

**El mismo mando puede estar en varios jugadores**, como en los emuladores; la fila lo indica con «También en J1». Dos mandos iguales (por ejemplo, dos DualSense) se distinguen solos, y con A eliges cuál pulsando un botón en él.

Abajo, la línea **"En los emuladores"** muestra si cada uno ya tiene esos mandos (igual ✓) o no (distinto).

Qué escribe en cada emulador:
- **Eden, Citron, Suyu** (`user\config\qt-config.ini`, sección [Controls]):
  - Si ese mando ya está configurado en alguno de ellos, reutiliza esa asignación, con su calibración y los cambios que hayas hecho a mano.
  - Si no, genera la misma que pondría el emulador al elegir el mando: los botones van por posición, como en Switch (la A es el botón de la derecha).
  - Esa asignación generada coincide botón por botón con la que hace Eden para un mando Xbox y un Pro Controller.
  - Al Pro Controller y a los mandos PlayStation se les activa el giroscopio.
- **Ryujinx** (`portable\Config.json`): sus botones tienen nombre genérico, así que usa la asignación que ya tenía, o la que Ryujinx pone por defecto.
- **Cómo reconoce los mandos:** con SDL3 (`SDL3.dll`, la misma versión que trae Ryujinx), por su identificador de SDL, que es el mismo que guardan los emuladores.
- **Emuladores nuevos:** Citron y Suyu reciben estos mandos al instalarse desde la sección Emuladores.

El lanzador también se maneja con el **Pro Controller y mandos PlayStation**, solo mientras está al frente: durante el juego esos mandos son del emulador. Los botones que muestra la interfaz cambian según el último mando que tocaste: A B X Y y LB RB en Xbox; ✕ ○ □ △, L1 R1 y Create en PlayStation; A B X Y, L R y + − en los de Switch. En Xbox y PlayStation el botón de abajo acepta y el de la derecha vuelve; en los de Switch, como en la consola, A (derecha) acepta y B (abajo) vuelve. La combinación para cerrar el juego funciona durante el juego con los tres: Vista + LB + RB (Xbox), Create + L1 + R1 (PS5) y − + L + R (Pro Controller).

## Interfaz, sonido y movimiento

- **Diseño:** sigue el Design System «Lanzador Switch» (colores, tipografía, tamaños y tiempos de animación). Los mismos valores están en `src\Lanzador.App\Tema\Tokens.xaml`.
- **Escala de tele:** todo se dibuja para 1920 × 1080 y se escala a la altura de la ventana, así se ve igual en un monitor que en una tele 4K.
- **Icono:** un mando genérico con el triángulo de «jugar», en el azul del Design System (`src\Lanzador.App\Icono\Lanzador.ico`, de 16 a 256 px). Se dibuja con `herramientas\GeneradorIcono`.
- **Fuentes:** Sora (títulos), Lexend (texto) y JetBrains Mono (identificadores). Van dentro del ejecutable; sus licencias (SIL OFL 1.1) están en la carpeta `Fuentes`, junto a `Lanzador.exe`.
- **Sonidos:** el lanzador los genera al momento, sin archivos de terceros: paso al moverse, borde, elegir, volver, encender y apagar un truco, aplicado, error, y un barrido al abrir un juego, con fundido a negro. Mientras el lanzador está al frente mantiene abierta la salida de audio (tocando silencio), porque las salidas HDMI se duermen y se comerían los sonidos cortos; al pasar al juego la suelta.
- **Intro:** al abrir el lanzador aparece «Switch» con su resplandor, un destello y el sonido de arranque (≈ 2 s) mientras se cargan los juegos detrás. Cualquier botón o tecla la salta; no aparece al volver de un juego ni con «reducir movimiento».
- **Ajustes:** en Configuración → Sonido y movimiento se pueden apagar los sonidos, cambiar el volumen (60 % por defecto), quitar la intro y **reducir el movimiento** (sin animaciones; del todo al reiniciar el lanzador).

## Qué hace

- **Biblioteca:** lee las carpetas de juegos que ya usan Eden y Ryujinx. Los nombres salen de Ryujinx (en español) y los iconos y fondos de tinfoil.media.
- **Compatibilidad:** junto a tu propia calificación por emulador, muestra tres listas:
  - **Ryubing (Ryujinx):** la oficial (`github.com/Ryubing/Compatibility`), con detalles como «LAN funciona». Se revisa cada semana.
  - **Citron:** la de Citron Neo (`github.com/citron-neo/Citron-Compatability`), hecha con reportes de su comunidad. Tiene pocos juegos (76), pero se revisa cada día.
  - **Eden y Suyu (yuzu 2024):** la de yuzu, archivada en marzo de 2024 cuando cerró (`github.com/yuzu-mirror/api`), con unos 2700 juegos. Eden y Suyu salen de yuzu, así que sirve de referencia, aunque hoy suelen correr mejor que entonces. Se descarga una sola vez.
  - Eden no publica su propia lista. EmuReady (`emuready.com`) tiene reportes de la comunidad por emulador, pero no está integrado.
  - Al cerrar un juego que no habías calificado, te pregunta cómo funcionó.
- **Trucos:**
  - Junta switch-cheats-db y tinfoil.io, agrupados por Build ID.
  - Detecta la versión que usa cada emulador: en Eden, por las actualizaciones activas; en Ryujinx, por la actualización seleccionada.
  - Traduce los nombres al español con Claude Code o Codex. La IA solo ve nombres, nunca el código del truco.
  - El botón del truco se lee del propio código: por ejemplo, `80000002` significa "mantener B".
- **Dónde instala los trucos:**
  - Eden y Citron: `user\load\<ID>\<nombre>\cheats\<BuildID>.txt`; se activan y desactivan en `[DisabledAddOns]` de `qt-config.ini`. **Eden debe estar cerrado** al aplicar, porque guarda su configuración al cerrarse.
  - Ryujinx: `portable\mods\contents\<id>\cheats\<BuildID>.txt`; los activos van en `enabled.txt`. Se aplican al abrir el juego.
- **Respaldos:** antes de modificar `qt-config.ini` o `enabled.txt` se guarda una copia en `datos\respaldos` (las 20 más recientes).

## Actualizar emuladores

En la sección **Emuladores** (LB / RB desde Inicio, o ☰ → Emuladores) ves la versión instalada y la publicada de cada uno, y los actualizas en su sitio.

| Emulador | De dónde sale | Cómo sabe la versión instalada |
|---|---|---|
| Eden | Nightly: `nightly.eden-emu.dev`, zip `…-amd64-clang-pgo.zip`. Estable: la API de `git.eden-emu.dev`, con el sha256 de `stable.eden-emu.dev` | Por el texto "Eden Nightly &lt;hash&gt;" de `eden.exe` o por el registro `user\log\eden_log.txt` |
| Citron Neo | GitHub `citron-neo/CI`, release `nightly-windows` (variante clang-cl o clangtron) | Por lo que instaló el lanzador |
| Suyu | GitHub `suyu-emu/suyu-main`, último release, `suyu-windows-x86_64.zip` | Por lo que instaló el lanzador |
| Ryujinx | Solo abre `ryujinx.app/download`, porque su servidor de actualizaciones (`update.ryujinx.app`) no respondía al crear el lanzador | Por las propiedades de `Ryujinx.exe` |

El canal de Eden y la variante de Citron se eligen en Configuración.

Cómo actualiza:
1. Comprueba que el emulador esté cerrado.
2. Descarga a una carpeta temporal y verifica el **sha256** publicado. Si no coincide, o si la fuente no lo publica, no instala nada.
3. Extrae en otra carpeta temporal. Si el zip trae una carpeta raíz, usa lo que hay dentro.
4. Respalda los archivos que va a reemplazar en `datos\respaldos\<emulador>-<fecha>`. Guarda los 3 respaldos más recientes de cada emulador.
5. Copia los archivos nuevos encima de la instalación.

**Nunca escribe, mueve ni borra nada dentro de `user` (Eden y Citron) ni de `portable` (Ryujinx).** Si el zip trae esas carpetas, no las copia.

- Si la copia falla a medias, restaura la versión anterior sola.
- **Volver a la versión anterior** (botón Y) repone los archivos del respaldo y quita los que agregó la actualización.
- **Emuladores nuevos:** se proponen junto a la carpeta del lanzador. Por ejemplo, con el lanzador en `C:\Switch\Lanzador`, Citron Neo se instala en `C:\Switch\Citron` y Suyu en `C:\Switch\Suyu`, o en la ruta que pongas en Configuración. Al instalarlos, el lanzador:
  1. crea siempre su carpeta de datos junto al ejecutable (`user`, o `portable` en Ryujinx), así quedan en modo portable y nada se va a `AppData`;
  2. los activa;
  3. les pone los mandos elegidos en Controles.

## Claves y firmware

El lanzador **no descarga, no incluye y no instala** claves (`prod.keys`, `title.keys`) ni firmware. Cada emulador necesita los suyos, sacados de tu propia consola; se ponen con las opciones del propio emulador. El lanzador nunca toca las carpetas `user` ni `portable`, que es donde quedan.

## Datos del lanzador

Todo vive en la carpeta `datos`, junto a `Lanzador.exe`:
- `config.json`: emuladores, carpetas, IA, canales de actualización y tus calificaciones.
- `cache\`: iconos, fondos, trucos descargados y traducciones ya hechas.
- `versiones.json`: la versión que el lanzador instaló de cada emulador.
- `respaldos\`: copias de `qt-config.ini` y `enabled.txt`, y los respaldos de las actualizaciones de emuladores.
- `errores.log`: solo aparece si algo falla.

## Traducción con IA

En Configuración eliges el motor (Claude Code, Codex o ninguno) y, opcionalmente, el modelo. El botón **Probar traducción** comprueba que funcione.

- **Claude Code:** si sale "OAuth session expired", abre una terminal, ejecuta `claude` y usa `/login`.
- **Codex:** si tu cuenta no admite el modelo de tu `config.toml`, escribe en el campo Modelo uno que sí admita, o actualiza Codex y vuelve a iniciar sesión.

## Agregarlo a Steam (opcional)

En Steam ve a *Juegos → Añadir un juego que no es de Steam* y elige `Lanzador.exe`. Desde Big Picture lo abres con el mando y, si quieres, con Steam Input.

## Para desarrollo

- **Código:** `src\Lanzador.Core` es el núcleo, `src\Lanzador.App` la interfaz WPF y `src\Lanzador.Pruebas` las pruebas.
- **Pruebas del núcleo:** `dotnet run --project src\Lanzador.Pruebas`. Trabajan sobre copias y no modifican los emuladores; agrega `-- --ia` para probar también la traducción. Leen la configuración real de `app\` junto a `Lanzador.slnx` (o de la carpeta en `LANZADOR_APP`), así que algunas comprobaciones esperan Eden y Ryujinx instalados y juegos concretos en la biblioteca: si no los tienes, fallan sin cambiar nada. Las pruebas del actualizador simulan instalaciones con carpetas `user` y `portable` falsas y descargan de un servidor falso; de internet solo consultan qué versión está publicada.
- **Publicar:** `dotnet publish src\Lanzador.App -c Release -o app`.
- **Paquete de una versión:** `pwsh herramientas\empaquetar.ps1` arma en `publicar\` el ZIP portable y `SHA256SUMS.txt`. Por fases (`-Fase Publicar`, firmar, `-Fase Comprimir`) es como lo hace GitHub Actions en `.github\workflows\compilar.yml`, que con un tag `v*` pide la firma a SignPath (ver `POLITICA-DE-FIRMA.md`).
- **Modo de prueba de la interfaz:** variables `LANZADOR_PRUEBA="Ir:Kirby,Trucos,Esperar5000,Abajo"` y `LANZADOR_CAPTURAS=<carpeta>`. Abre la app, simula esas acciones y guarda una captura tras cada una. También admite `Seccion:Emuladores` (cambiar de sección), `Intro` (repetir la intro; en modo de prueba no sale sola), `Negro:<juego>` y `QuitarNegro` (la pantalla de «abriendo juego» sin abrir ningún emulador), `Escucha` (activar la escucha de PS5 y Pro), `VentanaTrucos:<juego>` (la ventana de trucos sin abrir el emulador; guarda `ventana-trucos.png`), `TeclaTrucos` (comprueba que se puede registrar la tecla), `Aviso:<texto>` y `Error:<texto>` (mostrar un aviso) y `GuardarSonidos` (guarda cada sonido como WAV en la carpeta de capturas). Con `LANZADOR_AUDIO_LOG=<archivo>` anota cada sonido y cuándo se abre o cierra la salida de audio; el primer fallo de audio de cada sesión queda además en `datos\errores.log`. Usa `LANZADOR_DATOS` con una copia de `app\datos` para no tocar tu configuración.
- **Probar el actualizador sin tocar los emuladores reales:** pon `LANZADOR_DATOS=<carpeta temporal>` con un `config.json` cuyas rutas de Eden y Citron apunten a instalaciones falsas.
