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
    public const string PluginVersion = "0.4.16";

    private void Awake()
    {
        if (!Chainloader.PluginInfos.TryGetValue(MainGuid, out PluginInfo main) || main.Instance == null)
        {
            Logger.LogError("Crafting Queue is not loaded; the Mods menu integration is disabled.");
            return;
        }
        try
        {
            FrameworkApi.RegisterMod(new Bridge(main.Metadata.Version.ToString()), main.Instance.Config);
            Logger.LogInfo("Crafting Queue settings added to the GK2 Mod Framework Mods menu.");
        }
        catch (System.Exception e)
        {
            // Una API distinta del framework no debe afectar al mod: solo se pierde el menú.
            Logger.LogError("Could not register with GK2 Mod Framework: " + e.Message);
        }
    }

    private sealed class Bridge : Gk2ModBase
    {
        private readonly Gk2ModMetadata metadata;

        internal Bridge(string version)
        {
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
            Gk2Settings s = context.Settings;
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
            s.AddToggle(P, "MarcarCofres", true, "Chest bubbles",
                "Bubbles over the chests in your zone show which queued materials each one holds.", order++);
            s.AddToggle(P, "PinAlAgregar", true, "Pin new tasks",
                "New tasks start with their pin on, so their materials are marked on the chests right away.", order++);
            s.AddToggle(P, "SiempreVisible", false, "Always visible",
                "With a chest, station or the tech tree open, keep the panel fully visible instead of moving it aside or folding it.", order++);
            s.AddToggle(P, "OcultarConVentanas", true, "Hide with menus",
                "Hides the panel with menus, settings, dialogs and the map.", order++);
            s.AddToggle(P, "MostrarEnCofresYMesas", true, "Show in chests and stations",
                "Keeps the panel visible with chests, crafting stations, shops and the character page.", order++);
            s.AddToggle(P, "Movible", true, "Movable",
                "Lets you drag the panel by its title. The lock in the panel does the same.", order++);

            s.AddFloatSlider(P, "Ancho", 170f, 100f, 800f, "Width",
                "Panel width; long names wrap to a second line.", 10f, order++);
            s.AddFloatSlider(P, "AltoMaximo", 200f, 60f, 1000f, "Maximum height",
                "A longer queue scrolls.", 10f, order++);
            s.AddFloatSlider(P, "OpacidadFondo", 0.55f, 0f, 1f, "Background opacity",
                "0 = invisible background, 1 = solid.", 0.05f, order++);
            s.AddFloatSlider(P, "TamanoIconos", 16f, 10f, 48f, "Icon size",
                "Size of the icons in the panel.", 1f, order++);
            s.AddFloatSlider(P, "Escala", 1f, 0.3f, 2f, "Spacing scale",
                "Makes icons and spacing smaller or bigger; text stays at the game's crisp size.", 0.1f, order++);
        }
    }
}
