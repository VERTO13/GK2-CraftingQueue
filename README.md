# Crafting Queue for Graveyard Keeper 2

A BepInEx mod that keeps a **crafting queue always on screen**. You can see what you want to make, what it takes, what you already have, and which chest it's in.

> 🇪🇸 **Español más abajo** → [Leer en español](#español)

---

## Features

- **On-screen queue panel.** Stays visible while you play and inside chests and crafting stations. It shows `have/need` for every material.
- **Quick add with Ctrl + right-click.** Works on any item, on a recipe in a crafting station, on a building in the build menu, or on a town work (repair/upgrade).
- **Recipe tree.** Expand any material to see how it's made, one recipe at a time. Switch between alternative recipes with ◂ ▸ and see which station each one uses.
- **Real yields.** The `×N` output takes your perks and station upgrades into account, not just the base recipe.
- **Automatic progress.** Tasks go down by themselves when you craft, build, or finish a town work.
- **Chest bubbles.** Small bubbles over the chests and storages in your current zone show which queued materials they hold and how many.
  - With the panel's global pin, bubbles cover the whole queue. With a task's pin, they cover only that task.
  - When you already have an item, only the item itself is marked (so you know where to pick it up). When you don't, its ingredients are marked instead.
- **Quick recipe view.** Hold **Alt** over any item to see its recipe tree without adding it.
- **Plays nice with windows.** The panel moves beside an open chest or station window. At small resolutions it folds into a small "Queue" tab that expands when you hover it.
- **Pixel-crisp.** Uses integer scaling, so the pixel art stays sharp at 720p, 1080p, 1440p and 4K.
- **One queue per save slot.** The queue is stored in its own file and never touches your save.
- **16 languages.** Every official language of the game, and it follows the game's language live.
- **Gamepad (experimental).** See [Controls](#controls).

## Requirements

- Graveyard Keeper 2 (Steam, Windows)
- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) (Unity Mono, x64), version 5.4.23 or newer

## Installation

1. Install BepInEx 5 into the game folder and run the game once so it creates its folders.
2. Download `CraftingQueue.dll` from the [Releases](../../releases) page.
3. Put it in `Graveyard Keeper 2\BepInEx\plugins\CraftingQueue\`.
4. Start the game. You should see the **Queue** panel at the top right.

## Controls

| Action | Keyboard / mouse | Gamepad (experimental) |
|---|---|---|
| Add to queue | `Ctrl` + right-click | Tap **R3** on the selected slot |
| Show a recipe | Hold `Alt` over an item | Hold **R3** on the selected slot |
| Show / hide panel | `F3` | – |
| Detailed / compact recipes | `F4` | – |
| Navigate the panel | Mouse (click arrows, − + 🗑, pins) | **R3** to enter · D-pad ↑↓ move · → open · ← close · LB/RB switch recipe · X/Y −/+ · A pin · B exit |
| Move the panel | Drag its title (lock icon to pin it) | – |

## Configuration

Settings live in `BepInEx\config\verto13.gk2.craftingqueue.cfg`. The file is created on first launch and all settings are documented inside it. You can change the keys, panel side, width, max height, icon size, background opacity, recipe style and more.

Queue data is saved in `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\CraftingQueue\`, one file per save slot.

### Translations

The mod ships with all 16 official languages. To fix a translation or add a language from a language mod:

1. Copy `lang\_plantilla_en.txt` (next to the DLL) to `lang\<language code>.txt`.
2. Translate the right-hand side of each line.

## Building from source

```bash
dotnet build -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2"
```

- `GameDir` must point to your game install with BepInEx already installed.
- The game's assemblies are only referenced to compile. They are never copied into the output or this repository.

## Notes

- Not affiliated with or endorsed by Lazy Bear Games.
- This repository contains no game code or assets.
- The mod does not send any data anywhere.
- Inspired by the idea behind **"No More Running Back"** by Amosa Yang (Steam Workshop). This is an independent implementation that shares no code with it. If you use both at once, both queue panels will show.

## License

[MIT](LICENSE)

---

## Español

Un mod de BepInEx que mantiene una **cola de crafteo siempre a la vista**. Muestra qué quieres hacer, qué necesitas, qué ya tienes y en qué cofre está.

### Qué hace

- **Panel de la cola en pantalla.** Se ve mientras juegas y también dentro de cofres y mesas de crafteo, con `tienes/necesitas` de cada material.
- **Agregar con Ctrl + clic derecho.** Funciona sobre cualquier objeto, una receta en una mesa, una construcción del menú de construir o una obra del pueblo (reparar/mejorar).
- **Árbol de recetas.** Despliega cualquier material para ver cómo se hace, una receta a la vez. Cambia entre recetas alternativas con ◂ ▸ y ve en qué mesa se hace cada una.
- **Cantidades reales.** El `×N` toma en cuenta tus talentos y las mejoras de la estación, no solo la receta base.
- **Descuento automático** al craftear, construir o terminar obras del pueblo.
- **Burbujas sobre los cofres.** Muestran qué materiales de tu cola tiene cada almacén de la zona donde estás, y cuántos.
  - Con el pin general se marca toda la cola; con el pin de una tarea, solo esa tarea.
  - Si ya tienes el objeto, solo se marca el objeto (para que sepas dónde está). Si no, se marcan sus ingredientes.
- **Vista rápida.** Mantén **Alt** sobre cualquier objeto para ver su receta sin agregarlo.
- **No estorba.** Con un cofre o una mesa abierta, el panel se mueve a un lado. En resoluciones chicas se pliega a una pestaña "Cola" que se despliega al pasar el mouse.
- **Nítido en cualquier resolución**, gracias a escalas enteras: 720p, 1080p, 1440p y 4K.
- **Una cola por partida guardada**, en un archivo aparte. No modifica tu partida.
- **16 idiomas** (todos los oficiales del juego). Sigue el idioma del juego al momento.
- **Control / gamepad (experimental).**

### Instalación

1. Instala BepInEx 5 (Unity Mono, x64) en la carpeta del juego y abre el juego una vez para que cree sus carpetas.
2. Descarga `CraftingQueue.dll` desde [Releases](../../releases).
3. Colócalo en `Graveyard Keeper 2\BepInEx\plugins\CraftingQueue\`.

### Controles

| Acción | Teclado / mouse | Control (experimental) |
|---|---|---|
| Agregar a la cola | `Ctrl` + clic derecho | Tocar **R3** sobre lo seleccionado |
| Ver una receta | Mantener `Alt` sobre un objeto | Mantener **R3** sobre lo seleccionado |
| Mostrar / ocultar el panel | `F3` | – |
| Recetas detalladas / compactas | `F4` | – |
| Navegar el panel | Mouse | **R3** para entrar · cruceta ↑↓ moverse · → abrir · ← cerrar · LB/RB cambiar receta · X/Y −/+ · A pin · B salir |

La configuración está en `BepInEx\config\verto13.gk2.craftingqueue.cfg`.

### Notas

- Mod no oficial; sin relación con Lazy Bear Games.
- No incluye código ni recursos del juego y no envía datos a ningún lado.
- Inspirado en la idea de **"No More Running Back"** de Amosa Yang (Steam Workshop). Es una implementación independiente y no comparte código con ese mod.
