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
    private static readonly Color StripColor = new Color(0.1f, 0.08f, 0.07f, 0.75f);
    // Bolsa, cofre · vista Total, ojo, pin · candado, bote (ver ButtonBar): al lado del panel o arriba.
    private ButtonBar bar;
    private float barSlide;                  // 0 = barra guardada en su esquina, 1 = afuera (se anima)
    private bool barOnTopSeen;               // vertical u horizontal, la última vez (para volver a sacarla)
    private const float SlideSeconds = 0.2f;
    private Image gripIcon;                  // agarre para cambiar el tamaño (con el candado abierto)
    private RectTransform sizeBox;           // "240 × 300" junto al cursor mientras se arrastra
    private TMP_Text sizeText;
    private Vector2 resizeFrom;
    private float resizeWidth, resizeHeight, nextResizeBuild, nextResizeSave, savedWidth, savedHeight;
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
    internal static float GameScale; // la del juego (0 = todavía no se sabe: GameStyle.PanelScale usa su regla)
    internal static bool NoWindows; // jugando, sin cofres/mesas/menús abiertos
    private RectTransform frame;   // ventana visible (recorta el contenido)
    private RectTransform panel;   // contenido completo
    private RectTransform barTrack, barHandle;
    private string signature;
    private float nextCheck;
    private float gameScale;
    private int rows;
    private int hiddenRows;
    private float fontSize, rowHeight, iconSize;
    private float scrollY, contentHeight, viewHeight;
    private bool pressing, dragging, scrolling, pressOnBar, pressOnTitle, resizing, pressOnGrip;
    private TMP_Text titleText;
    private Vector2 pressPos, lastMouse;

    // Reordenar arrastrando la barra de una tarea: el bloque se atenúa, una línea dorada marca el hueco
    // donde va a quedar y, cerca del borde de arriba o de abajo, la lista se desplaza sola.
    private readonly List<(RectTransform block, RectTransform head, List<object> tasks)> taskBlocks =
        new List<(RectTransform, RectTransform, List<object>)>();
    private int pressTask = -1, reorderFrom = -1, reorderTo = -1;
    private bool reordering;
    private float reorderScroll;
    private RectTransform reorderLine;
    private CanvasGroup reorderFade, panelFade;
    // La tarea "en la mano": una copia de su barra que sigue al mouse, levantada (sombra y marco dorado).
    private RectTransform reorderGhost;
    private float reorderGrab; // del mouse al centro de la barra, para que no brinque al agarrarla
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

        if (panelFade != null)
            panelFade.alpha = Plugin.HudPanelOpacity;

        long tm = Perf.Start();
        HandleMouse();
        Perf.Stop("panel: mouse", tm, top: false);

        // Ajustes de Crafting Queue abiertos en el menú Mods del framework: el panel se ve encima, como
        // vista previa, y la ventana se achica lo justo para que quepa a un lado.
        float previewScale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : GameStyle.PanelScale(LazyUI.ScaleFactor);
        if (FrameworkPreview.Tick(Plugin.HudWidth, previewScale))
        {
            nextCheck = 0f;
            lastFit = null;
            GameWindows.Remeasure();
        }

        // Se abrió o cerró una ventana: revisar y acomodar ya, no en la siguiente vuelta.
        int windows = GameWindows.Signature();
        if (windows != lastWindows)
        {
            lastWindows = windows;
            nextCheck = 0f;
            GameWindows.Remeasure();
            lastFit = null;
        }
        // El juego escondió o volvió a mostrar su HUD (una escena, colocar una construcción): igual, ya.
        bool hudHidden = MainGame.PlayerData != null && GameState.HudHidden;
        if (hudHidden != lastHudHidden)
        {
            lastHudHidden = hudHidden;
            nextCheck = 0f;
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
            if (sig != signature)
            {
                signature = sig;
                long tb = Perf.Start();
                Build(items); // ya con lo que tienes al día
                Perf.Stop("panel: armar", tb, top: false);
                nextCountCheck = Time.unscaledTime + 1f;
            }
            // Lo que tienes cambia al craftear o recoger sin que el juego avise al panel: cada
            // segundo se revisan los materiales que el panel muestra (y en cuanto vuelve a verse).
            else if (Time.unscaledTime >= nextCountCheck)
            {
                nextCountCheck = Time.unscaledTime + 1f;
                // Solo números y colores: se actualizan en su lugar, sin rehacer el panel.
                long tc = Perf.Start();
                UpdateCounts();
                Perf.Stop("panel: contar", tc, top: false);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Panel de la cola: " + (e.InnerException ?? e).Message);
            SetShown(false);
            signature = null; // pudo quedar armado a medias: al volver, se arma de nuevo
            nextCheck = Time.unscaledTime + 5f;
        }
    }

    private LazyWidgetBase workWindow; // cofre/mesa abierta sobre la que se dibuja el panel

    private bool ShouldShow()
    {
        workWindow = null;
        if (!Plugin.HudVisible || MainGame.PlayerData == null)
            return false;
        // Vista previa en los ajustes del menú Mods: encima de esa ventana y a un lado (Fit lo acomoda).
        if (FrameworkPreview.Window != null)
        {
            workWindow = FrameworkPreview.Window;
            NoWindows = false;
            return true;
        }
        if (GameState.HudHidden || GameState.InFight)
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
                // Se queda armado mientras está oculto (un menú, un diálogo, una escena, cambiar de zona): al
                // volver a verse solo se revisan los números, y se rearma únicamente si cambió la cola o el
                // reparto. Antes se rearmaba entero, justo en el cuadro en que el juego cierra su ventana.
                nextCountCheck = 0f;
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
        sb.Append(Plugin.TotalView).Append(Plugin.ButtonsOnTop);
        sb.Append(Plugin.CountCarried).Append(Plugin.CountChests); // qué se cuenta: otros "tienes"
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
        // La escala es la de la interfaz del juego (LazyUI): el juego la pone con su PixelSize al
        // aplicar la resolución (×2 a 1080p y a 1440p "x2", ×4 en 4K). Antes se buscaba "el lienzo del
        // juego" entre todos y a veces se tomaba el de otro mod (uno con base de 640x360 va a ×4 en
        // 1440p): en 1440p el panel salía ×3 con el juego en ×2, y la letra se veía más grande que la
        // del juego. Mientras LazyUI no tenga escala, PanelScale usa la misma regla que el juego.
        GameScale = gameScale = LazyUI.ScaleFactor > 0.001f ? LazyUI.ScaleFactor : 0f;
        // Escala entera (pixeles exactos): la fuente y los íconos pixelados solo se ven nítidos así.
        // (Solo si cambia: esto corre cada 0.4 s y no hace falta tocar el lienzo si sigue igual.)
        float scale = GameStyle.PanelScale(gameScale);
        if (canvas.scaleFactor != scale)
            canvas.scaleFactor = scale;
        // Jugando: por encima de toda la interfaz. Con un cofre/mesa abierta: justo encima de
        // esa ventana, pero debajo de los menús que se abran sobre ella (clic derecho, cantidad…).
        int order = workWindow != null ? GameWindows.SortingAbove(workWindow, 30000) : 30000;
        if (canvas.sortingOrder != order)
            canvas.sortingOrder = order;
        // La posición la pone Fit() cada cuadro (su lugar, o junto a la ventana abierta).
        bar.Refresh();
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
        // Opacidad de todo el panel (OpacidadPanel); los avisos de tamaño y los nombres se ven siempre sólidos.
        panelFade = root.AddComponent<CanvasGroup>();

        GameObject b = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        b.transform.SetParent(root.transform, false);
        box = (RectTransform)b.transform;
        Instance = this;
        Image blocker = b.GetComponent<Image>();
        blocker.color = Color.clear; // invisible; tapa el clic para que no llegue al mundo

        // Título del panel: de aquí se agarra para moverlo (con el candado de la barra abierto).
        GameObject s = new GameObject("Titulo del panel", typeof(RectTransform), typeof(Image));
        s.transform.SetParent(box, false);
        strip = (RectTransform)s.transform;
        strip.anchorMin = new Vector2(0f, 1f);
        strip.anchorMax = new Vector2(1f, 1f);
        strip.pivot = new Vector2(0.5f, 1f);
        strip.sizeDelta = new Vector2(0f, stripHeight);
        s.GetComponent<Image>().color = StripColor;
        GameObject tt = new GameObject("Texto", typeof(RectTransform), typeof(TextMeshProUGUI));
        tt.transform.SetParent(strip, false);
        RectTransform trt = (RectTransform)tt.transform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(TitleMargin, 0f);
        trt.offsetMax = new Vector2(-TitleMargin, 0f);
        titleText = tt.GetComponent<TextMeshProUGUI>();
        titleText.alignment = TextAlignmentOptions.MidlineLeft;
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        titleText.overflowMode = TextOverflowModes.Ellipsis;
        titleText.color = new Color(0.64f, 0.59f, 0.51f);

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

        // La barra de botones y su esquina ⋮, por fuera del panel (sus fondos también tapan el clic). Las
        // acomoda Fit; el agarre sigue siendo el último hijo, encima de todo.
        BarArt.Search(); // normalmente ya se hizo al cargar la partida
        bar = new ButtonBar(box, StripColor);
        barSlide = Plugin.ButtonsShown ? 1f : 0f; // como se dejó, sin animar al cargar
        barOnTopSeen = Plugin.ButtonsOnTop;
        gr.transform.SetAsLastSibling();
    }

    private const float TitleMargin = 5f;

    // El panel con su esquina ⋮ y su barra de botones (quedan fuera del recuadro del panel).
    private bool OverPanel(Vector2 m) => Inside(box, m) || (bar != null && bar.Contains(m));

    internal bool Owns(RectTransform rt) => box != null && rt != null && rt.IsChildOf(box);

    // Bordes izquierdo y derecho, en pixeles de pantalla, del panel con su esquina y su barra: los globos
    // y la vista rápida (Alt) van por fuera.
    internal void OuterEdges(out float left, out float right)
    {
        Vector3[] c = new Vector3[4];
        box.GetWorldCorners(c); // en un lienzo overlay, "mundo" = pixeles de pantalla
        left = c[0].x;
        right = c[2].x;
        bar?.Extend(ref left, ref right);
    }

    // Lo que ocupa la barra encima del panel: la esquina ⋮, o los renglones de la barra horizontal. Al
    // sacarla en horizontal con más de un renglón, primero baja el panel lo necesario y después corren los
    // botones (al guardarla, al revés); "slide" es cuánto han salido los botones (0 a 1).
    private float BarHead(out float slide)
    {
        slide = barSlide;
        if (!bar.OnTop || bar.Block <= ButtonBar.Slot)
            return ButtonBar.Slot;
        const float drop = 0.35f;
        slide = Mathf.Clamp01((barSlide - drop) / (1f - drop));
        return Mathf.Round(Mathf.Lerp(ButtonBar.Slot, bar.Block, Mathf.Clamp01(barSlide / drop)));
    }

    // Lo más que ocupa encima del panel, con la barra afuera (para medir cuánto cabe del panel).
    private float BarHeadFull => bar.OnTop ? Mathf.Max(ButtonBar.Slot, bar.Block) : ButtonBar.Slot;

    // La barra vertical baja por el costado desde la esquina; si abajo no cabe (panel pegado abajo),
    // sube desde la esquina, si arriba sí cabe.
    private bool BarUp(float panelFromTop, float head, float screenH) =>
        !bar.OnTop && panelFromTop + ButtonBar.SideHeight > screenH && panelFromTop - head - ButtonBar.SideHeight >= 0f;

    // --- Mouse: arrastrar, rueda, clic en flechas y en la barra de botones ---

    private void HandleMouse()
    {
        if (box == null || !box.gameObject.activeInHierarchy)
        {
            pressing = dragging = scrolling = false;
            EndReorder(apply: false);
            EndResize(save: false);
            SetHover(null);
            UpdateTooltip(Vector2.zero, false);
            return;
        }
        bar.Refresh(); // prendidos, apagados, el bote: también si cambian desde el menú Mods o el .cfg
        if (GamepadInput.InPanel)
        {
            // Con el control manda la selección (UpdateNavMark), no el mouse.
            pressing = dragging = scrolling = false;
            EndReorder(apply: false);
            EndResize(save: false);
            if (gripIcon != null)
                gripIcon.gameObject.SetActive(false);
            SetHover(null);
            UpdateTooltip(Vector2.zero, false);
            return;
        }
        Vector2 m = Input.mousePosition;
        bool over = OverPanel(m);
        UpdateGrip(m, over);
        // La etiqueta "Íconos 22" / "Letra 24" de la rueda se va sola.
        if (!resizing && sizeBox != null && sizeBox.gameObject.activeSelf && Time.unscaledTime > sizeBoxUntil)
            sizeBox.gameObject.SetActive(false);
        UpdateHoverMarks(m, over && fit != FitMode.Folded && !resizing);
        UpdateTooltip(m, over && !dragging && !scrolling && !resizing && !reordering);
        float s = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

        // Ctrl + rueda: tamaño de los íconos; Shift + rueda: tamaño de la letra (se ve al momento).
        bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (over && !reordering && !resizing && Input.mouseScrollDelta.y != 0f && (ctrlHeld || Shift))
            WheelSize(Input.mouseScrollDelta.y > 0f ? 1 : -1, icons: ctrlHeld, m, s);
        // La rueda también sirve mientras se reordena (aunque el mouse salga del panel).
        else if ((over || reordering) && Input.mouseScrollDelta.y != 0f && contentHeight > viewHeight + 0.5f)
        {
            scrollY -= Input.mouseScrollDelta.y * rowHeight * 2f;
            ApplyScroll();
        }

        // Los botones − + basura aparecen en la barra de la tarea que tiene el mouse encima.
        bool busy = dragging || scrolling || resizing || reordering;
        ShowActionsFor(busy || !over || !Inside(frame, m) ? null : HeaderAt(m));
        SetHover(busy || !over ? null : ClickableAt(m));

        // Clic derecho o Esc mientras se reordena: la tarea se queda donde estaba.
        if (reordering && (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)))
        {
            EndReorder(apply: false);
            pressing = false;
            return;
        }

        // Clic derecho en la esquina ⋮: la barra cambia entre vertical (por el costado) y horizontal (por
        // encima del panel), y vuelve a salir deslizándose hacia el lado nuevo.
        if (Input.GetMouseButtonDown(1) && !pressing && bar.OnCorner(m))
        {
            Plugin.ButtonsOnTop = !Plugin.ButtonsOnTop; // LateUpdate la vuelve a sacar hacia el lado nuevo
            Dirty = true;
            try { LazyAudio.PlayAndForget("gui_click"); } catch { }
        }

        if (Input.GetMouseButtonDown(0) && over)
        {
            pressing = true;
            pressPos = lastMouse = m;
            pressOnBar = barTrack.gameObject.activeSelf && Inside(barTrack, m);
            // La esquina ⋮ (mantener y deslizar; un clic saca o guarda la barra) y el fondo de la barra
            // también sirven para moverlo.
            pressOnTitle = Inside(strip, m) || bar.DragArea(m);
            pressOnGrip = GripAt(m);
            // Sobre la barra de una tarea (fuera de sus botones, flecha y pin): arrastrarla la reordena.
            pressTask = !pressOnTitle && !pressOnBar && ClickableAt(m) == null ? TaskAt(m) : -1;
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
            // Desde la barra de una tarea, un arrastre hacia arriba o abajo la reordena.
            if (!dragging && !scrolling && !reordering && pressTask >= 0 && Mathf.Abs(m.y - pressPos.y) > 6f)
                StartReorder(pressTask);
            if (reordering)
            {
                lastMouse = m;
                UpdateReorder(m, s);
                return;
            }
            if (!dragging && !scrolling && pressTask < 0 && (m - pressPos).sqrMagnitude > 36f)
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
                BarWhileDragging(s); // primero el lado de la esquina y la barra: KeepOnScreen los mide
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
        if (reordering)
            EndReorder(apply: true);
        else if (resizing)
            EndResize(save: true);
        else if (dragging)
            SavePosition(s);
        else if (!scrolling && !pressOnGrip)
            Click(m);
        pressing = dragging = scrolling = false;
        pressTask = -1;
    }

    // --- Reordenar arrastrando ---

    // La tarea cuya barra de título está bajo el mouse (índice en taskBlocks), o -1.
    private int TaskAt(Vector2 m)
    {
        if (!Inside(frame, m))
            return -1;
        for (int i = 0; i < taskBlocks.Count; i++)
            if (taskBlocks[i].head != null && Inside(taskBlocks[i].head, m))
                return i;
        return -1;
    }

    private void StartReorder(int index)
    {
        if (index < 0 || index >= taskBlocks.Count || taskBlocks.Count < 2)
            return;
        reordering = true;
        reorderFrom = reorderTo = index;
        reorderScroll = 0f;
        // El bloque que se arrastra se ve atenuado mientras tanto.
        reorderFade = taskBlocks[index].block.gameObject.AddComponent<CanvasGroup>();
        reorderFade.alpha = 0.45f;
        reorderFade.blocksRaycasts = false;
        // La línea: 2 pixeles dorados con contorno oscuro, del ancho del panel.
        GameObject line = new GameObject("Orden", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        line.transform.SetParent(panel, false);
        line.GetComponent<LayoutElement>().ignoreLayout = true;
        Image dark = line.GetComponent<Image>();
        dark.color = new Color(0.18f, 0.11f, 0.09f, 1f);
        dark.raycastTarget = false;
        reorderLine = (RectTransform)line.transform;
        reorderLine.anchorMin = new Vector2(0f, 1f);
        reorderLine.anchorMax = new Vector2(1f, 1f);
        reorderLine.pivot = new Vector2(0.5f, 0.5f);
        reorderLine.sizeDelta = new Vector2(0f, 4f);
        GameObject gold = new GameObject("Oro", typeof(RectTransform), typeof(Image));
        gold.transform.SetParent(line.transform, false);
        RectTransform grt = (RectTransform)gold.transform;
        grt.anchorMin = Vector2.zero;
        grt.anchorMax = Vector2.one;
        grt.offsetMin = new Vector2(1f, 1f);
        grt.offsetMax = new Vector2(-1f, -1f);
        Image gi = gold.GetComponent<Image>();
        gi.color = new Color(0.91f, 0.69f, 0.25f, 1f);
        gi.raycastTarget = false;
        reorderLine.SetAsLastSibling();
        MakeGhost(taskBlocks[index].head);
        try { LazyAudio.PlayAndForget("gui_click"); } catch { }
    }

    // Copia de la barra de la tarea encima de todo el panel (sin el recorte de la lista), del mismo
    // tamaño y sin botones: la que se lleva "en la mano" mientras se arrastra.
    private void MakeGhost(RectTransform head)
    {
        if (head == null)
            return;
        Vector3[] c = new Vector3[4];
        head.GetWorldCorners(c);
        Vector2 size = head.rect.size;
        // Contenedor: primero la sombra y encima la copia (en Unity un hijo se dibuja sobre su padre).
        GameObject root = new GameObject("Tarea en la mano", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(canvas.transform, false);
        reorderGhost = (RectTransform)root.transform;
        reorderGhost.anchorMin = reorderGhost.anchorMax = new Vector2(0f, 0f);
        reorderGhost.pivot = new Vector2(0.5f, 0.5f);
        reorderGhost.sizeDelta = size;
        CanvasGroup cg = root.GetComponent<CanvasGroup>();
        cg.blocksRaycasts = cg.interactable = false;

        GameObject shadow = new GameObject("Sombra", typeof(RectTransform), typeof(Image));
        shadow.transform.SetParent(reorderGhost, false);
        RectTransform srt = (RectTransform)shadow.transform;
        srt.anchorMin = Vector2.zero;
        srt.anchorMax = Vector2.one;
        srt.offsetMin = new Vector2(3f, -3f);
        srt.offsetMax = new Vector2(3f, -3f);
        Image si = shadow.GetComponent<Image>();
        si.color = new Color(0f, 0f, 0f, 0.45f);
        si.raycastTarget = false;

        GameObject g = Instantiate(head.gameObject, reorderGhost, false);
        g.name = "Barra";
        RectTransform grt = (RectTransform)g.transform;
        if (g.GetComponent<LayoutElement>() is LayoutElement le)
            le.ignoreLayout = true;
        grt.anchorMin = Vector2.zero;
        grt.anchorMax = Vector2.one;
        grt.offsetMin = grt.offsetMax = Vector2.zero;
        grt.localScale = Vector3.one;
        Transform actions = grt.Find("Acciones");
        if (actions != null)
            actions.gameObject.SetActive(false);
        // Levantada: un marco dorado de 1 pixel alrededor de la barra.
        if (g.GetComponent<Image>() != null)
        {
            Outline gold = g.AddComponent<Outline>();
            gold.effectColor = new Color(0.91f, 0.69f, 0.25f, 1f);
            gold.effectDistance = new Vector2(1f, -1f);
        }
        foreach (Graphic gr in root.GetComponentsInChildren<Graphic>(true))
            gr.raycastTarget = false;
        reorderGhost.SetAsLastSibling();
        reorderGrab = (c[0].y + c[1].y) / 2f - pressPos.y;
        PlaceGhost(Input.mousePosition, (c[0].x + c[2].x) / 2f);
    }

    // Sigue al mouse de arriba a abajo (sin salirse de su columna), en pixeles enteros del juego.
    private void PlaceGhost(Vector2 m, float centerX)
    {
        if (reorderGhost == null)
            return;
        float s = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        float y = Mathf.Round((m.y + reorderGrab) / s) * s;
        float x = Mathf.Round((centerX + 2f * s) / s) * s; // un pixel del juego a la derecha: "levantada"
        reorderGhost.position = new Vector3(x, y, 0f);
    }

    private void UpdateReorder(Vector2 m, float s)
    {
        if (reorderLine == null || taskBlocks.Count == 0)
            return;
        if (reorderGhost != null)
            PlaceGhost(m, reorderGhost.position.x - 2f * s);
        // Cerca del borde de arriba o de abajo (o pasándose), la lista se desplaza sola: más rápido
        // mientras más al borde o más afuera esté el mouse.
        Vector3[] c = new Vector3[4];
        frame.GetWorldCorners(c);
        float top = c[1].y, bottom = c[0].y, zone = rowHeight * s * 1.5f;
        float speed = 0f;
        if (m.y > top - zone)
            speed = -Mathf.Clamp((m.y - (top - zone)) / zone, 0f, 3f);
        else if (m.y < bottom + zone)
            speed = Mathf.Clamp(((bottom + zone) - m.y) / zone, 0f, 3f);
        if (speed != 0f && contentHeight > viewHeight + 0.5f)
        {
            reorderScroll += speed * rowHeight * 8f * Time.unscaledDeltaTime;
            float whole = Mathf.Round(reorderScroll);
            if (whole != 0f)
            {
                reorderScroll -= whole;
                scrollY += whole;
                ApplyScroll();
            }
        }

        // El hueco: tantas tareas como tengan la mitad de su BARRA arriba del centro de la tarea que se
        // lleva en la mano (no la mitad del bloque entero: con sus ingredientes desplegados, había que
        // bajar casi hasta la siguiente tarea para que apareciera la línea).
        float probe = m.y + reorderGrab;
        int target = 0;
        for (int i = 0; i < taskBlocks.Count; i++)
        {
            taskBlocks[i].head.GetWorldCorners(c);
            if ((c[0].y + c[1].y) / 2f > probe)
                target = i + 1;
            else
                break;
        }
        reorderTo = target;

        // La línea, entre el bloque de arriba y el de abajo del hueco (oculta si no cambia nada).
        bool moves = target != reorderFrom && target != reorderFrom + 1;
        reorderLine.gameObject.SetActive(moves);
        if (!moves)
            return;
        float y;
        if (target == 0)
        {
            taskBlocks[0].block.GetWorldCorners(c);
            y = c[1].y;
        }
        else if (target >= taskBlocks.Count)
        {
            taskBlocks[taskBlocks.Count - 1].block.GetWorldCorners(c);
            y = c[0].y;
        }
        else
        {
            taskBlocks[target - 1].block.GetWorldCorners(c);
            float above = c[0].y;
            taskBlocks[target].block.GetWorldCorners(c);
            y = (above + c[1].y) / 2f;
        }
        Vector3 local = panel.InverseTransformPoint(new Vector3(0f, y, 0f));
        reorderLine.anchoredPosition = new Vector2(0f, Mathf.Round(local.y - panel.rect.yMax));
    }

    private void EndReorder(bool apply)
    {
        if (reorderFade != null)
            Destroy(reorderFade);
        if (reorderLine != null)
            Destroy(reorderLine.gameObject);
        if (reorderGhost != null)
            Destroy(reorderGhost.gameObject);
        reorderFade = null;
        reorderLine = null;
        reorderGhost = null;
        if (apply && reordering && reorderFrom >= 0 && reorderFrom < taskBlocks.Count
            && reorderTo != reorderFrom && reorderTo != reorderFrom + 1
            && taskBlocks[reorderFrom].tasks.Count > 0 && taskBlocks[reorderFrom].tasks[0] is QueueTask task)
        {
            MoveGroupTo(task, reorderTo);
            try { LazyAudio.PlayAndForget("gui_click"); } catch { }
            Dirty = true;
        }
        reordering = false;
        reorderFrom = reorderTo = pressTask = -1;
    }

    // Pone el bloque de esa tarea en el hueco 'gap' (0 = hasta arriba, N = hasta abajo), contado con el
    // bloque todavía en su lugar.
    private static void MoveGroupTo(QueueTask task, int gap)
    {
        List<List<QueueTask>> groups = Plan.Groups.Select(g => g.tasks).ToList();
        int i = groups.FindIndex(t => t.Contains(task));
        if (i < 0)
            return;
        List<QueueTask> moved = groups[i];
        groups.RemoveAt(i);
        if (gap > i)
            gap--;
        groups.Insert(Mathf.Clamp(gap, 0, groups.Count), moved);
        Queue.SetOrder(groups.SelectMany(t => t));
    }

    private static bool Inside(RectTransform rt, Vector2 screen) =>
        rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);

    // El botón (o la flecha) bajo el mouse, si hay.
    private Image ClickableAt(Vector2 m)
    {
        // Los de la barra: la celda completa.
        Image barButton = bar?.IconAt(m);
        if (barButton != null)
            return barButton;
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
        img.sprite = on ? BarArt.RowPin : BarArt.RowPinOff; // el pin rojo de la barra, en chico
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
        // − + la cantidad, 🗑 la quita. El orden se cambia arrastrando la barra de la tarea. Los dibujos
        // son los de la barra de botones en chico (BarArt), a su tamaño nativo: nunca se estiran.
        (Sprite sprite, int action)[] buttons = { (BarArt.RowMinus, -1), (BarArt.RowPlus, 1), (BarArt.RowTrash, 0) };
        foreach ((Sprite sprite, int action) in buttons)
        {
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

    private void SetHover(Image i)
    {
        if (hovered == i)
            return;
        if (hovered != null)
        {
            hovered.color = Color.white;
            if (hovered.sprite == BarArt.RowTrashRed)
                hovered.sprite = BarArt.RowTrash;
        }
        hovered = i;
        // Los de la barra los resalta la barra misma (sombra más clara, el bote en rojo); el bote de
        // una tarea también se pone rojo, como el de la barra.
        bar?.SetHovered(i);
        if (hovered != null && bar?.KindOf(hovered) == null)
        {
            if (hovered.sprite == BarArt.RowTrash)
                hovered.sprite = BarArt.RowTrashRed;
            else
                hovered.color = Hover;
        }
        bar?.Refresh();
    }

    private void Click(Vector2 m)
    {
        Image target = ClickableAt(m);
        if (target == null)
            return;
        if (bar.KindOf(target) is ButtonBar.Kind kind)
        {
            BarClick(kind);
        }
        else if (actionButtons.TryGetValue(target, out var act))
        {
            RunAction(act.entry, act.action, Shift);
        }
        else if (focusPins.TryGetValue(target, out var pin))
        {
            // Una receta en específico: el pin general (toda la cola) se apaga solo.
            if (Queue.TogglePin(pin.id) && Plugin.ChestMarks)
                Plugin.ChestMarks = false;
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

    // Los siete botones de la barra.
    private void BarClick(ButtonBar.Kind kind)
    {
        switch (kind)
        {
            case ButtonBar.Kind.Bag:
            case ButtonBar.Kind.Chest:
                // Qué se cuenta como "tienes": nunca las dos apagadas (apagar la única prendida cambia a la otra).
                Plugin.ToggleCount(carried: kind == ButtonBar.Kind.Bag);
                Dirty = true;
                break;
            case ButtonBar.Kind.Total:
                Plugin.TotalView = !Plugin.TotalView;
                Dirty = true;
                break;
            case ButtonBar.Kind.Eye:
                Plugin.HudAlwaysOpen = !Plugin.HudAlwaysOpen;
                lastFit = null; // acomodar ya con el modo nuevo
                break;
            case ButtonBar.Kind.Pin:
                // Pin general = toda la cola: al prenderlo se quitan los pines de recetas en específico.
                Plugin.ChestMarks = !Plugin.ChestMarks;
                if (Plugin.ChestMarks)
                    Queue.ClearPins();
                Dirty = true; // recalcular qué se marca en los cofres
                break;
            case ButtonBar.Kind.Lock:
                Plugin.HudMovable = !Plugin.HudMovable;
                break;
            case ButtonBar.Kind.Clear:
                // Primer clic: la palomita dorada y el globo pide otro; el segundo (antes de 4 s) vacía.
                if (ClearConfirm.Press() > 0)
                    Dirty = true;
                break;
            case ButtonBar.Kind.Toggle:
                // La esquina ⋮: saca o guarda la barra (se desliza; ver LateUpdate).
                Plugin.ButtonsShown = !Plugin.ButtonsShown;
                break;
        }
        bar.Refresh();
    }

    // Arrastrando el panel: la esquina y la barra van del lado que mira al centro (el que tendrá al
    // soltarlo), y la barra vertical baja o sube según quepa.
    private void BarWhileDragging(float s)
    {
        if (!bar.Visible)
            return;
        Vector3[] c = new Vector3[4];
        box.GetWorldCorners(c);
        bool panelLeft = (c[0].x + c[2].x) * 0.5f < Screen.width * 0.5f;
        float screenH = Screen.height / s, panelFromTop = (Screen.height - c[1].y) / s;
        float head = BarHead(out float slide);
        bar.Apply(true, !panelLeft, head, BarUp(panelFromTop, head, screenH), slide, box.rect.width);
    }

    private void KeepOnScreen(float s)
    {
        Vector3[] c = new Vector3[4];
        box.GetWorldCorners(c); // en un lienzo overlay, "mundo" = pixeles de pantalla
        OuterEdges(out float left, out float right); // con la esquina y la barra, que tampoco se salgan
        float head = BarHead(out _) * s;             // la esquina (o los renglones) encima del panel
        float dx = 0f, dy = 0f;
        if (left < 0f) dx = -left;
        else if (right > Screen.width) dx = Screen.width - right;
        if (c[1].y + head > Screen.height) dy = Screen.height - head - c[1].y;
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
            Plugin.ChestMarks = false; // (el pin de la barra se apaga solo: la barra lee el ajuste)
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
    private bool lastHudHidden;
    private static float peekUntil;

    // Recién agregaste algo: si el panel está plegado por una ventana, se muestra 2 segundos.
    internal static void ShowAfterAdd() => peekUntil = Time.unscaledTime + 2f;

    private FitMode fit = FitMode.Normal;
    private Vector2 fullSize;
    private bool barShown, peeking;
    private (FitMode, bool, float, Vector2, float, float, float, float, bool, bool, bool)? lastFit; // último acomodo aplicado

    private void LateUpdate()
    {
        long t = Perf.Start();
        try
        {
            // La barra se desliza al sacarla o guardarla (con la esquina ⋮, o desde el menú Mods); si cambia
            // entre vertical y horizontal estando afuera, vuelve a salir hacia el lado nuevo.
            if (bar != null && Plugin.ButtonsOnTop != barOnTopSeen)
            {
                barOnTopSeen = Plugin.ButtonsOnTop;
                if (Plugin.ButtonsShown)
                    barSlide = 0f;
            }
            float target = Plugin.ButtonsShown ? 1f : 0f;
            if (bar != null && barSlide != target)
            {
                barSlide = Mathf.MoveTowards(barSlide, target, Time.unscaledDeltaTime / SlideSeconds);
                lastFit = null;
                if (dragging)
                    BarWhileDragging(canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f);
            }
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
        // La esquina ⋮ va afuera, arriba y del lado de adentro (el que mira al centro); la barra baja por
        // ese costado o corre por encima del panel. Cuentan como parte del panel para no tapar la ventana
        // abierta ni salirse de la pantalla. La posición guardada es la de lo más alto: la esquina (o los
        // renglones de arriba); el panel va debajo.
        float rail = ButtonBar.Slot;
        float head = BarHead(out float slide);
        bool vertical = !bar.OnTop;
        // Dónde iría normalmente (unidades del lienzo, origen abajo a la izquierda).
        float xMin = left ? Plugin.HudSideOffset : screenW - Plugin.HudSideOffset - fullSize.x;
        float panelFromTop = Mathf.Clamp(Plugin.HudTop + head, head, Mathf.Max(head, screenH - fullSize.y));
        bool up = BarUp(panelFromTop, head, screenH);
        float top = screenH - panelFromTop;
        float column = vertical && slide > 0f ? ButtonBar.SideHeight : 0f;
        float footTop = top + head + (up ? column : 0f);
        float footBottom = Mathf.Min(top - fullSize.y, up ? top : top - column);
        Rect normal = new Rect(left ? xMin : xMin - rail, footBottom, fullSize.x + rail, footTop - footBottom);

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
                if (fullSize.x + rail <= ownSpace)
                {
                    mode = FitMode.Moved;
                    x = left ? Mathf.Max(0f, occ.xMin - gap - rail - fullSize.x) : Mathf.Min(occ.xMax + gap + rail, screenW - fullSize.x);
                }
                else if (!Plugin.HudAlwaysOpen)
                    mode = FitMode.Folded;
                // Siempre visible (el ojo): se queda completo en su lugar, encima de la ventana.
            }
        }

        // Plegado: al pasar el mouse por la barrita se despliega; sigue abierto mientras el mouse
        // esté sobre el panel desplegado (o su barra de botones).
        // También se despliega un momento al agregar algo, para que se vea que sí entró.
        if (mode == FitMode.Folded)
            peeking = OverPanel(Input.mousePosition) || Time.unscaledTime < peekUntil;
        else
            peeking = false;

        Vector2 size = fullSize;
        if (mode == FitMode.Folded)
        {
            // La barrita: del ancho de su título, siempre en la esquina de su lado (donde el
            // jugador dejó el panel), aunque toque un poco la ventana.
            float w = Mathf.Min(fullSize.x, Mathf.Ceil(titleText.GetPreferredValues(titleText.text).x) + 2f * TitleMargin);
            float foldX = left ? Plugin.HudSideOffset : screenW - Plugin.HudSideOffset - w;
            if (peeking)
                x = xMin; // se despliega en su lugar de siempre, encima de la ventana
            else
            {
                x = foldX;
                size = new Vector2(w, stripHeight);
            }
        }
        bool folded = mode == FitMode.Folded && !peeking;
        // Nunca fuera de la pantalla (resoluciones chicas o una posición guardada muy a la orilla),
        // tampoco la esquina ni la barra. Plegado, la esquina y la barra se esconden con el panel.
        float railNow = folded ? 0f : rail, headNow = folded ? 0f : head;
        float minX = left ? 0f : railNow, maxX = screenW - size.x - (left ? railNow : 0f);
        x = Mathf.Clamp(x, minX, Mathf.Max(minX, maxX));
        float fromTop = Mathf.Clamp(Plugin.HudTop + head, headNow, Mathf.Max(headNow, screenH - size.y));
        up = BarUp(fromTop, head, screenH);

        var key = (mode, peeking, x, size, fromTop, s, head, slide, up, vertical, left);
        if (lastFit.HasValue && lastFit.Value.Equals(key))
            return;
        lastFit = key;
        if (mode != fit)
            Plugin.Log.LogInfo($"Panel: {fit} → {mode} (ventanas: {GameWindows.Describe()}, ocupan {occPx})");
        fit = mode;
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(0f, 1f);
        box.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(fromTop));
        box.sizeDelta = size;
        frame.gameObject.SetActive(!folded);
        barTrack.gameObject.SetActive(!folded && barShown);
        bar.Apply(!folded, innerLeft: !left, head, up, slide, size.x);
    }

    // Al soltarlo se pega al lado más cercano y guarda su distancia a ese borde y al de arriba (de lo más
    // alto: la esquina ⋮ o los renglones de la barra), así queda en el mismo lugar relativo en cualquier
    // resolución.
    private void SavePosition(float s)
    {
        Vector3[] c = new Vector3[4];
        box.GetWorldCorners(c);
        float leftPx = c[0].x, rightPx = c[2].x, topPx = c[1].y;
        bool left = (leftPx + rightPx) * 0.5f < Screen.width * 0.5f;
        float side = left ? leftPx / s : (Screen.width - rightPx) / s;
        float top = (Screen.height - topPx) / s - BarHead(out _);
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
        nextResizeBuild = nextResizeSave = 0f;
        savedWidth = Plugin.HudWidth;
        savedHeight = Plugin.HudMaxHeight;
        Plugin.PanelResizing = true;
    }

    private void Resize(Vector2 m, float s)
    {
        float screenW = Screen.width / s, screenH = Screen.height / s;
        Vector2 d = (m - resizeFrom) / s;
        float w = resizeWidth + (Plugin.HudLeft ? d.x : -d.x);
        // La esquina ⋮ (y la barra vertical) también tienen que caber al lado; arriba, sus renglones.
        w = Mathf.Clamp(Mathf.Round(w), 100f, Mathf.Max(100f, Mathf.Min(800f, screenW - Plugin.HudSideOffset - ButtonBar.Slot)));
        if (!resizeHeightTouched && Mathf.Abs(d.y) >= 3f)
            resizeHeightTouched = true;
        bar.Layout(Plugin.ButtonsOnTop, w); // con el ancho nuevo, los renglones de arriba pueden ser otros
        float? h = null;
        if (resizeHeightTouched)
            h = Mathf.Clamp(Mathf.Round(resizeHeight - d.y), 60f,
                Mathf.Max(60f, Mathf.Min(1000f, screenH - Plugin.HudTop - BarHeadFull - stripHeight - 4f)));
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
            // También en el .cfg, unas veces por segundo: así el menú Mods muestra los valores en vivo.
            if (Time.unscaledTime >= nextResizeSave)
            {
                nextResizeSave = Time.unscaledTime + 0.15f;
                Plugin.SetHudSize(w, h ?? savedHeight);
            }
        }
        ShowSize(m, s, w, h ?? Plugin.HudMaxHeight);
    }

    // Íconos: de 2 en 2 (10 a 48). Si ya no caben en el ancho del panel (el ícono se limita al 11 % del
    // ancho), el panel se ensancha lo justo para que el cambio se vea. Letra: de 1 en 1 (8 a 32).
    private void WheelSize(int dir, bool icons, Vector2 m, float s)
    {
        string label;
        if (icons)
        {
            // Si ya no caben, con el candado abierto el panel se ensancha solo (Plugin.OnAnySettingChanged).
            float size = Mathf.Clamp(Mathf.Round(Plugin.HudIconSize) + dir * 2f, 10f, 48f);
            Plugin.SetHudIconSize(size);
            label = Lang.T("size_icons", (int)size);
        }
        else
        {
            float next = Mathf.Clamp(Mathf.Round(Plugin.HudTextSize) + dir, 8f, 32f);
            Plugin.SetHudTextSize(next);
            label = Lang.T("size_text", (int)next);
        }
        Dirty = true;
        nextCheck = 0f; // se rearma en el siguiente cuadro con el tamaño nuevo
        ShowSizeText(m, s, label);
        sizeBoxUntil = Time.unscaledTime + 1.2f;
    }

    private float sizeBoxUntil;

    private void EndResize(bool save)
    {
        if (!resizing)
            return;
        resizing = false;
        Plugin.PanelResizing = false;
        float w = Plugin.HudWidth, h = Plugin.HudMaxHeight; // con los valores en vivo
        Plugin.LiveHudWidth = Plugin.LiveHudMaxHeight = null;
        if (save)
            Plugin.SetHudSize(w, h);
        else
            Plugin.SetHudSize(savedWidth, savedHeight); // cancelado: se deshace lo que se guardó en el camino
        if (sizeBox != null)
            sizeBox.gameObject.SetActive(false);
        Dirty = true; // rearmar con el tamaño guardado (o el de antes, si se canceló)
    }

    // "ancho × alto" en las mismas unidades del .cfg y del menú, junto al cursor y del lado de afuera.
    private void ShowSize(Vector2 m, float s, float w, float h) => ShowSizeText(m, s, $"{w:0} × {h:0}");

    private void ShowSizeText(Vector2 m, float s, string text)
    {
        if (sizeBox == null)
        {
            GameObject t = new GameObject("Tamaño", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            t.transform.SetParent(canvas.transform, false);
            t.AddComponent<CanvasGroup>().ignoreParentGroups = true; // sólido aunque el panel sea transparente
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
        sizeText.text = text;
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
        EndReorder(apply: false); // el panel se rehace: un arrastre a medias se cancela
        taskBlocks.Clear();
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
        // con el ancho normal (170). Se usa el tamaño pedido tal cual (en unidades enteras): antes se llevaba
        // al "paso nítido" más cercano de 48 px (12, 16, 18, 24…) y valores como 17, 19 o 20 no cambiaban
        // nada. Los íconos ya van recortados (sin su margen), así que su escala nunca era entera de todos modos.
        float iconTarget = Mathf.Min(Plugin.HudIconSize * Plugin.HudScale, Mathf.Max(12f, Plugin.HudWidth * 0.11f));
        iconSize = Mathf.Max(8f, Mathf.Round(iconTarget));
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
                if (body.parent.Find("Titulo") is RectTransform headRow)
                {
                    int asked = g.total;
                    tipTargets[headRow] = () => ElsewhereTip(item, PlanRow(Plan.HeaderPath(item), item, asked));
                }
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
            // (La vista Total se rearma entera cuando cambia lo que tienes: su globo se calcula al armarla.)
            string tip = ElsewhereTip(t.id, new Plan.Row { id = t.id, want = t.want, avail = t.have });
            if (tip != null && line != null)
                tipTargets[line] = () => tip;
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
        // La barra de botones va por fuera del panel: en columna o, arriba, en renglones del ancho del
        // panel (uno a tres; nunca se encogen los botones). La acomoda Fit.
        bar.Layout(Plugin.ButtonsOnTop, width);
        float head = stripHeight;
        // Alto máximo: el configurado, pero nunca más de lo que cabe en pantalla desde donde está el
        // panel, debajo de su esquina o de los renglones de la barra (en resoluciones chicas no se sale).
        float screenUnits = Screen.height / (canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f);
        float maxHeight = Mathf.Max(rowHeight * 3f, Mathf.Min(Plugin.HudMaxHeight, screenUnits - Plugin.HudTop - BarHeadFull - head - 4f));
        strip.sizeDelta = new Vector2(0f, stripHeight);
        frame.offsetMax = new Vector2(0f, -head);
        GameStyle.Apply(titleText);
        titleText.fontSize = fontSize;
        string label = GameData.Plain(LLBase.HasL("ui_craft_queue") ? LLBase.L("ui_craft_queue") : "");
        titleText.text = label.Length > 0 ? label : Lang.T("queue");
        box.sizeDelta = new Vector2(width, maxHeight + head);
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
        box.sizeDelta = fullSize = new Vector2(width, viewHeight + head);
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
            if (entry is List<object> tasks)
                taskBlocks.Add(((RectTransform)block.transform, (RectTransform)head.transform, tasks));
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
        BeginBinding(new CountBinding { key = id, want = want, path = path });
        RectTransform line = Line(body, depth, id, name, $"{r.avail}/{want}", ok ? Done : Text, ok ? Done : Short, arrow);
        EndBinding();
        if (depth == 1 && line != null)
        {
            int wanted = want;
            tipTargets[line] = () => ElsewhereTip(id, PlanRow(path, id, wanted));
        }
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
    // El texto se pide al mostrarlo (null = ese renglón no tiene globo ahora): "Patio: 7" sale con lo
    // que hay en este momento, aunque el panel no se haya vuelto a armar.
    private readonly Dictionary<RectTransform, Func<string>> tipTargets = new Dictionary<RectTransform, Func<string>>();
    private RectTransform tipBox, tipTarget;
    private string tipKey;
    private TMP_Text tipText;

    private void UpdateTooltip(Vector2 m, bool active)
    {
        RectTransform target = null;
        string key = null;
        if (active)
        {
            // El agarre: cómo cambiar el tamaño (arrastrándolo, o con Ctrl / Shift + rueda).
            if (!resizing && GripAt(m))
            {
                key = Lang.T("grip_tip");
                target = (RectTransform)gripIcon.transform;
            }
            // Los botones primero (van dentro de la barra de la tarea, que puede tener su propio globo).
            if (key == null)
                key = ButtonTip(ClickableAt(m), out target);
            if (key == null && Inside(frame, m))
                foreach (KeyValuePair<RectTransform, Func<string>> c in tipTargets)
                    if (c.Key != null && Inside(c.Key, m))
                    {
                        key = c.Value();
                        target = key != null ? c.Key : null;
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

    // Qué hace cada botón (y lo que cambia con Shift). Sale a la altura de su barra (o de su celda).
    private string ButtonTip(Image i, out RectTransform at)
    {
        at = strip;
        if (i == null)
            return null;
        if (bar.KindOf(i) is ButtonBar.Kind kind)
        {
            at = bar.RectOf(kind);
            return kind switch
            {
                ButtonBar.Kind.Bag => Lang.T("count_bag_tip"),
                ButtonBar.Kind.Chest => Lang.T("count_chests_tip"),
                ButtonBar.Kind.Total => Lang.T(Plugin.TotalView ? "view_tasks" : "view_total"),
                ButtonBar.Kind.Eye => Lang.T("eye_tip"),
                ButtonBar.Kind.Pin => Lang.T("pin_tip"),
                ButtonBar.Kind.Lock => Lang.T("lock_tip"),
                ButtonBar.Kind.Toggle => Lang.T(Plugin.ButtonsShown ? "bar_hide" : "bar_show") + " · " + Lang.T("bar_turn"),
                _ => !Queue.HasSlot || Queue.Tasks.Count == 0 ? Lang.T("queue_empty")
                    : ClearConfirm.Armed ? Lang.T("clear_confirm", Queue.Tasks.Count) : Lang.T("clear"),
            };
        }
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
            t.AddComponent<CanvasGroup>().ignoreParentGroups = true; // sólido aunque el panel sea transparente
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

        // A un lado del panel (el lado con espacio; por fuera de su barra de botones, si va al lado),
        // con el borde de arriba a la altura del ingrediente.
        float s = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        OuterEdges(out float outerLeft, out float outerRight);
        float boxLeft = outerLeft / s, boxRight = outerRight / s;
        Vector3[] c = new Vector3[4];
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
        string chipName = GameData.Name(nid);
        tipTargets[(RectTransform)chip.transform] = () => chipName;
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
            irt.sizeDelta = new Vector2(size, size); // el hueco de la celda, en pixeles enteros
            Image img = ic.GetComponent<Image>();
            // El objeto llena su celda: el dibujo sin el margen transparente de su lienzo (antes ocupaba
            // menos de la mitad). Afuera de una celda, el sprite completo, como siempre.
            img.sprite = withCell ? GameStyle.Trimmed(icon) : icon;
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
