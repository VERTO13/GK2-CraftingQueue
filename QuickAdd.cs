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
//  - un requisito de misión                                 -> el objeto, con la cantidad que pide
//  - lo que desbloquea el árbol tecnológico                 -> la receta o el objeto
//  - lo que te pide un personaje en un diálogo              -> el objeto, con la cantidad que pide
//  - un pedido/encargo de un comerciante                    -> el objeto, con la cantidad del encargo
//  - lo que pide una opción de respuesta en una conversación -> el objeto, con la cantidad
// Mientras Ctrl está presionado, el clic derecho no hace lo que el juego hace normalmente
// (cerrar la ventana de crafteo o abrir el menú del objeto).
internal class QuickAdd : MonoBehaviour
{
    private readonly List<RaycastResult> hits = new List<RaycastResult>();

    // Los datos de cada elemento de la interfaz del juego están en un campo protegido "data".
    private static readonly System.Reflection.FieldInfo RecipeData = AccessTools.Field(typeof(LazyWidget<UICraftPreviewItemCellData>), "data");
    private static readonly System.Reflection.FieldInfo BuildingData = AccessTools.Field(typeof(LazyWidget<UIBuildingWidgetData>), "data");
    private static readonly System.Reflection.FieldInfo TownData = AccessTools.Field(typeof(LazyWidget<UITownBuildingWidgetData>), "data");
    private static readonly System.Reflection.FieldInfo LinkedData = AccessTools.Field(typeof(LazyWidget<LinkedEntityWidgetData>), "data");
    private static readonly System.Reflection.FieldInfo DialogData = AccessTools.Field(typeof(LazyWidget<UIDialogWindowData>), "data");
    private static readonly System.Reflection.FieldInfo DialogItemArea = AccessTools.Field(typeof(UIDialogWindow), "itemIconWithBackgroundParent");
    // Qué muestra un ícono de respuesta (precio, recompensa, requisito, día, encargo), si el juego lo guarda.
    private static readonly System.Reflection.FieldInfo AnswerIconKind =
        typeof(UIMultiAnswerIcon).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .FirstOrDefault(f => f.FieldType == typeof(UIMultiAnswerIcon.DisplayType));

    // El diálogo "te piden X (tienes/necesitas)" solo guarda el ícono y el nombre del objeto:
    // al crearse se anota qué objeto y cuántos pide, para poder agregarlo a la cola.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIDialogWindowData, Tuple<string, int>> dialogRequests =
        new System.Runtime.CompilerServices.ConditionalWeakTable<UIDialogWindowData, Tuple<string, int>>();

    private static void RememberDialogRequest(UIDialogWindowData __instance, Item item, int needCount)
    {
        if (__instance != null && item != null && !string.IsNullOrEmpty(item.id))
        {
            dialogRequests.Remove(__instance);
            dialogRequests.Add(__instance, Tuple.Create(item.id, Math.Max(1, needCount)));
        }
    }

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
                     typeof(UIFuelCraftWindow), typeof(UISingleCraftWindow),
                     typeof(UIDialogWindow), typeof(UIQuestInfoWindow), typeof(CharacterWindow),
                     typeof(UIVendorWindow), typeof(UIVendorOrdersWindow), typeof(UIVendorOrdersSelectionWindow) })
            // Solo si la ventana define sus propias teclas (nunca la versión común de todas las ventanas).
            Patch(harmony, AccessTools.DeclaredMethod(w, "GetGameKeyDelegates"), postfix: nameof(KeepWindowOpen));
        // Diálogo que pide un objeto con "tienes/necesitas": anotar cuál y cuántos.
        System.Reflection.ConstructorInfo askCtor = AccessTools.Constructor(typeof(UIDialogWindowData), new[]
        {
            typeof(Item), typeof(string), typeof(string), typeof(string), typeof(int), typeof(int), typeof(Action), typeof(Action), typeof(bool)
        });
        if (askCtor != null)
        {
            try { harmony.Patch(askCtor, postfix: new HarmonyMethod(typeof(QuickAdd), nameof(RememberDialogRequest))); }
            catch (Exception e) { Plugin.Log.LogWarning("Ctrl + clic derecho (diálogo): " + e.Message); }
        }
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
        long t = Perf.Start();
        CloseStrayMenu();
        bool adding = Input.GetMouseButtonDown(1) && ModifierHeld() && Queue.HasSlot;
        Perf.Stop("ctrl + clic", t);
        if (!adding)
            return;
        suppressUntil = Time.unscaledTime + 0.6f;
        try
        {
            if (TryAddUnderMouse())
            {
                QueueHud.ShowAfterAdd();
                try { LazyAudio.PlayAndForget("gui_click"); } catch { }
            }
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
        // Diálogo "te piden X": el ícono no recibe clics, así que se mira dónde está el mouse.
        if (LazyWindowsStackController.ActiveWindow is UIDialogWindow dialog && AddDialogRequest(dialog, Input.mousePosition))
            return true;
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

    // El objeto que pide un diálogo "tienes/necesitas", si el mouse está sobre su ícono o nombre.
    private static bool AddDialogRequest(UIDialogWindow dialog, Vector2 mouse)
    {
        if (!(DialogItemArea?.GetValue(dialog) is GameObject area) || !area.activeInHierarchy)
            return false;
        Canvas c = area.GetComponentInParent<Canvas>();
        Camera cam = c != null && c.renderMode != RenderMode.ScreenSpaceOverlay ? c.worldCamera : null;
        if (!RectTransformUtility.RectangleContainsScreenPoint((RectTransform)area.transform, mouse, cam))
            return false;
        if (!(DialogData?.GetValue(dialog) is UIDialogWindowData data) || !dialogRequests.TryGetValue(data, out Tuple<string, int> req))
            return false;
        return Queue.Add(TaskKind.Item, req.Item1, req.Item2) != null;
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

        // Requisito de una misión (objeto y cantidad), o lo que desbloquea el árbol tecnológico
        // (receta u objeto): el mismo elemento del juego en los dos lugares.
        LinkedEntityWidget linked = go.GetComponentInParent<LinkedEntityWidget>();
        if (linked != null && LinkedData?.GetValue(linked) is LinkedEntityWidgetData ld)
        {
            if (ld.Item != null && !ld.Item.IsEmpty)
                return Queue.Add(TaskKind.Item, ld.Item.id, Math.Max(1, ld.Item.Count)) != null;
            if (ld.CraftDef != null && ld.CraftDef.outputItems != null)
                return Queue.Add(TaskKind.Craft, ld.CraftDef.id, 1, GameData.Name(GameData.MainOutput(ld.CraftDef) ?? ld.CraftDef.id)) != null;
            if (ld.ItemDef != null)
                return Queue.Add(TaskKind.Item, ld.ItemDef.id, 1) != null;
        }

        // Opción de respuesta en una conversación ("Poesía impresa 0/1"): lo que te piden (precio,
        // requisito o encargo) con su cantidad. Las recompensas no: esas no las fabricas tú.
        UIMultiAnswerIcon answer = go.GetComponentInParent<UIMultiAnswerIcon>();
        if (answer != null)
        {
            string kind = AnswerIconKind?.GetValue(answer)?.ToString();
            if (kind == "Reward" || kind == "DayNumber")
                return false;
            ItemCount ic = answer.ItemCount;
            if (ic != null && !string.IsNullOrEmpty(ic.itemId))
                return Queue.Add(TaskKind.Item, ic.itemId, Math.Max(1, ic.count)) != null;
            var answerOrder = answer.VendorOrderDef;
            if (answerOrder != null && !string.IsNullOrEmpty(answerOrder.itemId))
                return Queue.Add(TaskKind.Item, answerOrder.itemId, Math.Max(1, answerOrder.count)) != null;
            return false;
        }

        // Pedido de un comerciante: el objeto con la cantidad que pide el encargo.
        UIVendorOrderWidget order = go.GetComponentInParent<UIVendorOrderWidget>();
        var orderDef = order != null ? order.Data?.VendorOrderData?.Definition : null;
        if (orderDef != null && !string.IsNullOrEmpty(orderDef.itemId))
            return Queue.Add(TaskKind.Item, orderDef.itemId, Math.Max(1, orderDef.count)) != null;

        // Diálogo con un objeto (por ejemplo, lo que te pide un personaje): su celda con la cantidad.
        UIDialogWindow dlg = go.GetComponentInParent<UIDialogWindow>();
        if (dlg != null && DialogData?.GetValue(dlg) is UIDialogWindowData dd && dd.Item != null && !dd.Item.IsEmpty
            && go.GetComponentInParent<UIItemCell>() != null)
            return Queue.Add(TaskKind.Item, dd.Item.id, Math.Max(1, dd.Item.Count)) != null;

        // Cualquier objeto.
        UIItemCell cell = go.GetComponentInParent<UIItemCell>();
        if (cell != null && cell.DisplayingItem != null && !cell.DisplayingItem.IsEmpty)
            return Queue.Add(TaskKind.Item, cell.DisplayingItem.id, 1) != null;
        return false;
    }
}
