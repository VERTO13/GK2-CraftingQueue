# Crafting Queue para Graveyard Keeper 2

[English](README.md) | **Español**

Un mod que mantiene una **cola de crafteo siempre a la vista**. Muestra qué quieres hacer, qué necesitas, qué ya tienes y en qué cofre está.

> ⚠️ **Versión de prueba (beta).** Todavía se está probando y puede tener algunos bugs. Por favor [repórtalos](#reportar-bugs); ayuda muchísimo.
> 🎮 **El soporte para control es aún más experimental** y casi no se ha probado.

![Crafting Queue en el juego: Ctrl + clic derecho agrega recetas y las burbujas muestran en qué cofre están los materiales](docs/images/usage.gif)

---

## Instalación (unos 2 minutos, sin experiencia)

Solo se hace una vez. Cierra el juego antes de empezar.

### Paso 1: Descarga el mod

Entra a [Releases](../../releases) y descarga **uno** de estos archivos:

- **`CraftingQueue-x.y.z-with-BepInEx.zip`**: elige este si no estás seguro. Trae todo lo necesario.
- `CraftingQueue-x.y.z.zip`: solo el mod, para quien ya juega con otros mods de BepInEx.

### Paso 2: Copia la dirección de la carpeta del juego

1. Abre **Steam** y ve a tu **Biblioteca**.
2. Clic derecho en **Graveyard Keeper 2** → **Administrar** → **Ver archivos locales**. Se abre una carpeta: esa es la *carpeta del juego*.
3. Da clic en la **barra de direcciones**, arriba de la carpeta (donde aparece la ruta), y presiona **Ctrl + C** para copiarla.

> 💡 No importa en qué disco o carpeta tengas el juego: Steam siempre abre la correcta.

### Paso 3: Descomprime el mod en esa carpeta

1. Busca el archivo que descargaste (normalmente está en **Descargas**).
2. Clic derecho sobre él → **Extraer todo…**
3. Borra la ruta que aparece en el cuadro y presiona **Ctrl + V** para pegar la dirección de la carpeta del juego.
4. Da clic en **Extraer**. Si Windows pregunta por reemplazar archivos, elige **Reemplazar**.

### Paso 4: Revisa que quedó bien

Abre otra vez la carpeta del juego. Ahora debe haber una carpeta **`BepInEx`** junto a `GraveyardKeeper2.exe`:

```
Graveyard Keeper 2
├─ BepInEx               ← nuevo
├─ winhttp.dll           ← nuevo
├─ doorstop_config.ini   ← nuevo
└─ GraveyardKeeper2.exe
```

Abre el juego y carga tu partida. Arriba a la derecha aparece el panel **Cola**. 🎉
Para agregar algo, mantén **Ctrl** y da clic derecho sobre cualquier objeto.

<details>
<summary><b>¿Algo salió mal?</b></summary>

| Lo que ves | Qué hacer |
|---|---|
| Dentro de la carpeta del juego quedó una carpeta llamada `CraftingQueue-...` con `BepInEx` adentro | Mueve todo lo que está dentro de esa carpeta a la carpeta del juego y borra la carpeta vacía. |
| El juego abre pero no aparece el panel | Presiona **F3** (puede estar oculto). Si sigue sin aparecer, abre **Solución de problemas** casi al final de esta página. |
| Windows o el antivirus avisan sobre `winhttp.dll` | Ese archivo es parte de BepInEx, el cargador de mods estándar de los juegos de Unity. Es seguro; permítelo. |

</details>

<details>
<summary><b>Actualizar o desinstalar</b></summary>

- **Actualizar:** descarga la versión nueva y descomprímela igual, eligiendo **Reemplazar**. Tu cola se conserva.
- **Desinstalar:** borra la carpeta `BepInEx\plugins\CraftingQueue` dentro de la carpeta del juego. Tus partidas no se tocan.
- Tus colas se guardan fuera de la carpeta del juego, así que se conservan al actualizar o reinstalar. Para borrarlas también, borra `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\CraftingQueue` (pégalo en la barra de direcciones del Explorador).

</details>

---

## Qué hace

### Panel de la cola siempre a la vista

`tienes/necesitas` de cada material. Despliega cualquier material para ver cómo se hace, una receta a la vez, con la estación donde se hace y cuánto sale (`×N`).

![Grasa derretida: dos recetas que se cambian con las flechas (cambian la cantidad y los ingredientes)](docs/images/multi-recipe.gif)

- **Una opción por receta y por estación.** Si un objeto tiene varias recetas, o una receta se hace en varias estaciones, cambias entre ellas con ◂ ▸. Cada opción muestra su estación, su cantidad y lo que pide.
- **Solo lo que puedes usar.** Las recetas que aún no desbloqueas no aparecen, ni las estaciones que todavía no puedes construir. En cuanto las desbloqueas (árbol tecnológico, misiones…), aparecen solas.
- **Cantidades reales.** El `×N` toma en cuenta tus talentos, o los de un zombi si trabaja esa estación. Para una estación que aún no construyes, muestra el edificio base con tus talentos.
- **Descuento automático.** Las tareas bajan solas al craftear, construir o terminar obras del pueblo.
- **Pasa el mouse sobre una tarea** para ver sus botones − + 🗑 y su pin. Arrastra el título para mover el panel; el candado lo deja fijo.

![Botones de una tarea](docs/images/task-buttons.png)

### Agrega lo que sea con Ctrl + clic derecho

Funciona en casi todo lo que muestra un objeto o una receta:

| Dónde | Qué se agrega |
|---|---|
| Inventario, cofres, cualquier objeto | El objeto |
| Una receta en una mesa de crafteo | "Hacer esta receta", con sus ingredientes |
| Menú de construir / obras del pueblo | La construcción con sus materiales |
| Requisitos de misión | El objeto, con la cantidad que pide la misión |
| Árbol tecnológico | La receta, objeto o construcción (plano) que desbloquea |
| Conversaciones con NPC (respuestas con "0/1") y ventanas de pedido | El objeto que te piden, con su cantidad |
| Encargos de comerciantes | El objeto del encargo, con su cantidad |

Si el panel está plegado porque hay una ventana abierta, se despliega un momento para que veas la tarea nueva.

![Quitar tareas y agregar planos desde el árbol tecnológico subiendo su cantidad](docs/images/remove-and-blueprints.gif)

![Un NPC pide un objeto: Ctrl + clic derecho en su ícono para agregarlo a la cola](docs/images/npc-request.png)

### Burbujas en los cofres: ve dónde está todo

Burbujas sobre los cofres y almacenes de tu zona muestran qué materiales de tu cola tienen y cuántos.

![Burbujas sobre los cofres](docs/images/chest-bubbles.png)

- **Pasa el mouse sobre una tarea o un ingrediente** del panel para ver al momento dónde están sus materiales, aunque no tenga pin.
- El **pin junto al candado** (dorado cuando está prendido) marca los materiales de toda la cola. El **pin de una tarea** marca solo esa tarea. Las tareas nuevas empiezan con su pin prendido. Los pines se guardan con tu partida.
- Si ya tienes un objeto, se marca solo el objeto, para que sepas dónde recogerlo. Si no, se marcan sus ingredientes.
- Las burbujas se vuelven transparentes cuando el mouse está encima o cuando tapan a tu personaje.

### Vista rápida de recetas (Alt)

Mantén **Alt** sobre cualquier objeto, en tu inventario, un cofre o el mismo panel de la cola, para ver su árbol de recetas completo sin agregarlo.

![Alt sobre los ingredientes en el panel de la cola](docs/images/alt-view.gif)

![Alt sobre un objeto del inventario](docs/images/alt-inventory.png)

### Y además

- **No estorba:** con un cofre, una mesa o el árbol tecnológico abierto, el panel se recorre de su lado o se pliega en una pestañita "Cola" que se despliega al pasar el mouse. ¿Lo prefieres siempre visible? Prende el **ojo** de la barra del panel.
- **Nítido** en 720p, 1080p, 1440p y 4K.
- **Muy ligero.** Las cantidades se actualizan en su lugar en vez de redibujar el panel, y no corre nada pesado mientras juegas (unos 0.1 ms por cuadro en promedio).
- **Una cola por partida guardada.** Se guarda en su propio archivo y nunca toca tu partida.
- **16 idiomas.** Todos los idiomas oficiales del juego; sigue el idioma del juego al momento.

## Controles

| Acción | Teclado / mouse | Control *(experimental)* |
|---|---|---|
| Agregar a la cola | `Ctrl` + clic derecho | Tocar **R3** sobre lo seleccionado |
| Ver una receta | Mantener `Alt` sobre un objeto | Mantener **R3** sobre lo seleccionado |
| Mostrar / ocultar el panel | `F3` | – |
| Recetas detalladas / compactas | `F4` | – |
| Ver dónde están los materiales de una tarea | Pasar el mouse sobre la tarea | Seleccionarla en el panel |
| Usar el panel | Mouse | **R3** entrar · cruceta ↑↓ moverse · → abrir · ← cerrar · LB/RB cambiar receta · X/Y −/+ · A pin · B salir |

## Reportar bugs

Abre un [issue](../../issues/new/choose) con:

1. Qué hiciste y qué pasó (una captura ayuda mucho).
2. Tu versión del mod y tu resolución.
3. El archivo `BepInEx\LogOutput.log` de la carpeta del juego (arrástralo al issue).

---

<details>
<summary><b>Configuración</b></summary>

Los ajustes están en `BepInEx\config\verto13.gk2.craftingqueue.cfg`. El archivo se crea la primera vez que juegas y cada ajuste viene explicado adentro: teclas, lado del panel, ancho, alto, tamaño de íconos, opacidad, estilo de receta…

Tus colas se guardan en `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\CraftingQueue\`.

**Traducciones:** para corregir una traducción o agregar un idioma, copia `BepInEx\plugins\CraftingQueue\lang\_plantilla_en.txt` como `lang\<código de idioma>.txt` y traduce el lado derecho.

**Diagnóstico:** el ajuste `[Diagnóstico] MedirRendimiento` (apagado de fábrica) escribe tiempos en `BepInEx\LogOutput.log` y una lista de las recetas cuya cantidad depende de talentos en la carpeta de las colas. Solo sirve para buscar problemas y no cambia el juego.

</details>

<details>
<summary><b>Solución de problemas</b></summary>

- **No aparece el panel:**
  1. Revisa que exista `BepInEx\LogOutput.log` en la carpeta del juego. Si no existe, BepInEx no quedó instalado; usa el zip *with-BepInEx*.
  2. Si el log existe, busca `Crafting Queue` adentro.
- **El panel está oculto:** presiona `F3`.
- **No salen burbujas en los cofres:** prende el pin junto al candado (o el de una tarea), o pasa el mouse sobre una tarea del panel. Las burbujas solo muestran los cofres de la zona donde estás.
- **Una receta no dice cómo se hace:** seguramente aún no la desbloqueas. Aparece sola en cuanto lo hagas.

</details>

<details>
<summary><b>Compilar desde el código</b></summary>

```bash
dotnet build -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2"
```

`GameDir` debe apuntar a una instalación del juego con BepInEx 5. Los ensamblados del juego solo se referencian para compilar; nunca se copian a la salida ni a este repositorio.

</details>

## Notas

- Mod no oficial, sin relación con Lazy Bear Games.
- Hecho con apoyo de inteligencia artificial ([Claude](https://claude.com), de Anthropic): el código se escribió junto con Claude y cada función se probó en el juego antes de publicarla.
- No incluye código ni recursos del juego y no envía datos a ningún lado.
- El paquete *with-BepInEx* incluye [BepInEx](https://github.com/BepInEx/BepInEx) 5.4.23.5 sin modificar (LGPL-2.1).

## Licencia

[MIT](LICENSE)
