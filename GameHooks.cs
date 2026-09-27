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

// La cola va con el nombre de la ranura ("Steam_1"), y el juego reusa esos nombres: al borrar una
// partida y empezar otra, la nueva toma el primer número libre. Sin esto, la partida nueva
// heredaba la cola de la borrada.
internal static class SlotHooks
{
    public static void Apply(Harmony harmony)
    {
        Patch(harmony, AccessTools.Method(typeof(MainGame), "StartNewGameWithSlotName", new[] { typeof(string), typeof(bool) }),
            prefix: nameof(BeforeNewGame));
        Patch(harmony, AccessTools.Method(typeof(SaveSystem), "Remove", new[] { typeof(SaveSlotData), typeof(Action) }),
            postfix: nameof(AfterRemove));
        Patch(harmony, AccessTools.Method(typeof(SaveSystem), "Save"), prefix: nameof(BeforeSave), postfix: nameof(AfterSave));
    }

    private static void Patch(Harmony harmony, MethodInfo target, string prefix = null, string postfix = null)
    {
        if (target == null)
        {
            Plugin.Log.LogWarning($"Colas por partida: no se encontró el punto del juego para {prefix ?? postfix}.");
            return;
        }
        try
        {
            harmony.Patch(target,
                prefix: prefix != null ? new HarmonyMethod(typeof(SlotHooks), prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(SlotHooks), postfix) : null);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Colas por partida ({prefix ?? postfix}): {e.Message}");
        }
    }

    // El juego eligió un nombre libre para la partida nueva: una cola con ese nombre es de una
    // partida que ya no existe (borrada antes de tener este mod, o fuera del juego).
    private static void BeforeNewGame(string slotName) => Queue.Retire(slotName, "partida nueva");

    private static void AfterRemove(SaveSlotData slotData, bool __result)
    {
        if (__result)
            Queue.Retire(slotData?.slotName, "partida borrada");
    }

    // Las partidas de la demo reciben un nombre nuevo al guardarse por primera vez en el juego
    // completo: su cola se muda con ellas.
    private static void BeforeSave(SaveSlotData slotData, out string __state) => __state = slotData?.slotName;

    private static void AfterSave(SaveSlotData slotData, string __state)
    {
        if (slotData != null && !string.IsNullOrEmpty(__state) && slotData.slotName != __state
            && ReferenceEquals(slotData, MainGame.Instance?.SaveSlotData))
            Queue.Rename(__state, slotData.slotName);
    }
}
