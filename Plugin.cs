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
[BepInPlugin(Guid, "Crafting Queue", "0.4.7")]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "verto13.gk2.craftingqueue";

    internal static ManualLogSource Log { get; private set; }

    private static ConfigEntry<KeyCode> addModifier, hoverKey, hudKey, recipeStyleKey;
    private static ConfigEntry<float> hoverTextSize, hudTop, hudSideOffset, hudWidth, hudMaxHeight, hudTextSize, hudIconSize, hudScale, hudOpacity;
    private static ConfigEntry<bool> hudVisible, hudHideWithWindows, hudInWorkWindows, hudMovable, chestMarks, measurePerf;
    private static ConfigEntry<string> hudSide, recipeStyle;
    private static ConfigEntry<int> hudMaxRows;

    internal static KeyCode AddModifier => addModifier.Value;
    internal static float HoverTextSize => hoverTextSize.Value;
    internal static KeyCode HudKey => hudKey.Value;
    internal static bool HudVisible { get => hudVisible.Value; set => hudVisible.Value = value; }
    internal static bool HudHideWithWindows => hudHideWithWindows.Value;
    internal static bool HudInWorkWindows => hudInWorkWindows.Value;
    internal static bool HudMovable { get => hudMovable.Value; set => hudMovable.Value = value; }
    internal static bool ChestMarks { get => chestMarks.Value; set => chestMarks.Value = value; }
    internal static bool HudLeft => hudSide.Value.StartsWith("Izq", StringComparison.OrdinalIgnoreCase)
                                    || hudSide.Value.StartsWith("Left", StringComparison.OrdinalIgnoreCase);
    internal static float HudTop => hudTop.Value;
    internal static float HudSideOffset => hudSideOffset.Value;
    internal static float HudWidth => hudWidth.Value;
    internal static float HudMaxHeight => hudMaxHeight.Value;
    internal static float HudTextSize => hudTextSize.Value;
    internal static float HudIconSize => hudIconSize.Value;
    internal static float HudScale => hudScale.Value;
    internal static float HudOpacity => hudOpacity.Value;
    internal static int HudMaxRows => hudMaxRows.Value;
    internal static KeyCode RecipeStyleKey => recipeStyleKey.Value;
    internal static bool CompactRecipes
    {
        get => recipeStyle.Value.StartsWith("Comp", StringComparison.OrdinalIgnoreCase);
        set => recipeStyle.Value = value ? "Compacta" : "Detallada";
    }

    internal static bool HoverHeld()
    {
        KeyCode k = hoverKey.Value;
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

    private static bool started;
    private string lastSlot;
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
        Log.LogInfo($"Crafting Queue listo: {addModifier.Value} + clic derecho agrega, {hudKey.Value} muestra u oculta el panel. Colas en {data}");
    }

    private void Update()
    {
        Perf.On = measurePerf.Value;
        long t = Perf.Start();
        string slot = GameState.Slot;
        if (slot != lastSlot)
        {
            lastSlot = slot;
            if (slot != null)
            {
                GameStyle.SearchButtonsOnce(); // durante la carga de la partida, una sola vez
                Diagnostics.DumpRecipesOnce(dataFolder); // solo con Diagnóstico activado
            }
        }
        Queue.SyncSlot(slot); // cada partida guardada tiene su propia cola
        Perf.Stop("partida y botones", t);
    }

    private void LateUpdate() => Perf.EndFrame(Time.unscaledDeltaTime);

    private void BindConfig()
    {
        addModifier = Config.Bind("Controles", "Modificador", KeyCode.LeftControl,
            "Mantén esta tecla y da clic derecho sobre un objeto, receta, construcción u obra del pueblo para agregarlo a la cola. " +
            "Si es LeftControl o RightControl, cualquiera de los dos Ctrl funciona.");
        hoverKey = Config.Bind("Vista rápida", "Tecla", KeyCode.LeftAlt,
            "Mantén esta tecla sobre cualquier objeto para ver su receta en un panel chico. None la apaga.");
        hoverTextSize = Config.Bind("Vista rápida", "TamanoLetra", 0f,
            new ConfigDescription("Tamaño de letra. 0 = el mismo que usa el juego (el más nítido).", new AcceptableValueRange<float>(0f, 40f)));

        const string P = "Panel en pantalla";
        hudVisible = Config.Bind(P, "Visible", true, "Muestra la cola en pantalla. La tecla de abajo lo cambia en el juego.");
        hudKey = Config.Bind(P, "Tecla", KeyCode.F3, "Muestra u oculta el panel.");
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
            "Se cambia en el juego con el botón junto al candado.");
        hudWidth = Config.Bind(P, "Ancho", 170f,
            new ConfigDescription("Ancho del panel; los nombres largos bajan de renglón.", new AcceptableValueRange<float>(100f, 800f)));
        hudMaxHeight = Config.Bind(P, "AltoMaximo", 200f,
            new ConfigDescription("Alto máximo; si la cola no cabe, se desplaza.", new AcceptableValueRange<float>(60f, 1000f)));
        hudTextSize = Config.Bind(P, "TamanoLetra", 0f,
            new ConfigDescription("Tamaño de letra. 0 = el mismo que usa el juego (el más nítido).", new AcceptableValueRange<float>(0f, 40f)));
        hudIconSize = Config.Bind(P, "TamanoIconos", 16f,
            new ConfigDescription("Tamaño de los íconos.", new AcceptableValueRange<float>(10f, 48f)));
        hudScale = Config.Bind(P, "Escala", 1f,
            new ConfigDescription("Achica o agranda íconos y márgenes (la letra se queda en el tamaño nítido del juego).", new AcceptableValueRange<float>(0.3f, 2f)));
        hudOpacity = Config.Bind(P, "OpacidadFondo", 0.55f,
            new ConfigDescription("0 = fondo invisible, 1 = fondo sólido.", new AcceptableValueRange<float>(0f, 1f)));
        hudMaxRows = Config.Bind(P, "MaxRenglones", 80,
            new ConfigDescription("Renglones máximos que se arman.", new AcceptableValueRange<int>(3, 300)));
        recipeStyle = Config.Bind(P, "EstiloReceta", "Detallada",
            new ConfigDescription("Detallada: línea + ingredientes con nombre. Compacta: una línea con lo que pide.",
                new AcceptableValueList<string>("Detallada", "Compacta")));
        recipeStyleKey = Config.Bind(P, "TeclaEstiloReceta", KeyCode.F4, "Cambia entre el estilo detallado y el compacto mientras juegas.");
        measurePerf = Config.Bind("Diagnóstico", "MedirRendimiento", false,
            "Escribe en BepInEx\\LogOutput.log cuánto tarda cada parte del mod (cada 15 s) y los cuadros lentos. Solo para buscar problemas.");
    }
}
