using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CraftQueue;

// Agregar a la cola con Ctrl + clic derecho sobre:
//  - un objeto (inventario, cofres, lo que sea)            -> "necesito 1 más de esto"
//  - una receta en una mesa de crafteo                      -> "hacer esta receta 1 vez"
//  - una construcción en el menú de construir               -> con sus materiales
//  - una obra del pueblo (reparar/mejorar)                  -> con sus materiales
// Mientras Ctrl está presionado, el clic derecho no hace lo que el juego hace normalmente
// (cerrar la ventana de crafteo o abrir el menú del objeto).
internal class QuickAdd : MonoBehaviour
{
    private readonly List<RaycastResult> hits = new List<RaycastResult>();

    // Los datos de cada elemento de la interfaz del juego están en un campo protegido "data".
    private static readonly System.Reflection.FieldInfo RecipeData = AccessTools.Field(typeof(LazyWidget<UICraftPreviewItemCellData>), "data");
    private static readonly System.Reflection.FieldInfo BuildingData = AccessTools.Field(typeof(LazyWidget<UIBuildingWidgetData>), "data");
    private static readonly System.Reflection.FieldInfo TownData = AccessTools.Field(typeof(LazyWidget<UITownBuildingWidgetData>), "data");

    public static bool ModifierHeld()
    {
        KeyCode k = Plugin.AddModifier;
        if (k == KeyCode.LeftControl || k == KeyCode.RightControl)
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        return Input.GetKey(k);
    }

    public static void Apply(Harmony harmony)
    {
        // Clic derecho sobre una celda de objeto (abre el menú del objeto, etc.).
        Patch(harmony, AccessTools.Method(typeof(UIItemCell), "OnPress2"), prefix: nameof(SkipWhenAdding));
        // Menú de clic derecho (del juego o de otros mods): no abrirlo durante un Ctrl + clic derecho.
        // Se engancha a la apertura genérica de ventanas y solo actúa si es ese menú.
        Patch(harmony, AccessTools.Method(typeof(LazyWindow<UIContextMenuWindowData>), "Open", new[] { typeof(UIContextMenuWindowData) }),
            prefix: nameof(SkipMenuWhileAdding));
        // Ventanas donde el clic derecho las cierra.
        foreach (Type w in new[] { typeof(UICraftWindow), typeof(UIBuildingWindow), typeof(UICraftSelectionWindow),
                     typeof(UIFuelCraftWindow), typeof(UISingleCraftWindow) })
            Patch(harmony, AccessTools.Method(w, "GetGameKeyDelegates"), postfix: nameof(KeepWindowOpen));
    }

    private static void Patch(Harmony harmony, System.Reflection.MethodInfo m, string prefix = null, string postfix = null)
    {
        if (m == null)
            return;
        try
        {
            harmony.Patch(m,
                prefix: prefix != null ? new HarmonyMethod(typeof(QuickAdd), prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(QuickAdd), postfix) : null);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Ctrl + clic derecho ({m.DeclaringType?.Name}): {e.Message}");
        }
    }

    private static bool SkipWhenAdding() => !ModifierHeld();

    private static float suppressUntil; // durante un Ctrl + clic derecho (y un momento después)

    private static bool SkipMenuWhileAdding(object __instance) =>
        !(__instance is UIContextMenuWindow) || Time.unscaledTime > suppressUntil;

    private static void KeepWindowOpen(Dictionary<GameKey, Func<bool>> __result)
    {
        if (__result == null || !__result.TryGetValue(GameKey.RightClick, out Func<bool> original) || original == null)
            return;
        __result[GameKey.RightClick] = () => ModifierHeld() || original();
    }

    // Otros mods (p. ej. el que agrega "Agregar a la cola" al menú) pueden abrir un menú de clic
    // derecho con el mismo clic. Mientras dura un Ctrl + clic derecho, ese menú no se abre
    // (SkipMenuWhileAdding); si aun así alguno se abriera, se cierra aquí como respaldo.
    private static System.Reflection.MethodInfo closeMenu;

    private void CloseStrayMenu()
    {
        if (Input.GetMouseButton(1) && ModifierHeld())
            suppressUntil = Mathf.Max(suppressUntil, Time.unscaledTime + 0.3f);
        if (Time.unscaledTime > suppressUntil)
            return;
        if (!(LazyWindowsStackController.ActiveWindow is UIContextMenuWindow menu) || !menu.IsShown)
            return;
        try
        {
            closeMenu ??= typeof(UIContextMenuWindow).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Where(m => m.Name == "Close" && m.GetParameters().All(p => p.IsOptional))
                .OrderBy(m => m.GetParameters().Length).FirstOrDefault();
            closeMenu?.Invoke(menu, closeMenu.GetParameters().Select(p => p.DefaultValue).ToArray());
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("No se pudo cerrar el menú de clic derecho: " + e.Message);
            suppressUntil = 0f;
        }
    }

    private void Update()
    {
        CloseStrayMenu();
        if (!Input.GetMouseButtonDown(1) || !ModifierHeld() || !Queue.HasSlot)
            return;
        suppressUntil = Time.unscaledTime + 0.6f;
        try
        {
            if (TryAddUnderMouse())
                try { LazyAudio.PlayAndForget("gui_click"); } catch { }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Ctrl + clic derecho: " + e.Message);
        }
    }

    private bool TryAddUnderMouse()
    {
        EventSystem es = EventSystem.current;
        if (es == null)
            return false;
        hits.Clear();
        es.RaycastAll(new PointerEventData(es) { position = Input.mousePosition }, hits);
        foreach (RaycastResult hit in hits)
        {
            GameObject go = hit.gameObject;
            if (go == null || go.transform.root.name.StartsWith("GK2 "))
                continue;
            if (AddFrom(go))
                return true;
        }
        return false;
    }

    // Agrega a la cola lo que representa ese elemento de la interfaz (o alguno de sus padres):
    // una receta de mesa, una construcción, una obra del pueblo o un objeto. Lo usan el mouse
    // (lo que está bajo el cursor) y el control (la celda seleccionada).
    internal static bool AddFrom(GameObject go)
    {
        if (go == null)
            return false;

        // Receta en una mesa de crafteo.
        UICraftPreviewItemCell recipe = go.GetComponentInParent<UICraftPreviewItemCell>();
        if (recipe != null && RecipeData?.GetValue(recipe) is UICraftPreviewItemCellData rd && !rd.IsTab && !rd.IsUnknown && rd.CraftDef != null)
            return Queue.Add(TaskKind.Craft, rd.CraftDef.id, 1, GameData.Name(GameData.MainOutput(rd.CraftDef) ?? rd.CraftDef.id)) != null;

        // Construcción del menú de construir.
        UIBuildingWidget building = go.GetComponentInParent<UIBuildingWidget>();
        if (building != null && BuildingData?.GetValue(building) is UIBuildingWidgetData bd && bd.BuildData?.Definition != null)
        {
            BuildingDef def = bd.BuildData.Definition;
            return Queue.Add(TaskKind.Build, def.id, 1, GameData.Plain(LLBase.L(def.id)), bd.BuildData.IconId,
                bd.WorldZoneData?.id, GameData.Needs(bd.GetCurrentNeedItems())) != null;
        }

        // Obra del pueblo.
        UITownBuildingWidget town = go.GetComponentInParent<UITownBuildingWidget>();
        if (town != null && TownData?.GetValue(town) is UITownBuildingWidgetData td && td.TownBuildingDef != null)
        {
            TownBuildingDef def = td.TownBuildingDef;
            return Queue.Add(TaskKind.Town, def.id, 1, GameData.Plain(LLBase.L(def.id)), def.iconId,
                td.WorldZoneData?.id, GameData.Needs(td.GetCurrentNeedItems())) != null;
        }

        // Cualquier objeto.
        UIItemCell cell = go.GetComponentInParent<UIItemCell>();
        if (cell != null && cell.DisplayingItem != null && !cell.DisplayingItem.IsEmpty)
            return Queue.Add(TaskKind.Item, cell.DisplayingItem.id, 1) != null;
        return false;
    }
}
