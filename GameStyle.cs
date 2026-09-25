using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CraftQueue;

// Copia el estilo real de la interfaz del juego para los paneles propios:
// - la fuente pixelada con SU material y a su tamaño nativo (si no, se ve borrosa);
// - el material de los íconos de objeto, que reemplaza el contorno azul de los sprites
//   (el juego lo hace con ImageExtensions.BlueColorReplace al dibujar cada celda).
internal static class GameStyle
{
    private static readonly int Tint = Shader.PropertyToID("_Color");
    private static readonly FieldInfo CellIcon = typeof(UIItemCell).GetField("icon", BindingFlags.Instance | BindingFlags.NonPublic);

    private static TMP_FontAsset font;
    private static Material fontMaterial;
    private static float baseSize;
    private static Material iconMaterial;
    private static float nextTry;

    public static bool Ready => font != null && iconMaterial != null;

    public static void TryInit()
    {
        if (Ready || Time.unscaledTime < nextTry)
            return;
        nextTry = Time.unscaledTime + 2f;

        if (font == null)
        {
            // La fuente más usada en la interfaz visible, con el tamaño más usado de esa fuente.
            TextMeshProUGUI[] texts = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(t => t.font != null && t.fontSize > 0f && !t.transform.root.name.StartsWith("GK2 "))
                .ToArray();
            var byFont = texts.GroupBy(t => t.font).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (byFont != null)
            {
                var bySize = byFont.GroupBy(t => Mathf.Round(t.fontSize)).OrderByDescending(g => g.Count()).First();
                font = byFont.Key;
                baseSize = bySize.Key;
                fontMaterial = bySize.First().fontSharedMaterial;
                // Todos los tamaños que el juego usa con esta fuente (también en ventanas ocultas):
                // son los que los diseñadores dejaron nítidos, así que solo elegimos entre ellos.
                foreach (TextMeshProUGUI t in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (t.font == font && t.fontSize >= 6f && t.fontStyle == FontStyles.Normal)
                        crispSizes.Add(t.fontSize);
                Plugin.Log.LogInfo($"Estilo de texto del juego: {font.name}, tamaño {baseSize}, material {fontMaterial?.name}, " +
                    $"tamaños usados: {string.Join(", ", crispSizes.Select(s => s.ToString("0.##")))}");
            }
        }

        if (iconMaterial == null && CellIcon != null)
        {
            foreach (UIItemCell cell in Object.FindObjectsByType<UIItemCell>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (CellIcon.GetValue(cell) is Image img && img.material != null && img.material != Graphic.defaultGraphicMaterial)
                {
                    iconMaterial = new Material(img.material) { name = "GK2 icono sin contorno" };
                    iconMaterial.SetColor(Tint, new Color(0f, 0f, 0f, 0f));
                    Plugin.Log.LogInfo($"Material de íconos del juego: {img.material.shader?.name}");
                    break;
                }
            }
        }
    }

    private static readonly System.Collections.Generic.SortedSet<float> crispSizes = new System.Collections.Generic.SortedSet<float>();

    // Tamaño de letra: si se configuró uno fijo, ese; si no, el tamaño NÍTIDO que usa el juego
    // más cercano a (tamaño base del juego × escala). Nunca un tamaño intermedio borroso.
    public static float FontSize(float configured, float scale = 1f, float canvasScale = 0f)
    {
        if (configured > 0f)
            return configured;
        float b = baseSize > 0f ? baseSize : 16f;
        float target = b * scale;
        var candidates = new System.Collections.Generic.SortedSet<float>(crispSizes);
        // Fuente de mapa de bits: se ve nítida cuando cada pixel del atlas cae en un número entero
        // de pixeles de pantalla, o sea en tamaños n × (tamaño del atlas) / (escala del lienzo).
        // Así salen tamaños más chicos que los que usa el juego, igual de nítidos.
        if (canvasScale > 0f && AtlasPointSize > 0f && !AtlasIsSdf)
            for (int n = 1; n <= 8; n++)
                candidates.Add(n * AtlasPointSize / canvasScale);
        if (candidates.Count == 0)
            return b;
        return candidates.OrderBy(s => Mathf.Abs(s - target)).ThenByDescending(s => s).First();
    }

    private static float atlasPointSize = -1f;
    private static bool atlasIsSdf;

    private static float AtlasPointSize
    {
        get
        {
            if (atlasPointSize >= 0f || font == null)
                return atlasPointSize;
            atlasPointSize = 0f;
            try
            {
                // Por reflexión: FaceInfo y GlyphRenderMode viven en un módulo que no referenciamos.
                object face = font.GetType().GetProperty("faceInfo")?.GetValue(font);
                object size = face?.GetType().GetProperty("pointSize")?.GetValue(face);
                atlasPointSize = size != null ? System.Convert.ToSingle(size) : 0f;
                string mode = font.GetType().GetProperty("atlasRenderMode")?.GetValue(font)?.ToString() ?? "";
                atlasIsSdf = mode.IndexOf("SDF", System.StringComparison.OrdinalIgnoreCase) >= 0;
                Plugin.Log.LogInfo($"Atlas de la fuente: tamaño {atlasPointSize}, modo {mode}");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("No se pudo leer el atlas de la fuente: " + e.Message);
            }
            return atlasPointSize;
        }
    }

    private static bool AtlasIsSdf => AtlasPointSize >= 0f && atlasIsSdf;

    // Escala de nuestros paneles: siempre un entero (pixeles exactos, nítido) y según el alto de
    // la pantalla, no la del juego (a 1080p el juego usa ×3 y el panel quedaba enorme):
    // 720p y 1080p → ×2, 1440p → ×3, 4K → ×4. Nunca más grande que la del juego.
    public static float PanelScale(float gameScale)
    {
        float s = Mathf.Max(1f, Mathf.Round(Screen.height / 480f));
        return gameScale >= 1f ? Mathf.Min(s, Mathf.Round(gameScale)) : s;
    }

    // Tamaño (en unidades de la interfaz) de un ícono: el pixel entero más cercano al tamaño
    // pedido, en fracciones "limpias" de su tamaño real. Los íconos del juego miden 48 px:
    // en 720p (escala 2) quedan a 2/3 para ir parejos con la letra; en 1080p quedan exactos.
    public static float IconUnits(Sprite s, float targetUnits, float canvasScale) =>
        s == null ? targetUnits : IconUnitsForNative(Mathf.Max(s.rect.width, s.rect.height), targetUnits, canvasScale);

    public static float IconUnitsForNative(float native, float targetUnits, float canvasScale)
    {
        if (native <= 0f || canvasScale <= 0f)
            return targetUnits;
        float targetPx = targetUnits * canvasScale;
        float[] factors = native >= 48f
            ? new[] { 0.5f, 2f / 3f, 0.75f, 1f, 1.5f, 2f, 3f }
            : new[] { 1f, 2f, 3f, 4f };
        float best = factors.OrderBy(f => Mathf.Abs(native * f - targetPx)).First();
        return Mathf.Round(native * best) / canvasScale;
    }

    public static void Apply(TMP_Text t)
    {
        if (font == null)
            return;
        t.font = font;
        if (fontMaterial != null)
            t.fontSharedMaterial = fontMaterial;
    }

    public static void Apply(Image icon)
    {
        if (iconMaterial != null)
            icon.material = iconMaterial;
    }

    // --- Botones − + basura del juego (los mismos de sus ventanas) ---
    // − y + son los de los deslizadores (UISlider); la basura, el botón de borrar partida (UISaveSlot).

    private static GameObject buttonHolder;
    private static readonly System.Collections.Generic.Dictionary<int, GameObject> buttonTemplates =
        new System.Collections.Generic.Dictionary<int, GameObject>();

    private static float nextButtonScan;

    // Se llama seguido desde el plugin: guarda los botones del juego en cuanto aparecen
    // (p. ej. en la pantalla de partidas guardadas), para tenerlos después aunque ya no estén.
    public static void CaptureButtons()
    {
        if (Time.unscaledTime < nextButtonScan)
            return;
        nextButtonScan = Time.unscaledTime + 3f;
        foreach (int action in new[] { -1, 1, 0 })
            ButtonTemplate(action);
    }

    // action: -1 = restar, 1 = sumar, 0 = quitar. Devuelve el botón clonado (solo imagen, sin lógica) o null.
    public static RectTransform CloneButton(int action, Transform parent)
    {
        GameObject tpl = ButtonTemplate(action);
        if (tpl == null)
            return null;
        GameObject copy = Object.Instantiate(tpl, parent, false);
        copy.SetActive(true);
        return (RectTransform)copy.transform;
    }

    private static GameObject ButtonTemplate(int action)
    {
        if (buttonTemplates.TryGetValue(action, out GameObject t) && t != null)
            return t;
        // Los botones del juego (deslizadores de Ajustes, borrar partida). Se capturan en cuanto
        // existen: la pantalla de partidas guardadas pasa al cargar, así que la basura casi siempre
        // está; − y + aparecen tras abrir Ajustes o una ventana con cantidad.
        Component source = SearchGameButton(action);
        if (source == null)
            return null;
        return MakeTemplate(action, source);
    }

    private static Component SearchGameButton(int action)
    {
        Component source = null;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            if (action == 0)
            {
                FieldInfo del = typeof(UISaveSlot).GetField("deleteButton", flags);
                foreach (UISaveSlot slot in Resources.FindObjectsOfTypeAll<UISaveSlot>())
                    if ((source = del?.GetValue(slot) as Component) != null)
                        break;
            }
            else
            {
                FieldInfo f = typeof(UISlider).GetField(action < 0 ? "decreaseButton" : "increaseButton", flags);
                foreach (UISlider slider in Resources.FindObjectsOfTypeAll<UISlider>())
                    if ((source = f?.GetValue(slider) as Component) != null)
                        break;
            }
        }
        catch
        {
            source = null;
        }
        return source;
    }

    private static GameObject MakeTemplate(int action, Component source)
    {
        if (buttonHolder == null)
        {
            buttonHolder = new GameObject("GK2 plantillas de botones");
            buttonHolder.SetActive(false);
            Object.DontDestroyOnLoad(buttonHolder);
        }
        GameObject tpl = Object.Instantiate(source.gameObject, buttonHolder.transform, false);
        tpl.name = action == 0 ? "Quitar" : action < 0 ? "Restar" : "Sumar";
        // Solo el dibujo: fuera botones, navegación de control, textos localizados, etc.
        foreach (Component c in tpl.GetComponentsInChildren<Component>(true))
        {
            if (c == null || c is Transform || c is CanvasRenderer || c is Image || c is LayoutElement)
                continue;
            Object.DestroyImmediate(c);
        }
        foreach (Image img in tpl.GetComponentsInChildren<Image>(true))
            img.raycastTarget = false;
        buttonTemplates[action] = tpl;
        return tpl;
    }

    // --- Piezas de ventana del juego para que los paneles se vean como el juego ---

    private static readonly FieldInfo CellBackground = typeof(UIItemCell).GetField("background", BindingFlags.Instance | BindingFlags.NonPublic);
    private static Image header;     // barra ornamentada de título ("Cola", "Información"…)
    private static Image cellBack;   // marco oscuro detrás de cada ícono

    public static Image Header
    {
        get
        {
            if (header == null)
                header = Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(i => i.name == "HeaderBackGround" && i.sprite != null && !i.transform.root.name.StartsWith("GK2 "));
            return header;
        }
    }

    public static Image CellBack
    {
        get
        {
            if (cellBack == null && CellBackground != null)
                cellBack = Object.FindObjectsByType<UIItemCell>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Select(c => CellBackground.GetValue(c) as Image)
                    .FirstOrDefault(i => i != null && i.sprite != null);
            return cellBack;
        }
    }

    // Copia el aspecto (sprite 9-slice, color, material) de una imagen del juego.
    public static bool CopyLook(Image from, Image to)
    {
        if (from == null)
            return false;
        to.sprite = from.sprite;
        to.type = from.type;
        to.color = from.color;
        to.pixelsPerUnitMultiplier = from.pixelsPerUnitMultiplier;
        to.fillCenter = from.fillCenter;
        if (from.material != null && from.material != Graphic.defaultGraphicMaterial)
            to.material = from.material;
        return true;
    }
}
