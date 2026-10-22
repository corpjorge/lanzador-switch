# Política de firma de código (Code signing policy)

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

> **Estado:** pendiente de aprobación por SignPath Foundation. Hasta entonces las versiones publicadas, incluida la 1.0.0, **no están firmadas**. Cada release dice si su `Lanzador.exe` va firmado.

## Qué se firma

- Solo **`Lanzador.exe`**, compilado de este repositorio por el workflow [`.github/workflows/compilar.yml`](.github/workflows/compilar.yml) en GitHub Actions, sin pasos a mano entre el código y el binario.
- No se firman binarios de terceros. `SDL3.dll` (SDL, licencia zlib) va en el paquete tal como lo publica su proyecto. El runtime de .NET que va dentro de `Lanzador.exe` es de Microsoft (licencia MIT).
- Los metadatos del ejecutable son fijos: producto **Lanzador Switch** y la versión de `<Version>` en `src/Lanzador.App/Lanzador.App.csproj`. SignPath los comprueba con [`.signpath/artifact-configuration.xml`](.signpath/artifact-configuration.xml).

## Cómo se firma una versión

1. Se sube `<Version>` en `src/Lanzador.App/Lanzador.App.csproj`, se hace commit en `main` y se crea el tag `vX.Y.Z`.
2. El workflow compila desde ese tag y envía la carpeta del lanzador a SignPath.
3. Un aprobador revisa y **aprueba a mano** la solicitud en SignPath. Ninguna versión se firma sin esa aprobación.
4. El workflow comprueba la firma y arma `LanzadorSwitch-X.Y.Z-win-x64.zip` y `SHA256SUMS.txt` con el ejecutable firmado.
5. El release se publica con esos archivos, sin cambiarlos.

## Equipo y roles

| Rol | Quién | Qué hace |
|---|---|---|
| Autores (committers) | [corpjorge](https://github.com/corpjorge) | Cambian el código sin revisión adicional |
| Revisores (reviewers) | [corpjorge](https://github.com/corpjorge) | Revisan cada pull request de otras personas antes de unirlo |
| Aprobadores (approvers) | [corpjorge](https://github.com/corpjorge) | Aprueban cada solicitud de firma en SignPath |

Quién puede escribir en el repositorio: [colaboradores de corpjorge/lanzador-switch](https://github.com/corpjorge/lanzador-switch/graphs/contributors). Todos los miembros usan autenticación en dos pasos en GitHub y en SignPath.

## Privacidad

Lanzador Switch **no tiene telemetría, cuentas ni analítica**. No envía tu configuración, tus partidas, tus claves ni tus archivos a ningún sitio. Lo que guarda queda en la carpeta `datos`, junto a `Lanzador.exe`.

**La primera vez que lo abres te pregunta** si permites descargar carátulas, trucos y listas de compatibilidad, con enlace a esta sección. Hasta que respondes no descarga nada; si cierras el aviso sin elegir, vuelve a salir la próxima vez.

Estos son los servicios a los que puede conectarse. Cada petición lleva, como cualquier conexión, tu dirección IP y el agente `LanzadorSwitch/1.0`.

| Servicio | Para qué | Qué dato tuyo lleva | Cuándo | Cómo se desactiva |
|---|---|---|---|---|
| tinfoil.media | Carátulas y fondos | El identificador (Title ID) de cada juego de tu biblioteca | Al cargar la biblioteca; lo descargado queda en caché | Descargas (ver abajo) |
| tinfoil.io | Trucos y versiones (Build ID) | El Title ID del juego | Al abrir sus trucos; caché de 3 días | Descargas |
| raw.githubusercontent.com: [switch-cheats-db](https://github.com/HamletDuFromage/switch-cheats-db) | Trucos | El Title ID del juego | Al abrir sus trucos; caché de 3 días | Descargas |
| raw.githubusercontent.com: listas de [Ryubing](https://github.com/Ryubing/Compatibility), [Citron Neo](https://github.com/citron-neo/Citron-Compatability) y [yuzu](https://github.com/yuzu-mirror/api) | Compatibilidad | Ninguno: se descarga la lista completa | Ryubing cada semana, Citron cada día, yuzu una vez | Descargas |
| Anthropic (Claude Code) u OpenAI (Codex), con la herramienta y la cuenta que tengas instaladas | Traducir al español los nombres de los trucos | El nombre del juego y los nombres de sus trucos (nunca el código del truco) | Al abrir trucos sin traducir, **solo si elegiste un motor de IA** | Viene desactivado: motor «Ninguno» |
| nightly.eden-emu.dev, stable.eden-emu.dev, git.eden-emu.dev, api.github.com y github.com (Citron Neo, Suyu) | Ver y descargar versiones de los emuladores | Ninguno | Al abrir la sección Emuladores; solo descarga cuando confirmas una instalación o actualización | No abrir la sección Emuladores |

- **Descargas:** ☰ → Configuración → Internet → «Descargar carátulas, fondos, trucos y listas de compatibilidad». Desactivado, el lanzador usa solo lo que ya se descargó y no envía el identificador de tus juegos a nadie.
- **Traducción con IA:** viene en «Ninguno». Si eliges Claude Code o Codex en ☰ → Configuración, los nombres se envían con tu propia cuenta de esa herramienta.
- Políticas de privacidad de terceros: [GitHub](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement), [Anthropic](https://www.anthropic.com/legal/privacy) y [OpenAI](https://openai.com/policies/privacy-policy/). Las de tinfoil.io y tinfoil.media, en sus propios sitios.

## English summary

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

- **What is signed:** only `Lanzador.exe`, built from this repository by the GitHub Actions workflow `.github/workflows/compilar.yml`. Third-party binaries (`SDL3.dll`) are shipped unsigned, as published upstream.
- **Process:** every release is built from a `vX.Y.Z` tag, submitted to SignPath and manually approved by an approver before signing.
- **Team:** committer, reviewer and approver: [corpjorge](https://github.com/corpjorge). All members use multi-factor authentication on GitHub and SignPath.
- **Privacy:** no telemetry, accounts or analytics. On first launch the program asks whether it may download cover art, cheats and compatibility lists, links to this policy, and downloads nothing until the user answers. If allowed, it contacts tinfoil.media and tinfoil.io (sends the game's Title ID) and raw.githubusercontent.com (cheat database and compatibility lists); this can be turned off at any time in Settings → Internet. AI translation is off by default; only if the user selects an engine are game and cheat names sent to Anthropic or OpenAI through the user's own CLI. The emulators' release servers are contacted only from the Emulators section and receive no user data.
