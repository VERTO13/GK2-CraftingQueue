using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CraftQueue;

// Burbujitas encima de los cofres y almacenes de la zona: qué materiales de tu cola tienen y cuántos.
// Así se ve de un vistazo a dónde ir, sin pasar el mouse. Solo jugando (sin ventanas abiertas),
// con el panel visible y el pin general o el de alguna receta prendido.
//  - Mismo punto de anclaje que las burbujas del juego sobre los objetos.
//  - Cuadrícula de 2 columnas (máximo 4 materiales, o 3 y "+N"): angostas, para no invadir al vecino.
//  - Si dos burbujas se enciman (cofres juntos), la de arriba se sube lo necesario.
//  - Translúcidas; casi invisibles con el mouse encima o si tapan a tu personaje.
//  - Solo la zona donde estás (cada cuarto o piso es su propia zona).
internal class ChestMarks : MonoBehaviour
{
    private const int Columns = 2;
    private const int MaxCells = 4;
    private const float Normal = 1f, Faded = 0.25f;

    internal static bool Dirty; // el panel cambió de materiales: revisar ya

    private sealed class Mark
    {
        public RectTransform box;
        public CanvasGroup group;
        public Wgo view;
        public SGuid id;
        public string signature;
    }

    private Canvas canvas;
    private readonly Dictionary<SGuid, Mark> marks = new Dictionary<SGuid, Mark>();
    private readonly List<(Mark mark, float x, float y)> placing = new List<(Mark, float, float)>();
    private readonly List<Rect> placed = new List<Rect>();
    private static readonly Comparison<(Mark mark, float x, float y)> ByHeight = (a, b) => a.y.CompareTo(b.y);
    private float nextScan;
    private string lastError;

    private void LateUpdate()
    {
        try
        {
            Tick();
        }
        catch (Exception e)
        {
            if (e.ToString() != lastError)
            {
                lastError = e.ToString();
                Plugin.Log.LogError("Marcas en cofres: " + e);
            }
            HideAll();
            nextScan = Time.unscaledTime + 2f;
        }
    }

    private void Tick()
    {
        // Materials ya viene filtrado: toda la cola (pin general) o solo las recetas con pin.
        bool show = QueueHud.Showing && QueueHud.NoWindows && QueueHud.Materials.Count > 0
                    && MainGame.PlayerData != null && !GameState.InCutscene;
        if (!show)
        {
            HideAll();
            return;
        }
        Ensure();
        if (Dirty || Time.unscaledTime >= nextScan)
        {
            Dirty = false;
            nextScan = Time.unscaledTime + 0.5f;
            Scan();
        }
        Place();
    }

    private void Ensure()
    {
        if (canvas == null)
        {
            GameObject root = new GameObject("GK2 Marcas en cofres");
            DontDestroyOnLoad(root);
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Debajo de la interfaz del juego (barra rápida, avisos…), encima del mundo.
            canvas.sortingOrder = -100;
            canvas.pixelPerfect = true;
        }
        canvas.gameObject.SetActive(true);
        float scale = GameStyle.PanelScale(QueueHud.GameScale);
        if (!Mathf.Approximately(canvas.scaleFactor, scale))
        {
            canvas.scaleFactor = scale;
            foreach (Mark m in marks.Values)
                m.signature = null; // rearmar con los tamaños nítidos de la nueva escala
        }
    }

    private void HideAll()
    {
        if (canvas != null && canvas.gameObject.activeSelf)
            canvas.gameObject.SetActive(false);
    }

    // Qué cofres tienen qué: se revisa cada medio segundo (y en cuanto cambia la cola).
    private void Scan()
    {
        HashSet<SGuid> seen = new HashSet<SGuid>();
        int storages = 0, withItems = 0, noView = 0;
        foreach (WgoData wgo in GameData.ZoneStorages())
        {
            storages++;
            List<(string key, int count)> found = QueueHud.Materials
                .Select(k => (k, GameData.CountIn(wgo.Inventory, k)))
                .Where(x => x.Item2 > 0)
                .OrderByDescending(x => x.Item2)
                .ToList();
            if (found.Count == 0)
                continue;
            withItems++;
            Wgo view = GameScene.GetWgoViewGlobal(wgo.UniqueId);
            if (view == null || !view.isActiveAndEnabled)
            {
                noView++;
                continue;
            }
            seen.Add(wgo.UniqueId);
            string sig = string.Join(";", found.Select(f => f.key + "=" + f.count));
            if (!marks.TryGetValue(wgo.UniqueId, out Mark mark) || mark.box == null)
                marks[wgo.UniqueId] = mark = new Mark { id = wgo.UniqueId };
            mark.view = view;
            if (mark.signature != sig)
            {
                mark.signature = sig;
                Build(mark, found);
            }
        }
        foreach (SGuid id in marks.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            if (marks[id].box != null)
                Destroy(marks[id].box.gameObject);
            marks.Remove(id);
        }
        // Diagnóstico (solo cuando cambia): qué se busca y qué se encontró.
        string summary = $"materiales {QueueHud.Materials.Count} ({string.Join(", ", QueueHud.Materials.Take(6))}), " +
                         $"almacenes en la zona {storages}, con materiales {withItems}, sin dibujo {noView}, marcas {marks.Count}";
        if (summary != lastSummary)
        {
            lastSummary = summary;
            logPosition = true;
            Plugin.Log.LogDebug("Marcas en cofres: " + summary);
        }
    }

    private string lastSummary;
    private bool logPosition;
    private bool cameraFailed;

    private void Build(Mark mark, List<(string key, int count)> found)
    {
        if (mark.box != null)
            Destroy(mark.box.gameObject);
        mark.box = Frame("Marca");
        mark.box.pivot = new Vector2(0.5f, 0f);
        mark.group = mark.box.gameObject.AddComponent<CanvasGroup>();
        mark.group.blocksRaycasts = mark.group.interactable = false;

        float s = canvas.scaleFactor;
        // Un paso más grande que en el panel (36 px en 1080p; 0.75 del ícono del juego, nítido).
        float icon = GameStyle.IconUnitsForNative(48f, 18f, s);
        float font = GameStyle.FontSize(0f, 1f, s);
        // Todas las celdas del mismo ancho: las columnas quedan alineadas.
        string widest = found.Max(x => x.count).ToString();
        float cellWidth = icon + 2f + Mathf.Ceil(Measure(widest, font));

        int shown = found.Count > MaxCells ? MaxCells - 1 : found.Count;
        int cells = shown + (found.Count > shown ? 1 : 0);
        Transform row = null;
        for (int i = 0; i < cells; i++)
        {
            if (i % Columns == 0)
                row = Row(mark.box);
            if (i < shown)
                Cell(row, found[i].key, found[i].count.ToString(), icon, font, cellWidth);
            else
                Cell(row, null, "+" + (found.Count - shown), icon, font, cellWidth, dim: true);
        }
    }

    // Recuadro con el marco de pixel art que se ajusta a su contenido (renglones de arriba a abajo).
    private RectTransform Frame(string name)
    {
        GameObject b = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        b.transform.SetParent(canvas.transform, false);
        RectTransform rt = (RectTransform)b.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        Image bg = b.GetComponent<Image>();
        // Marco propio en pixel art (9-slice): se estira a lo que mida el contenido sin deformarse.
        bg.sprite = BubbleFrame();
        bg.type = Image.Type.Sliced;
        bg.pixelsPerUnitMultiplier = 1f;
        bg.raycastTarget = false;
        VerticalLayoutGroup v = b.GetComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(3, 4, 2, 2);
        v.spacing = 1f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = v.childForceExpandHeight = false;
        ContentSizeFitter f = b.GetComponent<ContentSizeFitter>();
        f.horizontalFit = f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rt;
    }

    // 7×7: esquinas redondeadas, borde café (como las barras del panel) y relleno oscuro casi opaco.
    // Bordes de 3 px para el 9-slice: el centro se estira, las esquinas quedan intactas.
    private static Sprite bubbleFrame;

    private static Sprite BubbleFrame()
    {
        if (bubbleFrame != null)
            return bubbleFrame;
        string[] rows =
        {
            ".ooooo.",
            "o#####o",
            "o#####o",
            "o#####o",
            "o#####o",
            "o#####o",
            ".ooooo.",
        };
        Color line = new Color(0.52f, 0.38f, 0.24f, 1f);
        Color fill = new Color(0.1f, 0.08f, 0.07f, 0.72f); // translúcido: se alcanza a ver lo de atrás
        Texture2D tex = new Texture2D(7, 7, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 7; y++)
            for (int x = 0; x < 7; x++)
                tex.SetPixel(x, 6 - y, rows[y][x] == 'o' ? line : rows[y][x] == '#' ? fill : Color.clear);
        tex.Apply();
        // 100 pixeles por unidad = los mismos que la interfaz de Unity: 1 pixel del dibujo = 1 unidad.
        // (Con 1 por unidad, el borde medía 300 unidades y la burbuja tapaba media pantalla.)
        bubbleFrame = Sprite.Create(tex, new Rect(0, 0, 7, 7), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(3, 3, 3, 3));
        return bubbleFrame;
    }

    private static Transform Row(Transform parent)
    {
        GameObject r = new GameObject("Renglon", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        r.transform.SetParent(parent, false);
        HorizontalLayoutGroup h = r.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 4f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        return r.transform;
    }

    private static void Cell(Transform row, string key, string text, float icon, float font, float width, bool dim = false)
    {
        GameObject c = new GameObject("Material", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        c.transform.SetParent(row, false);
        if (width > 0f) // ancho fijo: columnas alineadas en las burbujas
            c.GetComponent<LayoutElement>().minWidth = c.GetComponent<LayoutElement>().preferredWidth = width;
        HorizontalLayoutGroup h = c.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 2f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        if (key != null)
        {
            GameObject g = new GameObject("Icono", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            g.transform.SetParent(c.transform, false);
            LayoutElement le = g.GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = le.minHeight = le.preferredHeight = icon;
            Image img = g.GetComponent<Image>();
            img.sprite = GameData.Icon(key);
            img.preserveAspect = true;
            img.raycastTarget = false;
            GameStyle.Apply(img);
        }

        GameObject tg = new GameObject("Cantidad", typeof(RectTransform), typeof(TextMeshProUGUI));
        tg.transform.SetParent(c.transform, false);
        TextMeshProUGUI t = tg.GetComponent<TextMeshProUGUI>();
        GameStyle.Apply(t);
        t.fontSize = font;
        t.color = dim ? new Color(0.64f, 0.59f, 0.51f) : new Color(0.93f, 0.9f, 0.84f);
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        t.text = text;
    }

    private float Measure(string text, float font)
    {
        GameObject g = new GameObject("Medida", typeof(RectTransform), typeof(TextMeshProUGUI));
        g.transform.SetParent(canvas.transform, false);
        TextMeshProUGUI t = g.GetComponent<TextMeshProUGUI>();
        GameStyle.Apply(t);
        t.fontSize = font;
        float w = t.GetPreferredValues(text).x;
        Destroy(g);
        return w;
    }

    // Del mundo a pixeles de pantalla, con la misma cuenta que usa el juego para sus burbujas;
    // si fallara, con la cámara principal en coordenadas 0..1 de la pantalla.
    private Vector3 ToScreen(Vector3 world)
    {
        if (!cameraFailed)
        {
            try
            {
                return CameraSystem.WorldToScreenPoint(world);
            }
            catch (Exception e)
            {
                cameraFailed = true;
                Plugin.Log.LogWarning("Marcas en cofres: sin la cámara del juego, uso la principal (" + e.Message + ")");
            }
        }
        Camera cam = Camera.main;
        if (cam == null)
            return new Vector3(-9999f, -9999f, 0f);
        Vector3 vp = cam.WorldToViewportPoint(world);
        return new Vector3(vp.x * Screen.width, vp.y * Screen.height, vp.z);
    }

    // Cada cuadro: donde el juego pone sus propias burbujas sobre el objeto, siguiendo a la cámara,
    // en pixeles enteros y sin encimarse (ni entre ellas ni con la burbuja del juego).
    private void Place()
    {
        float s = canvas.scaleFactor;
        placing.Clear();
        placed.Clear();
        foreach (Mark mark in marks.Values)
        {
            if (mark.box == null || mark.view == null)
                continue;
            Vector3 screen = ToScreen(mark.view.BubbleDrawablePosition);
            float x = screen.x / s, y = screen.y / s;
            bool visible = x > -50f && x < Screen.width / s + 50f && y > -50f && y < Screen.height / s + 50f;
            if (logPosition)
            {
                logPosition = false;
                Plugin.Log.LogDebug($"Marcas en cofres: primera en pantalla ({screen.x:0},{screen.y:0},{screen.z:0.0}) de {Screen.width}x{Screen.height}, " +
                                   $"visible {visible}, tamaño {mark.box.rect.size}");
            }
            if (mark.box.gameObject.activeSelf != visible)
                mark.box.gameObject.SetActive(visible);
            if (!visible)
                continue;
            // Si el juego ya muestra su burbuja sobre este objeto, la nuestra va encima de ella.
            if (UIObjectBubbleManager.Instance != null
                && UIObjectBubbleManager.Instance.TryGetDisplayedBubble(mark.id, out UIObjectBubble theirs)
                && theirs != null && theirs.gameObject.activeInHierarchy)
            {
                Vector3[] c = new Vector3[4];
                ((RectTransform)theirs.transform).GetWorldCorners(c);
                y = Mathf.Max(y, c[1].y / s + 1f);
            }
            placing.Add((mark, x, y));
        }
        // Tu personaje en pantalla (de los pies a la cabeza) y el mouse, en unidades del lienzo.
        // Se usa la figura dibujada: la posición guardada en los datos está en el plano del mapa
        // (x, z) y convertida daba un punto equivocado (burbujas atenuadas estando lejos).
        Rect? player = null;
        Transform body = MainGame.PlayerController?.View?.transform;
        if (body != null)
        {
            Vector3 feet = ToScreen(body.position);
            Vector3 head = ToScreen(MainGame.PlayerController.View.BubbleDrawablePosition);
            float left = Mathf.Min(feet.x, head.x) / s - 6f, bottom = Mathf.Min(feet.y, head.y) / s;
            player = new Rect(left, bottom, Mathf.Abs(head.x - feet.x) / s + 12f, Mathf.Abs(head.y - feet.y) / s);
        }
        Vector2 mouse = (Vector2)Input.mousePosition / s;
        // De abajo hacia arriba: cada burbuja que choca con una ya puesta sube lo justo.
        placing.Sort(ByHeight); // sin LINQ: esto corre cada cuadro
        foreach ((Mark mark, float x, float y0) in placing)
        {
            float w = mark.box.rect.width, h = mark.box.rect.height;
            float y = y0;
            for (int guard = 0; guard < 12; guard++)
            {
                Rect r = new Rect(x - w / 2f, y, w, h);
                Rect hit = default;
                foreach (Rect p in placed)
                    if (p.Overlaps(r))
                    {
                        hit = p;
                        break;
                    }
                if (hit.width <= 0f)
                    break;
                y = hit.yMax + 1f;
            }
            placed.Add(new Rect(x - w / 2f, y, w, h));
            mark.box.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(y));

            // Casi invisible solo si el mouse está encima o si la burbuja tapa a tu personaje
            // (caminando detrás de ella). Cambio suave, no de golpe.
            Rect bubble = new Rect(x - w / 2f, y, w, h);
            bool fade = bubble.Contains(mouse) || (player.HasValue && bubble.Overlaps(player.Value));
            if (mark.group != null)
                mark.group.alpha = Mathf.MoveTowards(mark.group.alpha, fade ? Faded : Normal, Time.unscaledDeltaTime * 4f);
        }
    }
}
