using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CraftQueue;

// Vista rápida: manteniendo Alt sobre cualquier objeto (inventario, cofres, cola…)
// aparece un panel chico con su receta en árbol, sin tener que agregarlo a la cola.
internal class HoverRecipe : MonoBehaviour
{
    private const int MaxDepth = 3;
    private const float Icon = 20f;
    private const float Indent = 14f;

    private static readonly Color Background = new Color(0.14f, 0.11f, 0.09f, 0.96f);
    private static readonly Color Border = new Color(0.47f, 0.35f, 0.22f, 1f);
    private static readonly Color Text = new Color(0.93f, 0.86f, 0.74f);
    private const string Dim = "#a39682";
    private const string Enough = "#9be57f";
    private const string Short = "#e0a070";

    private Canvas canvas;
    private RectTransform panel;
    private RectTransform anchorCell; // celda seleccionada con el control (si no, el mouse)
    private string shownId;
    private readonly List<RaycastResult> hits = new List<RaycastResult>();

    private void Update()
    {
        // Con el mouse: Alt sobre la celda. Con el control: mantener R3 sobre la celda seleccionada.
        UIItemCell pad = GamepadInput.RecipeCell;
        UIItemCell cell = Plugin.HoverHeld() ? CellUnderMouse() : pad;
        anchorCell = cell != null && cell == pad ? (RectTransform)pad.transform : null;
        Item item = cell != null ? cell.DisplayingItem : null;
        if (item == null || item.IsEmpty)
        {
            Hide();
            return;
        }
        try
        {
            Ensure(cell);
            if (item.id != shownId)
            {
                Build(item.id);
                shownId = item.id;
            }
            panel.gameObject.SetActive(true);
            Place();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Vista rápida: " + e.Message);
            Hide();
            enabled = false;
        }
    }

    private void Hide()
    {
        if (panel != null && panel.gameObject.activeSelf)
            panel.gameObject.SetActive(false);
        shownId = null;
    }

    private UIItemCell CellUnderMouse()
    {
        EventSystem es = EventSystem.current;
        if (es == null)
            return null;
        hits.Clear();
        es.RaycastAll(new PointerEventData(es) { position = Input.mousePosition }, hits);
        foreach (RaycastResult h in hits)
        {
            UIItemCell c = h.gameObject.GetComponentInParent<UIItemCell>();
            if (c != null)
                return c;
        }
        return null;
    }

    private void Ensure(UIItemCell cell)
    {
        GameStyle.TryInit();
        if (canvas == null)
        {
            GameObject root = new GameObject("GK2 Vista rápida de receta");
            DontDestroyOnLoad(root);
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            canvas.pixelPerfect = true;

            GameObject p = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(Outline),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            p.transform.SetParent(root.transform, false);
            panel = (RectTransform)p.transform;
            panel.anchorMin = panel.anchorMax = Vector2.zero;
            Image bg = p.GetComponent<Image>();
            bg.color = Background;
            bg.raycastTarget = false;
            Outline o = p.GetComponent<Outline>();
            o.effectColor = Border;
            o.effectDistance = new Vector2(2f, -2f);
            VerticalLayoutGroup v = p.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(8, 10, 6, 7);
            v.spacing = 1f;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = v.childForceExpandHeight = false;
            ContentSizeFitter f = p.GetComponent<ContentSizeFitter>();
            f.horizontalFit = f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
        // Misma escala que el panel de la cola: entera y según la resolución.
        Canvas gameCanvas = cell.GetComponentInParent<Canvas>()?.rootCanvas;
        if (gameCanvas != null && gameCanvas.scaleFactor > 0f)
            canvas.scaleFactor = GameStyle.PanelScale(gameCanvas.scaleFactor);
    }

    private void Build(string id)
    {
        for (int i = panel.childCount - 1; i >= 0; i--)
            Destroy(panel.GetChild(i).gameObject);
        panel.DetachChildren();

        List<CraftDef> recipes = GameData.RecipesFor(id);
        // La misma receta que se ve en la cola y el panel (la elegida con ◂ ▸, o la mejor).
        int sel = Prefs.SelectedRecipe(id, recipes, 1);
        string title = $"<b>{GameData.Name(id)}</b>";
        if (recipes.Count > 0)
            title += $"   <color={Dim}>{Where(recipes[sel], id)}</color>";
        Row(0, id, title);

        // Sin <size> ni <i>: la fuente pixelada solo se ve nítida a su tamaño y sin cursiva falsa.
        if (recipes.Count == 0)
        {
            Row(0, null, $"<color={Dim}>{Lang.T("no_recipe")}</color>");
        }
        else
        {
            if (recipes.Count > 1)
                Row(0, null, $"<color={Dim}>{Lang.T("recipe").Replace("{0}", (sel + 1) + "/" + recipes.Count)}</color>"); // la mesa ya va en el título
            Ingredients(recipes[sel], 1, new HashSet<string> { id });
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
    }

    private void Ingredients(CraftDef craft, int depth, HashSet<string> visited)
    {
        foreach ((string nid, int n) in GameData.Needs(craft))
        {
            int have = GameData.Owned(nid);
            string count = GameData.IsFuel(nid)
                ? ""
                : $" <color={(have >= n ? Enough : Short)}>{have}/{n}</color>";
            List<CraftDef> sub = depth < MaxDepth && !visited.Contains(nid) && !GameData.IsFuel(nid)
                ? GameData.RecipesFor(nid)
                : new List<CraftDef>();
            CraftDef chosen = sub.Count > 0 ? sub[Prefs.SelectedRecipe(nid, sub, n)] : null;
            string where = chosen != null ? $"   <color={Dim}>{Where(chosen, nid)}</color>" : "";
            Row(depth, nid, $"{GameData.Name(nid)}{count}{where}");
            if (chosen != null)
                Ingredients(chosen, depth + 1, new HashSet<string>(visited) { nid });
        }
    }

    private static string Where(CraftDef craft, string id)
    {
        int output = GameData.OutputCount(craft, id);
        return GameData.Station(craft) + Prefs.Yield(output);
    }

    private void Row(int depth, string itemId, string text)
    {
        GameObject row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(panel, false);
        HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(Mathf.RoundToInt(depth * Indent), 0, 0, 0);
        h.spacing = 5f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        if (itemId != null)
        {
            GameObject ic = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            ic.transform.SetParent(row.transform, false);
            LayoutElement le = ic.GetComponent<LayoutElement>();
            Sprite sp = GameData.Icon(itemId);
            // Múltiplo exacto del tamaño real del ícono: el pixel art no se difumina.
            le.minWidth = le.preferredWidth = le.minHeight = le.preferredHeight = GameStyle.IconUnits(sp, Icon, canvas.scaleFactor);
            Image img = ic.GetComponent<Image>();
            img.sprite = sp;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = img.sprite != null;
            GameStyle.Apply(img);
        }

        GameObject tx = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        tx.transform.SetParent(row.transform, false);
        TextMeshProUGUI t = tx.GetComponent<TextMeshProUGUI>();
        GameStyle.Apply(t);
        t.fontSize = GameStyle.FontSize(Plugin.HoverTextSize);
        t.color = Text;
        t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        t.text = text;
    }

    // Se pone a la izquierda del cursor (el tooltip del juego suele salir a la derecha)
    // y se voltea si no cabe en pantalla.
    private void Place()
    {
        float s = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        Vector2 m = (Vector2)Input.mousePosition / s;
        if (anchorCell != null)
        {
            // Con el control no hay mouse: junto a la celda seleccionada.
            Canvas c = anchorCell.GetComponentInParent<Canvas>();
            Camera cam = c != null && c.renderMode != RenderMode.ScreenSpaceOverlay ? c.worldCamera : null;
            Vector3[] corners = new Vector3[4];
            anchorCell.GetWorldCorners(corners);
            m = RectTransformUtility.WorldToScreenPoint(cam, corners[1]) / s;
        }
        Vector2 size = panel.rect.size;
        Vector2 screen = new Vector2(Screen.width, Screen.height) / s;
        bool left = m.x - 18f - size.x >= 4f;
        panel.pivot = new Vector2(left ? 1f : 0f, 1f);
        float x = left ? m.x - 18f : Mathf.Min(m.x + 28f, screen.x - size.x - 4f);
        float y = Mathf.Clamp(m.y + 8f, size.y + 4f, screen.y - 4f);
        panel.anchoredPosition = new Vector2(x, y);
    }
}
