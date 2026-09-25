using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace CraftQueue;

// Estado del juego que el mod necesita saber.
internal static class GameState
{
    private static Transform hud;
    private static CanvasGroup hudGroup;
    private static float nextHudLookup;

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

    // En escenas de historia el juego oculta su propia interfaz (HUD): el panel hace lo mismo.
    public static bool InCutscene
    {
        get
        {
            if (hud == null && Time.unscaledTime >= nextHudLookup)
            {
                nextHudLookup = Time.unscaledTime + 3f;
                GameObject go = GameObject.Find("UIRoot/HUD") ?? GameObject.Find("HUD");
                hud = go != null ? go.transform : null;
                hudGroup = hud != null ? hud.GetComponent<CanvasGroup>() : null;
            }
            if (hud == null)
                return false;
            return !hud.gameObject.activeInHierarchy || (hudGroup != null && hudGroup.alpha < 0.1f);
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
            if (data?.Definition != null && data.BuildingMode.ToString() != "Remove")
                Queue.OnBuilt(TaskKind.Build, data.Definition.id);
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
