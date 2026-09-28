using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace CraftQueue;

// Estado del juego que el mod necesita saber.
internal static class GameState
{
    private static HUD hud;
    private static float nextHudLookup;
    private static int hudFrame = -1, hudHiddenBy;
    private static bool hudHidden;

    // Partida cargada ("Steam_1"…), o null en el menú principal.
    public static string Slot
    {
        get
        {
            try
            {
                if (MainGame.Instance == null || MainGame.PlayerData == null)
                    return null;
                string name = MainGame.Instance.SaveSlotData?.slotName;
                return string.IsNullOrEmpty(name) ? null : name;
            }
            catch
            {
                return null;
            }
        }
    }

    // La partida cargada como objeto: al empezar otra partida el juego crea uno nuevo, y si solo
    // cambia de nombre (las de la demo, al guardarse por primera vez) es el mismo.
    public static SaveSlotData SlotData
    {
        get
        {
            try
            {
                if (MainGame.Instance == null || MainGame.PlayerData == null)
                    return null;
                return MainGame.Instance.SaveSlotData;
            }
            catch
            {
                return null;
            }
        }
    }

    // ¿El juego tiene guardada una partida con ese nombre? null = no se sabe. Se lee la lista que el
    // juego ya cargó, de su campo: la propiedad SaveSlotDataList la vuelve a leer del disco si falta.
    // (Sin parches de Harmony a MainGame ni a SaveSystem: parchear StartNewGameWithSlotName hacía
    // que el juego armara PlayerSkinHelper antes de tiempo, fallaba y el juego ya no arrancaba.)
    private static readonly FieldInfo SavedSlots = AccessTools.Field(typeof(SaveSystem), "saveSlotDataList");

    public static bool? IsSaved(string slot)
    {
        try
        {
            // Su archivo de guardado junto a los del juego: guardada, sin duda.
            string dir = Application.persistentDataPath;
            if (File.Exists(Path.Combine(dir, slot + ".dat")) || File.Exists(Path.Combine(dir, slot + ".info")))
                return true;
            if (!(SavedSlots?.GetValue(null) is List<SaveSlotData> saved))
                return null;
            return saved.Any(s => s != null && s.slotName == slot);
        }
        catch
        {
            return null;
        }
    }

    // En la preparación de una pelea y durante la pelea: el panel tapaba a tus escuadras, y R3
    // (terminar la preparación) es del juego. Se lee el campo del singleton y no su Instance: esa
    // crea el controlador si todavía no existe (p. ej. en el menú principal).
    private static readonly FieldInfo FightController = AccessTools.Field(typeof(LazySingleton<FightingGameController>), "instance");

    public static bool InFight
    {
        get
        {
            try
            {
                FightingGameController fight = FightController?.GetValue(null) as FightingGameController;
                return fight != null && fight.CurrentFightState != FightState.Disabled;
            }
            catch
            {
                return false;
            }
        }
    }

    // El juego esconde su HUD (HUD.SetDisableState) en las escenas de historia (Cinematic: las de
    // franjas negras; CinematicsScene: las ilustradas), al colocar una construcción (BuildController)
    // y en el menú principal y la carga (MainMenu): el panel y las burbujas se esconden con él. Se leen
    // las mismas banderas del juego, sin parches. El HUD se toma de la lista de LazyUI (LazyUI.Get<HUD>
    // lanza una excepción si todavía no existe) y se busca cada 2 s mientras no esté.
    private static readonly FieldInfo GuiElements = AccessTools.Field(typeof(LazyUI), "guiElementsDictionary");
    private static readonly FieldInfo HudFlags = AccessTools.Field(typeof(HUD), "disableStateType");
    private static readonly HudStateType[] HudStates = (HudStateType[])Enum.GetValues(typeof(HudStateType));

    public static bool HudHidden
    {
        get
        {
            if (hudFrame == Time.frameCount)
                return hudHidden;
            hudFrame = Time.frameCount;
            int by = HiddenBy();
            if (by != hudHiddenBy)
            {
                // Una línea por cambio: así se puede confirmar después en el log que una escena lo escondió.
                Plugin.Log.LogInfo(by != 0
                    ? $"El juego escondió su HUD ({Reasons(by)}): el panel también se esconde."
                    : "El juego volvió a mostrar su HUD: el panel también.");
                hudHiddenBy = by;
            }
            hudHidden = by != 0;
            return hudHidden;
        }
    }

    private const int OtherReason = 1 << 30;

    private static int Bit(HudStateType s) => 1 << ((int)s & 15);

    private static string Reasons(int by)
    {
        List<string> names = HudStates.Where(s => (by & Bit(s)) != 0).Select(s => s.ToString()).ToList();
        if ((by & OtherReason) != 0)
            names.Add("otra razón");
        return string.Join(", ", names);
    }

    // Las banderas que tienen escondido el HUD (0 = se ve). Sin HUD todavía (menú principal): 0.
    private static int HiddenBy()
    {
        try
        {
            if (hud == null && Time.unscaledTime >= nextHudLookup)
            {
                nextHudLookup = Time.unscaledTime + 2f;
                hud = GuiElements?.GetValue(null) is Dictionary<Type, ILazyGUIElement> elements
                      && elements.TryGetValue(typeof(HUD), out ILazyGUIElement e) ? e as HUD : null;
            }
            if (hud == null)
                return 0;
            if (HudFlags?.GetValue(hud) is MultiFlagAND<HudStateType> flags)
            {
                if (flags.ResultFlag)
                    return 0;
                int by = 0;
                foreach (HudStateType s in HudStates)
                    if (!flags.GetFlag(s))
                        by |= Bit(s);
                return by != 0 ? by : OtherReason;
            }
            // Si una versión del juego cambiara las banderas: esconderlo es desactivar el HUD.
            return hud.gameObject.activeInHierarchy ? 0 : OtherReason;
        }
        catch
        {
            return 0;
        }
    }
}

// Descuento automático: el mod se entera cuando el juego termina un crafteo (tuyo o de zombis),
// una obra del pueblo o una construcción.
internal static class GameHooks
{
    private const string TownCraftPrefix = "town_building_craft:";
    private static readonly FieldInfo PointerBuildData = AccessTools.Field(typeof(BuildPointerObject), "buildData");

    public static void Apply(Harmony harmony)
    {
        TryPatch(harmony, AccessTools.Method(typeof(CraftElementBase), "MakeOutput"), nameof(AfterOutputBase));
        TryPatch(harmony, AccessTools.Method(typeof(ConveyorCraftElement), "MakeOutput"), nameof(AfterOutputConveyor));
        TryPatch(harmony, AccessTools.Method(typeof(WgoBuildPointer), "TryDoBuildAction"), nameof(AfterBuild));
        TryPatch(harmony, AccessTools.Method(typeof(UpgradeBuildPointer), "TryDoBuildAction"), nameof(AfterBuild));
        TryPatch(harmony, AccessTools.Method(typeof(UIBuildingWindow), "OnBuildPressed"), nameof(AfterScriptBuild));
    }

    private static void TryPatch(Harmony harmony, MethodInfo target, string postfix)
    {
        if (target == null)
        {
            Plugin.Log.LogWarning($"Descuento automático: no se encontró el punto del juego para {postfix}.");
            return;
        }
        try
        {
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(GameHooks), postfix));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Descuento automático ({postfix}): {e.Message}");
        }
    }

    // Todo crafteo terminado pasa por aquí al generar lo producido. Los de cinta tienen su propia
    // versión; se cuentan solo ahí para no contar doble.
    private static void AfterOutputBase(CraftElementBase __instance, List<Item> __result)
    {
        if (__instance is ConveyorCraftElement)
            return;
        Crafted(__instance, __result);
    }

    private static void AfterOutputConveyor(CraftElementBase __instance, List<Item> __result) => Crafted(__instance, __result);

    private static void Crafted(CraftElementBase element, List<Item> output)
    {
        try
        {
            string id = element?.Def?.id;
            if (string.IsNullOrEmpty(id))
                return;
            // Las obras del pueblo (reparar/mejorar) terminan como un crafteo especial.
            if (id.StartsWith(TownCraftPrefix))
            {
                Queue.OnBuilt(TaskKind.Town, id.Substring(TownCraftPrefix.Length));
                return;
            }
            if (element.Def is CraftDef craft)
            {
                IEnumerable<(string, int)> produced = (output ?? new List<Item>())
                    .Where(i => i != null && !i.IsEmpty)
                    .Select(i => (i.id, i.Count));
                Queue.OnCrafted(craft, produced);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Descuento al craftear: " + e.Message);
        }
    }

    // Construcción colocada en el mundo.
    private static void AfterBuild(BuildPointerObject __instance, bool __result)
    {
        if (!__result)
            return;
        try
        {
            BuildData data = PointerBuildData?.GetValue(__instance) as BuildData;
            if (data?.Definition == null)
                return;
            if (data.BuildingMode.ToString() != "Remove")
                Queue.OnBuilt(TaskKind.Build, data.Definition.id);
            else
            {
                // Quitar una estación no descuenta ninguna tarea, pero cambia lo que rinden las recetas (y
                // en qué estaciones se pueden hacer): recalcular ya, sin depender de verlo en la cuenta de la zona.
                GameData.ResetStations();
                QueueHud.Dirty = true;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Descuento al construir: " + e.Message);
        }
    }

    // Mejoras que se hacen desde la ventana sin colocar nada (p. ej. las de la iglesia).
    // Las demás pasan después por AfterBuild al colocarlas, así que aquí solo las "Script".
    private static void AfterScriptBuild(object[] __args)
    {
        try
        {
            BuildData buildData = __args != null && __args.Length > 0 ? __args[0] as BuildData : null;
            if (buildData?.Definition != null && buildData.BuildingMode.ToString() == "Script")
                Queue.OnBuilt(TaskKind.Build, buildData.Definition.id);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Descuento en mejora: " + e.Message);
        }
    }
}

