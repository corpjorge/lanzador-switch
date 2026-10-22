# Avisos de terceros

Lanzador Switch se distribuye con la licencia MIT (ver [LICENSE](LICENSE)). Incluye o usa estos componentes de terceros, cada uno con su propia licencia.

## Componentes incluidos

| Componente | Versión | Licencia | Dónde está |
|---|---|---|---|
| [SDL3](https://github.com/libsdl-org/SDL) (Simple DirectMedia Layer) | 3.5.0 | zlib | `SDL3.dll` |
| [Sora](https://github.com/sora-xor/sora-font) | 600, 700 | SIL Open Font License 1.1 | dentro de `Lanzador.exe`; licencia en `Fuentes/OFL-Sora.txt` |
| [Lexend](https://github.com/googlefonts/lexend) | 400, 500, 600, 700 | SIL Open Font License 1.1 | dentro de `Lanzador.exe`; licencia en `Fuentes/OFL-Lexend.txt` |
| [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono) | 500 | SIL Open Font License 1.1 | dentro de `Lanzador.exe`; licencia en `Fuentes/OFL-JetBrainsMono.txt` |
| [.NET](https://github.com/dotnet/runtime) y [WPF](https://github.com/dotnet/wpf) | 10 | MIT, con sus propios avisos de terceros | dentro de `Lanzador.exe` (versión autocontenida) |

### SDL3 — licencia zlib

```
Copyright (C) 1997-2026 Sam Lantinga <slouken@libsdl.org>

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not
   claim that you wrote the original software. If you use this software
   in a product, an acknowledgment in the product documentation would be
   appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
   misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
```

### Fuentes — SIL Open Font License 1.1

El texto completo de la licencia y el aviso de copyright de cada fuente están en `src/Lanzador.App/Fuentes/OFL-*.txt` (y en la carpeta `Fuentes` del paquete):

- Sora: Copyright 2019 The Sora Project Authors.
- Lexend: Copyright 2018 The Lexend Project Authors, con el nombre de fuente reservado «RevReading Lexend».
- JetBrains Mono: Copyright 2020 The JetBrains Mono Project Authors.

## Datos y servicios que se consultan al usarlo

No se redistribuyen: el lanzador los descarga en tu equipo cuando hace falta, y siguen siendo de sus autores.

- Trucos: [switch-cheats-db](https://github.com/HamletDuFromage/switch-cheats-db) y tinfoil.io.
- Carátulas y fondos: tinfoil.media. Las carátulas de los juegos son de sus dueños.
- Compatibilidad: [Ryubing/Compatibility](https://github.com/Ryubing/Compatibility), [Citron-Compatability](https://github.com/citron-neo/Citron-Compatability) y la lista archivada de [yuzu-mirror/api](https://github.com/yuzu-mirror/api).
- Actualizaciones de emuladores: las páginas oficiales de Eden, Citron Neo, Suyu y Ryujinx (Ryubing).

## Marcas

Nintendo Switch es una marca de Nintendo. Este proyecto no está afiliado, patrocinado ni aprobado por Nintendo, ni por los proyectos de los emuladores. No incluye juegos, claves ni firmware.
