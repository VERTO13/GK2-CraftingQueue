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
    private readonly List<GameObject> glows = new List<GameObject>(); // brillos de "recién agregada"

    private Canvas canvas;
    private RectTransform box;     // todo el panel (se posiciona y se arrastra)
    private RectTransform strip;   // barrita de arriba
    private Image lockIcon, marksIcon, eyeIcon;
    private Image totalIcon, trashIcon;      // Σ (vista Total) y bote (vaciar la cola), a la izquierda del ojo
    private RectTransform titleRect;
    private Image gripIcon;                  // agarre para cambiar el tamaño (con el candado abierto)
    private RectTransform sizeBox;           // "240 × 300" junto al cursor mientras se arrastra
    private TMP_Text sizeText;
    private Vector2 resizeFrom;
    private float resizeWidth, resizeHeight, nextResizeBuild;
    private bool resizeHeightTouched;

    // Para las marcas en los cofres: los materiales que el panel muestra ahora mismo y la escala.
    internal static readonly HashSet<string> Materials = new HashSet<string>();
    // Con el mouse sobre una tarea (o un ingrediente) del panel: se marca eso en los cofres,
    // aunque no tenga pin, mientras el mouse siga ahí.
    internal static readonly HashSet<string> HoverMaterials = new HashSet<string>();
    private object hoverMarksFor;

    private void UpdateHoverMarks(Vector2 m, bool active)
    {
        NavRow row = null;
        if (active && Inside(frame, m))
            foreach (NavRow r in navRows)
                if (r.rt != null && (r.pin != null || r.recipeOf != null) && Inside(r.rt, m))
                {
                    row = r;
                    break;
                }
        SetHoverMarks(row);
    }

    // También lo usa el control: el renglón seleccionado al navegar el panel.
    private void SetHoverMarks(NavRow row)
    {
        if (row != null && row.pin == null && row.recipeOf == null)
            row = null;
        object key = row == null ? null : (object)(row.pin ?? row.recipeOf);
        if (Equals(key, hoverMarksFor))
            return;
        hoverMarksFor = key;
        HoverMaterials.Clear();
        if (row?.pin != null && blockMaterials.TryGetValue(row.pin, out HashSet<string> set))
            HoverMaterials.UnionWith(set);
        else if (row?.recipeOf != null)
            HoverMaterials.Add(row.recipeOf);
        HoverMaterials.RemoveWhere(GameData.IsFuel);
        ChestMarks.Dirty = true; // mostrarlo ya, sin esperar la siguiente revisión
    }
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
    private bool pressing, dragging, scrolling, pressOnBar, pressOnTitle, resizing, pressOnGrip;
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
            // Cola vacía: el panel se ve igual, con la ayuda de cómo agregar (así se sabe que el mod está
            // activo), salvo con "OcultarSiVacia".
            if (items == null || (items.Count == 0 && (!Queue.HasSlot || Plugin.HudHideEmpty)))
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
            string sig = Signature(items) + "|" + gameScale; // la escala del juego cambia con la resolución
            // Al terminar el brillo de "recién agregada" se quita en su lugar (antes se redibujaba todo).
            if (glows.Count > 0 && Time.unscaledTime >= flashUntil)
            {
                foreach (GameObject g in glows)
                    if (g != null)
                        Destroy(g);
                glows.Clear();
            }
            Perf.Stop("panel: firma", tg, top: false);
            // Activo ANTES de armarlo: con el panel oculto Unity no mide el texto.
            SetShown(true);
            // Lo que tienes cambia al craftear o recoger sin que el juego avise al panel: cada
            // segundo se revisan los materiales que el panel muestra.
            if (Time.unscaledTime >= nextCountCheck)
            {
                nextCountCheck = Time.unscaledTime + 1f;
                // Solo números y colores: se actualizan en su lugar, sin rehacer el panel.
                long tc = Perf.Start();
                if (signature != null)
                    UpdateCounts();
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
        if (!Plugin.HudVisible || MainGame.PlayerData == null || GameState.InCutscene || GameState.InFight)
            return false;
        bool? onlyWork = GameWindows.OnlyWorkWindows(out workWindow);
        NoWindows = onlyWork == null;
        if (onlyWork == null)
            return true; // jugando, sin ventanas
        if (onlyWork == true && Plugin.HudInWorkWindows)
            return true; // cofres, mesas de crafteo, construcción, tiendas…
        return !Plugin.HudHideWithWindows;
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
                EndResize(save: false);
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
        sb.Append(Plugin.TotalView);
        sb.Append(LLBase.CurrentLang); // si cambias el idioma del juego, se redibuja traducido
        sb.Append('|').Append(GameData.KnowledgeStamp); // receta recién desbloqueada: aparece ya
        sb.Append('|').Append(GameData.PerksStamp);     // talento o tecnología nueva: el ×N al momento
        // (una estación construida o mejorada redibuja por su cuenta: Queue.OnBuilt)
        sb.Append(Screen.width).Append('x').Append(Screen.height).Append(Plugin.HudTop); // y si cambias la resolución
        return sb.ToString();
    }

    private void Ensure()
    {
        if (canvas == null)
            Create();
        // La escala de la interfaz del juego sale de LazyUI: el juego la pone con su PixelSize al
        // aplicar la resolución (2 a 1080p y a 1440p "x2", 4 en 4K). Buscar "el lienzo del juego"
        // entre todos podía dar con el de otro mod (uno con base de 640x360 va a ×3 en 1080p y a ×4
        // en 1440p): a 1080p el tope de PanelScale lo tapaba, pero a 1440p el panel salía a ×3 con
        // el juego en ×2. La búsqueda queda solo de respaldo, por si LazyUI aún no tiene escala.
        if (LazyUI.ScaleFactor > 0.001f)
            GameScale = gameScale = LazyUI.ScaleFactor;
        else
        {
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
        }
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

    private void RefreshEyeIcon()
    {
        eyeIcon.sprite = Plugin.HudAlwaysOpen ? EyeOn() : EyeOff();
        ((RectTransform)eyeIcon.transform).sizeDelta = eyeIcon.sprite.rect.size;
    }

    private void RefreshMarksIcon()
    {
        RefreshEyeIcon();
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
        trt.offsetMax = new Vector2(-42f, 0f); // lugar para el ojo, el pin y el candado
        titleText = tt.GetComponent<TextMeshProUGUI>();
        titleRect = trt;
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
        // Y a su izquierda: "siempre visible" aunque haya un cofre, mesa o el árbol abierto.
        GameObject ey = new GameObject("Siempre visible", typeof(RectTransform), typeof(Image));
        ey.transform.SetParent(strip, false);
        RectTransform ert = (RectTransform)ey.transform;
        ert.anchorMin = ert.anchorMax = ert.pivot = new Vector2(1f, 0.5f);
        ert.anchoredPosition = new Vector2(-3f - 7f - 5f - 7f - 5f, 0f); // candado + pin + separaciones
        eyeIcon = ey.GetComponent<Image>();
        eyeIcon.raycastTarget = false;
        // Más a la izquierda, con el mouse encima: la Σ (vista Total) y el bote (vaciar la cola).
        // Su lugar lo pone UpdateStripIcons según cuáles se vean.
        totalIcon = StripIcon("Vista total");
        trashIcon = StripIcon("Vaciar cola");

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

        // Agarre en la esquina de abajo del lado de adentro: el último hijo, encima del contenido.
        GameObject gr = new GameObject("Agarre", typeof(RectTransform), typeof(Image));
        gr.transform.SetParent(box, false);
        gripIcon = gr.GetComponent<Image>();
        gr.SetActive(false);

        foreach (Graphic g in root.GetComponentsInChildren<Graphic>(true))
            g.raycastTarget = g == blocker;
    }

    private Image StripIcon(string name)
    {
        GameObject g = new GameObject(name, typeof(RectTransform), typeof(Image));
        g.transform.SetParent(strip, false);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
        Image img = g.GetComponent<Image>();
        img.raycastTarget = false;
        g.SetActive(false);
        return img;
    }

    // Σ y bote: aparecen con el mouse sobre el panel. La Σ se queda a la vista (dorada) mientras la
    // vista Total está puesta, y el bote mientras espera la confirmación (ya como palomita). El
    // título deja lugar solo para los que se ven.
    private void UpdateStripIcons(bool over)
    {
        if (totalIcon == null)
            return;
        bool folded = fit == FitMode.Folded && !peeking;
        bool armed = ClearConfirm.Armed;
        bool showTotal = !folded && (over || Plugin.TotalView);
        bool showTrash = !folded && (over || armed) && Queue.HasSlot && Queue.Tasks.Count > 0;
        float right = -41f, left = -36f; // el ojo (9 px) termina en -36
        if (SetStripIcon(totalIcon, showTotal, Plugin.TotalView ? TotalOn() : TotalOff(), right))
        {
            left = right - totalIcon.sprite.rect.width;
            right = left - 5f;
        }
        Sprite trash = armed ? CheckGold() : hovered == trashIcon ? TrashRed() : TrashGrey();
        if (SetStripIcon(trashIcon, showTrash, trash, right))
            left = right - trashIcon.sprite.rect.width;
        float reserve = -left + 6f;
        if (titleRect != null && !Mathf.Approximately(titleRect.offsetMax.x, -reserve))
            titleRect.offsetMax = new Vector2(-reserve, 0f);
    }

    private static bool SetStripIcon(Image icon, bool show, Sprite sprite, float right)
    {
        if (icon.gameObject.activeSelf != show)
            icon.gameObject.SetActive(show);
        if (!show)
            return false;
        if (icon.sprite != sprite)
        {
            icon.sprite = sprite;
            ((RectTransform)icon.transform).sizeDelta = sprite.rect.size;
        }
        RectTransform rt = (RectTransform)icon.transform;
        if (rt.anchoredPosition.x != right)
            rt.anchoredPosition = new Vector2(right, 0f);
        return true;
    }

    // --- Mouse: arrastrar, rueda, clic en flechas y candado ---

    private void HandleMouse()
    {
        if (box == null || !box.gameObject.activeInHierarchy)
        {
            pressing = dragging = scrolling = false;
            EndResize(save: false);
            SetHover(null);
            UpdateTooltip(Vector2.zero, false);
            return;
        }
        if (GamepadInput.InPanel)
        {
            // Con el control manda la selección (UpdateNavMark), no el mouse.
            pressing = dragging = scrolling = false;
            EndResize(save: false);
            if (gripIcon != null)
                gripIcon.gameObject.SetActive(false);
            UpdateStripIcons(false);
            SetHover(null);
            UpdateTooltip(Vector2.zero, false);
            return;
        }
        Vector2 m = Input.mousePosition;
        bool over = Inside(box, m);
        UpdateStripIcons(over && !dragging && !resizing);
        UpdateGrip(m, over);
        UpdateHoverMarks(m, over && fit != FitMode.Folded && !resizing);
        UpdateTooltip(m, over && !dragging && !scrolling && !resizing);
        float s = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

        if (over && Input.mouseScrollDelta.y != 0f && contentHeight > viewHeight + 0.5f)
        {
            scrollY -= Input.mouseScrollDelta.y * rowHeight * 2f;
            ApplyScroll();
        }

        // Los botones − + basura aparecen en la barra de la tarea que tiene el mouse encima.
        ShowActionsFor(dragging || scrolling || resizing || !over || !Inside(frame, m) ? null : HeaderAt(m));
        SetHover(dragging || scrolling || resizing || !over ? null : ClickableAt(m));

        if (Input.GetMouseButtonDown(0) && over)
        {
            pressing = true;
            pressPos = lastMouse = m;
            pressOnBar = barTrack.gameObject.activeSelf && Inside(barTrack, m);
            pressOnTitle = Inside(strip, m);
            pressOnGrip = GripAt(m);
        }
        if (!pressing)
            return;

        if (Input.GetMouseButton(0))
        {
            bool overflow = contentHeight > viewHeight + 0.5f;
            // Desde el agarre, cambiar el tamaño (con 2 px basta: es un control chiquito).
            if (!dragging && !scrolling && !resizing && pressOnGrip && (m - pressPos).sqrMagnitude > 4f)
                StartResize();
            if (resizing)
            {
                lastMouse = m;
                Resize(m, s);
                return;
            }
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
        if (resizing)
            EndResize(save: true);
        else if (dragging)
            SavePosition(s);
        else if (!scrolling && !pressOnGrip)
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
        if (Inside(strip, m) && m.x >= LeftEdge(marksIcon) - 2f * canvas.scaleFactor)
            return marksIcon;
        if (Inside(strip, m) && m.x >= LeftEdge(eyeIcon) - 3f * canvas.scaleFactor)
            return eyeIcon;
        if (Inside(strip, m) && totalIcon.gameObject.activeSelf && m.x >= LeftEdge(totalIcon) - 3f * canvas.scaleFactor)
            return totalIcon;
        if (Inside(strip, m) && trashIcon.gameObject.activeSelf && m.x >= LeftEdge(trashIcon) - 3f * canvas.scaleFactor)
            return trashIcon;
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

    // Qué se marca en los cofres de cada tarea: lo decide el plan (Plan.MarksFor), con lo que ya
    // tienes repartido en el orden de la cola.

    // Renglón del plan para esa ruta (lo que pide y lo que le tocó de lo que tienes).
    private static Plan.Row PlanRow(string path, string id, int want)
    {
        if (path != null && Plan.Rows.TryGetValue(path, out Plan.Row r))
            return r;
        return new Plan.Row { id = id, want = want, avail = GameData.Owned(id), fuel = GameData.IsFuel(id) };
    }

    // "Patio: 7": si aquí no te alcanza y en otra zona tienes, dónde y cuánto. Sale en el globo al
    // pasar el mouse por la tarea o el renglón; pegado al nombre lo hacía bajar dos o tres renglones.
    private static string ElsewhereTip(string id, Plan.Row r)
    {
        if (r.fuel || r.Missing == 0)
            return null;
        (string zone, int count) = GameData.Elsewhere(id);
        return count > 0 && !string.IsNullOrEmpty(zone) ? $"{zone}: {count}" : null;
    }

    // La forma del plan (qué renglones hay y cuánto pide cada uno): si cambia, hay que redibujar;
    // si no, basta con actualizar los números en su lugar.
    private static string PlanShape()
    {
        StringBuilder sb = new StringBuilder();
        foreach (KeyValuePair<string, Plan.Row> kv in Plan.Rows)
        {
            sb.Append(kv.Key).Append('=').Append(kv.Value.want);
            // Los renglones de arriba llevan el aviso de otras zonas solo si falta: eso sí redibuja.
            if (kv.Key[0] == '#' || kv.Key.IndexOf('/') == kv.Key.LastIndexOf('/'))
                sb.Append(kv.Value.Missing > 0 ? '-' : '+');
            sb.Append(';');
        }
        foreach (KeyValuePair<string, int> kv in Plan.Crafts)
            sb.Append(kv.Key).Append('x').Append(kv.Value).Append(';');
        return sb.ToString();
    }

    private string builtShape;
    private List<QueueView.Entry> builtItems = new List<QueueView.Entry>();

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
            if (on && pendingActions.TryGetValue(actions, out object entry))
            {
                pendingActions.Remove(actions);
                FillActions(actions, entry);
            }
            // Con los botones, el título pasa a un renglón y la barra se haría más baja: el mouse
            // quedaba fuera, se ocultaban, el título volvía a dos renglones… y temblaba. Se
            // conserva la altura de la barra mientras se ven los botones.
            LayoutElement hle = h.GetComponent<LayoutElement>();
            if (hle != null)
                hle.minHeight = on ? Mathf.Max(rowHeight + U(1f), h.rect.height) : rowHeight + U(1f);
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
        g.SetActive(false);
        headers.Add((head, g, title));
        pendingActions[g] = entry;
    }

    // Los botones − + 🗑 se crean la primera vez que se muestran (al pasar el mouse por la tarea),
    // no en cada redibujado: casi nunca se ven todos y crearlos costaba varios ms por redibujado.
    private readonly Dictionary<GameObject, object> pendingActions = new Dictionary<GameObject, object>();

    private void FillActions(GameObject g, object entry)
    {
        // ▲ ▼ cambian el orden (la de arriba toma primero lo que tienes), − + la cantidad, 🗑 la quita.
        (Sprite sprite, int action)[] buttons =
        {
            (UpSprite(), ActionUp), (DownSprite(), ActionDown), (MinusSprite(), -1), (PlusSprite(), 1), (TrashSprite(), 0)
        };
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
    }

    private const int ActionUp = 2, ActionDown = 3;

    private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    // Botones de cada tarea.
    // Un bloque puede juntar varias tareas del mismo objeto: − y + cambian la última agregada,
    // la basura las quita todas, ▲ ▼ las mueven juntas.
    // Con Shift: − + de 10 en 10 (− se detiene en 1; en 1, la quita como siempre) y ▲ ▼ hasta
    // arriba o hasta abajo.
    private static void RunAction(object entry, int action, bool shift = false)
    {
        List<object> entries = entry as List<object> ?? new List<object> { entry };
        if (entries.Count == 0)
            return;
        if (action == 0)
            foreach (object e in entries)
                Queue.Remove(e as QueueTask);
        else if (action == ActionUp || action == ActionDown)
        {
            if (shift)
                MoveGroupToEdge(entries[0] as QueueTask, top: action == ActionUp);
            else
                MoveGroup(entries[0] as QueueTask, action == ActionUp ? -1 : 1);
        }
        else
        {
            QueueTask last = entries[entries.Count - 1] as QueueTask;
            int delta = action;
            if (shift && last != null)
                delta = action > 0 ? 10 : -Math.Min(10, Math.Max(1, last.count - 1));
            Queue.Change(last, delta);
        }
        Dirty = true;
    }

    private static void MoveGroupToEdge(QueueTask task, bool top)
    {
        List<List<QueueTask>> groups = Plan.Groups.Select(g => g.tasks).ToList();
        int i = groups.FindIndex(t => t.Contains(task));
        if (i < 0)
            return;
        List<QueueTask> moved = groups[i];
        groups.RemoveAt(i);
        if (top)
            groups.Insert(0, moved);
        else
            groups.Add(moved);
        Queue.SetOrder(groups.SelectMany(t => t));
    }

    // Sube o baja un bloque entero de la cola (con todas sus tareas).
    private static void MoveGroup(QueueTask task, int delta)
    {
        List<List<QueueTask>> groups = Plan.Groups.Select(g => g.tasks).ToList();
        int i = groups.FindIndex(t => t.Contains(task));
        int j = i + delta;
        if (i < 0 || j < 0 || j >= groups.Count)
            return;
        (groups[i], groups[j]) = (groups[j], groups[i]);
        Queue.SetOrder(groups.SelectMany(t => t));
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
        // El bote no se tiñe: cambia de dibujo (rojo, o la palomita dorada; ver UpdateStripIcons).
        if (hovered != null && hovered != trashIcon)
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
        else if (target == eyeIcon)
        {
            Plugin.HudAlwaysOpen = !Plugin.HudAlwaysOpen;
            RefreshEyeIcon();
            lastFit = null; // acomodar ya con el modo nuevo
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
        else if (target == totalIcon)
        {
            Plugin.TotalView = !Plugin.TotalView;
            Dirty = true;
        }
        else if (target == trashIcon)
        {
            // Primer clic: se pone rojo y el globo pide otro; el segundo (antes de 4 s) vacía.
            if (ClearConfirm.Press() > 0)
                Dirty = true;
        }
        else if (actionButtons.TryGetValue(target, out var act))
        {
            RunAction(act.entry, act.action, Shift);
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

    // Para la vista con Alt: qué objeto hay en el renglón (o ficha compacta) bajo el mouse.
    internal bool ItemAt(Vector2 mouse, out string item, out RectTransform row)
    {
        item = null;
        row = null;
        if (box == null || !box.gameObject.activeInHierarchy || !Inside(frame, mouse))
            return false;
        foreach (KeyValuePair<RectTransform, string> c in chipItems)
            if (c.Key != null && Inside(c.Key, mouse))
            {
                item = c.Value;
                row = c.Key;
                return true;
            }
        foreach (NavRow r in navRows)
            if (r.rt != null && r.recipeOf != null && Inside(r.rt, mouse))
            {
                item = r.recipeOf;
                row = r.rt;
                return true;
            }
        return false;
    }

    private readonly Dictionary<RectTransform, string> chipItems = new Dictionary<RectTransform, string>();

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
        SetHoverMarks(null);
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

    internal void GamepadToggleView()
    {
        Plugin.TotalView = !Plugin.TotalView;
        Dirty = true;
        try { LazyAudio.PlayAndForget("gui_click"); } catch { }
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
        SetHoverMarks(row); // y sus materiales marcados en los cofres, como con el mouse
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
                // Nunca cambia de lado (confundía): si cabe junto a la ventana de SU lado, se
                // recorre hacia allá; si no, se pliega en su misma esquina.
                float gap = 3f;
                float ownSpace = left ? occ.xMin - gap : screenW - occ.xMax - gap;
                if (fullSize.x <= ownSpace)
                {
                    mode = FitMode.Moved;
                    x = left ? Mathf.Max(0f, occ.xMin - gap - fullSize.x) : Mathf.Min(occ.xMax + gap, screenW - fullSize.x);
                }
                else if (!Plugin.HudAlwaysOpen)
                    mode = FitMode.Folded;
                // Siempre visible (el ojo): se queda completo en su lugar, encima de la ventana.
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
            // La barrita: del ancho de su título y los dos botones, siempre en la esquina de su
            // lado (donde el jugador dejó el panel), aunque toque un poco la ventana.
            float w = Mathf.Min(fullSize.x, Mathf.Ceil(titleText.GetPreferredValues(titleText.text).x) + 5f + 42f + 4f);
            float foldX = left ? Plugin.HudSideOffset : screenW - Plugin.HudSideOffset - w;
            if (peeking)
                x = xMin; // se despliega en su lugar de siempre, encima de la ventana
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
        if (mode != fit)
            Plugin.Log.LogInfo($"Panel: {fit} → {mode} (ventanas: {GameWindows.Describe()}, ocupan {occPx})");
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

    // --- Agarre: cambiar el tamaño arrastrando, con el candado abierto ---
    // Se ve con el mouse sobre el panel, en la esquina de abajo del lado de adentro (abajo a la
    // izquierda si el panel va a la derecha). Mientras se arrastra, todo se reacomoda en vivo:
    // Layout() al momento para el texto y los íconos, y un Build() unas 8 veces por segundo para
    // repartir de nuevo los ingredientes por renglón. El .cfg se escribe una sola vez, al soltar.

    private void UpdateGrip(Vector2 m, bool over)
    {
        if (gripIcon == null)
            return;
        bool show = resizing || (over && Plugin.HudMovable && fit == FitMode.Normal && !dragging && !scrolling);
        if (gripIcon.gameObject.activeSelf != show)
            gripIcon.gameObject.SetActive(show);
        if (!show)
            return;
        bool left = Plugin.HudLeft;
        Sprite sp = Grip(left, resizing ? 2 : GripAt(m) ? 1 : 0);
        if (gripIcon.sprite != sp)
        {
            gripIcon.sprite = sp;
            RectTransform g = (RectTransform)gripIcon.transform;
            g.sizeDelta = sp.rect.size;
            // Panel a la derecha: esquina de abajo a la izquierda; a la izquierda: abajo a la derecha.
            g.anchorMin = g.anchorMax = g.pivot = new Vector2(left ? 1f : 0f, 0f);
            g.anchoredPosition = new Vector2(left ? -1f : 1f, 1f);
        }
    }

    // El agarre con 3 unidades de margen alrededor: es chiquito y hay que poder atinarle.
    private bool GripAt(Vector2 m)
    {
        if (gripIcon == null || !gripIcon.gameObject.activeInHierarchy)
            return false;
        Vector3[] c = new Vector3[4];
        ((RectTransform)gripIcon.transform).GetWorldCorners(c);
        float pad = 3f * (canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f);
        return m.x >= c[0].x - pad && m.x <= c[2].x + pad && m.y >= c[0].y - pad && m.y <= c[2].y + pad;
    }

    private void StartResize()
    {
        resizing = true;
        resizeFrom = pressPos;
        resizeWidth = Plugin.HudWidth;
        // El alto parte de lo que se ve: si la cola es más corta que el máximo, subir el agarre
        // achica el panel desde el primer pixel. El máximo guardado solo cambia si el mouse se
        // mueve de verdad en vertical (así ajustar el ancho no pisa un alto máximo más grande).
        resizeHeight = barShown ? Plugin.HudMaxHeight : Mathf.Round(viewHeight);
        resizeHeightTouched = false;
        nextResizeBuild = 0f;
    }

    private void Resize(Vector2 m, float s)
    {
        float screenW = Screen.width / s, screenH = Screen.height / s;
        Vector2 d = (m - resizeFrom) / s;
        float w = resizeWidth + (Plugin.HudLeft ? d.x : -d.x);
        w = Mathf.Clamp(Mathf.Round(w), 100f, Mathf.Min(800f, screenW - Plugin.HudSideOffset));
        if (!resizeHeightTouched && Mathf.Abs(d.y) >= 3f)
            resizeHeightTouched = true;
        float? h = null;
        if (resizeHeightTouched)
            h = Mathf.Clamp(Mathf.Round(resizeHeight - d.y), 60f, Mathf.Min(1000f, screenH - Plugin.HudTop - stripHeight - 4f));
        if (w != Plugin.LiveHudWidth || h != Plugin.LiveHudMaxHeight)
        {
            Plugin.LiveHudWidth = w;
            Plugin.LiveHudMaxHeight = h;
            Layout(); // texto e íconos al momento; Fit() lo reacomoda contra su orilla
            if (Time.unscaledTime >= nextResizeBuild)
            {
                nextResizeBuild = Time.unscaledTime + 0.12f;
                nextCheck = 0f; // el siguiente Tick lo rearma: los ingredientes se reparten por renglón al armar
            }
        }
        ShowSize(m, s, w, h ?? Plugin.HudMaxHeight);
    }

    private void EndResize(bool save)
    {
        if (!resizing)
            return;
        resizing = false;
        float w = Plugin.HudWidth, h = Plugin.HudMaxHeight; // con los valores en vivo
        Plugin.LiveHudWidth = Plugin.LiveHudMaxHeight = null;
        if (save)
            Plugin.SetHudSize(w, h);
        if (sizeBox != null)
            sizeBox.gameObject.SetActive(false);
        Dirty = true; // rearmar con el tamaño guardado (o el de antes, si se canceló)
    }

    // "ancho × alto" en las mismas unidades del .cfg y del menú, junto al cursor y del lado de afuera.
    private void ShowSize(Vector2 m, float s, float w, float h)
    {
        if (sizeBox == null)
        {
            GameObject t = new GameObject("Tamaño", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            t.transform.SetParent(canvas.transform, false);
            sizeBox = (RectTransform)t.transform;
            sizeBox.anchorMin = sizeBox.anchorMax = sizeBox.pivot = Vector2.zero;
            Image bg = t.GetComponent<Image>();
            bg.color = new Color(0.1f, 0.08f, 0.07f, 0.95f);
            bg.raycastTarget = false;
            HorizontalLayoutGroup hl = t.GetComponent<HorizontalLayoutGroup>();
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = hl.childForceExpandHeight = false;
            ContentSizeFitter f = t.GetComponent<ContentSizeFitter>();
            f.horizontalFit = f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sizeText = MakeText(t.transform, "", Text, TextAlignmentOptions.MidlineLeft, wrap: false);
        }
        sizeBox.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset((int)U(4f), (int)U(4f), (int)U(1f), (int)U(1f));
        sizeText.fontSize = fontSize;
        sizeText.text = $"{w:0} × {h:0}";
        sizeBox.gameObject.SetActive(true);
        sizeBox.SetAsLastSibling();
        LayoutRebuilder.ForceRebuildLayoutImmediate(sizeBox);
        Vector2 size = sizeBox.rect.size, p = m / s;
        float x = Plugin.HudLeft ? p.x + 8f : p.x - size.x - 8f;
        float y = p.y - size.y - 6f;
        x = Mathf.Clamp(x, 0f, Mathf.Max(0f, Screen.width / s - size.x));
        y = Mathf.Clamp(y, 0f, Mathf.Max(0f, Screen.height / s - size.y));
        sizeBox.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(y));
    }

    // Puntos grises como los íconos apagados; crema con el mouse encima; mientras se arrastra,
    // dorado con borde oscuro como el pin prendido. Para el panel a la izquierda, en espejo.
    private static readonly Sprite[] grips = new Sprite[6];

    private static Sprite Grip(bool left, int state)
    {
        int i = (left ? 3 : 0) + state;
        if (grips[i] != null)
            return grips[i];
        string[] dots = { "o......", ".......", "o.o....", ".......", "o.o.o..", ".......", "o.o.o.o" };
        string[] drag = { "#o.......", "###......", "#o#o.....", "#####....", "#o#o#o...", "#######..", "#o#o#o#o.", "#########" };
        string[] rows = state == 2 ? drag : dots;
        if (left)
            rows = rows.Select(r => new string(r.Reverse().ToArray())).ToArray();
        Color off = new Color(0.62f, 0.58f, 0.52f), cream = new Color(0.93f, 0.86f, 0.74f);
        return grips[i] = state == 2
            ? PixelSprite(rows, new Color(0.98f, 0.78f, 0.26f), new Color(0.30f, 0.19f, 0.07f), Color.clear)
            : PixelSprite(rows, state == 1 ? cream : off, Color.clear, Color.clear);
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
        pendingActions.Clear();
        glows.Clear();
        actionButtons.Clear();
        cycleButtons.Clear();
        navRows.Clear();
        arrows.Clear();
        countBindings.Clear();
        markMakers.Clear();
        chipItems.Clear();
        pendingBinding = null;
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
        // Y se adapta al ancho: "Tamaño de íconos" (por la escala) es el máximo, y en un panel angosto
        // se achica para dejarles sitio a los nombres. El 0.11 del ancho respeta el tamaño normal (16)
        // con el ancho normal (170); IconUnitsForNative lo lleva al paso nítido más cercano.
        float iconTarget = Mathf.Min(Plugin.HudIconSize * Plugin.HudScale, Mathf.Max(12f, Plugin.HudWidth * 0.11f));
        iconSize = GameStyle.IconUnitsForNative(48f, iconTarget, canvas.scaleFactor);
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

        // Lo que tienes se reparte entre las tareas en el orden de la cola (la de arriba primero).
        long tp = Perf.Start();
        Plan.Compute(items);
        builtItems = items;
        builtShape = PlanShape();
        foreach (Plan.Row r in Plan.Rows.Values)
            if (!r.fuel)
                Seen(r.id, GameData.Owned(r.id)); // se vigila aunque no se vea (tareas plegadas)
        Perf.Stop("panel: plan", tp, top: false);

        // Lo que se marca en los cofres, de todas las tareas (aunque no quepan o estés en la vista Total).
        foreach (Plan.Group g in Plan.Groups)
        {
            Plan.Group group = g;
            markMakers.Add((g.Key, () => Plan.MarksFor(group)));
            blockMaterials[g.Key] = Plan.MarksFor(g);
        }

        // En la vista Total no se dibujan las tareas una por una: todo va en un solo bloque.
        bool total = Plugin.TotalView && items.Count > 0;
        if (total)
            BuildTotal();
        foreach (Plan.Group g in total ? Enumerable.Empty<Plan.Group>() : Plan.Groups)
        {
            if (rows >= Plugin.HudMaxRows)
            {
                hiddenRows++;
                continue;
            }
            List<object> entry = g.tasks.Cast<object>().ToList();
            if (g.item != null)
            {
                // Un solo bloque por objeto (aunque se haya agregado varias veces o desde distintas
                // mesas): "tienes/necesitas" con lo que le tocó del reparto. Abierto por defecto
                // ("~id" = plegado); dentro, la receta de lo que falta, una a la vez con ◂ ▸.
                string item = g.item;
                Plan.Row head = PlanRow(Plan.HeaderPath(item), item, g.total);
                string itemFold = "~" + item;
                bool showRecipe = head.Missing > 0 && GameData.OptionsFor(item).Count > 0;
                BeginBinding(new CountBinding { key = item, want = g.total, path = Plan.HeaderPath(item) });
                Transform body = Block(item, null, GameData.Name(item), $"{head.avail}/{g.total}",
                    head.Missing == 0, showRecipe ? itemFold : null, inverted: true, flash: g.ids.Any(flashIds.Contains),
                    entry: entry, focusId: g.Key);
                EndBinding();
                string headTip = ElsewhereTip(item, head);
                if (headTip != null && body.parent.Find("Titulo") is RectTransform headRow)
                    tipTargets[headRow] = headTip;
                if (showRecipe && !Prefs.Expanded.Contains(itemFold))
                    RecipeRows(body, item, head.Missing, item, 1, g.preferred);
                FinishBody(body);
                continue;
            }
            QueueView.Entry e = g.build;
            bool flash = flashIds.Contains(e.id);
            List<string> partPaths = e.parts.Select(p => e.id + "/" + p.id).ToList();
            bool ready = partPaths.All(p => PlanRow(p, null, 0).Missing == 0);
            string times = e.need > 1 ? $" ×{e.need}" : "";
            // Construcciones: la flecha pliega sus requisitos (abiertas por defecto; "~id" = plegada).
            string fold = "~" + e.id;
            BeginBinding(new CountBinding { parts = partPaths });
            Transform b = Block(e.iconItem, e.buildIcon, e.title + times, null, ready, fold, inverted: true, flash: flash,
                entry: entry, focusId: e.id);
            EndBinding();
            if (!Prefs.Expanded.Contains(fold))
            {
                foreach ((string pid, int per) in e.parts)
                {
                    string path = e.id + "/" + pid;
                    Item(b, 1, pid, per * e.need, path);
                    Tree(b, pid, path, 2);
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

        RecomputeMaterials();
        Layout();
    }

    // Vista Total: todo lo que pide la cola junto, un renglón por material, sumado de todas las
    // tareas: lo que hay que conseguir, no lo que se craftea en medio (eso ya está desglosado).
    // Primero lo que falta, en el orden de la cola; luego lo que ya tienes; al final los combustibles.
    // En la barra, cuántos materiales ya están completos.
    private void BuildTotal()
    {
        List<Plan.Total> totals = Plan.Totals();
        int materials = totals.Count(t => !t.fuel), ready = totals.Count(t => !t.fuel && t.have >= t.want);
        Transform body = Block(null, null, Lang.T("total"), $"{ready}/{materials}", ready == materials, null, false);
        foreach (Plan.Total t in totals.Where(t => !t.fuel && t.have < t.want)
                     .Concat(totals.Where(t => !t.fuel && t.have >= t.want))
                     .Concat(totals.Where(t => t.fuel)))
        {
            string name = GameData.Name(t.id);
            if (t.fuel)
            {
                Line(body, 1, t.id, $"{name} ×{t.want}", null, Text, Text, null);
                continue;
            }
            bool ok = t.have >= t.want;
            RectTransform line = Line(body, 1, t.id, name, $"{t.have}/{t.want}", ok ? Done : Text, ok ? Done : Short, null);
            string tip = ElsewhereTip(t.id, new Plan.Row { id = t.id, want = t.want, avail = t.have });
            if (tip != null && line != null)
                tipTargets[line] = tip;
        }
        FinishBody(body);
    }

    // Lo que se marca en los cofres: pin general prendido = toda la cola; si no, solo las
    // tareas con pin (o nada). Un pin de una tarea que ya no está en la cola no cuenta.
    private void RecomputeMaterials()
    {
        List<string> ids = (Plugin.ChestMarks ? blockMaterials.Keys : Queue.Pins.Where(blockMaterials.ContainsKey)).ToList();
        Materials.Clear();
        foreach (string id in ids)
            foreach (string key in blockMaterials[id])
                if (!GameData.IsFuel(key))
                    Materials.Add(key);
        ChestMarks.Dirty = true;
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
            glows.Add(glow);
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
        if (GameData.IsFuel(id))
        {
            Line(body, depth, id, $"{name} ×{want}", null, Text, Text, null);
            return;
        }
        // Lo que le tocó del reparto (las tareas de más arriba toman primero).
        Plan.Row r = PlanRow(path, id, want);
        want = r.want;
        Seen(id, GameData.Owned(id)); // se vigila para redibujar si cambia (crafteo, recolección…)
        bool ok = r.Missing == 0;
        // La flecha solo si falta algo: lo que ya tienes no hay que hacerlo.
        string arrow = !ok && Expandable(id, path, depth) ? path : null;
        string zoneTip = depth == 1 ? ElsewhereTip(id, r) : null;
        BeginBinding(new CountBinding { key = id, want = want, path = path });
        RectTransform line = Line(body, depth, id, name, $"{r.avail}/{want}", ok ? Done : Text, ok ? Done : Short, arrow);
        EndBinding();
        if (zoneTip != null && line != null)
            tipTargets[line] = zoneTip;
    }

    // Tiene flecha si hay receta conocida, no es combustible, no repite un material de más arriba
    // (evita ciclos) y no pasa de la profundidad máxima.
    private static bool Expandable(string id, string path, int depth)
    {
        if (depth > Prefs.MaxDepth + 1 || GameData.IsFuel(id))
            return false;
        if (path.Split('/').Count(p => p == id) > 1)
            return false;
        return GameData.OptionsFor(id).Count > 0; // alguna receta que sí puedas hacer
    }

    // Las mismas flechas que la página del personaje (mismas rutas): abrir aquí o allá es lo mismo.
    // Una sola receta a la vez: la elegida (o la mejor). Con varias, un renglón
    // "Receta 1/2 · rinde 4  ◂ ▸" para cambiarla (la elección se comparte con la página del personaje).
    private void Tree(Transform body, string id, string path, int depth)
    {
        if (!Prefs.Expanded.Contains(path) || depth > Prefs.MaxDepth + 2)
            return;
        int missing = PlanRow(path, id, 0).Missing; // solo se hace lo que falta
        if (missing > 0)
            RecipeRows(body, id, missing, path, depth, -1);
    }

    // La receta de un objeto: la elegida con ◂ ▸, si no la que se usó al agregarlo desde la mesa,
    // si no la mejor. Dos estilos (F4): una línea compacta, o línea + ingredientes.
    private void RecipeRows(Transform body, string id, int need, string path, int depth, int preferred)
    {
        // Cada opción es receta + estación: si se hace en varias estaciones, se cambia con ◂ ▸.
        List<GameData.Recipe> recipes = GameData.OptionsFor(id);
        if (recipes.Count == 0)
            return;
        int sel = Prefs.SelectedRecipe(id, recipes, Mathf.Max(1, need), preferred);
        GameData.Recipe recipe = recipes[sel];
        int output = GameData.OutputCount(recipe, id);
        int crafts = Plan.CraftsAt(path);
        if (crafts <= 0)
            crafts = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0, need) / (float)Mathf.Max(1, output)));
        if (Plugin.CompactRecipes)
        {
            CompactRecipe(body, depth, id, sel, recipes.Count, output, recipe, crafts, path);
            return;
        }
        // Con varias opciones: "1/2 · Mesa ×N ◂ ▸". Con una sola que dé más de 1: "Mesa ×N" (sin
        // flechas), para saber cuánto sale por vez (y ver al momento si un talento lo sube).
        if (recipes.Count > 1 || output > 1)
            RecipeSwitcher(body, depth, id, sel, recipes.Count, output, recipe);
        foreach ((string nid, int n) in GameData.Needs(recipe))
        {
            string childPath = path + "/" + nid;
            Item(body, depth, nid, n * crafts, childPath);
            if (!path.Split('/').Contains(nid))
                Tree(body, nid, childPath, depth + 1);
        }
    }

    // Estilo compacto: los ingredientes como ícono + "tienes/necesitas" (nombre al pasar el mouse),
    // hasta 3 por renglón. Con varias recetas, arriba "◂ 1/2 ▸ · rinde 4"; con una sola que
    // rinda más de 1, "· rinde N" al final de los ingredientes.
    private void CompactRecipe(Transform body, int depth, string id, int sel, int count, int output, GameData.Recipe recipe, int crafts, string path)
    {
        List<(string nid, int want)> needs = GameData.Needs(recipe).Select(x => (x.key, x.count * crafts)).ToList();
        // Mesa y cuánto da: "· Sierra circular ×4" (con varias recetas, junto a "◂ 1/2 ▸").
        string yields = count > 1 || output > 1 ? $"<color={Dim}>· {GameData.Station(recipe)}{Prefs.Yield(output)}</color>" : null;

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
            pieces.Add(Chip(row.transform, nid, want, path + "/" + nid));
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

    // Texto extra al pasar el mouse: el nombre del ingrediente en el estilo compacto (ahí no se
    // escribe) o todas las estaciones de una receta ("Yunque de madera +1"). Sale a un
    // lado del panel, a la altura del ingrediente, sin tapar nada. Depende de qué elemento está
    // "enfocado", no de dónde está el mouse: lo mismo servirá para el gamepad.
    private readonly Dictionary<RectTransform, string> tipTargets = new Dictionary<RectTransform, string>();
    private RectTransform tipBox, tipTarget;
    private string tipKey;
    private TMP_Text tipText;

    private void UpdateTooltip(Vector2 m, bool active)
    {
        RectTransform target = null;
        string key = null;
        if (active)
        {
            // Los botones primero (van dentro de la barra de la tarea, que puede tener su propio globo).
            key = ButtonTip(ClickableAt(m), out target);
            if (key == null && Inside(frame, m))
                foreach (KeyValuePair<RectTransform, string> c in tipTargets)
                    if (c.Key != null && Inside(c.Key, m))
                    {
                        target = c.Key;
                        key = c.Value;
                        break;
                    }
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

    // Qué hace cada botón (y lo que cambia con Shift). Sale a la altura de su barra.
    private string ButtonTip(Image i, out RectTransform at)
    {
        at = strip;
        if (i == null)
            return null;
        if (i == lockIcon)
            return Lang.T("lock_tip");
        if (i == marksIcon)
            return Lang.T("pin_tip");
        if (i == eyeIcon)
            return Lang.T("eye_tip");
        if (i == totalIcon)
            return Lang.T(Plugin.TotalView ? "view_tasks" : "view_total");
        if (i == trashIcon)
            return ClearConfirm.Armed ? Lang.T("clear_confirm", Queue.Tasks.Count) : Lang.T("clear");
        string key = null;
        if (focusPins.ContainsKey(i))
            key = "pin_task_tip";
        else if (actionButtons.TryGetValue(i, out var act))
            key = act.action == -1 ? "btn_minus" : act.action == 1 ? "btn_plus" : act.action == ActionUp ? "btn_up"
                : act.action == ActionDown ? "btn_down" : "btn_remove";
        if (key == null)
            return null;
        Transform head = i.transform;
        while (head != null && head.name != "Titulo")
            head = head.parent;
        at = head as RectTransform ?? (RectTransform)i.transform;
        return Lang.T(key);
    }

    private void ShowDetail(RectTransform target, string key)
    {
        if (target == null || key == null)
        {
            tipTarget = null;
            tipKey = null;
            if (tipBox != null && tipBox.gameObject.activeSelf)
                tipBox.gameObject.SetActive(false);
            return;
        }
        if (target == tipTarget && key == tipKey && tipBox != null && tipBox.gameObject.activeSelf)
            return;
        tipTarget = target;
        tipKey = key;
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
        tipText.text = key; // el texto a mostrar (nombre del ingrediente, lista de estaciones…)
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
    private RectTransform Chip(Transform row, string nid, int want, string path)
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
            Plan.Row r = PlanRow(path, nid, want);
            want = r.want;
            Seen(nid, GameData.Owned(nid));
            label = r.avail + "/" + want;
            color = r.Missing == 0 ? Done : Short;
        }
        bool fuel = GameData.IsFuel(nid);
        if (!fuel)
            BeginBinding(new CountBinding { key = nid, want = want, chip = true, path = path });
        Fill(chip.transform, nid, null, label, null, color, color, withCell: true, stretch: false);
        if (!fuel)
            EndBinding();
        tipTargets[(RectTransform)chip.transform] = GameData.Name(nid);
        chipItems[(RectTransform)chip.transform] = nid;
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
    private void RecipeSwitcher(Transform body, int depth, string id, int sel, int count, int output, GameData.Recipe recipe)
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
        string place = Prefs.RecipePlace(sel, count, recipe);
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
        foreach (int delta in count > 1 ? new[] { -1, 1 } : Array.Empty<int>()) // una sola receta: sin flechas
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
        if (pendingBinding != null)
            pendingBinding.name = main;

        if (count != null)
        {
            TMP_Text c = MakeText(row, count, countColor, TextAlignmentOptions.MidlineRight, wrap: false);
            LayoutElement cle = c.gameObject.AddComponent<LayoutElement>();
            cle.flexibleWidth = 0f;
            cle.minWidth = 20f;
            if (pendingBinding != null)
                pendingBinding.count = c;
        }
    }

    // --- Cantidades que se actualizan en su lugar ---
    // Al recoger o craftear solo cambian números y colores: se actualizan esos textos en vez de
    // rehacer todo el panel (rehacerlo costaba 20-30 ms, un pequeño tirón cada vez).

    private sealed class CountBinding
    {
        public string key;       // material cuya cantidad se muestra
        public string path;      // su renglón en el plan (lo que le tocó del reparto)
        public int want;
        public bool chip;        // estilo compacto: el texto es "tienes/necesitas"
        public List<string> parts; // barra de construcción: rutas de sus materiales (solo el color "listo")
        public TMP_Text name, count;
    }

    private readonly List<CountBinding> countBindings = new List<CountBinding>();
    private readonly List<(string id, Func<HashSet<string>> make)> markMakers = new List<(string, Func<HashSet<string>>)>();
    private CountBinding pendingBinding;

    private void BeginBinding(CountBinding b) => pendingBinding = b;

    private void EndBinding()
    {
        if (pendingBinding != null && pendingBinding.name != null)
            countBindings.Add(pendingBinding);
        pendingBinding = null;
    }

    // Revisa las cantidades y actualiza solo lo que cambió. Devuelve si hubo cambios.
    private bool UpdateCounts()
    {
        // Todo lo que usa el plan se vigila (incluye ingredientes de tareas plegadas).
        bool changed = false;
        foreach (string key in shownCounts.Keys.ToList())
        {
            int now = GameData.Owned(key);
            if (now != shownCounts[key])
            {
                shownCounts[key] = now;
                changed = true;
            }
        }
        if (!changed)
            return false;
        if (Plugin.TotalView)
        {
            Dirty = true; // la vista Total es un solo bloque: se rearma entera con el reparto nuevo
            return true;
        }

        // Se reparte de nuevo. Si cambió qué renglones hay o cuánto pide cada uno (p. ej. ya no
        // falta un objeto y su receta se va), se redibuja; si no, solo cambian números y colores.
        Plan.Compute(builtItems);
        if (PlanShape() != builtShape)
        {
            Dirty = true;
            return true;
        }

        bool resized = false;
        foreach (CountBinding b in countBindings)
        {
            if (b.name == null)
                continue;
            if (b.parts != null)
            {
                bool ready = b.parts.All(p => PlanRow(p, null, 0).Missing == 0);
                b.name.color = ready ? Done : Text;
                continue;
            }
            Plan.Row r = PlanRow(b.path, b.key, b.want);
            int have = r.avail;
            bool ok = r.Missing == 0;
            string text = $"{have}/{b.want}";
            TMP_Text target = b.chip ? b.name : b.count;
            if (target != null)
            {
                resized |= target.text.Length != text.Length;
                target.text = text;
                target.color = ok ? Done : Short;
            }
            if (!b.chip)
                b.name.color = ok ? Done : Text;
        }
        // Lo que se marca en los cofres depende de qué ya tienes completo.
        foreach ((string id, Func<HashSet<string>> make) in markMakers)
            blockMaterials[id] = make();
        RecomputeMaterials();
        hoverMarksFor = null; // la vista temporal se recalcula con lo nuevo
        if (resized)
            Layout(); // un número con más cifras puede mover el renglón
        return true;
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

    // Siempre visible: ojo dorado = prendido, solo contorno = apagado.
    private static Sprite eyeOn, eyeOff;

    private static Sprite EyeOn() => eyeOn != null ? eyeOn : eyeOn = PixelSprite(new[]
    {
        "..ooooo..", ".o#####o.", "o##ooo##o", "o##ooo##o", "o##ooo##o", ".o#####o.", "..ooooo.."
    }, new Color(0.30f, 0.19f, 0.07f), new Color(0.98f, 0.78f, 0.26f), Color.clear);

    private static Sprite EyeOff() => eyeOff != null ? eyeOff : eyeOff = PixelSprite(new[]
    {
        "..ooooo..", ".o.....o.", "o..ooo..o", "o..ooo..o", "o..ooo..o", ".o.....o.", "..ooooo.."
    }, new Color(0.62f, 0.58f, 0.52f), Color.clear, Color.clear);

    // Vista Total: Σ dorada = puesta, gris = apagada.
    private static Sprite totalOn, totalOff;
    private static readonly string[] SigmaRows =
    {
        "ooooooo", ".o....o", "..o....", "...o...", "....o..", "...o...", "..o....", ".o....o", "ooooooo"
    };

    private static Sprite TotalOn() => totalOn != null ? totalOn : totalOn =
        PixelSprite(SigmaRows, new Color(0.98f, 0.78f, 0.26f), Color.clear, Color.clear);

    private static Sprite TotalOff() => totalOff != null ? totalOff : totalOff =
        PixelSprite(SigmaRows, new Color(0.62f, 0.58f, 0.52f), Color.clear, Color.clear);

    // Vaciar la cola: bote gris (tapa con asa separada del cuerpo, rayas, fondo redondeado); rojo
    // con el mouse encima, "esto vacía la cola"; tras el primer clic, palomita dorada: el segundo
    // clic confirma. (El bote relleno de rojo de antes se leía como un "!".)
    private static Sprite trashGrey, trashRed, checkGold;
    private static readonly string[] TrashRows =
    {
        "...ooo...", "ooooooooo", ".........", ".ooooooo.", ".o.o.o.o.", ".o.o.o.o.", ".o.o.o.o.", ".o.o.o.o.", "..ooooo.."
    };

    private static Sprite TrashGrey() => trashGrey != null ? trashGrey : trashGrey =
        PixelSprite(TrashRows, new Color(0.62f, 0.58f, 0.52f), Color.clear, Color.clear);

    private static Sprite TrashRed() => trashRed != null ? trashRed : trashRed =
        PixelSprite(TrashRows, new Color(0.88f, 0.31f, 0.24f), Color.clear, Color.clear);

    private static Sprite CheckGold() => checkGold != null ? checkGold : checkGold = PixelSprite(new[]
    {
        ".......oo", "......oo.", "o....oo..", "oo..oo...", ".oooo....", "..oo....."
    }, new Color(0.98f, 0.78f, 0.26f), Color.clear, Color.clear);

    // Marcar cofres: pin relleno = prendido, solo contorno = apagado.
    private static Sprite pinOn, pinOff;

    // Prendido: dorado con borde oscuro (se reconoce de un vistazo). Apagado: solo contorno gris.
    private static Sprite PinOn() => pinOn != null ? pinOn : pinOn = PixelSprite(new[]
    {
        ".ooooo.", "o#####o", "o##o##o", "o#ooo#o", "o##o##o", ".o###o.", "..o#o..", "..o#o..", "...o..."
    }, new Color(0.30f, 0.19f, 0.07f), new Color(0.98f, 0.78f, 0.26f), Color.clear);

    private static Sprite PinOff() => pinOff != null ? pinOff : pinOff = PixelSprite(new[]
    {
        ".ooooo.", "o.....o", "o..o..o", "o.ooo.o", "o..o..o", ".o...o.", "..o.o..", "..o.o..", "...o..."
    }, new Color(0.62f, 0.58f, 0.52f), Color.clear, Color.clear);

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

    private static Sprite up, down;

    private static Sprite UpSprite() => up != null ? up : up = PixelSprite(new[]
    {
        ".ooooooo.", "o#######o", "o#######o", "o###w###o", "o##www##o", "o#wwwww#o", "o#######o", "o#######o", ".ooooooo."
    }, BtnLine, BtnGrey, BtnMark);

    private static Sprite DownSprite() => down != null ? down : down = PixelSprite(new[]
    {
        ".ooooooo.", "o#######o", "o#######o", "o#wwwww#o", "o##www##o", "o###w###o", "o#######o", "o#######o", ".ooooooo."
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
