using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CraftQueue;

// Crafting Queue: una cola de lo que quieres hacer, siempre a la vista mientras juegas.
// - Ctrl + clic derecho agrega objetos, recetas, construcciones y obras del pueblo.
// - Panel en pantalla con "tienes/necesitas", recetas desplegables (una a la vez, con ◂ ▸),
//   botones − + basura, scroll, se puede mover y fijar con el candado.
// - Descuento automático al craftear, construir o terminar obras del pueblo.
// - Mantener Alt sobre cualquier objeto muestra su receta.
[BepInPlugin(Guid, "Crafting Queue", "0.4.16")]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "verto13.gk2.craftingqueue";

    internal static ManualLogSource Log { get; private set; }

    // Teclas como KeyboardShortcut (el formato de BepInEx que también usa GK2 Mod Framework para editarlas
    // desde su menú Mods). Un valor viejo como "LeftControl" se lee igual. Se usa solo la tecla principal.
    private static ConfigEntry<KeyboardShortcut> addModifier, hoverKey, hudKey, recipeStyleKey;
    private static ConfigEntry<float> hoverTextSize, hudTop, hudSideOffset, hudWidth, hudMaxHeight, hudTextSize, hudIconSize, hudScale, hudOpacity;
    private static ConfigEntry<bool> hudVisible, hudHideWithWindows, hudInWorkWindows, hudMovable, chestMarks, measurePerf, hudAlwaysOpen, pinNewTasks,
        hudHideEmpty, countCarried, countChests, buttonsOnTop, buttonsShown;
    private static ConfigEntry<string> hudSide, recipeStyle, hudView;
    private static ConfigEntry<int> hudMaxRows;

    internal static KeyCode AddModifier => addModifier.Value.MainKey;
    internal static float HoverTextSize => hoverTextSize.Value;
    internal static KeyCode HudKey => hudKey.Value.MainKey;
    internal static bool HudVisible { get => hudVisible.Value; set => hudVisible.Value = value; }
    internal static bool HudHideWithWindows => hudHideWithWindows.Value;
    internal static bool HudInWorkWindows => hudInWorkWindows.Value;
    internal static bool HudMovable { get => hudMovable.Value; set => hudMovable.Value = value; }
    internal static bool ChestMarks { get => chestMarks.Value; set => chestMarks.Value = value; }
    internal static bool HudAlwaysOpen { get => hudAlwaysOpen.Value; set => hudAlwaysOpen.Value = value; }
    internal static bool PinNewTasks => pinNewTasks.Value;
    internal static bool HudHideEmpty => hudHideEmpty.Value;
    // Qué se cuenta como "tienes": la bolsa (lo que llevas encima) y el cofre (los almacenes de la zona)
    // de la barra de botones. Nunca las dos apagadas: un .cfg editado a mano con las dos en false cuenta
    // las dos, como de fábrica.
    internal static bool CountCarried => countCarried.Value || !countChests.Value;
    internal static bool CountChests => countChests.Value || !countCarried.Value;
    // Botones del panel: salen de la esquina ⋮; false = por el costado (de fábrica), true = por encima del panel.
    internal static bool ButtonsOnTop { get => buttonsOnTop.Value; set => buttonsOnTop.Value = value; }
    // La barra afuera o guardada en su esquina ⋮.
    internal static bool ButtonsShown { get => buttonsShown.Value; set => buttonsShown.Value = value; }

    // Clic en la bolsa o el cofre: prende o apaga ese; si era el único prendido, se prende el otro.
    internal static void ToggleCount(bool carried)
    {
        bool bag = CountCarried, chests = CountChests;
        if (carried)
            bag = !bag;
        else
            chests = !chests;
        if (!bag && !chests)
        {
            bag = !carried;
            chests = carried;
        }
        countCarried.Value = bag;
        countChests.Value = chests;
    }
    // Vista Total: todo lo que pide la cola junto, un renglón por material (el Σ de la barra del panel).
    internal static bool TotalView
    {
        get => hudView.Value.StartsWith("Total", StringComparison.OrdinalIgnoreCase);
        set => hudView.Value = value ? "Total" : "Tareas";
    }
    internal static bool HudLeft => hudSide.Value.StartsWith("Izq", StringComparison.OrdinalIgnoreCase)
                                    || hudSide.Value.StartsWith("Left", StringComparison.OrdinalIgnoreCase);
    internal static float HudTop => hudTop.Value;
    internal static float HudSideOffset => hudSideOffset.Value;
    // Mientras se arrastra el agarre del panel, el tamaño nuevo se ve en vivo sin escribir el .cfg
    // en cada cuadro; al soltar se guarda una sola vez (SetHudSize).
    internal static float? LiveHudWidth, LiveHudMaxHeight;
    internal static float HudWidth => LiveHudWidth ?? hudWidth.Value;
    internal static float HudMaxHeight => LiveHudMaxHeight ?? hudMaxHeight.Value;
    internal static float HudTextSize => hudTextSize.Value;
    internal static float HudIconSize => hudIconSize.Value;
    internal static float HudScale => hudScale.Value;
    internal static float HudOpacity => hudOpacity.Value;
    internal static int HudMaxRows => hudMaxRows.Value;
    internal static KeyCode RecipeStyleKey => recipeStyleKey.Value.MainKey;
    internal static bool CompactRecipes
    {
        get => recipeStyle.Value.StartsWith("Comp", StringComparison.OrdinalIgnoreCase);
        set => recipeStyle.Value = value ? "Compacta" : "Detallada";
    }

    internal static bool HoverHeld()
    {
        KeyCode k = hoverKey.Value.MainKey;
        if (k == KeyCode.None)
            return false;
        if (k == KeyCode.LeftAlt || k == KeyCode.RightAlt)
            return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        return Input.GetKey(k);
    }

    // Guarda donde el jugador dejó el panel al arrastrarlo.
    internal static void SetHudPosition(bool left, float side, float top)
    {
        hudSide.Value = left ? "Izquierda" : "Derecha";
        hudSideOffset.Value = Mathf.Max(0f, side);
        hudTop.Value = Mathf.Max(0f, top);
    }

    // Los mismos límites que el .cfg y el menú del framework (Ancho 100–800, AltoMaximo 60–1000).
    internal static void SetHudSize(float width, float maxHeight)
    {
        hudWidth.Value = Mathf.Clamp(Mathf.Round(width), 100f, 800f);
        hudMaxHeight.Value = Mathf.Clamp(Mathf.Round(maxHeight), 60f, 1000f);
    }

    // Botón "Vaciar la cola" del menú Mods. El puente (FrameworkBridge) no referencia este DLL:
    // llama estos dos por reflexión. Con confirmación: el primer clic pide otro antes de 4 segundos.
    public static string ClearQueueLabel()
    {
        if (!Queue.HasSlot)
            return Lang.T("no_game");
        int n = Queue.Tasks.Count;
        if (n == 0)
            return Lang.T("queue_empty");
        return ClearConfirm.Armed ? Lang.T("clear_confirm", n) : Lang.T("clear_n", n);
    }

    public static void ClearQueueClick() => ClearConfirm.Press();

    // --- Opciones del menú Mods que no son "sí/no" ---
    // El puente muestra cada una como un botón con la opción elegida, en el idioma del jugador (la lista
    // desplegable del framework no traduce sus opciones); un clic pasa a la siguiente. Guardan en las mismas
    // claves del .cfg de siempre (dos de ellas juntan dos claves). También se llaman por reflexión.
    private sealed class Choice
    {
        public string[] labels; // claves de Lang, en orden
        public Func<int> get;   // la opción elegida ahora
        public Action<int> set; // elegir otra
    }

    private static readonly System.Collections.Generic.Dictionary<string, Choice> choices =
        new System.Collections.Generic.Dictionary<string, Choice>();

    private static void AddChoice(string id, string[] labels, Func<int> get, Action<int> set) =>
        choices[id] = new Choice { labels = labels, get = get, set = set };

    public static string ChoiceLabel(string id)
    {
        if (!choices.TryGetValue(id, out Choice c))
            return id;
        return Lang.T(c.labels[Mathf.Clamp(c.get(), 0, c.labels.Length - 1)]);
    }

    public static void ChoiceNext(string id)
    {
        if (!choices.TryGetValue(id, out Choice c))
            return;
        c.set((c.get() + 1) % c.labels.Length);
        QueueHud.Dirty = true; // rehacer el panel con la opción nueva
    }

    private static void BindChoices()
    {
        AddChoice("Vista", new[] { "opt_by_task", "total" }, () => TotalView ? 1 : 0, i => TotalView = i == 1);
        AddChoice("Recetas", new[] { "opt_detailed", "opt_compact" }, () => CompactRecipes ? 1 : 0, i => CompactRecipes = i == 1);
        AddChoice("Contar", new[] { "opt_count_both", "opt_count_carried", "opt_count_chests" },
            () => CountCarried && CountChests ? 0 : CountCarried ? 1 : 2,
            i =>
            {
                // En este orden, para que ni un momento queden las dos apagadas.
                if (i == 2)
                {
                    countChests.Value = true;
                    countCarried.Value = false;
                }
                else
                {
                    countCarried.Value = true;
                    countChests.Value = i == 0;
                }
            });
        AddChoice("Burbujas", new[] { "opt_marks_all", "opt_marks_pinned" }, () => ChestMarks ? 0 : 1, i =>
        {
            ChestMarks = i == 0;
            if (ChestMarks)
                Queue.ClearPins(); // como el pin de la barra: toda la cola en vez de tareas sueltas
        });
        AddChoice("PinNuevas", new[] { "opt_with_pin", "opt_no_pin" }, () => pinNewTasks.Value ? 0 : 1, i => pinNewTasks.Value = i == 0);
        AddChoice("ColaVacia", new[] { "opt_hint", "opt_hide" }, () => hudHideEmpty.Value ? 1 : 0, i => hudHideEmpty.Value = i == 1);
        // Se ve con ventanas abiertas: con cofres y mesas (de fábrica) / con ninguna / con todas.
        AddChoice("ConVentanas", new[] { "opt_win_work", "opt_win_none", "opt_win_all" },
            () => !hudHideWithWindows.Value ? 2 : hudInWorkWindows.Value ? 0 : 1,
            i =>
            {
                hudHideWithWindows.Value = i != 2;
                hudInWorkWindows.Value = i != 1;
            });
        AddChoice("SiTapa", new[] { "opt_aside", "opt_over" }, () => HudAlwaysOpen ? 1 : 0, i => HudAlwaysOpen = i == 1);
        AddChoice("Barra", new[] { "opt_out", "opt_tucked" }, () => ButtonsShown ? 0 : 1, i => ButtonsShown = i == 0);
        AddChoice("BarraSale", new[] { "opt_side", "opt_top" }, () => ButtonsOnTop ? 1 : 0, i => ButtonsOnTop = i == 1);
        AddChoice("Mover", new[] { "opt_movable", "opt_fixed" }, () => HudMovable ? 0 : 1, i => HudMovable = i == 0);
        AddChoice("Lado", new[] { "opt_right", "opt_left" }, () => HudLeft ? 1 : 0, i => hudSide.Value = i == 1 ? "Izquierda" : "Derecha");
    }

    private static bool started;
    private string lastSlot;
    private SaveSlotData lastSlotData; // la última partida cargada (objeto) y su nombre
    private string lastLoadedSlot;
    private static string dataFolder;

    private void Awake()
    {
        // Solo una vez: si Unity volviera a crear el componente, no duplicar panel ni descuentos.
        if (started)
        {
            Destroy(this);
            return;
        }
        started = true;
        Log = Logger;
        BindConfig();
        BindChoices();

        string data = dataFolder = Path.Combine(Application.persistentDataPath, "CraftingQueue");
        Queue.Init(data);
        Prefs.Load(data);
        try { Lang.Init(Path.GetDirectoryName(Info.Location)); }
        catch (Exception e) { Log.LogError("Idiomas: " + (e.InnerException ?? e).Message); }

        Harmony harmony = new Harmony(Guid);
        GameHooks.Apply(harmony);
        QuickAdd.Apply(harmony);
        GameStyle.Apply(harmony); // botones del juego: se guardan cuando el juego los crea

        gameObject.AddComponent<QuickAdd>();
        gameObject.AddComponent<QueueHud>();
        gameObject.AddComponent<HoverRecipe>();
        gameObject.AddComponent<ChestMarks>();
        gameObject.AddComponent<GamepadInput>();
        Log.LogInfo($"Crafting Queue listo: {AddModifier} + clic derecho agrega, {HudKey} muestra u oculta el panel. Colas en {data}");
    }

    private void Update()
    {
        Perf.On = measurePerf.Value;
        long t = Perf.Start();
        string slot = GameState.Slot;
        if (slot != lastSlot)
        {
            SlotChanged(slot);
            lastSlot = slot;
            if (slot != null)
            {
                GameStyle.SearchButtonsOnce(); // durante la carga de la partida, una sola vez
                BarArt.Search();               // la celda, el marco, la bolsa y el cofre del juego (idem)
                Diagnostics.DumpRecipesOnce(dataFolder); // solo con Diagnóstico activado
            }
        }
        Queue.SyncSlot(slot); // cada partida guardada tiene su propia cola
        Perf.Stop("partida y botones", t);
        Plan.Tick(); // quita las tareas que se completan (cada medio segundo)
    }

    private void LateUpdate() => Perf.EndFrame(Time.unscaledDeltaTime);

    // El juego reusa los nombres de ranura (una partida nueva toma el primer "Steam_N" libre), así
    // que antes de cargar la cola de una partida:
    //  - la misma partida con otro nombre (las de la demo, al guardarse por primera vez): la cola se muda;
    //  - otra partida que el juego no tiene guardada (nueva): una cola con su nombre es de una partida
    //    borrada, o de una nueva que no se llegó a guardar, y se aparta.
    // (Volver a la misma partida tras un parpadeo del nombre no hace nada: es el mismo objeto.)
    private void SlotChanged(string slot)
    {
        if (slot == null)
            return;
        SaveSlotData data = GameState.SlotData;
        if (data == null)
            return;
        bool samePlay = ReferenceEquals(data, lastSlotData);
        if (samePlay && lastLoadedSlot != null && lastLoadedSlot != slot)
            Queue.Rename(lastLoadedSlot, slot);
        else if (!samePlay && GameState.IsSaved(slot) == false)
            Queue.Retire(slot, "partida nueva");
        lastSlotData = data;
        lastLoadedSlot = slot;
    }

    private void BindConfig()
    {
        addModifier = Config.Bind("Controles", "Modificador", new KeyboardShortcut(KeyCode.LeftControl),
            "Mantén esta tecla y da clic derecho sobre un objeto, receta, construcción u obra del pueblo para agregarlo a la cola. " +
            "Si es LeftControl o RightControl, cualquiera de los dos Ctrl funciona.");
        hoverKey = Config.Bind("Vista rápida", "Tecla", new KeyboardShortcut(KeyCode.LeftAlt),
            "Mantén esta tecla sobre cualquier objeto para ver su receta en un panel chico. None la apaga.");
        hoverTextSize = Config.Bind("Vista rápida", "TamanoLetra", 0f,
            new ConfigDescription("Tamaño de letra. 0 = el mismo que usa el juego (el más nítido).", new AcceptableValueRange<float>(0f, 40f)));

        const string P = "Panel en pantalla";
        hudVisible = Config.Bind(P, "Visible", true, "Muestra la cola en pantalla. La tecla de abajo lo cambia en el juego.");
        hudKey = Config.Bind(P, "Tecla", new KeyboardShortcut(KeyCode.F3), "Muestra u oculta el panel.");
        hudHideWithWindows = Config.Bind(P, "OcultarConVentanas", true, "Lo oculta con menús, ajustes, diálogos y mapa.");
        hudInWorkWindows = Config.Bind(P, "MostrarEnCofresYMesas", true,
            "Lo sigue mostrando con cofres, mesas de crafteo, construcción, tiendas, zombis, estaciones y la página del personaje.");
        hudSide = Config.Bind(P, "Lado", "Derecha",
            new ConfigDescription("De qué lado de la pantalla va.", new AcceptableValueList<string>("Derecha", "Izquierda")));
        hudTop = Config.Bind(P, "DistanciaArriba", 34f,
            new ConfigDescription("Separación desde el borde de arriba (se ajusta sola al arrastrar el panel).", new AcceptableValueRange<float>(0f, 1000f)));
        hudSideOffset = Config.Bind(P, "DistanciaLado", 6f,
            new ConfigDescription("Separación desde el borde del lado elegido (se ajusta sola al arrastrar el panel).", new AcceptableValueRange<float>(0f, 2000f)));
        hudMovable = Config.Bind(P, "Movible", true, "Permite arrastrar el panel desde su título. Se cambia en el juego con el candado.");
        chestMarks = Config.Bind(P, "MarcarCofres", true,
            "Encima de cada cofre o almacén de la zona, una burbujita con los materiales de tu cola que tiene. " +
            "Se cambia en el juego con el pin de la barra de botones del panel.");
        pinNewTasks = Config.Bind(P, "PinAlAgregar", true,
            "Al agregar una tarea nueva, su pin se prende solo: sus materiales se marcan en los cofres al momento.");
        hudHideEmpty = Config.Bind(P, "OcultarSiVacia", false,
            "Oculta el panel mientras la cola está vacía; vuelve a salir al agregar algo. Si no, la cola vacía dice cómo agregar.");
        countCarried = Config.Bind(P, "ContarLoQueLlevas", true,
            "Cuenta lo que llevas encima (la bolsa de la barra de botones del panel).");
        countChests = Config.Bind(P, "ContarCofres", true,
            "Cuenta los cofres y almacenes de la zona donde estás (el cofre de la barra de botones), como el juego al craftear. " +
            "Nunca se apagan las dos: si apagas la única prendida, se prende la otra.");
        // Desde el .cfg o el menú Mods también: nunca las dos apagadas.
        countCarried.SettingChanged += (_, _) =>
        {
            if (!countCarried.Value && !countChests.Value)
                countChests.Value = true;
        };
        countChests.SettingChanged += (_, _) =>
        {
            if (!countChests.Value && !countCarried.Value)
                countCarried.Value = true;
        };
        buttonsOnTop = Config.Bind(P, "BotonesArriba", false,
            "Los botones del panel (bolsa, cofre, vista Total, ojo, pin, candado y bote) salen de la esquina ⋮ del panel: " +
            "false = bajan por su costado; true = corren por encima del panel (en dos o tres renglones si es angosto).");
        buttonsShown = Config.Bind(P, "MostrarBotones", true,
            "true = la barra de botones afuera; false = guardada en su esquina ⋮. Se cambia en el juego con un clic en la esquina ⋮.");
        hudView = Config.Bind(P, "Vista", "Tareas",
            new ConfigDescription("Tareas: cada tarea con su receta. Total: todo lo que pide la cola junto, un renglón por material. " +
                "Se cambia en el juego con la hoja de la barra de botones del panel.", new AcceptableValueList<string>("Tareas", "Total")));
        hudAlwaysOpen = Config.Bind(P, "SiempreVisible", false,
            "Con un cofre, mesa o el árbol abierto: false = el panel se recorre o se pliega para no tapar la ventana " +
            "(al pasar el mouse se despliega); true = siempre se ve completo. Se cambia en el juego con el ojo de la barra de botones.");
        hudWidth = Config.Bind(P, "Ancho", 170f,
            new ConfigDescription("Ancho del panel; los nombres largos bajan de renglón.", new AcceptableValueRange<float>(100f, 800f)));
        hudMaxHeight = Config.Bind(P, "AltoMaximo", 200f,
            new ConfigDescription("Alto máximo; si la cola no cabe, se desplaza.", new AcceptableValueRange<float>(60f, 1000f)));
        hudTextSize = Config.Bind(P, "TamanoLetra", 0f,
            new ConfigDescription("Tamaño de letra del panel. 0 = el mismo que usa el juego (16). Cualquier tamaño funciona; 8, 16, 24 y 32 " +
                "se ven perfectos y los de en medio, con algunos trazos un poco más gruesos que otros.", new AcceptableValueRange<float>(0f, 40f)));
        hudIconSize = Config.Bind(P, "TamanoIconos", 16f,
            new ConfigDescription("Tamaño máximo de los íconos: en un panel angosto se achican solos para dejarles sitio a los nombres.", new AcceptableValueRange<float>(10f, 48f)));
        hudScale = Config.Bind(P, "Escala", 1f,
            new ConfigDescription("Achica o agranda íconos y márgenes (la letra se queda en el tamaño nítido del juego).", new AcceptableValueRange<float>(0.3f, 2f)));
        hudOpacity = Config.Bind(P, "OpacidadFondo", 0.55f,
            new ConfigDescription("0 = fondo invisible, 1 = fondo sólido.", new AcceptableValueRange<float>(0f, 1f)));
        hudMaxRows = Config.Bind(P, "MaxRenglones", 80,
            new ConfigDescription("Renglones máximos que se arman.", new AcceptableValueRange<int>(3, 300)));
        recipeStyle = Config.Bind(P, "EstiloReceta", "Detallada",
            new ConfigDescription("Detallada: línea + ingredientes con nombre. Compacta: una línea con lo que pide.",
                new AcceptableValueList<string>("Detallada", "Compacta")));
        recipeStyleKey = Config.Bind(P, "TeclaEstiloReceta", new KeyboardShortcut(KeyCode.F4), "Cambia entre el estilo detallado y el compacto mientras juegas.");
        measurePerf = Config.Bind("Diagnóstico", "MedirRendimiento", false,
            "Escribe en BepInEx\\LogOutput.log cuánto tarda cada parte del mod (cada 15 s) y los cuadros lentos. Solo para buscar problemas.");
    }
}
