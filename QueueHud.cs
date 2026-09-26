using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CraftQueue;

// Panel fijo en pantalla mientras juegas: la cola con tienes/necesitas y las recetas
// abiertas con la flecha. Cada tarea es un bloque (barra de título del juego + requisitos con
// líneas de árbol). Ancho fijo con nombres en varias líneas, alto máximo con scroll, y se
// puede arrastrar (con un candado para dejarlo fijo).
//
// El mouse se lee directo (Input) en vez de por el EventSystem: la interfaz del juego tiene
// capas encima que se quedaban con los clics y el panel nunca los recibía.
internal class QueueHud : MonoBehaviour
{
    // Medidas en unidades de la interfaz; se multiplican por la escala del panel y se
    // redondean a enteros para que líneas y márgenes caigan en pixeles exactos.
    private float Indent => U(8f);
    private float BodyIndent => U(12f);  // espacio para la línea de árbol
    private float GuideX => U(5f);
    private float BarWidth => U(4f);
    private float stripHeight = 11f; // título del panel: cabe el candado (9 px) y la letra
    private static float U(float v) => Mathf.Max(1f, Mathf.Round(v * Plugin.HudScale));
    private const string Dim = "#a39682";
    private static readonly Color Text = new Color(0.93f, 0.86f, 0.74f);
    private static readonly Color Done = new Color(0.6f, 0.9f, 0.5f);
    private static readonly Color Short = new Color(0.88f, 0.63f, 0.44f);
    private static readonly Color Guide = new Color(0.64f, 0.59f, 0.51f, 0.45f);
    private static readonly Color Hover = new Color(1f, 0.95f, 0.6f);

    internal static bool Dirty;
    internal static bool Showing;
    private static bool loggedSizes; // el panel está a la vista (entonces no hace falta el aviso de Ctrl+clic)

    private readonly Dictionary<string, int> shownCounts = new Dictionary<string, int>();
    private Dictionary<string, int> prevNeeds;               // cola del dibujo anterior
    private readonly HashSet<string> flashIds = new HashSet<string>(); // tareas recién agregadas/aumentadas
    private float flashUntil, nextCountCheck;

    private Canvas canvas;
    private RectTransform box;     // todo el panel (se posiciona y se arrastra)
    private RectTransform strip;   // barrita de arriba
    private Image lockIcon, marksIcon;

    // Para las marcas en los cofres: los materiales que el panel muestra ahora mismo y la escala.
    internal static readonly HashSet<string> Materials = new HashSet<string>();
    internal static float GameScale = 1f;
    internal static bool NoWindows; // jugando, sin cofres/mesas/menús abiertos
    private RectTransform frame;   // ventana visible (recorta el contenido)
    private RectTransform panel;   // contenido completo
    private RectTransform barTrack, barHandle;
    private string signature;
    private float nextCheck, nextScale;
    private Canvas gameCanvas; // el lienzo principal del juego (para copiar su escala)
    private float gameScale = 1f;
    private int rows;
    private int hiddenRows;
    private float fontSize, rowHeight, iconSize;
    private float scrollY, contentHeight, viewHeight;
    private bool pressing, dragging, scrolling, pressOnBar, pressOnTitle;
    private TMP_Text titleText;
    private Vector2 pressPos, lastMouse;
    private Image hovered;
    private readonly List<(RectTransform guide, RectTransform body, RectTransform lastRow)> guides =
        new List<(RectTransform, RectTransform, RectTransform)>();

    // Un error del panel nunca debe sacar la ventana de "Exception" del juego: se anota una vez
    // en el log del mod y el panel se vuelve a armar desde cero en el siguiente cuadro.
    private string lastError;

    private void Update()
    {
        long t = Perf.Start();
        try
        {
            Tick();
            Perf.Stop("panel", t);
        }
        catch (Exception e)
        {
            if (e.ToString() != lastError)
            {
                lastError = e.ToString();
                Plugin.Log.LogError("Panel de la cola: " + e);
            }
            if (Time.unscaledTime >= nextRetry) // reintentar, pero sin rearmarlo cada cuadro
            {
                nextRetry = Time.unscaledTime + 2f;
                Dirty = true;
            }
        }
    }

    private float nextRetry;

    private void Tick()
    {
        if (Input.GetKeyDown(Plugin.HudKey))
            Plugin.HudVisible = !Plugin.HudVisible;
        if (Input.GetKeyDown(Plugin.RecipeStyleKey) && Showing)
        {
            Plugin.CompactRecipes = !Plugin.CompactRecipes;
            Dirty = true;
        }
        if (Dirty)
        {
            Dirty = false;
            nextCheck = 0f; // se abrió/cerró una flecha: redibujar ya, no en el siguiente ciclo
            signature = null; // aunque la cola no cambió (p. ej. ya hay botones del juego para usar)
        }

        long tm = Perf.Start();
        HandleMouse();
        Perf.Stop("panel: mouse", tm, top: false);

        // Se abrió o cerró una ventana: revisar y acomodar ya, no en la siguiente vuelta.
        int windows = GameWindows.Signature();
        if (windows != lastWindows)
        {
            lastWindows = windows;
            nextCheck = 0f;
            GameWindows.Remeasure();
            lastFit = null;
        }

        if (Time.unscaledTime < nextCheck)
            return;
        nextCheck = Time.unscaledTime + 0.4f;
        try
        {
            long tq = Perf.Start();
            List<QueueView.Entry> items = ShouldShow() ? QueueView.Items() : null;
            Perf.Stop("panel: leer cola", tq, top: false);
            // Cola vacía: el panel se ve igual, con la ayuda de cómo agregar (así se sabe que el mod está activo).
            if (items == null || (items.Count == 0 && !Queue.HasSlot))
            {
                SetShown(false);
                return;
            }
            long ts = Perf.Start();
            GameStyle.TryInit();
            Perf.Stop("panel: estilo del juego", ts, top: false);
            long te = Perf.Start();
            Ensure();
            Perf.Stop("panel: escala", te, top: false);
            long tg = Perf.Start();
            string sig = Signature(items) + "|" + gameScale // la escala del juego cambia con la resolución
                + (Time.unscaledTime < flashUntil ? "|resaltado" : ""); // al terminar el resaltado se redibuja sin él
            Perf.Stop("panel: firma", tg, top: false);
            // Activo ANTES de armarlo: con el panel oculto Unity no mide el texto.
            SetShown(true);
            // Lo que tienes cambia al craftear o recoger sin que el juego avise al panel: cada
            // segundo se revisan los materiales que el panel muestra.
            if (Time.unscaledTime >= nextCountCheck)
            {
                nextCountCheck = Time.unscaledTime + 1f;
                long tc = Perf.Start();
                if (CountsChanged())
                    signature = null;
                Perf.Stop("panel: contar", tc, top: false);
            }
            if (sig != signature)
            {
                signature = sig;
                long tb = Perf.Start();
                Build(items);
                Perf.Stop("panel: armar", tb, top: false);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Panel de la cola: " + (e.InnerException ?? e).Message);
            SetShown(false);
            nextCheck = Time.unscaledTime + 5f;
        }
    }

    private LazyWidgetBase workWindow; // cofre/mesa abierta sobre la que se dibuja el panel

    private bool ShouldShow()
    {
        workWindow = null;
        if (!Plugin.HudVisible || MainGame.PlayerData == null || GameState.InCutscene)
            return false;
        bool? onlyWork = GameWindows.OnlyWorkWindows(out workWindow);
        NoWindows = onlyWork == null;
        if (onlyWork == null)
            return true; // jugando, sin ventanas
        if (onlyWork == true && Plugin.HudInWorkWindows)
            return true; // cofres, mesas de crafteo, construcción, tiendas…
        return !Plugin.HudHideWithWindows;
    }

    private bool CountsChanged()
    {
        foreach (KeyValuePair<string, int> kv in shownCounts)
            if (GameData.Owned(kv.Key) != kv.Value)
                return true;
        // También lo que decide qué se marca en los cofres (ingredientes de tareas plegadas).
        foreach (KeyValuePair<string, int> kv in ownedCache)
            if (!shownCounts.ContainsKey(kv.Key) && GameData.Owned(kv.Key) != kv.Value)
                return true;
        return false;
    }

    private void SetShown(bool shown)
    {
        Showing = shown && box != null;
        if (box != null && box.gameObject.activeSelf != shown)
        {
            box.gameObject.SetActive(shown);
            if (!shown)
            {
                signature = null; // al volver a mostrarse, se redibuja con los datos al día
                pressing = dragging = scrolling = false;
            }
        }
    }

    // Solo se redibuja cuando cambia algo: la cola, lo que tienes, qué flechas están abiertas o la configuración.
    private static string Signature(List<QueueView.Entry> items)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(Queue.Version).Append('|').Append(GameStyle.Ready).Append(GameStyle.Header != null).Append('|');
        foreach (QueueView.Entry e in items)
            sb.Append(e.id).Append('x').Append(e.need).Append(';');
        foreach (string p in Prefs.Expanded.OrderBy(p => p))
            sb.Append(p).Append(',');
        foreach (KeyValuePair<string, int> c in Prefs.Chosen.OrderBy(c => c.Key))
            sb.Append(c.Key).Append('=').Append(c.Value).Append(','); // receta elegida con ◂ ▸
        sb.Append(Plugin.HudTextSize).Append(Plugin.HudIconSize).Append(Plugin.HudOpacity).Append(Plugin.HudWidth);
        sb.Append(Plugin.HudMaxRows).Append(Plugin.HudScale).Append(Plugin.HudMaxHeight).Append(Plugin.CompactRecipes);
        sb.Append(LLBase.CurrentLang); // si cambias el idioma del juego, se redibuja traducido
        sb.Append('|').Append(GameData.KnowledgeStamp); // receta recién desbloqueada: aparece ya
        sb.Append(Screen.width).Append('x').Append(Screen.height).Append(Plugin.HudTop); // y si cambias la resolución
        return sb.ToString();
    }

    private void Ensure()
    {
        if (canvas == null)
            Create();
        // El lienzo del juego se busca una vez (buscar entre todos cuesta); después solo se lee su
        // escala. Si desaparece (cambio de escena), se vuelve a buscar, como mucho cada 5 s.
        if ((gameCanvas == null || !gameCanvas.isActiveAndEnabled) && Time.unscaledTime >= nextScale)
        {
            nextScale = Time.unscaledTime + 5f;
            gameCanvas = FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c != canvas && c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.scaleFactor > 0f)
                .OrderByDescending(c => c.GetComponent<CanvasScaler>() != null)
                .FirstOrDefault();
        }
        if (gameCanvas != null && gameCanvas.scaleFactor > 0f)
            GameScale = gameScale = gameCanvas.scaleFactor;
        // Escala entera (pixeles exactos): la fuente y los íconos pixelados solo se ven nítidos así.
        float panelScale = GameStyle.PanelScale(gameScale);
        canvas.scaleFactor = panelScale;
        // Jugando: por encima de toda la interfaz. Con un cofre/mesa abierta: justo encima de
        // esa ventana, pero debajo de los menús que se abran sobre ella (clic derecho, cantidad…).
        canvas.sortingOrder = workWindow != null ? GameWindows.SortingAbove(workWindow, 30000) : 30000;
        // La posición la pone Fit() cada cuadro (su lugar, o junto a la ventana abierta).
        lockIcon.sprite = Plugin.HudMovable ? LockOpen() : LockClosed();
        ((RectTransform)lockIcon.transform).sizeDelta = lockIcon.sprite.rect.size;
        RefreshMarksIcon();
    }

    private void RefreshMarksIcon()
    {
        marksIcon.sprite = Plugin.ChestMarks ? PinOn() : PinOff();
        ((RectTransform)marksIcon.transform).sizeDelta = marksIcon.sprite.rect.size;
    }

    private void Create()
    {
        GameObject root = new GameObject("GK2 Panel de la cola");
        DontDestroyOnLoad(root);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Encima de la interfaz del juego: así el juego sabe que el mouse está sobre interfaz
        // (y no mueve al personaje). Las ventanas del juego no chocan: el panel se oculta con ellas.
        canvas.sortingOrder = 30000;
        canvas.pixelPerfect = true; // la fuente pixelada se ve nítida solo en pixeles enteros
        root.AddComponent<GraphicRaycaster>();

        GameObject b = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        b.transform.SetParent(root.transform, false);
        box = (RectTransform)b.transform;
        Instance = this;
        Image blocker = b.GetComponent<Image>();
        blocker.color = Color.clear; // invisible; tapa el clic para que no llegue al mundo

        // Título del panel: de aquí se agarra para moverlo; el candado a la derecha lo deja fijo.
        GameObject s = new GameObject("Titulo del panel", typeof(RectTransform), typeof(Image));
        s.transform.SetParent(box, false);
        strip = (RectTransform)s.transform;
        strip.anchorMin = new Vector2(0f, 1f);
        strip.anchorMax = new Vector2(1f, 1f);
        strip.pivot = new Vector2(0.5f, 1f);
        strip.sizeDelta = new Vector2(0f, stripHeight);
        s.GetComponent<Image>().color = new Color(0.1f, 0.08f, 0.07f, 0.75f);
        GameObject tt = new GameObject("Texto", typeof(RectTransform), typeof(TextMeshProUGUI));
        tt.transform.SetParent(strip, false);
        RectTransform trt = (RectTransform)tt.transform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(5f, 0f);
        trt.offsetMax = new Vector2(-28f, 0f);
        titleText = tt.GetComponent<TextMeshProUGUI>();
        titleText.alignment = TextAlignmentOptions.MidlineLeft;
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        titleText.overflowMode = TextOverflowModes.Ellipsis;
        titleText.color = new Color(0.64f, 0.59f, 0.51f);
        GameObject l = new GameObject("Candado", typeof(RectTransform), typeof(Image));
        l.transform.SetParent(strip, false);
        RectTransform lrt = (RectTransform)l.transform;
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(1f, 0.5f);
        lrt.anchoredPosition = new Vector2(-3f, 0f);
        lockIcon = l.GetComponent<Image>();
        // A su izquierda: marcar en los cofres dónde están los materiales (prender/apagar).
        GameObject mk = new GameObject("Marcar cofres", typeof(RectTransform), typeof(Image));
        mk.transform.SetParent(strip, false);
        RectTransform mrt = (RectTransform)mk.transform;
        mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(1f, 0.5f);
        mrt.anchoredPosition = new Vector2(-3f - 7f - 5f, 0f); // candado (7 px) + separación
        marksIcon = mk.GetComponent<Image>();
        marksIcon.raycastTarget = false;

        // Ventana que recorta el contenido (debajo de la barrita).
        GameObject f = new GameObject("Ventana", typeof(RectTransform), typeof(RectMask2D));
        f.transform.SetParent(box, false);
        frame = (RectTransform)f.transform;
        frame.anchorMin = new Vector2(0f, 0f);
        frame.anchorMax = new Vector2(1f, 1f);
        frame.offsetMin = Vector2.zero;
        frame.offsetMax = new Vector2(0f, -stripHeight);

        GameObject p = new GameObject("Contenido", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        p.transform.SetParent(frame, false);
        panel = (RectTransform)p.transform;
        panel.anchorMin = new Vector2(0f, 1f);
        panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(0.5f, 1f);
        VerticalLayoutGroup v = p.GetComponent<VerticalLayoutGroup>();
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        p.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Barrita de scroll (solo se ve si el contenido no cabe).
        GameObject t = new GameObject("Barra", typeof(RectTransform), typeof(Image));
        t.transform.SetParent(frame, false);
        barTrack = (RectTransform)t.transform;
        barTrack.anchorMin = new Vector2(1f, 0f);
        barTrack.anchorMax = new Vector2(1f, 1f);
        barTrack.pivot = new Vector2(1f, 0.5f);
        t.GetComponent<Image>().color = new Color(0.1f, 0.08f, 0.07f, 0.6f);
        GameObject h = new GameObject("Mango", typeof(RectTransform), typeof(Image));
        h.transform.SetParent(barTrack, false);
        barHandle = (RectTransform)h.transform;
        barHandle.anchorMin = new Vector2(0f, 1f);
        barHandle.anchorMax = new Vector2(1f, 1f);
        barHandle.pivot = new Vector2(0.5f, 1f);
        h.GetComponent<Image>().color = new Color(0.64f, 0.59f, 0.51f, 0.9f);

        foreach (Graphic g in root.GetComponentsInChildren<Graphic>(true))
            g.raycastTarget = g == blocker;
    }

    // --- Mouse: arrastrar, rueda, clic en flechas y candado ---

    private void HandleMouse()
    {
        if (box == null || !box.gameObject.activeInHierarchy)
        {
            pressing = dragging = scrolling = false;
            SetHover(null);
            UpdateTooltip(Vector2.zero, false);
            return;
        }
        if (GamepadInput.InPanel)
        {
            // Con el control manda la selección (UpdateNavMark), no el mouse.
            pressing = dragging = scrolling = false;
            SetHover(null);
            UpdateTooltip(Vector2.zero, false);
            return;
        }
        Vector2 m = Input.mousePosition;
        bool over = Inside(box, m);
        UpdateTooltip(m, over && !dragging && !scrolling && Inside(frame, m));
        float s = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

        if (over && Input.mouseScrollDelta.y != 0f && contentHeight > viewHeight + 0.5f)
        {
            scrollY -= Input.mouseScrollDelta.y * rowHeight * 2f;
            ApplyScroll();
        }

        // Los botones − + basura aparecen en la barra de la tarea que tiene el mouse encima.
        ShowActionsFor(dragging || scrolling || !over || !Inside(frame, m) ? null : HeaderAt(m));
        SetHover(dragging || scrolling || !over ? null : ClickableAt(m));

        if (Input.GetMouseButtonDown(0) && over)
        {
            pressing = true;
            pressPos = lastMouse = m;
            pressOnBar = barTrack.gameObject.activeSelf && Inside(barTrack, m);
            pressOnTitle = Inside(strip, m);
        }
        if (!pressing)
            return;

        if (Input.GetMouseButton(0))
        {
            bool overflow = contentHeight > viewHeight + 0.5f;
            if (!dragging && !scrolling && (m - pressPos).sqrMagnitude > 36f)
            {
                // Mantener y deslizar: desde el título se mueve el panel (si el candado está
                // abierto); desde cualquier otra parte se hace scroll (por si la rueda no funciona).
                if (pressOnTitle)
                    dragging = Plugin.HudMovable && fit == FitMode.Normal; // movido o plegado por una ventana: no
                else if (overflow)
                    scrolling = true;
            }
            Vector2 delta = (m - lastMouse) / s;
            lastMouse = m;
            if (dragging)
            {
                box.anchoredPosition += delta;
                KeepOnScreen(s);
            }
            else if (scrolling)
            {
                // Arrastrar el contenido lo sigue al dedo; arrastrar la barrita la mueve a ella
                // (proporcional: la barrita recorre todo el contenido en su propio alto).
                scrollY += pressOnBar ? -delta.y * contentHeight / Mathf.Max(1f, viewHeight) : delta.y;
                ApplyScroll();
            }
            return;
        }

        // Se soltó el botón.
        if (dragging)
            SavePosition(s);
        else if (!scrolling)
            Click(m);
        pressing = dragging = scrolling = false;
    }

    private static bool Inside(RectTransform rt, Vector2 screen) =>
        rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);

    // La flecha (o el candado) bajo el mouse, si hay.
    private Image ClickableAt(Vector2 m)
    {
        // El candado y el botón de marcas: su lado de la barrita, con margen para atinarles fácil.
        if (Inside(strip, m) && m.x >= LeftEdge(lockIcon) - 2f * canvas.scaleFactor)
            return lockIcon;
        if (Inside(strip, m) && m.x >= LeftEdge(marksIcon) - 3f * canvas.scaleFactor)
            return marksIcon;
        if (!Inside(frame, m))
            return null;
        foreach (KeyValuePair<Image, (object entry, int action)> b in actionButtons)
            if (b.Key != null && b.Key.gameObject.activeInHierarchy && Inside((RectTransform)b.Key.transform, m))
                return b.Key;
        foreach (Image pin in focusPins.Keys)
            if (pin != null && pin.gameObject.activeInHierarchy && Inside((RectTransform)pin.transform, m))
                return pin;
        foreach (Image c in cycleButtons.Keys)
            if (c != null && Inside((RectTransform)c.transform.parent, m)) // el área completa del botón
                return c;
        foreach (HudArrow a in arrows)
            if (a != null && Inside((RectTransform)a.transform, m))
                return a.image;
        return null;
    }

    // --- Pin de cada tarea: marcar en los cofres solo sus materiales ---
    // Pin general prendido: toda la cola; si no, solo las tareas con pin. El pin se ve al pasar el
    // mouse por la barra de la tarea y se queda visible (relleno) mientras esté marcado.

    private readonly Dictionary<string, HashSet<string>> blockMaterials = new Dictionary<string, HashSet<string>>();
    private readonly Dictionary<Image, (RectTransform head, string id)> focusPins = new Dictionary<Image, (RectTransform, string)>();
    private readonly Dictionary<string, int> ownedCache = new Dictionary<string, int>();

    private void Seen(string key, int have) => shownCounts[key] = have;

    private int OwnedNow(string id)
    {
        if (!ownedCache.TryGetValue(id, out int n))
            ownedCache[id] = n = GameData.Owned(id);
        return n;
    }

    private bool Complete(string id, int need) => GameData.IsFuel(id) || OwnedNow(id) >= need;

    // Qué se marca en los cofres de una tarea, se vea o no en el panel (aunque esté plegada):
    //  - el objeto mismo (si lo tienes, para saber dónde está);
    //  - si no lo tienes: los ingredientes de la receta elegida;
    //  - de esos, los que te falten y tengas desplegados en el panel: sus ingredientes, y así.
    private HashSet<string> MarksForItem(string item, int total, int preferred)
    {
        HashSet<string> m = new HashSet<string> { item };
        if (!Complete(item, total))
            MarkRecipe(m, item, total, item, 1, preferred);
        return m;
    }

    private HashSet<string> MarksForBuild(QueueView.Entry e)
    {
        HashSet<string> m = new HashSet<string>();
        foreach ((string pid, int per) in e.parts)
        {
            int need = per * e.need;
            bool done = Complete(pid, need);
            m.Add(pid);
            string path = e.id + "/" + pid;
            if (!done && Prefs.Expanded.Contains(path))
                MarkRecipe(m, pid, need, path, 2, -1);
        }
        return m;
    }

    private void MarkRecipe(HashSet<string> m, string id, int need, string path, int depth, int preferred)
    {
        if (depth > Prefs.MaxDepth + 2)
            return;
        List<CraftDef> recipes = GameData.RecipesFor(id);
        if (recipes.Count == 0)
            return;
        CraftDef craft = recipes[Prefs.SelectedRecipe(id, recipes, Mathf.Max(1, need), preferred)];
        int output = Mathf.Max(1, GameData.OutputCount(craft, id));
        int crafts = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0, need) / (float)output));
        foreach ((string nid, int n) in GameData.Needs(craft))
        {
            bool done = Complete(nid, n * crafts);
            m.Add(nid);
            string childPath = path + "/" + nid;
            if (!path.Split('/').Contains(nid) && !done && Prefs.Expanded.Contains(childPath))
                MarkRecipe(m, nid, n * crafts, childPath, depth + 1, -1);
        }
    }

    private void FocusPin(RectTransform head, GameObject count, string id)
    {
        GameObject p = new GameObject("Pin", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        p.transform.SetParent(head, false);
        if (count != null)
            p.transform.SetSiblingIndex(count.transform.GetSiblingIndex()); // antes de los botones y la cantidad
        Image img = p.GetComponent<Image>();
        bool on = Queue.Pins.Contains(id);
        img.sprite = on ? PinOn() : PinOff();
        img.raycastTarget = false;
        LayoutElement le = p.GetComponent<LayoutElement>(); // tamaño nativo: pixeles enteros
        le.minWidth = le.preferredWidth = img.sprite.rect.width;
        le.minHeight = le.preferredHeight = img.sprite.rect.height;
        p.SetActive(on);
        focusPins[img] = (head, id);
    }

    private void RefreshFocusPins()
    {
        foreach (KeyValuePair<Image, (RectTransform head, string id)> f in focusPins)
            if (f.Key != null)
                f.Key.gameObject.SetActive(Queue.Pins.Contains(f.Value.id) || f.Value.head == shownActions);
    }

    // --- Botones − + basura en la barra de cada tarea (aparecen al pasar el mouse) ---

    private readonly List<(RectTransform head, GameObject actions, TMP_Text title)> headers =
        new List<(RectTransform, GameObject, TMP_Text)>();
    private readonly Dictionary<Image, (object entry, int action)> actionButtons = new Dictionary<Image, (object, int)>();
    private RectTransform shownActions;

    private RectTransform HeaderAt(Vector2 m)
    {
        foreach ((RectTransform head, GameObject _, TMP_Text _) in headers)
            if (Inside(head, m))
                return head;
        return null;
    }

    // Con el mouse encima: aparecen los botones a la IZQUIERDA de la cantidad (que sigue
    // visible, para ver cómo sube o baja) y el nombre se corta con "…" para dejarles lugar.
    private void ShowActionsFor(RectTransform head)
    {
        if (head == shownActions)
            return;
        foreach ((RectTransform h, GameObject actions, TMP_Text title) in headers)
        {
            if (h == null)
                continue;
            bool on = h == head;
            actions.SetActive(on);
            if (title != null)
            {
                title.textWrappingMode = on ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
                title.overflowMode = on ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            }
        }
        shownActions = head;
        RefreshFocusPins();
        Layout(); // un nombre de dos renglones pasa a uno: recalcular altos y líneas de árbol
    }

    private void HeaderActions(RectTransform head, GameObject count, object entry)
    {
        TMP_Text title = null;
        foreach (Transform c in head)
            if (c.gameObject != count && c.GetComponent<TMP_Text>() is TMP_Text t)
            {
                title = t;
                break;
            }

        GameObject g = new GameObject("Acciones", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        g.transform.SetParent(head, false);
        if (count != null)
            g.transform.SetSiblingIndex(count.transform.GetSiblingIndex()); // antes de la cantidad
        HorizontalLayoutGroup h = g.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 2f;
        h.childAlignment = TextAnchor.MiddleRight;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        g.GetComponent<LayoutElement>().flexibleWidth = 0f;
        (Sprite sprite, int action)[] buttons = { (MinusSprite(), -1), (PlusSprite(), 1), (TrashSprite(), 0) };
        foreach ((Sprite sprite, int action) in buttons)
        {
            // Primero el botón real del juego; si no está cargado, el dibujo propio.
            GameObject holder = new GameObject("Boton", typeof(RectTransform), typeof(LayoutElement));
            holder.transform.SetParent(g.transform, false);
            RectTransform game = GameStyle.CloneButton(action, holder.transform);
            if (game != null && game.GetComponent<Image>() is Image gimg)
            {
                Vector2 native = game.rect.size;
                if (native.x <= 0f || native.y <= 0f)
                    native = new Vector2(13f, 13f);
                // Más alto que el renglón: a la mitad exacta (sigue nítido). Se escala el botón
                // completo, así fondo y dibujo se achican parejos.
                float scale = native.y > rowHeight && native.y >= 18f ? 0.5f : 1f;
                game.anchorMin = game.anchorMax = game.pivot = new Vector2(0.5f, 0.5f);
                game.anchoredPosition = Vector2.zero;
                game.sizeDelta = native;
                game.localScale = new Vector3(scale, scale, 1f);
                if (game.GetComponent<LayoutElement>() is LayoutElement inner)
                    inner.ignoreLayout = true;
                LayoutElement hle = holder.GetComponent<LayoutElement>();
                hle.minWidth = hle.preferredWidth = native.x * scale;
                hle.minHeight = hle.preferredHeight = native.y * scale;
                actionButtons[gimg] = (entry, action);
                continue;
            }
            Destroy(holder);
            GameObject b = new GameObject("Boton", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            b.transform.SetParent(g.transform, false);
            Image img = b.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            LayoutElement le = b.GetComponent<LayoutElement>(); // tamaño nativo: pixeles enteros
            le.minWidth = le.preferredWidth = sprite.rect.width;
            le.minHeight = le.preferredHeight = sprite.rect.height;
            actionButtons[img] = (entry, action);
        }
        g.SetActive(false);
        headers.Add((head, g, title));
    }

    // Botones − + basura de cada tarea.
    // Un bloque puede juntar varias tareas del mismo objeto: − y + cambian la última agregada,
    // la basura las quita todas.
    private static void RunAction(object entry, int action)
    {
        List<object> entries = entry as List<object> ?? new List<object> { entry };
        if (entries.Count == 0)
            return;
        if (action == 0)
            foreach (object e in entries)
                Queue.Remove(e as QueueTask);
        else
            Queue.Change(entries[entries.Count - 1] as QueueTask, action);
        Dirty = true;
    }

    private static float LeftEdge(Image i)
    {
        Vector3[] c = new Vector3[4];
        ((RectTransform)i.transform).GetWorldCorners(c);
        return c[0].x;
    }

    private void SetHover(Image i)
    {
        if (hovered == i)
            return;
        if (hovered != null)
            hovered.color = Color.white;
        hovered = i;
        if (hovered != null)
            hovered.color = Hover;
    }

    private void Click(Vector2 m)
    {
        Image target = ClickableAt(m);
        if (target == null)
            return;
        if (target == lockIcon)
        {
            Plugin.HudMovable = !Plugin.HudMovable;
            lockIcon.sprite = Plugin.HudMovable ? LockOpen() : LockClosed();
            ((RectTransform)lockIcon.transform).sizeDelta = lockIcon.sprite.rect.size;
        }
        else if (target == marksIcon)
        {
            // Pin general = toda la cola: al prenderlo se quitan los pines de recetas en específico.
            Plugin.ChestMarks = !Plugin.ChestMarks;
            if (Plugin.ChestMarks)
                Queue.ClearPins();
            RefreshMarksIcon();
            Dirty = true; // recalcular qué se marca en los cofres
        }
        else if (actionButtons.TryGetValue(target, out var act))
        {
            RunAction(act.entry, act.action);
        }
        else if (focusPins.TryGetValue(target, out var pin))
        {
            // Una receta en específico: el pin general (toda la cola) se apaga solo.
            if (Queue.TogglePin(pin.id) && Plugin.ChestMarks)
            {
                Plugin.ChestMarks = false;
                RefreshMarksIcon();
            }

        }
        else if (cycleButtons.TryGetValue(target, out var cyc))
        {
            Prefs.CycleRecipe(cyc.id, cyc.current, cyc.delta, cyc.count);
            Dirty = true;
        }
        else
        {
            target.GetComponentInParent<HudArrow>()?.Toggle();
        }
        try { LazyAudio.PlayAndForget("gui_click"); } catch { }
    }

    private void KeepOnScreen(float s)
    {
        Vector3[] c = new Vector3[4];
        box.GetWorldCorners(c); // en un lienzo overlay, "mundo" = pixeles de pantalla
        float dx = 0f, dy = 0f;
        if (c[0].x < 0f) dx = -c[0].x;
        else if (c[2].x > Screen.width) dx = Screen.width - c[2].x;
        if (c[1].y > Screen.height) dy = Screen.height - c[1].y;
        else if (c[0].y < 0f) dy = -c[0].y;
        if (dx != 0f || dy != 0f)
            box.anchoredPosition += new Vector2(dx, dy) / s;
    }

    // --- Control: navegar el panel (ver GamepadInput) ---
    // Cada renglón navegable: barras de tarea, ingredientes y el renglón "1/2 · Mesa ◂ ▸".
    // El seleccionado se resalta y, si es una tarea, muestra sus botones − + como con el mouse.

    internal static QueueHud Instance;

    private sealed class NavRow
    {
        public RectTransform rt;
        public string key;      // para no perder la selección al redibujar
        public string arrow;    // ruta de su flecha (abrir/cerrar), si tiene
        public object entry;    // tareas de la cola (− +), si es una barra de tarea
        public string pin;      // id para el pin, si es una barra de tarea
        public string recipeOf; // objeto cuyas recetas se cambian con LB/RB
    }

    private readonly List<NavRow> navRows = new List<NavRow>();
    private readonly List<HudArrow> arrows = new List<HudArrow>(); // flechas del panel (sin buscarlas cada cuadro)
    private int navIndex;
    private string navKey;
    private Image navMark;

    private void AddNav(RectTransform rt, string key, string arrow = null, object entry = null, string pin = null, string recipeOf = null) =>
        navRows.Add(new NavRow { rt = rt, key = key + "#" + navRows.Count(n => n.key.StartsWith(key)), arrow = arrow, entry = entry, pin = pin, recipeOf = recipeOf });

    private NavRow Focused => GamepadInput.InPanel && navIndex >= 0 && navIndex < navRows.Count ? navRows[navIndex] : null;

    internal bool GamepadEnter()
    {
        if (navRows.Count == 0 || fit != FitMode.Normal)
            return false;
        int i = navKey != null ? navRows.FindIndex(n => n.key == navKey) : -1;
        navIndex = i >= 0 ? i : 0;
        navKey = navRows[navIndex].key;
        return true;
    }

    internal void GamepadExit()
    {
        if (navMark != null)
            navMark.gameObject.SetActive(false);
        ShowActionsFor(null);
    }

    internal void GamepadMove(int delta)
    {
        if (navRows.Count == 0)
            return;
        navIndex = Mathf.Clamp(navIndex + delta, 0, navRows.Count - 1);
        navKey = navRows[navIndex].key;
        try { LazyAudio.PlayAndForget("gui_click"); } catch { }
    }

    internal void GamepadFold(bool open)
    {
        string path = Focused?.arrow;
        if (path == null)
            return;
        bool inverted = path.StartsWith("~"); // "~id" guardado = plegado (las tareas van abiertas por defecto)
        bool isOpen = Prefs.Expanded.Contains(path) != inverted;
        if (isOpen == open)
            return;
        Prefs.Toggle(path);
        Dirty = true;
    }

    internal void GamepadCycle(int delta)
    {
        string id = Focused?.recipeOf;
        if (id == null)
            return;
        foreach (var (cid, current, _, count) in cycleButtons.Values)
            if (cid == id)
            {
                Prefs.CycleRecipe(cid, current, delta, count);
                Dirty = true;
                return;
            }
    }

    internal void GamepadChange(int delta)
    {
        if (Focused?.entry != null)
            RunAction(Focused.entry, delta);
    }

    internal void GamepadPin()
    {
        string id = Focused?.pin;
        if (id == null)
            return;
        if (Queue.TogglePin(id) && Plugin.ChestMarks)
        {
            Plugin.ChestMarks = false;
            RefreshMarksIcon();
        }
    }

    // Cada cuadro: resaltado sobre el renglón seleccionado y scroll para que se vea.
    private void UpdateNavMark()
    {
        NavRow row = Focused;
        if (row == null || row.rt == null)
        {
            if (navMark != null && navMark.gameObject.activeSelf)
                navMark.gameObject.SetActive(false);
            return;
        }
        if (navMark == null)
        {
            GameObject g = new GameObject("Seleccion del control", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            g.GetComponent<LayoutElement>().ignoreLayout = true;
            navMark = g.GetComponent<Image>();
            navMark.raycastTarget = false;
            navMark.color = new Color(1f, 0.85f, 0.45f, 0.22f);
        }
        if (navMark.transform.parent != panel)
            navMark.transform.SetParent(panel, false);
        navMark.transform.SetAsLastSibling();
        navMark.gameObject.SetActive(true);

        // Posición del renglón dentro del contenido del panel.
        Vector3[] c = new Vector3[4];
        row.rt.GetWorldCorners(c);
        Vector3 bl = panel.InverseTransformPoint(c[0]), tr = panel.InverseTransformPoint(c[2]);
        RectTransform m = (RectTransform)navMark.transform;
        m.anchorMin = m.anchorMax = m.pivot = new Vector2(0f, 1f);
        Rect content = panel.rect;
        m.anchoredPosition = new Vector2(0f, Mathf.Round(tr.y - content.yMax));
        m.sizeDelta = new Vector2(content.width, Mathf.Round(tr.y - bl.y));

        // Que se vea: si queda fuera de la ventanita, se desplaza lo justo.
        float top = content.yMax - tr.y, bottom = content.yMax - bl.y; // distancia desde arriba del contenido
        if (top < scrollY)
            scrollY = top;
        else if (bottom > scrollY + viewHeight)
            scrollY = bottom - viewHeight;
        ApplyScroll();

        ShowActionsFor(row.entry != null ? row.rt : null); // una tarea: sus botones − + a la vista
    }

    // --- Junto a cofres y mesas: no tapar la ventana ---
    // Si el panel taparía la ventana abierta: se mueve al espacio libre de un lado si cabe; si no
    // cabe (p. ej. 720p), se pliega a su barrita "Cola" en una esquina libre y al pasar el mouse
    // se despliega encima de la ventana; al quitar el mouse se vuelve a plegar.

    private enum FitMode { Normal, Moved, Folded }

    private int lastWindows;
    private static float peekUntil;

    // Recién agregaste algo: si el panel está plegado por una ventana, se muestra 2 segundos.
    internal static void ShowAfterAdd() => peekUntil = Time.unscaledTime + 2f;

    private FitMode fit = FitMode.Normal;
    private Vector2 fullSize;
    private bool barShown, peeking;
    private (FitMode, bool, float, Vector2, float, float)? lastFit; // último acomodo aplicado

    private void LateUpdate()
    {
        long t = Perf.Start();
        try
        {
            Fit();
            UpdateNavMark();
            Perf.Stop("panel: acomodo", t);
        }
        catch (Exception e)
        {
            if (e.ToString() != lastError)
            {
                lastError = e.ToString();
                Plugin.Log.LogError("Panel de la cola (acomodo): " + e);
            }
        }
    }

    private void Fit()
    {
        if (box == null || !box.gameObject.activeSelf || dragging || fullSize.x <= 0f)
            return;
        float s = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        float screenW = Screen.width / s, screenH = Screen.height / s;
        bool left = Plugin.HudLeft;
        // Dónde iría normalmente (unidades del lienzo, origen abajo a la izquierda).
        float xMin = left ? Plugin.HudSideOffset : screenW - Plugin.HudSideOffset - fullSize.x;
        float top = screenH - Mathf.Clamp(Plugin.HudTop, 0f, Mathf.Max(0f, screenH - fullSize.y));
        Rect normal = new Rect(xMin, top - fullSize.y, fullSize.x, fullSize.y);

        FitMode mode = FitMode.Normal;
        float x = xMin;
        Rect? occPx = workWindow != null ? GameWindows.Occupied() : null;
        Rect occ = default;
        if (occPx.HasValue)
        {
            occ = new Rect(occPx.Value.x / s, occPx.Value.y / s, occPx.Value.width / s, occPx.Value.height / s);
            if (normal.Overlaps(occ))
            {
                float gap = 3f;
                float rightSpace = screenW - occ.xMax - gap, leftSpace = occ.xMin - gap;
                if (fullSize.x <= rightSpace && (!left || fullSize.x > leftSpace))
                {
                    mode = FitMode.Moved;
                    x = Mathf.Min(occ.xMax + gap, screenW - fullSize.x);
                }
                else if (fullSize.x <= leftSpace)
                {
                    mode = FitMode.Moved;
                    x = Mathf.Max(0f, occ.xMin - gap - fullSize.x);
                }
                else
                    mode = FitMode.Folded;
            }
        }

        // Plegado: al pasar el mouse por la barrita se despliega; sigue abierto mientras el mouse
        // esté sobre el panel desplegado.
        // También se despliega un momento al agregar algo, para que se vea que sí entró.
        if (mode == FitMode.Folded)
            peeking = Inside(box, Input.mousePosition) || Time.unscaledTime < peekUntil;
        else
            peeking = false;

        Vector2 size = fullSize;
        if (mode == FitMode.Folded)
        {
            // La barrita: del ancho de su título y los dos botones, en la esquina de su lado
            // (o del otro, si solo allá hay espacio libre).
            float w = Mathf.Min(fullSize.x, Mathf.Ceil(titleText.GetPreferredValues(titleText.text).x) + 5f + 28f + 4f);
            float mine = left ? occ.xMin : screenW - occ.xMax, other = left ? screenW - occ.xMax : occ.xMin;
            bool onLeft = left ? (mine >= w || other < w) : !(mine >= w || other < w);
            float foldX = onLeft ? Mathf.Min(Plugin.HudSideOffset, Mathf.Max(0f, occ.xMin - w - 3f))
                                 : Mathf.Max(screenW - Plugin.HudSideOffset - w, Mathf.Min(screenW - w, occ.xMax + 3f));
            if (peeking)
                x = onLeft ? foldX : foldX + w - fullSize.x; // se despliega desde la barrita, encima de la ventana
            else
            {
                x = foldX;
                size = new Vector2(w, stripHeight);
            }
        }
        // Nunca fuera de la pantalla (resoluciones chicas o una posición guardada muy a la orilla).
        x = Mathf.Clamp(x, 0f, Mathf.Max(0f, screenW - size.x));
        float fromTop = Mathf.Clamp(Plugin.HudTop, 0f, Mathf.Max(0f, screenH - size.y));

        var key = (mode, peeking, x, size, fromTop, s);
        if (lastFit.HasValue && lastFit.Value.Equals(key))
            return;
        lastFit = key;
        fit = mode;
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(0f, 1f);
        box.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(fromTop));
        box.sizeDelta = size;
        bool folded = mode == FitMode.Folded && !peeking;
        frame.gameObject.SetActive(!folded);
        barTrack.gameObject.SetActive(!folded && barShown);
    }

    // Al soltarlo se pega al lado más cercano y guarda su distancia a ese borde y al de arriba,
    // así queda en el mismo lugar relativo en cualquier resolución.
    private void SavePosition(float s)
    {
        Vector3[] c = new Vector3[4];
        box.GetWorldCorners(c);
        float leftPx = c[0].x, rightPx = c[2].x, topPx = c[1].y;
        bool left = (leftPx + rightPx) * 0.5f < Screen.width * 0.5f;
        float side = left ? leftPx / s : (Screen.width - rightPx) / s;
        float top = (Screen.height - topPx) / s;
        Plugin.SetHudPosition(left, Mathf.Round(side), Mathf.Round(top));
    }

    private void ApplyScroll()
    {
        float max = Mathf.Max(0f, contentHeight - viewHeight);
        scrollY = Mathf.Clamp(Mathf.Round(scrollY), 0f, max);
        panel.anchoredPosition = new Vector2(0f, scrollY);
        if (max <= 0f)
            return;
        float handle = Mathf.Max(U(8f), viewHeight * viewHeight / contentHeight);
        barHandle.sizeDelta = new Vector2(0f, Mathf.Round(handle));
        barHandle.anchoredPosition = new Vector2(0f, -Mathf.Round((viewHeight - handle) * scrollY / max));
    }

    // --- Armado del contenido ---

    private void Build(List<QueueView.Entry> items)
    {
        for (int i = panel.childCount - 1; i >= 0; i--)
            Destroy(panel.GetChild(i).gameObject);
        panel.DetachChildren();
        guides.Clear();
        headers.Clear();
        actionButtons.Clear();
        cycleButtons.Clear();
        navRows.Clear();
        arrows.Clear();
        tipTargets.Clear();
        ShowDetail(null, null);
        shownActions = null;
        SetHover(null);
        rows = 0;
        hiddenRows = 0;
        // Letra al tamaño de los textos del juego en pantalla (16): la fuente pixelada solo es nítida
        // en 8 o 16 en 720p, y 8 es demasiado chica. La escala solo aprieta márgenes e íconos.
        fontSize = GameStyle.FontSize(Plugin.HudTextSize, 1f, canvas.scaleFactor);
        Sprite sample = items.Select(e => GameData.Icon(e.isBuild ? e.parts.FirstOrDefault().id ?? e.id : e.id)).FirstOrDefault(s => s != null);
        // Un solo recuadro para todos los íconos, calculado sobre el tamaño estándar de los íconos del
        // juego (48 px). Antes se calculaba ícono por ícono con su lado más largo y los dibujos anchos
        // (p. ej. 86×48) salían de otro tamaño y hacían los renglones más altos.
        iconSize = GameStyle.IconUnitsForNative(48f, Plugin.HudIconSize * Plugin.HudScale, canvas.scaleFactor);
        if (!loggedSizes && sample != null)
        {
            loggedSizes = true;
            Plugin.Log.LogInfo($"Panel: escala del juego {canvas.scaleFactor}, ícono real {sample.rect.width}x{sample.rect.height} px " +
                $"-> {iconSize} u, letra {fontSize} u");
        }
        rowHeight = Mathf.Max(iconSize + U(2f), fontSize) + U(1f);
        panel.GetComponent<VerticalLayoutGroup>().spacing = U(3f);
        shownCounts.Clear();
        ownedCache.Clear();
        blockMaterials.Clear();
        focusPins.Clear();

        // Tareas recién agregadas (o con más cantidad) desde el dibujo anterior: se iluminan un
        // momento, en lugar del aviso junto al cursor. La primera vez no se ilumina nada.
        if (Time.unscaledTime >= flashUntil)
            flashIds.Clear();
        if (prevNeeds != null)
            foreach (QueueView.Entry e in items)
                if (!prevNeeds.TryGetValue(e.id, out int before) || e.need > before)
                {
                    flashIds.Add(e.id);
                    flashUntil = Time.unscaledTime + 1.2f;
                }
        prevNeeds = new Dictionary<string, int>();
        foreach (QueueView.Entry e in items)
            prevNeeds[e.id] = e.need;

        foreach (object block in Blocks(items))
        {
            if (rows >= Plugin.HudMaxRows)
            {
                hiddenRows++;
                continue;
            }
            if (block is ItemBlock ib)
            {
                // Un solo bloque por objeto (aunque se haya agregado varias veces o desde distintas
                // mesas), con "tienes/necesitas" como cualquier objeto. Abierto por defecto
                // ("~id" = plegado); dentro, una receta a la vez con ◂ ▸.
                long tk = Perf.Start();
                blockMaterials["i:" + ib.item] = MarksForItem(ib.item, ib.total, ib.preferred);
                Perf.Stop("panel: qué marcar", tk, top: false);
                int have = GameData.Owned(ib.item);
                Seen(ib.item, have);
                string itemFold = "~" + ib.item;
                bool hasRecipe = GameData.RecipesFor(ib.item).Count > 0;
                Transform body = Block(ib.item, null, GameData.Name(ib.item), $"{have}/{ib.total}", have >= ib.total,
                    hasRecipe ? itemFold : null, inverted: true, flash: ib.ids.Any(flashIds.Contains), entry: ib.raws,
                    focusId: "i:" + ib.item);
                if (hasRecipe && !Prefs.Expanded.Contains(itemFold))
                    RecipeRows(body, ib.item, ib.total, ib.item, 1, ib.preferred);
                FinishBody(body);
                continue;
            }
            QueueView.Entry e = (QueueView.Entry)block;
            blockMaterials[e.id] = MarksForBuild(e);
            bool flash = flashIds.Contains(e.id);
            bool ready = true;
            foreach ((string pid, int per) in e.parts)
            {
                if (GameData.IsFuel(pid))
                    continue;
                int have = GameData.Owned(pid);
                Seen(pid, have); // aunque la tarea esté plegada, su color depende de esto
                ready &= have >= per * e.need;
            }
            string times = e.need > 1 ? $" ×{e.need}" : "";
            // Construcciones: la flecha pliega sus requisitos (abiertas por defecto; "~id" = plegada).
            string fold = "~" + e.id;
            Transform b = Block(e.iconItem, e.buildIcon, e.title + times, null, ready, fold, inverted: true, flash: flash,
                entry: new List<object> { e.raw }, focusId: e.id);
            if (!Prefs.Expanded.Contains(fold))
            {
                foreach ((string pid, int per) in e.parts)
                {
                    string path = e.id + "/" + pid;
                    Item(b, 1, pid, per * e.need, path);
                    Tree(b, pid, per * e.need, path, 2);
                }
            }
            FinishBody(b);
        }
        if (items.Count == 0)
            FinishBody(Block(null, null, $"<color={Dim}>{Lang.T("empty")}</color>", null, false, null, false, slot: false));
        if (hiddenRows > 0)
            FinishBody(Block(null, null, $"<color={Dim}>{Lang.T("more_rows", hiddenRows)}</color>", null, false, null, false, slot: false));

        // Control: seguir en el mismo renglón después de redibujar (o en el más cercano).
        if (navRows.Count == 0)
            navIndex = 0;
        else
        {
            int keep = navKey != null ? navRows.FindIndex(n => n.key == navKey) : -1;
            navIndex = keep >= 0 ? keep : Mathf.Clamp(navIndex, 0, navRows.Count - 1);
            navKey = navRows[navIndex].key;
        }

        // Lo que se marca en los cofres: pin general prendido = toda la cola; si no, solo las
        // tareas con pin (o nada). Un pin de una tarea que ya no está en la cola no cuenta.
        List<string> ids = (Plugin.ChestMarks ? blockMaterials.Keys : Queue.Pins.Where(blockMaterials.ContainsKey)).ToList();
        Materials.Clear();
        foreach (string id in ids)
            foreach (string key in blockMaterials[id])
                if (!GameData.IsFuel(key))
                    Materials.Add(key);
        ChestMarks.Dirty = true;

        Layout();
    }

    // Tareas de un mismo objeto juntas: las agregadas como objeto y las agregadas desde una mesa
    // ("receta ×N", que producen N × lo que rinda la receta). Construcciones quedan aparte.
    private class ItemBlock
    {
        public string item;
        public int total;
        public int preferred = -1; // receta con la que se agregó desde la mesa (si no se eligió otra)
        public readonly List<object> raws = new List<object>();
        public readonly List<string> ids = new List<string>();
    }

    private static List<object> Blocks(List<QueueView.Entry> items)
    {
        List<object> blocks = new List<object>();
        Dictionary<string, ItemBlock> byItem = new Dictionary<string, ItemBlock>();
        foreach (QueueView.Entry e in items)
        {
            string item = null;
            int amount = e.need;
            int preferred = -1;
            if (!e.isBuild)
            {
                item = e.id;
            }
            else if (e.id.StartsWith("craft:") && e.iconItem != null)
            {
                item = e.iconItem;
                CraftDef craft = GameBalance.Me?.GetDataOrNull<CraftDef>(e.id.Substring("craft:".Length));
                if (craft != null)
                {
                    amount = e.need * GameData.OutputCount(craft, item);
                    preferred = GameData.RecipesFor(item).IndexOf(craft);
                }
            }
            if (item == null)
            {
                blocks.Add(e); // construcción, obra del pueblo…
                continue;
            }
            if (!byItem.TryGetValue(item, out ItemBlock b))
            {
                b = new ItemBlock { item = item };
                byItem[item] = b;
                blocks.Add(b);
            }
            b.total += amount;
            if (b.preferred < 0)
                b.preferred = preferred;
            b.raws.Add(e.raw);
            b.ids.Add(e.id);
        }
        return blocks;
    }

    // Ancho fijo (los nombres largos bajan de renglón) y alto máximo con scroll.
    private void Layout()
    {
        long t = Perf.Start();
        LayoutCore();
        Perf.Stop("panel: medir tamaño", t, top: false);
    }

    private void LayoutCore()
    {
        float width = Plugin.HudWidth;
        // Título del panel a la altura de la letra, con el nombre que el juego usa para la cola.
        stripHeight = Mathf.Max(11f, fontSize + 2f);
        // Alto máximo: el configurado, pero nunca más de lo que cabe en pantalla desde donde
        // está el panel (en resoluciones chicas no se sale por abajo).
        float screenUnits = Screen.height / (canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f);
        float maxHeight = Mathf.Max(rowHeight * 3f, Mathf.Min(Plugin.HudMaxHeight, screenUnits - Plugin.HudTop - stripHeight - 4f));
        strip.sizeDelta = new Vector2(0f, stripHeight);
        frame.offsetMax = new Vector2(0f, -stripHeight);
        GameStyle.Apply(titleText);
        titleText.fontSize = fontSize;
        string label = GameData.Plain(LLBase.HasL("ui_craft_queue") ? LLBase.L("ui_craft_queue") : "");
        titleText.text = label.Length > 0 ? label : Lang.T("queue");
        box.sizeDelta = new Vector2(width, maxHeight + stripHeight);
        // El carril de la barrita siempre reservado: el contenido mide lo mismo con o sin scroll,
        // así nada se mueve ni se compacta al agregar o quitar tareas.
        panel.offsetMin = new Vector2(0f, panel.offsetMin.y);
        panel.offsetMax = new Vector2(-(BarWidth + U(2f)), panel.offsetMax.y);
        // Dos pasadas: el alto de un texto que baja de renglón depende del ancho que le tocó.
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

        contentHeight = panel.rect.height;
        bool overflow = contentHeight > maxHeight + 0.5f;
        viewHeight = Mathf.Min(contentHeight, maxHeight);
        box.sizeDelta = fullSize = new Vector2(width, viewHeight + stripHeight);
        barTrack.sizeDelta = new Vector2(BarWidth, 0f);
        barTrack.gameObject.SetActive(barShown = overflow);
        lastFit = null; // volver a acomodarlo junto a las ventanas con el tamaño nuevo
        ApplyScroll(); // conserva dónde ibas al redibujar

        // Líneas de árbol: hasta la mitad del último requisito directo, ya con los altos reales.
        foreach ((RectTransform guide, RectTransform body, RectTransform lastRow) in guides)
        {
            if (guide == null || body == null || lastRow == null)
                continue;
            Vector3 mid = body.InverseTransformPoint(lastRow.TransformPoint(lastRow.rect.center));
            guide.offsetMin = new Vector2(GuideX, mid.y - body.rect.yMin);
            guide.offsetMax = new Vector2(GuideX + U(2f), -1f);
        }
    }

    // Un bloque por tarea: barra de título (la del juego) + cuerpo para sus requisitos.
    // Devuelve el cuerpo, donde se agregan los renglones.
    private Transform Block(string iconItem, Sprite iconSprite, string title, string count, bool ready,
        string arrowPath, bool inverted, bool slot = true, bool flash = false, object entry = null, string focusId = null)
    {
        rows++;
        GameObject block = new GameObject("Tarea", typeof(RectTransform), typeof(VerticalLayoutGroup));
        block.transform.SetParent(panel, false);
        VerticalLayoutGroup bv = block.GetComponent<VerticalLayoutGroup>();
        bv.spacing = 0f;
        bv.childControlWidth = bv.childControlHeight = true;
        bv.childForceExpandWidth = true;
        bv.childForceExpandHeight = false;

        // Barra de título
        GameObject head = new GameObject("Titulo", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        head.transform.SetParent(block.transform, false);
        Image hbg = head.GetComponent<Image>();
        hbg.raycastTarget = false;
        if (!GameStyle.CopyLook(GameStyle.Header, hbg))
            hbg.color = new Color(0.35f, 0.24f, 0.15f, 0.95f); // sin el sprite del juego: barra café lisa
        hbg.raycastTarget = false;
        if (flash)
        {
            // Brillo encima de la barra de título: "esto se acaba de agregar".
            GameObject glow = new GameObject("Resaltado", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            glow.transform.SetParent(head.transform, false);
            glow.GetComponent<LayoutElement>().ignoreLayout = true;
            RectTransform grt = (RectTransform)glow.transform;
            grt.anchorMin = Vector2.zero;
            grt.anchorMax = Vector2.one;
            grt.offsetMin = grt.offsetMax = Vector2.zero;
            Image gi = glow.GetComponent<Image>();
            gi.color = new Color(1f, 0.9f, 0.5f, 0.28f);
            gi.raycastTarget = false;
        }
        HorizontalLayoutGroup hh = head.GetComponent<HorizontalLayoutGroup>();
        hh.padding = new RectOffset((int)U(4f), (int)U(5f), (int)U(1f), (int)U(1f));
        hh.spacing = U(3f);
        hh.childAlignment = TextAnchor.MiddleLeft;
        hh.childControlWidth = hh.childControlHeight = true;
        hh.childForceExpandWidth = hh.childForceExpandHeight = false;
        head.GetComponent<LayoutElement>().minHeight = rowHeight + U(1f);
        if (slot)
            ArrowSlot(head.transform, arrowPath, inverted);
        // Con marco de celda igual que los ingredientes (como en el juego): mismo tamaño en todo el panel.
        Fill(head.transform, iconItem, iconSprite, title, count, ready ? Done : Text, ready ? Done : Short, withCell: true);
        if (entry != null)
        {
            GameObject countObj = count != null ? head.transform.GetChild(head.transform.childCount - 1).gameObject : null;
            if (focusId != null)
                FocusPin((RectTransform)head.transform, countObj, focusId);
            long tb = Perf.Start();
            HeaderActions((RectTransform)head.transform, countObj, entry);
            Perf.Stop("panel: botones − +", tb, top: false);
            // Para el control: una tarea de objeto cambia sus recetas con LB/RB.
            AddNav((RectTransform)head.transform, "h:" + (focusId ?? title), arrowPath, entry, focusId,
                focusId != null && focusId.StartsWith("i:") ? iconItem : null);
        }

        // Cuerpo semitransparente con la línea de árbol a la izquierda
        GameObject body = new GameObject("Requisitos", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        body.transform.SetParent(block.transform, false);
        Image bbg = body.GetComponent<Image>();
        bbg.color = new Color(0.1f, 0.08f, 0.07f, Plugin.HudOpacity);
        bbg.raycastTarget = false;
        VerticalLayoutGroup v = body.GetComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset((int)BodyIndent, (int)U(5f), (int)U(1f), (int)U(2f));
        v.spacing = 0f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return body.transform;
    }

    // Sin requisitos visibles: se quita el cuerpo; con requisitos: línea vertical del árbol
    // (su largo se ajusta en Layout, cuando ya se conocen los altos reales).
    private void FinishBody(Transform body)
    {
        if (body.childCount == 0)
        {
            Destroy(body.gameObject);
            body.SetParent(null);
            return;
        }
        RectTransform lastTicked = null;
        foreach (Transform c in body)
            if (c.Find("Tick") != null)
                lastTicked = (RectTransform)c;
        if (lastTicked == null)
            return;
        GameObject g = new GameObject("Guia", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        g.transform.SetParent(body, false);
        g.GetComponent<LayoutElement>().ignoreLayout = true;
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 1f);
        Image gi = g.GetComponent<Image>();
        gi.color = Guide;
        gi.raycastTarget = false;
        guides.Add((rt, (RectTransform)body, lastTicked));
    }

    private void Item(Transform body, int depth, string id, int want, string path)
    {
        string name = GameData.Name(id);
        string arrow = Expandable(id, path, depth) ? path : null;
        if (GameData.IsFuel(id))
        {
            Line(body, depth, id, $"{name} ×{want}", null, Text, Text, arrow);
            return;
        }
        int have = GameData.Owned(id);
        Seen(id, have); // se vigila para redibujar si cambia (crafteo, recolección…)
        bool ok = have >= want;
        Line(body, depth, id, name, $"{have}/{want}", ok ? Done : Text, ok ? Done : Short, arrow);
    }

    // Tiene flecha si hay receta conocida, no es combustible, no repite un material de más arriba
    // (evita ciclos) y no pasa de la profundidad máxima.
    private static bool Expandable(string id, string path, int depth)
    {
        if (depth > Prefs.MaxDepth + 1 || GameData.IsFuel(id))
            return false;
        if (path.Split('/').Count(p => p == id) > 1)
            return false;
        return GameData.RecipesFor(id).Count > 0;
    }

    // Las mismas flechas que la página del personaje (mismas rutas): abrir aquí o allá es lo mismo.
    // Una sola receta a la vez: la elegida (o la mejor). Con varias, un renglón
    // "Receta 1/2 · rinde 4  ◂ ▸" para cambiarla (la elección se comparte con la página del personaje).
    private void Tree(Transform body, string id, int need, string path, int depth)
    {
        if (!Prefs.Expanded.Contains(path) || depth > Prefs.MaxDepth + 2)
            return;
        RecipeRows(body, id, need, path, depth, -1);
    }

    // La receta de un objeto: la elegida con ◂ ▸, si no la que se usó al agregarlo desde la mesa,
    // si no la mejor. Dos estilos (F4): una línea compacta, o línea + ingredientes.
    private void RecipeRows(Transform body, string id, int need, string path, int depth, int preferred)
    {
        List<CraftDef> recipes = GameData.RecipesFor(id);
        if (recipes.Count == 0)
            return;
        int sel = Prefs.SelectedRecipe(id, recipes, Mathf.Max(1, need), preferred);
        CraftDef craft = recipes[sel];
        int output = GameData.OutputCount(craft, id);
        int crafts = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0, need) / (float)Mathf.Max(1, output)));
        if (Plugin.CompactRecipes)
        {
            CompactRecipe(body, depth, id, sel, recipes.Count, output, craft, crafts);
            return;
        }
        if (recipes.Count > 1)
            RecipeSwitcher(body, depth, id, sel, recipes.Count, output, craft);
        foreach ((string nid, int n) in GameData.Needs(craft))
        {
            string childPath = path + "/" + nid;
            Item(body, depth, nid, n * crafts, childPath);
            if (!path.Split('/').Contains(nid))
                Tree(body, nid, n * crafts, childPath, depth + 1);
        }
    }

    // Estilo compacto: los ingredientes como ícono + "tienes/necesitas" (nombre al pasar el mouse),
    // hasta 3 por renglón. Con varias recetas, arriba "◂ 1/2 ▸ · rinde 4"; con una sola que
    // rinda más de 1, "· rinde N" al final de los ingredientes.
    private void CompactRecipe(Transform body, int depth, string id, int sel, int count, int output, CraftDef craft, int crafts)
    {
        List<(string nid, int want)> needs = GameData.Needs(craft).Select(x => (x.key, x.count * crafts)).ToList();
        // Mesa y cuánto da: "· Sierra circular ×4" (con varias recetas, junto a "◂ 1/2 ▸").
        string yields = count > 1 || output > 1 ? $"<color={Dim}>· {GameData.Station(craft)}{Prefs.Yield(output)}</color>" : null;

        if (count > 1)
        {
            GameObject top = CompactRow(body, depth);
            if (top == null)
                return;
            AddNav((RectTransform)top.transform, "s:" + id + ":" + depth, recipeOf: id);
            CycleArrow(top.transform, id, sel, -1, count);
            MakeText(top.transform, $"<color={Dim}>{sel + 1}/{count}</color>", Text, TextAlignmentOptions.Midline, wrap: false);
            CycleArrow(top.transform, id, sel, 1, count);
            if (yields != null)
                MakeText(top.transform, yields, Text, TextAlignmentOptions.MidlineLeft, wrap: false);
        }

        // Los ingredientes se acomodan según el ancho real disponible: si el siguiente no cabe,
        // baja de renglón (antes eran 3 fijos por renglón y se salían del panel).
        float available = Plugin.HudWidth - BodyIndent - U(5f) - (BarWidth + U(2f)) - Mathf.Max(0, depth - 1) * Indent;
        float gap = U(4f);
        GameObject row = CompactRow(body, depth);
        if (row == null)
            return;
        float used = 0f;
        List<RectTransform> pieces = new List<RectTransform>();
        foreach ((string nid, int want) in needs)
            pieces.Add(Chip(row.transform, nid, want));
        if (count <= 1 && yields != null)
            pieces.Add((RectTransform)MakeText(row.transform, yields, Text, TextAlignmentOptions.MidlineLeft, wrap: false).transform);
        foreach (RectTransform piece in pieces)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(piece);
            float w = LayoutUtility.GetPreferredWidth(piece);
            if (used > 0f && used + gap + w > available)
            {
                row = CompactRow(body, depth);
                if (row == null)
                {
                    piece.gameObject.SetActive(false);
                    Destroy(piece.gameObject);
                    continue;
                }
                used = 0f;
            }
            piece.SetParent(row.transform, false);
            used += (used > 0f ? gap : 0f) + w;
        }
    }

    private GameObject CompactRow(Transform body, int depth)
    {
        if (rows >= Plugin.HudMaxRows)
        {
            hiddenRows++;
            return null;
        }
        rows++;
        GameObject row = new GameObject("Receta compacta", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(body, false);
        HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(Mathf.RoundToInt(Mathf.Max(0, depth - 1) * Indent), 0, 0, 0);
        h.spacing = U(4f);
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        row.GetComponent<LayoutElement>().minHeight = rowHeight;
        return row;
    }

    // Nombre del ingrediente al pasar sobre él en el estilo compacto (ahí no se escribe). Sale a un
    // lado del panel, a la altura del ingrediente, sin tapar nada. Depende de qué elemento está
    // "enfocado", no de dónde está el mouse: lo mismo servirá para el gamepad.
    private readonly Dictionary<RectTransform, string> tipTargets = new Dictionary<RectTransform, string>();
    private RectTransform tipBox, tipTarget;
    private TMP_Text tipText;

    private void UpdateTooltip(Vector2 m, bool active)
    {
        RectTransform target = null;
        string key = null;
        if (active)
            foreach (KeyValuePair<RectTransform, string> c in tipTargets)
                if (c.Key != null && Inside(c.Key, m))
                {
                    target = c.Key;
                    key = c.Value;
                    break;
                }
        try
        {
            ShowDetail(target, key);
        }
        catch (Exception e)
        {
            // Un detalle que falla no debe romper el panel ni sacar la ventana de error del juego.
            if (tipBox != null)
                tipBox.gameObject.SetActive(false);
            if (!loggedTipError)
            {
                loggedTipError = true;
                Plugin.Log.LogWarning("Nombre del ingrediente: " + e);
            }
        }
    }

    private bool loggedTipError;

    private void ShowDetail(RectTransform target, string key)
    {
        if (target == null || key == null)
        {
            tipTarget = null;
            if (tipBox != null && tipBox.gameObject.activeSelf)
                tipBox.gameObject.SetActive(false);
            return;
        }
        if (target == tipTarget && tipBox != null && tipBox.gameObject.activeSelf)
            return;
        tipTarget = target;
        if (tipBox == null)
        {
            GameObject t = new GameObject("Nombre", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            t.transform.SetParent(canvas.transform, false);
            tipBox = (RectTransform)t.transform;
            tipBox.anchorMin = tipBox.anchorMax = Vector2.zero;
            Image bg = t.GetComponent<Image>();
            bg.color = new Color(0.1f, 0.08f, 0.07f, 0.95f);
            bg.raycastTarget = false;
            HorizontalLayoutGroup h = t.GetComponent<HorizontalLayoutGroup>();
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            ContentSizeFitter f = t.GetComponent<ContentSizeFitter>();
            f.horizontalFit = f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            tipText = MakeText(t.transform, "", Text, TextAlignmentOptions.MidlineLeft, wrap: false);
        }
        tipBox.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset((int)U(4f), (int)U(4f), (int)U(1f), (int)U(1f));
        tipText.fontSize = fontSize;
        tipText.text = GameData.Name(key);
        tipBox.gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(tipBox);

        // A un lado del panel (el lado con espacio), con el borde de arriba a la altura del ingrediente.
        float s = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        Vector3[] c = new Vector3[4];
        box.GetWorldCorners(c);
        float boxLeft = c[0].x / s, boxRight = c[2].x / s;
        target.GetWorldCorners(c);
        float top = c[1].y / s;
        float w = tipBox.rect.width, hgt = tipBox.rect.height;
        bool toLeft = boxLeft - w - U(2f) >= 0f || boxRight + w + U(2f) > Screen.width / s;
        tipBox.pivot = new Vector2(toLeft ? 1f : 0f, 1f);
        float x = toLeft ? boxLeft - U(2f) : boxRight + U(2f);
        float y = Mathf.Clamp(top, hgt, Screen.height / s);
        tipBox.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(y));
    }

    // Ingrediente en el estilo compacto: ícono con marco + "tienes/necesitas" (o "×N" si es combustible).
    private RectTransform Chip(Transform row, string nid, int want)
    {
        GameObject chip = new GameObject("Ingrediente", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        chip.transform.SetParent(row, false);
        HorizontalLayoutGroup h = chip.GetComponent<HorizontalLayoutGroup>();
        h.spacing = U(2f);
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        string label;
        Color color;
        if (GameData.IsFuel(nid))
        {
            label = "×" + want;
            color = Text;
        }
        else
        {
            int have = GameData.Owned(nid);
            Seen(nid, have);
            label = have + "/" + want;
            color = have >= want ? Done : Short;
        }
        Fill(chip.transform, nid, null, label, null, color, color, withCell: true, stretch: false);
        tipTargets[(RectTransform)chip.transform] = nid;
        return (RectTransform)chip.transform;
    }

    private void CycleArrow(Transform parent, string id, int sel, int delta, int count)
    {
        GameObject b = new GameObject(delta < 0 ? "Anterior" : "Siguiente", typeof(RectTransform), typeof(LayoutElement));
        b.transform.SetParent(parent, false);
        LayoutElement le = b.GetComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = 10f;
        le.minHeight = le.preferredHeight = fontSize;
        GameObject a = new GameObject("Flecha", typeof(RectTransform), typeof(Image));
        a.transform.SetParent(b.transform, false);
        RectTransform art = (RectTransform)a.transform;
        art.anchorMin = art.anchorMax = art.pivot = new Vector2(0.5f, 0.5f);
        Image img = a.GetComponent<Image>();
        img.sprite = delta < 0 ? Arrows.Left() : Arrows.Closed();
        img.raycastTarget = false;
        art.sizeDelta = img.sprite.rect.size; // tamaño nativo: pixeles enteros
        cycleButtons[img] = (id, sel, delta, count);
    }

    private readonly Dictionary<Image, (string id, int current, int delta, int count)> cycleButtons =
        new Dictionary<Image, (string, int, int, int)>();

    // "1/2 · Sierra circular ×4  ◂ ▸", alineado con los íconos de sus ingredientes.
    private void RecipeSwitcher(Transform body, int depth, string id, int sel, int count, int output, CraftDef craft)
    {
        if (rows >= Plugin.HudMaxRows)
        {
            hiddenRows++;
            return;
        }
        rows++;
        GameObject row = new GameObject("Receta", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(body, false);
        HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(Mathf.RoundToInt(Mathf.Max(0, depth - 1) * Indent + 10f + U(3f)), 0, 0, 0);
        h.spacing = U(3f);
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        row.GetComponent<LayoutElement>().minHeight = fontSize + U(1f);
        AddNav((RectTransform)row.transform, "s:" + id + ":" + depth, recipeOf: id);
        // Nombre de la mesa en una ventanita: si no cabe, se desliza como carrusel para leerlo
        // completo; el "×N" y las flechas quedan fijos a la derecha.
        GameObject view = new GameObject("Mesa", typeof(RectTransform), typeof(RectMask2D), typeof(LayoutElement));
        view.transform.SetParent(row.transform, false);
        LayoutElement vle = view.GetComponent<LayoutElement>();
        vle.flexibleWidth = 1f;
        vle.preferredWidth = 0f;
        vle.minWidth = 20f;
        vle.minHeight = vle.preferredHeight = fontSize + U(1f);
        string place = Prefs.RecipePlace(sel, count, craft);
        TMP_Text caption = MakeText(view.transform, $"<color={Dim}>{place}</color>", Text, TextAlignmentOptions.MidlineLeft, wrap: false);
        RectTransform crt = caption.rectTransform;
        crt.anchorMin = new Vector2(0f, 0f);
        crt.anchorMax = new Vector2(0f, 1f);
        crt.pivot = new Vector2(0f, 0.5f);
        crt.sizeDelta = new Vector2(Mathf.Ceil(caption.GetPreferredValues(place).x) + 1f, 0f);
        crt.anchoredPosition = Vector2.zero;
        view.AddComponent<Marquee>().Init(crt, canvas);
        string yield = Prefs.Yield(output).Trim();
        if (yield.Length > 0)
        {
            TMP_Text y = MakeText(row.transform, yield, Text, TextAlignmentOptions.MidlineRight, wrap: false);
            y.overflowMode = TextOverflowModes.Overflow;
            LayoutElement yle = y.gameObject.AddComponent<LayoutElement>();
            yle.minWidth = yle.preferredWidth = Mathf.Ceil(y.GetPreferredValues(yield).x) + 1f;
        }
        foreach (int delta in new[] { -1, 1 })
        {
            GameObject b = new GameObject(delta < 0 ? "Anterior" : "Siguiente", typeof(RectTransform), typeof(LayoutElement));
            b.transform.SetParent(row.transform, false);
            LayoutElement le = b.GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = 10f;
            le.minHeight = le.preferredHeight = fontSize;
            GameObject a = new GameObject("Flecha", typeof(RectTransform), typeof(Image));
            a.transform.SetParent(b.transform, false);
            RectTransform art = (RectTransform)a.transform;
            art.anchorMin = art.anchorMax = art.pivot = new Vector2(0.5f, 0.5f);
            Image img = a.GetComponent<Image>();
            img.sprite = delta < 0 ? Arrows.Left() : Arrows.Closed();
            img.raycastTarget = false;
            art.sizeDelta = img.sprite.rect.size; // tamaño nativo: pixeles enteros
            cycleButtons[img] = (id, sel, delta, count);
        }
    }

    private RectTransform Line(Transform body, int depth, string itemId, string text, string count, Color textColor, Color countColor,
        string arrowPath, bool slot = true)
    {
        if (rows >= Plugin.HudMaxRows)
        {
            hiddenRows++;
            return null;
        }
        rows++;
        GameObject row = new GameObject("Renglon", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(body, false);
        HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(Mathf.RoundToInt(Mathf.Max(0, depth - 1) * Indent), 0, 0, 0);
        h.spacing = U(3f);
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        row.GetComponent<LayoutElement>().minHeight = rowHeight; // crece si el nombre baja de renglón

        // Rama horizontal del árbol para los requisitos directos de la tarea.
        if (depth == 1 && itemId != null)
        {
            GameObject tick = new GameObject("Tick", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            tick.transform.SetParent(row.transform, false);
            tick.GetComponent<LayoutElement>().ignoreLayout = true;
            RectTransform rt = (RectTransform)tick.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(GuideX + U(2f) - BodyIndent, 0f);
            rt.sizeDelta = new Vector2(BodyIndent - GuideX - U(4f), U(2f));
            Image ti = tick.GetComponent<Image>();
            ti.color = Guide;
            ti.raycastTarget = false;
        }
        if (slot)
            ArrowSlot(row.transform, arrowPath, inverted: false);
        Fill(row.transform, itemId, null, text, count, textColor, countColor, withCell: true);
        if (itemId != null)
            AddNav((RectTransform)row.transform, "l:" + (arrowPath ?? itemId), arrowPath, recipeOf: itemId);
        return (RectTransform)row.transform;
    }

    // Flecha clicable (o un hueco del mismo ancho, para que los íconos queden alineados).
    private void ArrowSlot(Transform row, string path, bool inverted)
    {
        GameObject holder = new GameObject(path == null ? "Hueco" : "Flecha", typeof(RectTransform), typeof(LayoutElement));
        holder.transform.SetParent(row, false);
        LayoutElement le = holder.GetComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = 10f; // la flecha mide 9 px de diseño: no se achica para no difuminarla
        le.minHeight = le.preferredHeight = rowHeight;
        if (path == null)
            return;

        GameObject a = new GameObject("Imagen", typeof(RectTransform), typeof(Image));
        a.transform.SetParent(holder.transform, false);
        RectTransform rt = (RectTransform)a.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        Image img = a.GetComponent<Image>();
        img.raycastTarget = false;
        bool open = Prefs.Expanded.Contains(path) != inverted;
        img.sprite = open ? Arrows.Open() : Arrows.Closed();
        rt.sizeDelta = img.sprite.rect.size; // tamaño nativo: pixeles enteros
        HudArrow h = holder.AddComponent<HudArrow>();
        h.path = path;
        h.image = img;
        arrows.Add(h);
    }

    // Ícono (con el marco de celda del juego) + nombre (puede bajar de renglón) + tienes/necesitas.
    private void Fill(Transform row, string itemId, Sprite sprite, string text, string count, Color textColor, Color countColor, bool withCell,
        bool stretch = true)
    {
        Sprite icon = sprite ?? (itemId != null ? GameData.Icon(itemId) : null);
        if (icon != null)
        {
            float size = iconSize; // mismo recuadro para todos; los dibujos anchos se ajustan dentro (preserveAspect)
            GameObject holder = new GameObject("Icono", typeof(RectTransform), typeof(LayoutElement));
            holder.transform.SetParent(row, false);
            LayoutElement le = holder.GetComponent<LayoutElement>();
            float boxSize = withCell ? size + U(2f) : size;
            le.minWidth = le.preferredWidth = le.minHeight = le.preferredHeight = boxSize;
            if (withCell)
            {
                Image cell = holder.AddComponent<Image>();
                if (!GameStyle.CopyLook(GameStyle.CellBack, cell))
                    cell.color = new Color(0f, 0f, 0f, 0.35f);
                cell.raycastTarget = false;
            }
            GameObject ic = new GameObject("Imagen", typeof(RectTransform), typeof(Image));
            ic.transform.SetParent(holder.transform, false);
            RectTransform irt = (RectTransform)ic.transform;
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(size, size); // tamaño exacto: sin reescalar el pixel art
            Image img = ic.GetComponent<Image>();
            img.sprite = icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            if (itemId != null)
                GameStyle.Apply(img);
        }

        TMP_Text main = MakeText(row, text, textColor, TextAlignmentOptions.MidlineLeft, wrap: stretch);
        if (stretch)
        {
            LayoutElement mle = main.gameObject.AddComponent<LayoutElement>();
            mle.flexibleWidth = 1f;
            mle.preferredWidth = 0f; // toma el espacio que sobre; si no alcanza, baja de renglón
            mle.minWidth = 20f;
        }

        if (count != null)
        {
            TMP_Text c = MakeText(row, count, countColor, TextAlignmentOptions.MidlineRight, wrap: false);
            LayoutElement cle = c.gameObject.AddComponent<LayoutElement>();
            cle.flexibleWidth = 0f;
            cle.minWidth = 20f;
        }
    }

    private TMP_Text MakeText(Transform parent, string text, Color color, TextAlignmentOptions align, bool wrap)
    {
        GameObject go = new GameObject("Texto", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
        GameStyle.Apply(t);
        t.fontSize = fontSize;
        t.color = color;
        t.richText = true;
        t.alignment = align;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        t.text = text;
        return t;
    }

    // --- Candado (pixel art, mismo estilo que las flechas) ---

    private static Sprite lockOpen, lockClosed;

    private static Sprite LockClosed() => lockClosed != null ? lockClosed : lockClosed = PixelSprite(new[]
    {
        "..ooo..", ".o...o.", ".o...o.", "ooooooo", "o#####o", "o##o##o", "o##o##o", "o#####o", "ooooooo"
    });

    private static Sprite LockOpen() => lockOpen != null ? lockOpen : lockOpen = PixelSprite(new[]
    {
        "..ooo..", ".o...o.", ".....o.", "ooooooo", "o#####o", "o##o##o", "o##o##o", "o#####o", "ooooooo"
    });

    // Marcar cofres: pin relleno = prendido, solo contorno = apagado.
    private static Sprite pinOn, pinOff;

    private static Sprite PinOn() => pinOn != null ? pinOn : pinOn = PixelSprite(new[]
    {
        ".ooooo.", "o#####o", "o##o##o", "o#ooo#o", "o##o##o", ".o###o.", "..o#o..", "..o#o..", "...o..."
    });

    private static Sprite PinOff() => pinOff != null ? pinOff : pinOff = PixelSprite(new[]
    {
        ".ooooo.", "o.....o", "o..o..o", "o.ooo.o", "o..o..o", ".o...o.", "..o.o..", "..o.o..", "...o..."
    });

    // Botoncitos − + basura (gris y rojo, como los del juego).
    private static Sprite minus, plus, trash;
    private static readonly Color BtnLine = new Color(0.17f, 0.1f, 0.07f);
    private static readonly Color BtnGrey = new Color(0.36f, 0.34f, 0.36f);
    private static readonly Color BtnRed = new Color(0.62f, 0.16f, 0.13f);
    private static readonly Color BtnMark = new Color(0.93f, 0.86f, 0.74f);

    private static Sprite MinusSprite() => minus != null ? minus : minus = PixelSprite(new[]
    {
        ".ooooooo.", "o#######o", "o#######o", "o#######o", "o#wwwww#o", "o#######o", "o#######o", "o#######o", ".ooooooo."
    }, BtnLine, BtnGrey, BtnMark);

    private static Sprite PlusSprite() => plus != null ? plus : plus = PixelSprite(new[]
    {
        ".ooooooo.", "o#######o", "o###w###o", "o###w###o", "o#wwwww#o", "o###w###o", "o###w###o", "o#######o", ".ooooooo."
    }, BtnLine, BtnGrey, BtnMark);

    private static Sprite TrashSprite() => trash != null ? trash : trash = PixelSprite(new[]
    {
        ".ooooooo.", "o#######o", "o#wwwww#o", "o##w#w##o", "o##w#w##o", "o##w#w##o", "o##www##o", "o#######o", ".ooooooo."
    }, BtnLine, BtnRed, BtnMark);

    private static Sprite PixelSprite(string[] rows) =>
        PixelSprite(rows, new Color(0.93f, 0.86f, 0.74f), new Color(0.47f, 0.35f, 0.22f), Color.clear);

    private static Sprite PixelSprite(string[] rows, Color line, Color fill, Color mark)
    {
        int w = rows[0].Length, h = rows.Length;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, h - 1 - y, rows[y][x] == 'o' ? line : rows[y][x] == '#' ? fill : rows[y][x] == 'w' ? mark : Color.clear);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f);
    }
}

// Flecha del panel: abre/cierra igual que en la página del personaje (estado compartido).
// El clic lo detecta QueueHud leyendo el mouse directamente.
internal class HudArrow : MonoBehaviour
{
    public string path;
    public Image image;

    public void Toggle()
    {
        Prefs.Toggle(path);
        QueueHud.Dirty = true;
    }
}

// Carrusel: si el texto no cabe en su ventanita, se desliza despacio hasta el final, espera,
// regresa y vuelve a empezar. Se mueve de pixel en pixel para que la letra siga nítida.
internal class Marquee : MonoBehaviour
{
    private const float Pause = 1.6f;   // segundos quieto en cada extremo
    private const float Speed = 14f;    // unidades por segundo

    private RectTransform text, view;
    private Canvas canvas;
    private float t;

    public void Init(RectTransform text, Canvas canvas)
    {
        this.text = text;
        this.canvas = canvas;
        view = (RectTransform)transform;
    }

    private void Update()
    {
        if (text == null || view == null)
            return;
        float overflow = text.sizeDelta.x - view.rect.width;
        if (overflow <= 0.5f)
        {
            text.anchoredPosition = Vector2.zero;
            t = 0f;
            return;
        }
        float travel = overflow / Speed;
        float cycle = 2f * (Pause + travel);
        t = (t + Time.unscaledDeltaTime) % cycle;
        float x;
        if (t < Pause)
            x = 0f;
        else if (t < Pause + travel)
            x = (t - Pause) * Speed;
        else if (t < 2f * Pause + travel)
            x = overflow;
        else
            x = overflow - (t - 2f * Pause - travel) * Speed;
        float s = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        text.anchoredPosition = new Vector2(-Mathf.Round(Mathf.Clamp(x, 0f, overflow) * s) / s, 0f);
    }
}
