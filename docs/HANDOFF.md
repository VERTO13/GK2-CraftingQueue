# Traspaso para una nueva sesión de Claude Code

Este documento es para que otra sesión de Claude Code (en otra computadora) retome **Crafting Queue** sin
perder contexto. Léelo completo antes de tocar código. `CLAUDE.md` (en la raíz) resume las reglas.

---

## 1. Qué es el proyecto

**Crafting Queue** es un mod de BepInEx 5 para **Graveyard Keeper 2** (Unity 6, Mono). Pone en pantalla una
cola de crafteo: qué quieres hacer, qué necesitas, qué tienes y en qué cofre está.

- **GitHub (público):** https://github.com/VERTO13/GK2-CraftingQueue — licencia MIT.
- **Nexus Mods:** https://www.nexusmods.com/graveyardkeeper2/mods/177 (autor en Nexus: LeBetoven).
- **Versión publicada:** 0.4.16 (GitHub Release `v0.4.16` y Nexus). La siguiente será **0.5.0**.
- **Anunciado** en el Discord oficial de Lazy Bear Games, foro `#gk2-modding`.

### Funciones principales (0.4.16)
- **Ctrl + clic derecho** agrega casi todo: objetos, recetas de estación, construcciones, obras del pueblo,
  requisitos de misión, planos del árbol tecnológico, pedidos de NPC (diálogos) y encargos de comerciantes.
- **Panel** con `tienes/necesitas`, árbol de recetas (una a la vez, ◂ ▸ entre recetas/estaciones), rendimiento
  real `×N` con talentos (perks), solo recetas/estaciones desbloqueadas.
- **Una tarea pide TENER N** (no "hacer N más"). Ctrl + clic en objeto/receta pide "uno más de lo que tienes";
  los pedidos (NPC, misión, encargo, construcción) piden su cantidad exacta.
- **Reparto en orden de la cola** (`Plan.cs`): la tarea de arriba toma primero lo que tienes; ▲ ▼ reordenan.
- **Se completan solas** cuando pasan de "falta" a "completa" en la misma zona (craftear, recoger, comprar).
- **Burbujas sobre los cofres** de la zona (pin general y por tarea) + aviso "· Yard: 7" si hay en otra zona.
- **Alt** = vista rápida de receta (inventario, cofres y renglones del panel).
- El panel se recorre o se pliega con ventanas abiertas (nunca cambia de lado); el **ojo** lo deja siempre visible.
- Una cola por partida guardada (`AppData/LocalLow/Lazy Bear Games/Graveyard Keeper 2/CraftingQueue/<slot>.txt`).
- 16 idiomas propios (`Lang.cs`). Soporte de control (R3) **experimental, nunca probado con control real**.

---

## 2. Estado actual del trabajo (rama `feature/framework-bridge`)

Se está haciendo la **0.5.0**. En esta rama (sin publicar, **versión aún 0.4.16** a propósito):

1. **Integración opcional con GK2 Mod Framework** (https://github.com/SuperMan4eg/GK2-Mod-Framework, MIT,
   Nexus mod 42). El framework agrega un menú **Mods** (ESC y menú principal) con ajustes, teclas editables y
   navegación con control. Probado contra **Framework 0.1.14** (API "preview" 0.1.x).
   - Proyecto nuevo `FrameworkBridge/` → `CraftingQueue.GK2Framework.dll` (netstandard2.1, como el framework).
   - **No referencia `CraftingQueue.dll`**: toma el `ConfigFile` del mod principal en tiempo de ejecución
     (`Chainloader.PluginInfos["verto13.gk2.craftingqueue"].Instance.Config`) y registra **las mismas
     secciones/claves/tipos**, así que el menú edita los mismos valores del `.cfg`.
   - Dependencias duras de BepInEx a nuestro mod y al framework: **sin el framework, BepInEx omite el puente**
     y el mod funciona igual (modelo "standalone + bridge" de `docs/OPTIONAL_INTEGRATION.md` del framework).
   - `frameworkManagesEnabledState: false` → el framework no muestra Enable/Disable para nosotros.
   - Traducciones del menú: `FrameworkBridge/Localization/verto13.gk2.craftingqueue/en.json` y `es.json`.
     Se instalan en `BepInEx/plugins/GK2.Framework/Localization/verto13.gk2.craftingqueue/`. Los nombres de
     sección del config están en español ("Controles", "Vista rápida", "Panel en pantalla"); `en.json` los
     traduce para el menú. Otros idiomas caen a `en`.
2. **Teclas como `KeyboardShortcut`** en `Plugin.cs` (antes `KeyCode`). Requisito del framework (`AddKeybind`).
   Un valor guardado viejo como `LeftControl` se lee igual. El resto del mod usa `.Value.MainKey`.

### Lo que falta probar (hazlo con el usuario, en su juego)
- [ ] Con el framework instalado: ESC → **Mods** → **Crafting Queue** muestra las 3 secciones con nombres bien
      traducidos (español si el juego está en español).
- [ ] Cambiar una tecla en el menú (p. ej. F3 → F5) y que funcione **sin reiniciar**. Revisa también que el
      `.cfg` guarde el nuevo valor.
- [ ] Mover deslizadores (ancho, opacidad…) y que el panel cambie en vivo.
- [ ] Interruptores (burbujas, siempre visible…) y que coincidan con los botones del panel (pin dorado, ojo).
- [ ] **Sin el framework** (quítalo temporalmente): el mod carga normal y el log dice que el puente se omitió.
- [ ] Que un `.cfg` viejo (teclas guardadas como `KeyCode`) no pierda valores.
- [ ] **Control / gamepad** (ver sección 6): es la primera vez que se prueba con control real.

### Decisiones pendientes para empaquetar la 0.5.0
- El zip debe incluir `BepInEx/plugins/CraftingQueue/CraftingQueue.GK2Framework.dll` y las traducciones en
  `BepInEx/plugins/GK2.Framework/Localization/verto13.gk2.craftingqueue/`. Ojo: si el jugador **no** tiene el
  framework, eso crea una carpeta `GK2.Framework/Localization/...` casi vacía (inofensiva). Alternativa: ofrecer
  el puente como archivo opcional aparte. Decídelo con el usuario.
- Si el usuario instala el framework con **Vortex**, Vortex puede quejarse de archivos externos en su carpeta.
- Hay un trabajo guardado solo en la PC original (`git stash`, "config en ingles 0.4.17") que traducía el
  `.cfg` al inglés. El usuario decidió **no** publicarlo por ahora; con el menú del framework ya no hace falta.

---

## 3. Preparar la otra computadora

1. **Git + GitHub CLI** (`gh auth login` con la cuenta del usuario, eso lo hace él).
2. **.NET SDK 8 o superior** (`dotnet --version`).
3. **Graveyard Keeper 2** con **BepInEx 5.4.23.5** instalado y ejecutado al menos una vez.
4. **GK2 Mod Framework** instalado (Nexus mod 42) — hace falta para compilar el puente (referencia
   `BepInEx/plugins/GK2.Framework.dll`) y para probarlo.
5. Clonar y cambiar a la rama:
   ```bash
   git clone https://github.com/VERTO13/GK2-CraftingQueue.git
   cd GK2-CraftingQueue
   git checkout feature/framework-bridge
   ```
6. **Identidad de git del repo (obligatorio):** usar el correo privado de GitHub, nunca uno personal.
   ```bash
   git config user.name "VERTO13"
   git config user.email "13241922+VERTO13@users.noreply.github.com"
   ```
7. Opcional: **FFmpeg** (`winget install Gyan.FFmpeg`) para GIFs/videos del README.

## 4. Compilar e instalar

Si el juego no está en la ruta por defecto de Steam, pasa `-p:GameDir="<carpeta del juego>"`.

```bash
dotnet build -c Release
dotnet build FrameworkBridge -c Release
```

- Mod: `bin/Release/net472/CraftingQueue.dll` → `<juego>/BepInEx/plugins/CraftingQueue/`
- Puente: `FrameworkBridge/bin/Release/netstandard2.1/CraftingQueue.GK2Framework.dll` → misma carpeta
- Traducciones del menú: `FrameworkBridge/Localization/verto13.gk2.craftingqueue/*.json` →
  `<juego>/BepInEx/plugins/GK2.Framework/Localization/verto13.gk2.craftingqueue/`
- **El juego bloquea las DLL mientras está abierto:** pide al usuario que lo cierre antes de copiar.
- Log: `<juego>/BepInEx/LogOutput.log`. Config: `<juego>/BepInEx/config/verto13.gk2.craftingqueue.cfg`.
- Diagnóstico: `[Diagnóstico] MedirRendimiento = true` escribe tiempos (cada 15 s) en el log. **Apágalo
  después de probar.**

---

## 5. Mapa del código

| Archivo | Qué hace |
|---|---|
| `Plugin.cs` | Entrada BepInEx (GUID `verto13.gk2.craftingqueue`), config (secciones en español), arranque. |
| `Queue.cs` | Tareas por partida, persistencia en texto (`task`/`pin`), pines, agregar (`oneMore`), reordenar. |
| `Plan.cs` | Reparto de lo que tienes en orden de la cola; renglones por ruta; completado automático (`Tick`). |
| `QueueView.cs` | Convierte tareas en entradas para el panel. |
| `QueueHud.cs` | El panel (UGUI en tiempo real): bloques, árbol de recetas, botones ▲ ▼ − + 🗑, pines, ojo, plegado, navegación con control, actualización de números en su lugar. |
| `GameData.cs` | Todo lo que se lee del juego: recetas (`OptionsFor`), rendimientos con talentos, estaciones disponibles, conteos (`Owned`), otras zonas (`Elsewhere`). |
| `ChestMarks.cs` | Burbujas sobre los cofres. |
| `QuickAdd.cs` | Ctrl + clic derecho en todos lados (parches Harmony, diálogos de NPC, encargos). |
| `HoverRecipe.cs` | Vista rápida con Alt. |
| `GamepadInput.cs` | Control (Rewired). |
| `GameHooks.cs` | Parches: crafteo terminado, construcción, obras del pueblo. |
| `GameStyle.cs`, `GameWindows.cs` | Estilo del juego (botones/fuente) y medición de ventanas abiertas. |
| `Prefs.cs`, `Lang.cs`, `Perf.cs`, `Diagnostics.cs` | Preferencias (flechas/recetas elegidas), 16 idiomas, rendimiento, diagnóstico. |
| `FrameworkBridge/` | Puente opcional con GK2 Mod Framework (0.5.0, en pruebas). |
| `docs/nexus/` | Descripción de Nexus (BBCode), resumen, miniatura (HTML + base) y script de vista previa. |

### Conocimiento del juego que costó descubrir
- **Estación de vista previa segura:** `new WgoData { id = stationId, isTempObject = true }`. El constructor normal
  tiene efectos secundarios (suma calidad al pueblo). Úsala solo para calcular rendimientos.
- Rendimientos: `CraftDef.GetOutputPreview(WgoData)`; necesidades: `NeedItemData.GetCount(WgoData)`. Todas las
  fórmulas dependen solo de perks (`PPar(perk_*)`, `WorkerPar(...)`); no hay mejoras de edificio que cambien el output.
- Estación disponible = construida en algún lado, o alguna `BuildingDef` con ese `wgoId` y modo distinto de
  `None/Remove` desbloqueada y no bloqueada (el `steel_anvil_r` en modo Remove engañaba antes).
- Conteo "tienes" = inventario + almacenes de la zona actual (`MultiInventory(PlayerData, addCurrentPlayerWorldZone: true)`), igual que el juego al craftear.
- Control (Rewired, jugador 0): X=2, Y=3, A=4, B=5, LB=6, RB=7, D-pad ↑12 ↓13 ←14 →15, R3=19.
- Los mods oficiales del juego son solo idiomas/voces; el código se carga únicamente vía BepInEx.

---

## 6. Prueba con control (primera vez con control real)

Según el diseño:
- **Tocar R3** sobre una casilla en una ventana → la agrega a la cola.
- **Mantener R3** sobre una casilla → muestra su receta.
- **R3 en el mundo** → entra a navegar el panel (se pausa la entrada del juego): cruceta ↑↓ moverse, → abrir,
  ← cerrar, LB/RB cambiar receta, X/Y −/+, A pin, B salir.
- Falta: reordenar tareas (▲ ▼) con control.
Anota todo lo que no funcione o se sienta raro y corrígelo con el usuario.

---

## 7. Publicar una versión

**Regla de versiones:** el número **solo sube al publicar** (nada de subirlo en cada compilación de prueba).
Misma versión en GitHub y Nexus. Siguiente: **0.5.0**; arreglos 0.5.1, 0.5.2…

1. Subir `<Version>` en `CraftQueue.csproj` y `FrameworkBridge/*.csproj`, y en `[BepInPlugin]` de `Plugin.cs` y
   del puente. Compilar ambos.
2. Empaquetar dos zips con rutas con `/` (usa `tar` de Windows: `C:\Windows\System32\tar.exe -a -cf x.zip ...`):
   - `CraftingQueue-X.Y.Z.zip`: `BepInEx/plugins/CraftingQueue/...` (+ puente y traducciones, según se decida).
   - `CraftingQueue-X.Y.Z-with-BepInEx.zip`: lo anterior + BepInEx 5.4.23.5 sin modificar
     (`.doorstop_version`, `BepInEx/core`, `winhttp.dll`, `doorstop_config.ini`, `changelog.txt`,
     `BepInEx_LICENSE_NOTICE.txt`).
3. `gh release create vX.Y.Z --prerelease --title "Crafting Queue X.Y.Z (beta)" --notes-file notes.md <zips>`.
   Notas en inglés y español: qué archivo descargar, cómo instalar, qué hay de nuevo.
4. **Nexus:** en la página del mod, en Files, usar **Update** sobre el archivo existente (mantiene el historial).
   Actualizar la descripción desde `docs/nexus/description.bbcode` si cambió.
5. Actualizar `README.md` y `README.es.md` (siempre los dos).
6. **Idea pendiente:** subida automática a Nexus con GitHub Actions al publicar un release (acción oficial
   `Nexus-Mods/upload-action`). La clave de API de Nexus la guarda **el usuario** como secreto del repo
   (Settings → Secrets → Actions). **Nunca pidas ni escribas la clave.** La primera subida es manual (ya hecha).

---

## 8. Reglas del usuario (importantes)

- **Idioma:** el usuario habla español; respóndele en español. README, notas y textos para jugadores en inglés + español.
- **Diseño:** es muy exigente con lo visual: pixel art nítido (escala entera, sin difuminar), sin espacio
  desperdiciado, estilo del juego. Cuida siempre 720p, 1080p, 1440p y 4K.
- **Privacidad:** nunca subas datos personales, rutas con su usuario, correos personales, claves ni tokens.
  El repo es público.
- **Acciones públicas o irreversibles** (publicar, subir releases, mensajes, borrar): confírmalas antes.
- **Extensión de navegador (Claude in Chrome/Brave):** puede estar conectada en **otra computadora**. Antes de
  usarla, confirma con el usuario que es la máquina que tiene enfrente.
- **Créditos:** el mod ya no tiene código del mod GK2Notepad; no le des crédito. BepInEx sí se acredita (LGPL).
- La descripción dice "Made with the help of AI (Claude)" y Nexus lleva la etiqueta AI-Generated Content.

## 9. Pendientes conocidos

- Terminar y probar la integración con el framework → publicar **0.5.0**.
- Probar el control a fondo.
- Nexus: activar los **Donation Points de este mod** en la página "Opt In Your Mods" (el mod recién publicado
  no aparecía todavía en la lista). El usuario ya activó el programa en su cuenta.
- Subida automática a Nexus (sección 7).
- Posible: más traducciones del menú del framework (hoy solo en/es).
