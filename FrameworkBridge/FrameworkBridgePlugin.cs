using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using GK2.Framework;
using UnityEngine;

namespace CraftQueue.FrameworkBridge;

// Integración opcional con GK2 Mod Framework: muestra los ajustes de Crafting Queue en el menú Mods
// del juego. Depende de los dos plugins: si falta el framework, BepInEx no carga este DLL y el mod
// principal sigue igual. No referencia CraftingQueue.dll: toma su ConfigFile en tiempo de ejecución,
// y registra las mismas secciones/claves/tipos que el mod ya usa, así que el menú edita los mismos
// valores del archivo .cfg (BepInEx devuelve la entrada existente).
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(MainGuid, BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency(FrameworkPlugin.PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
public sealed class FrameworkBridgePlugin : BaseUnityPlugin
{
    public const string MainGuid = "verto13.gk2.craftingqueue";
    public const string PluginGuid = "verto13.gk2.craftingqueue.framework";
    public const string PluginName = "Crafting Queue - GK2 Mod Framework integration";
    public const string PluginVersion = "0.5.1";

    private void Awake()
    {
        if (!Chainloader.PluginInfos.TryGetValue(MainGuid, out PluginInfo main) || main.Instance == null)
        {
            Logger.LogError("Crafting Queue is not loaded; the Mods menu integration is disabled.");
            return;
        }
        try
        {
            bridge = new Bridge(main.Metadata.Version.ToString(), main.Instance.GetType());
            FrameworkApi.RegisterMod(bridge, main.Instance.Config);
            // Con un Crafting Queue que no la tiene, simplemente no se refrescan los valores.
            settingsVersion = main.Instance.GetType().GetProperty("SettingsVersion", BindingFlags.Public | BindingFlags.Static);
            panelResizing = main.Instance.GetType().GetProperty("PanelResizing", BindingFlags.Public | BindingFlags.Static);
            lastVersion = ReadVersion();
            Logger.LogInfo("Crafting Queue settings added to the GK2 Mod Framework Mods menu.");
        }
        catch (Exception e)
        {
            // Una API distinta del framework no debe afectar al mod: solo se pierde el menú.
            Logger.LogError("Could not register with GK2 Mod Framework: " + e.Message);
        }
    }

    private Bridge bridge;
    private PropertyInfo settingsVersion, panelResizing;
    private int lastVersion;
    private float nextPoll;

    // La página de ajustes del framework no se entera si un valor cambia desde otro lado (el agarre del panel,
    // Ctrl/Shift + rueda, los botones del panel): se le pide que se rearme en cuanto algo cambió. Con el botón
    // del mouse presionado solo si es el agarre del panel, para no cortar el arrastre de un deslizador.
    private void Update()
    {
        if (bridge == null || settingsVersion == null || Time.unscaledTime < nextPoll)
            return;
        nextPoll = Time.unscaledTime + 0.15f;
        if (Input.GetMouseButton(0) && !PanelResizing())
            return;
        int v = ReadVersion();
        if (v == lastVersion)
            return;
        lastVersion = v;
        bridge.RefreshShownValues();
    }

    private bool PanelResizing()
    {
        try { return panelResizing != null && (bool)panelResizing.GetValue(null); }
        catch { return false; }
    }

    private int ReadVersion()
    {
        try { return settingsVersion != null ? (int)settingsVersion.GetValue(null) : 0; }
        catch { return 0; }
    }

    private sealed class Bridge : Gk2ModBase
    {
        private readonly Gk2ModMetadata metadata;
        private readonly Type main;
        private Gk2Settings settings;
        private bool pulse;

        // Truco con la API pública: la página se rearma (y relee todos los valores) cuando cambia cómo se
        // presenta algún ajuste. Un renglón se "desactiva" y se "reactiva" en seguida; la página se rearma una
        // sola vez, en el siguiente cuadro, ya con el renglón activo, así que nunca se ve desactivado.
        internal void RefreshShownValues()
        {
            if (settings == null)
                return;
            try
            {
                pulse = true;
                settings.RefreshConditions();
                pulse = false;
                settings.RefreshConditions();
            }
            catch
            {
                pulse = false;
            }
        }

        internal Bridge(string version, Type main)
        {
            this.main = main;
            metadata = new Gk2ModMetadata(
                MainGuid,
                "Crafting Queue",
                "VERTO13",
                version,
                "An on-screen crafting to-do list: what you need, what you have and which chest it's in.",
                supportsRuntimeToggle: false,
                requiresKnownBuild: false,
                frameworkManagesEnabledState: false);
        }

        public override Gk2ModMetadata Metadata => metadata;

        // Secciones y claves = las del archivo .cfg del mod (no se pueden cambiar sin perder los ajustes
        // guardados). Los textos del menú van en inglés; las traducciones salen de los catálogos
        // Localization/verto13.gk2.craftingqueue/<idioma>.json del framework.
        public override void OnRegister(Gk2ModContext context)
        {
            Gk2Settings s = settings = context.Settings;
            const string C = "Controles", Q = "Vista rápida", P = "Panel en pantalla";
            int order = 0;

            s.AddKeybind(C, "Modificador", new KeyboardShortcut(KeyCode.LeftControl), "Add to queue",
                "Hold this key and right-click an item, recipe, building or town work to add it to the queue.", order++);
            s.AddKeybind(Q, "Tecla", new KeyboardShortcut(KeyCode.LeftAlt), "Recipe preview",
                "Hold this key over any item to see its recipe.", order++);
            s.AddKeybind(P, "Tecla", new KeyboardShortcut(KeyCode.F3), "Show / hide panel",
                "Shows or hides the queue panel.", order++);
            s.AddKeybind(P, "TeclaEstiloReceta", new KeyboardShortcut(KeyCode.F4), "Detailed / compact recipes",
                "Switches between the detailed and the compact recipe style.", order++);

            s.AddToggle(P, "Visible", true, "Show panel",
                "Shows the queue on screen.", order++);

            // Las opciones que no son "sí/no": un botón con la opción elegida, en el idioma del jugador (la
            // lista desplegable del framework no traduce sus opciones); cada clic pasa a la siguiente. El texto
            // y el cambio los pone el mod (Plugin.ChoiceLabel/ChoiceNext), por reflexión; guardan en las mismas
            // claves del .cfg. Con un Crafting Queue más viejo, que no las tiene, no salen.
            MethodInfo choiceLabel = main?.GetMethod("ChoiceLabel", BindingFlags.Public | BindingFlags.Static);
            MethodInfo choiceNext = main?.GetMethod("ChoiceNext", BindingFlags.Public | BindingFlags.Static);
            void Choice(string id, string name, string description)
            {
                if (choiceLabel == null || choiceNext == null)
                    return;
                var label = (Func<string, string>)Delegate.CreateDelegate(typeof(Func<string, string>), choiceLabel);
                var next = (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), choiceNext);
                s.AddButton(P, id, name, description, () => label(id), () => next(id), order++);
            }
            Choice("Vista", "View",
                "By task: each task with its recipe. Total: everything the queue needs in one list. The list in the " +
                "panel's button bar does the same.");
            Choice("Recetas", "Recipes",
                "Detailed: one ingredient per row, with its name. Compact: one line with icons. F4 does the same while you play.");
            Choice("Contar", "What counts as \"have\"",
                "What you carry and the chests of the zone you're in (like the game when crafting), only what you carry, " +
                "or only the chests. The bag and the chest in the panel do the same.");
            Choice("Burbujas", "Chest bubbles",
                "Bubbles over the chests of your zone: for the whole queue, or only for the tasks with their pin on. The " +
                "pin in the panel's button bar does the same.");
            Choice("PinNuevas", "New tasks",
                "New tasks start with their pin on (their materials are marked on the chests right away) or off.");
            Choice("ColaVacia", "When the queue is empty",
                "The panel shows how to add things, or hides until you add something.");
            Choice("ConVentanas", "Shown with windows open",
                "With chests and stations: it stays with chests, crafting stations, shops and the character page, and " +
                "hides with menus, dialogs and the map. With none: it hides with any window. With all: always shown.");
            Choice("SiTapa", "If it covers the window",
                "With a chest, station or the tech tree open: the panel moves aside or folds into a small tab, or stays " +
                "on top. The eye in the panel's button bar does the same.");
            Choice("Barra", "Button bar",
                "The panel's buttons out, or tucked into their ⋮ corner. A click on the ⋮ corner does the same.");
            Choice("BarraSale", "Buttons come out",
                "Down the side of the panel, or across its top (in two or three rows on a narrow panel). A right-click " +
                "on the ⋮ corner does the same.");
            Choice("Mover", "Panel",
                "Movable: drag it by its title or its ⋮ corner, and resize it by its corner grip. Fixed: it stays put. " +
                "The lock in the panel's button bar does the same.");
            Choice("Lado", "Screen side",
                "Which side of the screen the panel sticks to. Dragging the panel also sets it.");

            s.AddFloatSlider(P, "Ancho", 170f, 100f, 800f, "Width",
                "Panel width; long names wrap to a second line.", 10f, order++);
            s.AddFloatSlider(P, "AltoMaximo", 200f, 60f, 1000f, "Maximum height",
                "A longer queue scrolls.", 10f, order++);
            s.AddFloatSlider(P, "TamanoLetra", 16f, 8f, 32f, "Text size",
                "16 = the game's own size. 8, 16, 24 and 32 look perfectly crisp, sizes in between a little uneven. " +
                "Shift + mouse wheel over the panel does the same.", 1f, order++);
            s.AddFloatSlider(P, "OpacidadPanel", 1f, 0.3f, 1f, "Panel opacity",
                "The whole panel: text, icons, buttons and background. 1 = solid.", 0.05f, order++);
            s.AddFloatSlider(P, "OpacidadFondo", 0.55f, 0f, 1f, "Background opacity",
                "Only the background behind each recipe: 0 = invisible, 1 = solid.", 0.05f, order++);
            s.AddFloatSlider(P, "TamanoIconos", 16f, 10f, 48f, "Icon size",
                "With the panel's lock open, the panel widens when the icons no longer fit. With the lock closed, the " +
                "panel keeps its size and the icons stay within its width. Ctrl + mouse wheel over the panel does the same.",
                1f, order++);
            s.SetEnabledCondition(P, "TamanoIconos", () => !pulse); // ver RefreshShownValues
            s.AddFloatSlider(P, "Escala", 1f, 0.3f, 2f, "Spacing scale",
                "Makes icons and spacing smaller or bigger; text stays at the game's crisp size.", 0.1f, order++);

            // Vaciar la cola, con confirmación (el primer clic pide otro). El texto y el clic los pone el
            // mod: se toman por reflexión porque este puente no referencia CraftingQueue.dll. Con un
            // Crafting Queue más viejo, que no los tiene, simplemente no sale el botón.
            MethodInfo label = main?.GetMethod("ClearQueueLabel", BindingFlags.Public | BindingFlags.Static);
            MethodInfo click = main?.GetMethod("ClearQueueClick", BindingFlags.Public | BindingFlags.Static);
            if (label != null && click != null)
                s.AddButton(P, "VaciarCola", "Clear the queue",
                    "Removes every task from the queue of the loaded game. Click twice to confirm.",
                    (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), label),
                    (Action)Delegate.CreateDelegate(typeof(Action), click), order++);
        }
    }
}
