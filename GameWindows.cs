using System.Collections.Generic;
using System.Reflection;
using LazyBearTechnology;
using UnityEngine;

namespace CraftQueue;

// Qué ventanas del juego hay abiertas, para decidir si el panel de la cola se ve.
// Se muestra en cofres, mesas de crafteo, construcción, tiendas, zombis y demás estaciones
// (justo donde sirve tener la cola a la vista); se oculta en la página del personaje (ahí
// ya está la cola), menús, ajustes, diálogos, mapa, etc.
internal static class GameWindows
{
    private static readonly FieldInfo StackField =
        typeof(LazyWindowsStackController).GetField("openedWindowsStack", BindingFlags.Static | BindingFlags.NonPublic);

    // Ventanas de trabajo donde el panel sí se muestra.
    private static bool IsWorkWindow(LazyWidgetBase w) =>
        w is UIBaseChestWindow || w is UIMultiInventoryWindow
        || w is UIBaseCraftWindow || w is UIBaseCraftSelectionWindow || w is UIResourceBasedCraftWindow
        || w is UIBuildingWindow || w is UITownBuildingWindow
        || w is UIVendorWindow || w is UIVendorOrdersWindow || w is UIVendorOrdersSelectionWindow
        || w is UIZombieWorkerWindow || w is UIPorterStationWindow
        || w is UIAlchemyWindow || w is UIAlchemyBoostsWindow
        || w is UIAutopsyWindow || w is UIEmbalmWindow || w is UIGardenBedWindow || w is UIGraveWindow
        || w is CharacterWindow; // la página del personaje: ya no hay otra cola ahí, se ve la nuestra

    // Ventanitas que se abren encima de otra (menú de clic derecho, elegir cantidad…):
    // no cambian nada, el panel sigue como estaba.
    private static bool IsTransient(LazyWidgetBase w) =>
        w is UIContextMenuWindow || w is UIItemCountWindow || w is UIHotBarSelectionWindow;

    private static IEnumerable<LazyWidgetBase> Stack()
    {
        if (StackField?.GetValue(null) is List<LazyWidgetBase> list)
            return list;
        LazyWidgetBase active = LazyWindowsStackController.ActiveWindow;
        return active == null ? new LazyWidgetBase[0] : new[] { active };
    }

    // null = no hay ventanas; true = solo ventanas de trabajo; false = hay alguna que lo oculta.
    public static bool? OnlyWorkWindows(out LazyWidgetBase lowestWork)
    {
        lowestWork = null;
        bool any = false;
        foreach (LazyWidgetBase w in Stack())
        {
            if (w == null)
                continue;
            any = true;
            if (IsTransient(w))
                continue;
            if (!IsWorkWindow(w))
                return false;
            if (lowestWork == null)
                lowestWork = w;
        }
        return any ? true : (bool?)null;
    }

    // Huella barata de qué ventanas hay abiertas: cambia en el mismo cuadro en que se abre o
    // cierra una, para reaccionar al instante (sin esperar las revisiones periódicas).
    public static int Signature()
    {
        int h = 17;
        foreach (LazyWidgetBase w in Stack())
            h = h * 31 + (w == null ? 0 : w.GetInstanceID());
        return h;
    }

    // Recién abierta, la ventana todavía está en su animación de entrada (se ve más chica):
    // durante medio segundo se mide en cada cuadro, luego cada medio segundo.
    public static void Remeasure()
    {
        nextOccupied = 0f;
        fastUntil = Time.unscaledTime + 0.6f;
    }

    private static float fastUntil;

    // El área de pantalla (en pixeles) que ocupan las ventanas abiertas: la suma de sus partes
    // visibles (marcos, celdas, botones), sin los fondos que oscurecen toda la pantalla.
    // Se recalcula cada medio segundo (recorrer los elementos de una ventana es caro).
    private static Rect? occupied;
    private static float nextOccupied;

    public static Rect? Occupied()
    {
        if (Time.unscaledTime < nextOccupied)
            return occupied;
        nextOccupied = Time.unscaledTime < fastUntil ? 0f : Time.unscaledTime + 0.5f;
        occupied = null;
        Vector3[] c = new Vector3[4];
        float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
        foreach (LazyWidgetBase w in Stack())
        {
            if (w == null || IsTransient(w) || !w.gameObject.activeInHierarchy)
                continue;
            foreach (UnityEngine.UI.Graphic g in w.GetComponentsInChildren<UnityEngine.UI.Graphic>())
            {
                if (!g.enabled || g.color.a < 0.05f || g.canvas == null)
                    continue;
                Camera cam = g.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : g.canvas.worldCamera;
                g.rectTransform.GetWorldCorners(c);
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]);
                Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
                float w0 = Mathf.Abs(b.x - a.x), h0 = Mathf.Abs(b.y - a.y);
                if (w0 < 1f || h0 < 1f || (w0 >= Screen.width * 0.9f && h0 >= Screen.height * 0.9f))
                    continue; // vacío, o fondo de pantalla completa
                xMin = Mathf.Min(xMin, Mathf.Min(a.x, b.x));
                xMax = Mathf.Max(xMax, Mathf.Max(a.x, b.x));
                yMin = Mathf.Min(yMin, Mathf.Min(a.y, b.y));
                yMax = Mathf.Max(yMax, Mathf.Max(a.y, b.y));
            }
        }
        if (xMax > xMin && yMax > yMin)
            occupied = Rect.MinMaxRect(Mathf.Max(0f, xMin), Mathf.Max(0f, yMin), Mathf.Min(Screen.width, xMax), Mathf.Min(Screen.height, yMax));
        return occupied;
    }

    // Capa donde dibujar el panel: justo encima de la ventana de trabajo (para no quedar
    // detrás del cofre) pero debajo de los menús que se abran sobre ella (que van +15).
    public static int SortingAbove(LazyWidgetBase w, int fallback)
    {
        Canvas c = w != null ? (w.GetComponent<Canvas>() ?? w.GetComponentInParent<Canvas>()) : null;
        return c != null ? c.sortingOrder + 5 : fallback;
    }
}
