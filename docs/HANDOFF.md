# Traspaso para una nueva sesión de Claude Code

Este documento es para que otra sesión de Claude Code (en otra computadora) retome **Crafting Queue** sin
perder contexto. Léelo completo antes de tocar código. `CLAUDE.md` (en la raíz) resume las reglas.

---

## 1. Qué es el proyecto

**Crafting Queue** es un mod de BepInEx 5 para **Graveyard Keeper 2** (Unity 6, Mono). Pone en pantalla una
cola de crafteo: qué quieres hacer, qué necesitas, qué tienes y en qué cofre está.

- **GitHub (público):** https://github.com/VERTO13/GK2-CraftingQueue — licencia MIT.
- **Nexus Mods:** https://www.nexusmods.com/graveyardkeeper2/mods/177 (autor en Nexus: LeBetoven).
- **Versión publicada:** 0.5.0 (GitHub Release `v0.5.0` y Nexus, 2026-09-28). La siguiente será **0.5.1**.
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

## 2. Lo que trae la 0.5.0 (publicada el 2026-09-28)

El usuario la publicó sin terminar la lista de pruebas de abajo: hazlas con él y los arreglos van en la 0.5.1. Las
imágenes del README y de Nexus todavía son de la 0.4.x (clips nuevos pendientes). Los dos zips llevan el puente y sus
traducciones; sin el framework, BepInEx no carga el puente y el mod funciona igual.

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
3. **Panel** (probado por el usuario en 1080p el 2026-09-27):
   - Escala de la interfaz desde `LazyUI.ScaleFactor` (el juego: ×2 a 1080p y 1440p "x2", ×4 en 4K). Antes se
     buscaba "el lienzo del juego" y a veces tomaba el de otro mod: a 1440p el panel salía ×3 (queja en Nexus de
     LibertyGTX y UltraJohn: "el texto es mucho más grande que el del juego"). Desde la barra de botones,
     `GameStyle.PanelScale` usa **exactamente** la escala del juego (sin el alto de pantalla ÷ 480, que con alturas de
     1621 a 1679 daba ×3 con el juego en ×4); sin escala del juego todavía, la regla del juego
     (`ResolutionConfig.GetPixelSize`: <720 → 1, ≤1440 → 2, si no ceil(alto/540)). La tabla de las 24 resoluciones
     del juego está en el tablero 10 del lienzo de mockups.
   - **Agarre** en la esquina de abajo del lado de adentro (con el candado abierto): cambia ancho y alto en vivo;
     el `.cfg` se escribe una vez al soltar (`Plugin.LiveHudWidth/LiveHudMaxHeight` mientras se arrastra).
   - "Otra zona: N" pasó del nombre al **globo** al pasar el mouse (antes hacía bajar dos o tres renglones).
   - Íconos que se adaptan al ancho: `TamanoIconos` es el máximo (`min(tamaño × escala, ancho × 0.11)`).
   - Juego 1.006: `QuickAdd` busca `GetGameKeyDelegates` con `GetMethod(DeclaredOnly)` (sin avisos de HarmonyX).
4. **"Adelante con todo"** (2026-09-27, falta que el usuario lo pruebe):
   - **Guardado seguro** de la cola: `.tmp` completo (termina en `# end`) → `File.Replace` con `.bak`; al cargar, si
     falta el archivo, se recupera del `.tmp` o `.bak` completos.
   - **Partida nueva** (`Plugin.SlotChanged`, sin parches al juego): el juego reusa nombres de ranura
     (`GetNameForNewSlot` da el primer `Steam_N` libre). Al cargar una partida que es otro objeto
     `SaveSlotData` y que el juego no tiene guardada (`GameState.IsSaved`, lee el campo `saveSlotDataList`), una
     cola con su nombre es de una partida borrada o no guardada y se aparta a `CraftingQueue/anteriores/` (se
     guardan las 20 más recientes). Mismo objeto con otro nombre (la demo, al guardarse): la cola se muda.
   - **Lección (2026-09-27):** la primera versión parchaba `MainGame.StartNewGameWithSlotName`, `SaveSystem.Remove`
     y `SaveSystem.Save` con Harmony y **rompió el juego**: al parchar, Mono compila el método y, como su cuerpo lee
     `PlayerSkinHelper.playerStandardCustomizationData` (clase `beforefieldinit`), corre ese constructor estático en
     ese momento, antes de que el juego cargue sus datos; falla, la clase queda inservible toda la sesión
     (`TypeInitializationException`) y `MainGame.Awake` ya no crea al personaje (pantalla de "Error" con la
     interfaz en ruso y "ver. 0.000"). En el log de BepInEx solo se ve `IL Compile Error` en el parche. **No parchar
     métodos cuyo cuerpo lea campos estáticos de clases del juego que dependen de datos** (revisar su IL antes).
   - **Peleas:** el panel se oculta en la preparación y en la pelea (`GameState.InFight`), y el control no usa R3
     ahí (en la preparación, R3 = "terminar preparación" en `UIBuildingWindow`; antes además agregaba el edificio
     enfocado a la cola).
   - **Encargos (1.006):** Ctrl + clic en un encargo pide solo lo que falta (`GameData.OrderMissing`).
   - **Vista Total** (la hoja de la barra de botones, ajuste `Vista`): renglones "hoja" del plan sumados por material
     (`Plan.Totals`).
   - **Vaciar la cola:** bote de la barra de botones (dos clics, `ClearConfirm`) y botón en el menú Mods
     (`Plugin.ClearQueueLabel/ClearQueueClick`, que el puente llama por reflexión). Íconos elegidos por el usuario
     (tablero 6 del lienzo de mockups): bote gris con tapa separada, rojo con el mouse encima, palomita dorada tras el
     primer clic. El bote rojo relleno de antes se leía como "!".
   - **Shift** en − + (±10; − se detiene en 1) y en ▲ ▼ (hasta arriba/abajo). **Globos** en todos los botones.
   - Ajustes nuevos: `OcultarSiVacia`, `Vista` (Tareas/Total). `OcultarSiVacia` también en el menú del framework.
   - 17 textos nuevos en los 16 idiomas (`Lang.MoreKeys`/`Lang.More`).
5. **Barra de botones** (`ButtonBar.cs`, 2026-09-27; diseño en los tableros 8 a 13 del lienzo de mockups):
   - Siete celdas en tres grupos: bolsa, cofre · hoja (vista Total), ojo (siempre visible), pin (marcar cofres) ·
     candado, bote. Sustituyen a los íconos chiquitos de la barra de "Cola" (ojo, pin, candado, Σ y bote).
   - Celda, marco dorado de selección, sombra de "inactivo", bolsa y cofre son **sprites del juego** buscados por
     nombre (`comm-item_cell-dark`, `selection` de 42×42, `comm-item-inactive_shade`,
     `comm-header_2-type_icon-main_inventory`, `comm-header_2-type_icon-simple_chest`) con
     `Resources.FindObjectsOfTypeAll<Sprite>` **una vez, durante la carga de la partida** (`BarArt.Search`, desde
     `Plugin.Update`); las piezas se rehacen con `Sprite.Create` en nueve partes (esquinas de 8) para llevarlas a
     26 × 26. Si falta alguna, hay dibujo propio. El resto de los íconos son dibujos propios (hoja, ojo abierto y
     cerrado, pin, candado, bote, palomita) con la paleta de los mockups. **No se incluye arte del juego en el repo.**
   - Prendido = marco dorado; apagado = sombra (el ojo apagado además se cierra). Todo mide 26 pixeles del juego y
     nunca se estira: solo la escala del juego cambia su tamaño.
   - **La esquina ⋮** (dibujo del usuario, tablero 13): una celda afuera del panel, en su esquina de arriba del lado
     de adentro (el que mira al centro). Un clic saca o guarda la barra (`MostrarBotones`), que se desliza desde la
     esquina en pixeles enteros (`QueueHud.LateUpdate`: `barSlide` de 0 a 1 en 0.2 s; la bandeja con
     `RectMask2D` recorta los botones mientras salen). Mantener y deslizar la esquina arrastra el panel. Clic derecho en la esquina: vertical ↔ horizontal (la barra
     vuelve a salir hacia el lado nuevo). «Cola» sigue
     siendo el título del panel.
   - **Vertical** (de fábrica): 30 × 200, baja por el costado desde la esquina; si abajo no cabe (panel pegado abajo),
     sube desde la esquina (`QueueHud.BarUp`). **Horizontal** (`BotonesArriba = true`): corre por encima del panel;
     los grupos bajan de renglón completos (1 renglón desde 200 de ancho, 2 de 142 a 199, 3 por debajo) y el panel
     baja con ellos: al sacarla, primero baja el panel y después corren los botones (`QueueHud.BarHead`).
   - **Posición:** `DistanciaArriba` es ahora la de lo más alto (la esquina o los renglones de arriba); el panel va
     debajo. La esquina y la barra cuentan como parte del panel en `Fit` (recorrerse junto a una ventana, no salirse
     de la pantalla), en los globos y en la vista con Alt (`QueueHud.OuterEdges`); se esconden con el panel plegado.
   - **Íconos de objeto** en las celdas del panel: el recorte del atlas (`GameStyle.Trimmed`, sin el margen
     transparente del lienzo de 48 × 48), así llenan su celda.
   - **Letra:** `TamanoLetra` (0 = la del juego) también en el menú del framework, libre de 1 en 1 (8, 16, 24 y 32 se
     ven perfectos; los de en medio, un poco disparejos).
   - **Qué se cuenta** (`ContarLoQueLlevas`, `ContarCofres`, reemplazan a `SoloMochila`, que nunca se publicó):
     bolsa = inventario, cofre = almacenes de la zona (todo menos lo que llevas), las dos = como el juego al craftear.
     Nunca las dos apagadas (`Plugin.ToggleCount` y `SettingChanged`). Cambiar qué se cuenta no completa tareas
     (`Plan.Tick` lo toma como contexto nuevo). Los tres ajustes también en el menú del framework.
   - **Menú Mods con opciones en palabras** (idea del usuario: no todo es On/Off): 12 renglones del puente son botones
     (`AddButton`) cuyo texto es la opción elegida en el idioma del jugador y que con cada clic pasan a la siguiente
     (`Plugin.ChoiceLabel/ChoiceNext` por reflexión; las opciones, en `Plugin.BindChoices`). Guardan en las mismas
     claves del .cfg; "Contar" junta `ContarLoQueLlevas`+`ContarCofres` y "ConVentanas" junta `OcultarConVentanas`+
     `MostrarEnCofresYMesas`. Se hizo así porque la lista desplegable del framework muestra el valor crudo (no traduce
     sus opciones). Estos renglones no tienen el botón «Restablecer» del framework (son de solo lectura para él).
   - 30 textos nuevos (`count_bag_tip`, `count_chests_tip`, `bar_show`, `bar_hide`, `bar_turn` y 25 `opt_*` de las
     opciones del menú) en los 16 idiomas.
   - README (en/es) y `docs/nexus/description.bbcode` ya describen todo esto: subir la descripción a Nexus al publicar.
6. **Escenas y rendimiento** (2026-09-27, falta que el usuario lo pruebe):
   - **Se esconde cuando el juego esconde su HUD** (`GameState.HudHidden`, reemplaza a `InCutscene`, que buscaba
     `GameObject.Find("UIRoot/HUD")` y nunca se comprobó que lo encontrara). Lee las banderas del propio juego sin
     parches: el HUD se toma de `LazyUI.guiElementsDictionary` y su campo `disableStateType`
     (`MultiFlagAND<HudStateType>`) dice por qué está escondido: `Cinematic` (escenas con franjas negras,
     `UICinematic`), `CinematicsScene` (las ilustradas, `CinematicsSceneDisplayManager`), `BuildController` (colocando
     una construcción) y `MainMenu` (menú principal y la carga, hasta que empieza la partida). El panel, las burbujas
     y el control lo siguen al instante, y el log dice "El juego escondió su HUD (…)" / "volvió a mostrar" en cada
     cambio: así se confirma una escena después de jugar. El juego también lo anota en `Player.log`
     (`HUD: SetDisableState:[Cinematic] …`).
   - **El panel ya no se rearma entero al volver a verse** (después de un menú, un diálogo, una escena, la carga o un
     cambio de zona): se queda armado mientras está oculto y al volver solo se revisan los números (`UpdateCounts`, que
     rearma si cambió la forma del plan). Los globos "Otra zona: N" se calculan al mostrarse (`tipTargets` guarda
     funciones), así no quedan viejos.
   - **Cambiar de zona ya no recalcula todas las recetas:** `GameData.StationsStamp` contaba los objetos de la zona
     actual, así que cada cambio de zona vaciaba los cachés de estaciones y opciones de receta. Ahora solo cuenta un
     cambio en esa cuenta sin cambiar de zona (se construyó o quitó algo) y `ResetStations`, que también se llama al
     **quitar** una estación (`GameHooks.AfterBuild` en modo Remove; antes solo lo notaba la cuenta de la zona, y si
     salías de la zona antes de la siguiente revisión, se quedaba con la estación quitada). Las estaciones se buscan
     en todo el mundo (`WorldData.GetWgoDataList` usa un caché global por id), así que la zona no importa.
   - `GameData.Elsewhere` ya no recorre todos los objetos del mundo cada vez: la lista de almacenes de otras zonas se
     rearma solo si cambió la cuenta de objetos de alguna zona (o cada 30 s). `StationAvailable` usa las
     construcciones agrupadas por estación una sola vez.
   - Al abrir o cerrar una ventana, su área se mide 30 veces por segundo durante 0.6 s (antes, en cada cuadro) y sin
     crear listas nuevas. Las burbujas de los cofres miden su texto con un solo objeto reusado.
   - **Tirones anotados siempre:** con el diagnóstico apagado, si en un cuadro el mod tarda más de 5 ms, el log dice
     `[Rendimiento] cuadro pesado del mod: …` con la parte que más tardó (máximo una línea cada 30 s).

### Lo que falta probar (hazlo con el usuario, en su juego)
- [ ] **Escenas:** al colocar una construcción (mismo mecanismo que una escena), el panel y las burbujas se esconden y
      vuelven al terminar; el log de BepInEx dice "El juego escondió su HUD (BuildController)" y "volvió a mostrar".
      En la próxima escena de historia, buscar "(Cinematic)" o "(CinematicsScene)" en el log.
- [ ] Al cargar una partida, el panel aparece cuando aparece el HUD del juego (no durante la carga).
- [ ] Cerrar un menú o un diálogo, o cambiar de zona: el panel vuelve sin trabarse y con los números al día; buscar
      `[Rendimiento] cuadro pesado` en el log después de jugar un rato.
- [ ] Con el framework instalado: ESC → **Mods** → **Crafting Queue** muestra las 3 secciones con nombres bien
      traducidos (español si el juego está en español).
- [ ] Cambiar una tecla en el menú (p. ej. F3 → F5) y que funcione **sin reiniciar**. Revisa también que el
      `.cfg` guarde el nuevo valor.
- [ ] Mover deslizadores (ancho, opacidad…) y que el panel cambie en vivo.
- [ ] Interruptores (burbujas, siempre visible…) y que coincidan con los botones del panel (pin dorado, ojo).
- [ ] **Sin el framework** (quítalo temporalmente): el mod carga normal y el log dice que el puente se omitió.
- [ ] Que un `.cfg` viejo (teclas guardadas como `KeyCode`) no pierda valores.
- [ ] **Control / gamepad** (ver sección 6): es la primera vez que se prueba con control real.
- [ ] Vista Total (Σ): sumas correctas, lo que falta primero, globo de otra zona; volver a Tareas.
- [ ] Bote: gris; rojo con el mouse encima; primer clic → palomita dorada + globo "otra vez"; clic en la palomita
      vacía; a los 4 s vuelve el bote. Botón del menú Mods: dos clics (texto que cambia).
- [ ] Shift en − + ▲ ▼. Globos de todos los botones (y que no tapen nada).
- [ ] Partida nueva después de borrar una: empieza sin cola, y la vieja queda en `CraftingQueue/anteriores/`.
- [ ] El juego arranca normal y el log de BepInEx no muestra `IL Compile Error` (ver la lección de arriba).
- [ ] Pelea: el panel se oculta en la preparación y vuelve al terminar; R3 termina la preparación sin agregar nada.
- [ ] Encargo con parte entregada: Ctrl + clic pide solo lo que falta.
- [ ] `OcultarSiVacia`, `ContarLoQueLlevas`, `ContarCofres`, `BotonesArriba`, `MostrarBotones` y `TamanoLetra`
      desde el menú Mods.
- [ ] Esquina ⋮: clic saca y guarda la barra deslizándose (vertical y horizontal); mantener y deslizar mueve el panel;
      marco dorado con la barra afuera; se acuerda al reiniciar.
- [ ] Vertical: celdas del juego (el log dice "Barra de botones: arte del juego …" con los tamaños), marco dorado /
      sombra, ojo cerrado, globos por fuera de la barra; panel a la izquierda → esquina y barra a su derecha; panel
      pegado abajo → la barra sube desde la esquina; con un cofre abierto se recorre sin taparlo o se pliega (y la
      esquina y la barra se esconden).
- [ ] Horizontal: un renglón con 300 de ancho, dos con 170 (el panel baja con el segundo), tres con menos de 142;
      cambiar el ancho con el agarre; «Cola» debajo de los botones.
- [ ] Íconos de objeto llenando su celda en tareas, ingredientes y el estilo compacto.
- [ ] Tamaño de letra desde el menú Mods (el panel se rehace con la letra nueva).
- [ ] Bolsa y cofre: solo la bolsa cuenta lo que llevas; solo el cofre, lo guardado; apagar el único prendido prende el
      otro; ninguna tarea se completa sola al cambiarlo.
- [ ] 1440p (x2): la letra del panel del mismo tamaño que la del juego.

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
  después de probar.** Apagado, igual se anota `[Rendimiento] cuadro pesado del mod` si un cuadro del mod pasa de
  5 ms (máximo una línea cada 30 s).
- Log del propio juego (sus `Debug.Log`, sin marcas de hora):
  `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\Player.log` (y `Player-prev.log`).

---

## 5. Mapa del código

| Archivo | Qué hace |
|---|---|
| `Plugin.cs` | Entrada BepInEx (GUID `verto13.gk2.craftingqueue`), config (secciones en español), arranque. |
| `Queue.cs` | Tareas por partida, persistencia en texto (`task`/`pin`, guardado seguro con `.tmp`/`.bak`), pines, agregar (`oneMore`), reordenar, apartar/renombrar colas de ranuras, vaciar (`ClearConfirm`). |
| `Plan.cs` | Reparto de lo que tienes en orden de la cola; renglones por ruta; completado automático (`Tick`); vista Total (`Totals`). |
| `QueueView.cs` | Convierte tareas en entradas para el panel. |
| `QueueHud.cs` | El panel (UGUI en tiempo real): bloques, árbol de recetas, botones ▲ ▼ − + 🗑, pines, plegado, navegación con control, actualización de números en su lugar. |
| `ButtonBar.cs` | La barra de botones del panel (bolsa, cofre · Total, ojo, pin · candado, bote), al lado o arriba; `BarArt`: sprites del juego y dibujos propios. |
| `GameData.cs` | Todo lo que se lee del juego: recetas (`OptionsFor`), rendimientos con talentos, estaciones disponibles, conteos (`Owned`), otras zonas (`Elsewhere`). |
| `ChestMarks.cs` | Burbujas sobre los cofres. |
| `QuickAdd.cs` | Ctrl + clic derecho en todos lados (parches Harmony, diálogos de NPC, encargos). |
| `HoverRecipe.cs` | Vista rápida con Alt. |
| `GamepadInput.cs` | Control (Rewired). |
| `GameHooks.cs` | Parches: crafteo terminado, construcción, obras del pueblo; `GameState` (partida, si está guardada, HUD del juego escondido, peleas). |
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
- Control (Rewired, jugador 0; tabla en `GamepadController..ctor`): X=2, Y=3, A=4, B=5, LB=6, RB=7, LT=8, RT=9,
  View/Back=10, Start=11, D-pad ↑12 ↓13 ←14 →15, R3=19, L3=20 (stick derecho: ejes 16/17). Las acciones del juego
  son `GameKey` (clase tipo enum en `LazyBearTechnology`) y cada una se asigna a un `GamepadButton` en el asset
  `GameBindings` (no está en el código).
- `LazySingleton<T>.Instance` **crea** el objeto si no existe: para leer uno que quizá no exista (p. ej.
  `FightingGameController`), leer su campo estático `instance`. `FightState`: Disabled, InPreFight, ActiveFight.
- Ranuras: `SaveSystem.GetNameForNewSlot` = primer `{plataforma}_{n}` libre desde 1; nueva partida →
  `MainGame.StartNewGameWithSlotName` (privado); borrar → `SaveSystem.Remove(SaveSlotData, Action)`; la demo se
  renombra dentro de `SaveSystem.Save` (si `isDemoSave`). Los autoguardados son la misma ranura con una marca.
  `IsLimitedSaveSlotsEnabled` es `false` fijo en PC.
- Encargos 1.006 (`VendorSystem.TryResolveOrders`): cuenta lo que hay en las tarimas (`interactionType` 31) de
  `warehouse` y `warehouse_cellar`; los normales se entregan por partes (`VendorOrderData.Count`), los urgentes
  completos o nada.
- Escala de la interfaz: `LazyUI.ScaleFactor` (= `ResolutionConfig.PixelSize`, lo pone
  `GUIElements.ApplyUiForResolution`).
- **HUD del juego escondido:** `HUD.SetDisableState(HudStateType, bool enabled, HUDData)` actualiza
  `HUD.disableStateType` (`MultiFlagAND<HudStateType>`: el resultado es el AND de todas; las que faltan cuentan como
  `true`) y, si queda en `false`, `Hide()` = `SetActive(false)`. `HudStateType`: MainMenu=0, Cinematic=1,
  BuildController=2, AnimationTestingManager=3, CinematicsScene=4. Quién lo llama: `UICinematic.Enable/DisableCinematic`
  (desde `Flow_Cinematic` y `Flow_SetControlActive` con `isAffectCinematic`), `CinematicsSceneDisplayManager`,
  `BuildController.Enable/DisableBuildMode` y `MainGame` (menú y carga). El juego toma el HUD con `LazyUI.Get<HUD>()`,
  que lanza una excepción si no está en `guiElementsDictionary`. El control del jugador tiene su propio
  `MultiFlagAND<TakenControlType>` (`PlayerController.IsControlEnabledByType`, `ByCinematics`=11).
- Los mods oficiales del juego son solo idiomas/voces; el código se carga únicamente vía BepInEx.

---

## 6. Prueba con control (primera vez con control real)

Según el diseño:
- **Tocar R3** sobre una casilla en una ventana → la agrega a la cola.
- **Mantener R3** sobre una casilla → muestra su receta.
- **R3 en el mundo** → entra a navegar el panel (se pausa la entrada del juego): cruceta ↑↓ moverse, → abrir,
  ← cerrar, LB/RB cambiar receta, X/Y −/+, A pin, View vista Total, B salir.
- En la preparación de una pelea y en la pelea el mod no usa R3 (el panel está oculto).
- Falta: reordenar tareas (▲ ▼) con control.
Anota todo lo que no funcione o se sienta raro y corrígelo con el usuario.

---

## 7. Publicar una versión

**Regla de versiones:** el número **solo sube al publicar** (nada de subirlo en cada compilación de prueba).
Misma versión en GitHub y Nexus. Siguiente: **0.5.1** (arreglos de la 0.5.0); después 0.5.2…

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

- Probar la 0.5.0 con el usuario (lista de la sección 2) y arreglar en la **0.5.1**.
- Probar el control a fondo.
- Nexus: activar los **Donation Points de este mod** en la página "Opt In Your Mods" (el mod recién publicado
  no aparecía todavía en la lista). El usuario ya activó el programa en su cuenta.
- Subida automática a Nexus (sección 7).
- Posible: más traducciones del menú del framework (hoy solo en/es).
- Posible optimización: la vista Total se rearma entera cuando cambia lo que tienes (una vez por segundo como mucho,
  solo con esa vista abierta); podría actualizar los números en su lugar como la vista de tareas.
- Capturas/GIF nuevos para el README y Nexus: barra de botones, vista Total, bote, agarre (las de ahora muestran la
  barra de título vieja y el aviso de zona en gris, que ya no existe).
- Responder en Nexus a LibertyGTX y UltraJohn (letra más grande que la del juego en 1440p, ocultar el panel vacío,
  achicarlo) cuando salga la 0.5.0: todo queda resuelto en esa versión.
- Competencia (revisada el 2026-09-27): **Shopping List** de Saint ArchI (Nexus 45, el más parecido; cuenta solo la
  mochila, vista por tarea o sumada, pedidos del pueblo), su **Codex** y **Thoughtful Week**; Pin My Recipe, Queue
  Count, Kebo, Keeper's Little Helpers. Saint ArchI mantiene la guía de Steam de mods de calidad de vida
  (https://steamcommunity.com/sharedfiles/filedetails/?id=3806471142) e invita a sugerir mods en los comentarios:
  el usuario puede comentar (borrador en la sesión del 2026-09-27; es acción pública, la hace él).
