using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CraftQueue;

// Barra de botones del panel: siete celdas como las del inventario del juego, en tres grupos (qué se
// cuenta: bolsa, cofre · qué se ve: vista Total, siempre visible, marcar cofres · el panel: candado,
// vaciar), y la esquina ⋮ de donde sale. La esquina va afuera del panel, en su esquina de arriba del lado
// de adentro (el que mira al centro de la pantalla). Un clic en ella saca o guarda la barra, que se
// desliza desde ahí: de fábrica baja por el costado del panel (o sube desde la esquina si abajo no cabe);
// con "BotonesArriba" corre por encima del panel, en renglones si el panel es angosto (el grupo que no
// cabe baja completo, y el panel baja con él). "Cola" sigue siendo el título del panel.
// Las celdas miden siempre 26 pixeles del juego: no se estiran ni se encogen con el panel; solo la escala
// del juego (la resolución) cambia su tamaño en pantalla, en múltiplos enteros.
// El clic, el globo y el resalte los maneja QueueHud (lee el mouse directo, como el resto del panel).
internal sealed class ButtonBar
{
    internal enum Kind { Bag, Chest, Total, Eye, Pin, Lock, Clear, Toggle }

    // Medidas en pixeles del juego (unidades del lienzo): celda, separación dentro de un grupo, entre
    // grupos (con la raya al centro) y orilla. La esquina (y el ancho de la barra vertical) mide 30; la
    // barra vertical, 30 × 200; arriba caben los 7 en un renglón desde 200 de ancho.
    private const float Cell = 26f, Gap = 1f, GroupGap = 5f, Pad = 2f, LineLength = 16f;
    internal const float Slot = Pad + Cell + Pad;
    internal const float SideHeight = Pad + 7 * Cell + 4 * Gap + 2 * GroupGap + Pad;

    private static readonly Kind[][] Groups =
    {
        new[] { Kind.Bag, Kind.Chest },
        new[] { Kind.Total, Kind.Eye, Kind.Pin },
        new[] { Kind.Lock, Kind.Clear },
    };

    private static readonly Color LineColor = new Color(0.23f, 0.18f, 0.21f);
    private static readonly Color ShadeColor = new Color(1f, 1f, 1f, 0.62f);      // la sombra de "inactivo"
    private static readonly Color ShadeHoverColor = new Color(1f, 1f, 1f, 0.35f); // con el mouse encima
    internal static readonly Color HoverTint = new Color(1f, 0.95f, 0.6f);

    private enum Look { Plain, On, Off }

    private sealed class Button
    {
        public Kind kind;
        public RectTransform rt;
        public Image cell, icon, frame, shade;
    }

    // La esquina, y la bandeja: recorta a los botones mientras entran y salen de la esquina.
    private readonly RectTransform corner, tray, inner;
    private readonly List<Image> lines = new List<Image>();
    private readonly Dictionary<Kind, Button> buttons = new Dictionary<Kind, Button>();
    private Kind? hovered;
    private int artStamp = -1;

    internal bool OnTop { get; private set; }
    // Lo que ocupa la barra afuera encima del panel: arriba, sus renglones (30, 57 u 84); al lado, la esquina.
    internal float Block { get; private set; } = Slot;
    internal bool Visible => corner.gameObject.activeSelf;

    internal ButtonBar(RectTransform panel, Color background)
    {
        tray = NewRect("Bandeja", panel);
        tray.gameObject.AddComponent<RectMask2D>();
        inner = NewRect("Botones", tray);
        Image back = inner.gameObject.AddComponent<Image>();
        back.color = background;
        back.raycastTarget = true; // como el fondo del panel: el clic no llega al mundo
        for (int i = 0; i < Groups.Length - 1; i++)
        {
            Image li = NewRect("Raya", inner).gameObject.AddComponent<Image>();
            li.color = LineColor;
            li.raycastTarget = false;
            lines.Add(li);
        }
        foreach (Kind[] group in Groups)
            foreach (Kind k in group)
                buttons[k] = MakeButton(k, inner);
        // La esquina después: queda encima de la bandeja, y los botones salen "de detrás" de ella.
        corner = NewRect("Esquina", panel);
        corner.sizeDelta = new Vector2(Slot, Slot);
        Image cback = corner.gameObject.AddComponent<Image>();
        cback.color = background;
        cback.raycastTarget = true;
        Button toggle = MakeButton(Kind.Toggle, corner);
        toggle.rt.anchoredPosition = new Vector2(Pad, -Pad);
        buttons[Kind.Toggle] = toggle;
        Refresh();
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        return rt;
    }

    private static Button MakeButton(Kind kind, Transform parent)
    {
        RectTransform rt = NewRect(kind.ToString(), parent);
        rt.sizeDelta = new Vector2(Cell, Cell);
        Button b = new Button { kind = kind, rt = rt };
        b.cell = Layer(rt, "Celda");
        b.icon = NewRect("Icono", rt).gameObject.AddComponent<Image>();
        b.icon.raycastTarget = false;
        b.frame = Layer(rt, "Marco");
        b.shade = Layer(rt, "Sombra");
        b.shade.color = ShadeColor;
        return b;
    }

    private static Image Layer(RectTransform parent, string name)
    {
        GameObject g = new GameObject(name, typeof(RectTransform), typeof(Image));
        g.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        Image i = g.GetComponent<Image>();
        i.raycastTarget = false;
        return i;
    }

    private static void SetSprite(Image i, Sprite s)
    {
        i.sprite = s;
        // Las piezas del juego vienen en nueve partes (esquinas de 8 intactas); las propias ya miden 26.
        i.type = s != null && s.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
    }

    // --- Estado: marco dorado = prendido, sombra = apagado; el bote cambia de dibujo ---

    internal void Refresh()
    {
        if (artStamp != BarArt.Stamp)
        {
            artStamp = BarArt.Stamp; // se encontró el arte del juego (o se cargó otra vez): cambiar las piezas
            foreach (Button b in buttons.Values)
            {
                SetSprite(b.cell, BarArt.Cell);
                SetSprite(b.frame, BarArt.Frame);
                SetSprite(b.shade, BarArt.Shade);
                b.icon.sprite = null; // que Set vuelva a poner el ícono (la bolsa y el cofre pueden ser otros)
            }
        }
        Set(Kind.Bag, BarArt.Bag, Plugin.CountCarried ? Look.On : Look.Off);
        Set(Kind.Chest, BarArt.Chest, Plugin.CountChests ? Look.On : Look.Off);
        Set(Kind.Total, BarArt.List, Plugin.TotalView ? Look.On : Look.Off);
        Set(Kind.Eye, Plugin.HudAlwaysOpen ? BarArt.EyeOpen : BarArt.EyeClosed, Plugin.HudAlwaysOpen ? Look.On : Look.Off);
        Set(Kind.Pin, BarArt.Pin, Plugin.ChestMarks ? Look.On : Look.Off);
        Set(Kind.Lock, Plugin.HudMovable ? BarArt.LockOpen : BarArt.LockClosed, Look.Plain);
        // Vaciar: gris; rojo con el mouse encima; tras el primer clic, la palomita dorada (el segundo vacía).
        bool empty = !Queue.HasSlot || Queue.Tasks.Count == 0;
        Sprite trash = ClearConfirm.Armed ? BarArt.Check : hovered == Kind.Clear && !empty ? BarArt.TrashRed : BarArt.TrashGrey;
        Set(Kind.Clear, trash, empty ? Look.Off : Look.Plain);
        // La esquina: marco dorado con la barra afuera.
        Set(Kind.Toggle, BarArt.Dots, Plugin.ButtonsShown ? Look.On : Look.Plain);
    }

    private void Set(Kind kind, Sprite icon, Look look)
    {
        Button b = buttons[kind];
        if (b.icon.sprite != icon)
        {
            b.icon.sprite = icon;
            // Tamaño nativo (1 pixel del dibujo = 1 pixel del juego), centrado en pixeles enteros.
            float w = icon.rect.width, h = icon.rect.height;
            RectTransform irt = (RectTransform)b.icon.transform;
            irt.sizeDelta = new Vector2(w, h);
            irt.anchoredPosition = new Vector2(Mathf.Floor((Cell - w) / 2f), -Mathf.Floor((Cell - h) / 2f));
        }
        bool over = hovered == kind;
        Show(b.frame, look == Look.On);
        Show(b.shade, look == Look.Off);
        if (look == Look.Off)
        {
            Color shade = over ? ShadeHoverColor : ShadeColor;
            if (b.shade.color != shade)
                b.shade.color = shade;
        }
        Color tint = over && look != Look.Off && kind != Kind.Clear ? HoverTint : Color.white;
        if (b.icon.color != tint)
            b.icon.color = tint;
    }

    private static void Show(Image i, bool on)
    {
        if (i.gameObject.activeSelf != on)
            i.gameObject.SetActive(on);
    }

    // --- Acomodo ---

    // Reparte los siete botones: al lado, en columna (30 × 200); arriba, en renglones del ancho del panel.
    internal void Layout(bool onTop, float width)
    {
        OnTop = onTop;
        int line = 0;
        float x = Pad, y = Pad;
        bool first = true;
        foreach (Kind[] group in Groups)
        {
            float span = group.Length * Cell + (group.Length - 1) * Gap;
            if (!first)
            {
                if (!onTop)
                {
                    PlaceLine(line++, (Slot - LineLength) / 2f, y + (GroupGap - 1f) / 2f, LineLength, 1f);
                    y += GroupGap;
                }
                else if (x + GroupGap + span > width - Pad)
                {
                    x = Pad; // no cabe: el grupo completo baja de renglón
                    y += Cell + Gap;
                }
                else
                {
                    PlaceLine(line++, x + (GroupGap - 1f) / 2f, y + (Cell - LineLength) / 2f, 1f, LineLength);
                    x += GroupGap;
                }
            }
            for (int i = 0; i < group.Length; i++)
            {
                if (i > 0)
                {
                    if (onTop)
                        x += Gap;
                    else
                        y += Gap;
                }
                buttons[group[i]].rt.anchoredPosition = new Vector2(x, -y);
                if (onTop)
                    x += Cell;
                else
                    y += Cell;
            }
            if (!onTop)
                x = Pad;
            first = false;
        }
        for (int i = line; i < lines.Count; i++)
            Show(lines[i], false);
        Block = onTop ? y + Cell + Pad : Slot;
        inner.sizeDelta = onTop ? new Vector2(width, Block) : new Vector2(Slot, SideHeight);
    }

    private void PlaceLine(int i, float x, float y, float w, float h)
    {
        Show(lines[i], true);
        RectTransform rt = (RectTransform)lines[i].transform;
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    // Acomoda la esquina y la bandeja junto al panel (sus hijos, con origen en su esquina de arriba a la
    // izquierda y "y" hacia arriba desde su orilla de arriba). innerLeft: el lado de adentro es el izquierdo
    // (panel a la derecha de la pantalla). head: lo que ocupa encima del panel (la esquina, o los renglones
    // de arriba mientras salen). up: la barra vertical sube desde la esquina. slide: 0 guardada, 1 afuera.
    internal void Apply(bool visible, bool innerLeft, float head, bool up, float slide, float panelWidth)
    {
        if (corner.gameObject.activeSelf != visible)
            corner.gameObject.SetActive(visible);
        bool open = visible && slide > 0f;
        if (tray.gameObject.activeSelf != open)
            tray.gameObject.SetActive(open);
        if (!visible)
            return;
        float x = innerLeft ? -Slot : panelWidth;
        head = Mathf.Round(head);
        corner.anchoredPosition = new Vector2(x, head);
        if (!open)
            return;
        float s = 1f - Mathf.Pow(1f - Mathf.Clamp01(slide), 3f); // sale rápido y frena al final
        if (!OnTop)
        {
            tray.sizeDelta = new Vector2(Slot, SideHeight);
            tray.anchoredPosition = new Vector2(x, up ? head + SideHeight : 0f);
            inner.anchoredPosition = new Vector2(0f, Mathf.Round((up ? -1f : 1f) * SideHeight * (1f - s)));
        }
        else
        {
            tray.sizeDelta = new Vector2(panelWidth, Block);
            tray.anchoredPosition = new Vector2(0f, Block);
            inner.anchoredPosition = new Vector2(Mathf.Round((innerLeft ? -1f : 1f) * panelWidth * (1f - s)), 0f);
        }
    }

    // --- Mouse ---

    private static bool Inside(RectTransform rt, Vector2 screen) =>
        RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);

    // La esquina, o la parte de la barra que ya salió.
    internal bool Contains(Vector2 screen) =>
        Visible && (Inside(corner, screen) || (tray.gameObject.activeSelf && Inside(tray, screen)));

    // La esquina ⋮ (clic: sacar o guardar la barra; clic derecho: vertical u horizontal).
    internal bool OnCorner(Vector2 screen) => Visible && Inside(corner, screen);

    // Donde se agarra para mover el panel, como del título: la esquina (sin soltar) y el fondo de la barra.
    internal bool DragArea(Vector2 screen) =>
        Visible && (Inside(corner, screen) || (tray.gameObject.activeSelf && Inside(tray, screen) && ButtonAt(screen) == null));

    // El ícono del botón bajo el mouse (la celda completa cuenta; de la barra, solo lo que ya salió), o null.
    internal Image IconAt(Vector2 screen) => ButtonAt(screen)?.icon;

    private Button ButtonAt(Vector2 screen)
    {
        if (!Visible)
            return null;
        Button toggle = buttons[Kind.Toggle];
        if (Inside(corner, screen))
            return Inside(toggle.rt, screen) ? toggle : null;
        if (!tray.gameObject.activeSelf || !Inside(tray, screen))
            return null;
        foreach (Button b in buttons.Values)
            if (b != toggle && Inside(b.rt, screen))
                return b;
        return null;
    }

    internal Kind? KindOf(Image icon)
    {
        if (icon == null)
            return null;
        foreach (Button b in buttons.Values)
            if (b.icon == icon)
                return b.kind;
        return null;
    }

    internal RectTransform RectOf(Kind kind) => buttons[kind].rt;

    internal void SetHovered(Image icon) => hovered = KindOf(icon);

    // Amplía los bordes izquierdo y derecho (pixeles de pantalla) con la esquina y la barra que ya salió.
    internal void Extend(ref float left, ref float right)
    {
        if (!Visible)
            return;
        Grow(corner, ref left, ref right);
        if (tray.gameObject.activeSelf)
            Grow(tray, ref left, ref right);
    }

    private static void Grow(RectTransform rt, ref float left, ref float right)
    {
        Vector3[] c = new Vector3[4];
        rt.GetWorldCorners(c); // en un lienzo overlay, "mundo" = pixeles de pantalla
        left = Mathf.Min(left, c[0].x);
        right = Mathf.Max(right, c[2].x);
    }
}

// Los dibujos de la barra. La celda, el marco dorado de selección, la sombra de "inactivo", la bolsa y
// el cofre son los del juego (se buscan por nombre una sola vez, durante la carga de la partida); si
// no están, se usan dibujos propios. Los demás íconos son propios, con la técnica de los del juego:
// contorno oscuro, 3–4 tonos y un aro claro alrededor.
internal static class BarArt
{
    private static int searches;
    private static Sprite cell, frame, shade, bag, chest;
    private static Sprite drawnCell, drawnFrame, drawnShade, drawnBag, drawnChest;
    private static Sprite list, eyeOpen, eyeClosed, pin, lockOpen, lockClosed, trashGrey, trashRed, check, dots;

    // Sube cada vez que cambia el arte encontrado (para que la barra cambie sus piezas).
    internal static int Stamp { get; private set; }

    internal static Sprite Cell => Alive(cell) ? cell : Once(ref drawnCell, () => Plate(DrawnPlate.Cell));
    internal static Sprite Frame => Alive(frame) ? frame : Once(ref drawnFrame, () => Plate(DrawnPlate.Frame));
    internal static Sprite Shade => Alive(shade) ? shade : Once(ref drawnShade, () => Plate(DrawnPlate.Shade));
    internal static Sprite Bag => Alive(bag) ? bag : Once(ref drawnBag, () => Draw(Ring(BagRows)));
    internal static Sprite Chest => Alive(chest) ? chest : Once(ref drawnChest, () => Draw(ChestRows));
    internal static Sprite List => Once(ref list, () => Draw(Ring(ListRows)));
    internal static Sprite EyeOpen => Once(ref eyeOpen, () => Draw(Ring(EyeRows)));
    internal static Sprite EyeClosed => Once(ref eyeClosed, () => Draw(Ring(EyeClosedRows)));
    internal static Sprite Pin => Once(ref pin, () => Draw(Ring(PinRows)));
    internal static Sprite LockOpen => Once(ref lockOpen, () => Draw(Ring(LockOpenRows)));
    internal static Sprite LockClosed => Once(ref lockClosed, () => Draw(Ring(LockRows)));
    internal static Sprite TrashGrey => Once(ref trashGrey, () => Draw(Ring(TrashRows)));
    internal static Sprite TrashRed => Once(ref trashRed, () => Draw(Ring(Recolor(TrashRows, "WNMO", "UTSR"))));
    internal static Sprite Check => Once(ref check, () => Draw(Ring(Outline(CheckFill))));
    internal static Sprite Dots => Once(ref dots, () => Draw(Ring(DotsRows)));

    private static bool Alive(Sprite s) => s != null && s.texture != null;

    private static Sprite Once(ref Sprite s, Func<Sprite> make)
    {
        if (s == null)
            s = make();
        return s;
    }

    // Recorre todos los sprites cargados (también los de ventanas cerradas): es caro, así que se hace
    // durante la carga de una partida y solo mientras falte alguna pieza (tres intentos como mucho). El
    // cofre del juego no siempre está cargado; tiene dibujo propio, así que no se busca otra vez solo por él.
    internal static void Search()
    {
        if (searches >= 3 || (Alive(cell) && Alive(frame) && Alive(shade) && Alive(bag)))
            return;
        searches++;
        Sprite cellSrc = null, frameSrc = null, shadeSrc = null;
        try
        {
            foreach (Sprite s in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (s == null)
                    continue;
                switch (s.name)
                {
                    case "comm-item_cell-dark": // la celda de los objetos
                        if (cellSrc == null)
                            cellSrc = s;
                        break;
                    case "selection": // el marco dorado de lo seleccionado (el de 42 × 42)
                        if (frameSrc == null || Mathf.Approximately(s.rect.width, 42f))
                            frameSrc = s;
                        break;
                    case "comm-item-inactive_shade": // la sombra de un objeto que no se puede usar
                        if (shadeSrc == null)
                            shadeSrc = s;
                        break;
                    case "comm-header_2-type_icon-main_inventory": // la bolsa del inventario
                        if (bag == null)
                            bag = s;
                        break;
                    case "comm-header_2-type_icon-simple_chest": // el cofre de las ventanas de cofre
                        if (chest == null)
                            chest = s;
                        break;
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Barra de botones: no se pudo buscar el arte del juego: " + e.Message);
        }
        if (!Alive(cell))
            cell = Sliced(cellSrc);
        if (!Alive(frame))
            frame = Sliced(frameSrc);
        if (!Alive(shade))
            shade = Sliced(shadeSrc);
        if (bag != null && !FitsCell(bag))
            bag = null;
        if (chest != null && !FitsCell(chest))
            chest = null;
        Stamp++;
        Plugin.Log.LogInfo($"Barra de botones: arte del juego (intento {searches}) — celda {Found(cell)}, marco {Found(frame)}, " +
            $"sombra {Found(shade)}, bolsa {Found(bag)}, cofre {Found(chest)}");
    }

    private static string Found(Sprite s) => Alive(s) ? $"{s.rect.width:0}×{s.rect.height:0}" : "no (dibujo propio)";

    private static bool FitsCell(Sprite s) => s.rect.width <= 24f && s.rect.height <= 24f;

    // La pieza del juego en nueve partes con esquinas de 8: así se lleva a 26 × 26 sin deformar el borde
    // (igual que en los mockups). 100 pixeles por unidad = los del lienzo: 1 pixel del dibujo = 1 del juego.
    private static Sprite Sliced(Sprite src)
    {
        if (!Alive(src))
            return null;
        try
        {
            Rect r = src.textureRect; // falla si el atlas no la guarda como rectángulo
            if (r.width < 18f || r.height < 18f)
                return null;
            Sprite s = Sprite.Create(src.texture, r, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(8f, 8f, 8f, 8f));
            s.name = "GK2 " + src.name;
            return s;
        }
        catch
        {
            return null;
        }
    }

    // --- Dibujos propios ---

    private static readonly Dictionary<char, Color32> Pal = new Dictionary<char, Color32>
    {
        // café (el de la bolsa del juego) y el aro claro
        ['A'] = Hex(0x6e5640), ['B'] = Hex(0x2e1d16), ['H'] = Hex(0x271410), ['E'] = Hex(0x3c2c20), ['F'] = Hex(0x523d2a),
        ['J'] = Hex(0x5a4430), ['C'] = Hex(0x72573b), ['D'] = Hex(0x93704b), ['L'] = Hex(0xb4895b),
        // hierro
        ['O'] = Hex(0x3a3a46), ['M'] = Hex(0x5c5c68), ['N'] = Hex(0x8c8c9a), ['W'] = Hex(0xc8c8d2),
        // oro / latón
        ['1'] = Hex(0x6b4a14), ['2'] = Hex(0xb07a1e), ['3'] = Hex(0xe8b040), ['4'] = Hex(0xffe07a),
        // papel y tinta
        ['5'] = Hex(0x8a7a5e), ['6'] = Hex(0xc9b48a), ['7'] = Hex(0xe8dcc0), ['8'] = Hex(0xf6f0de), ['9'] = Hex(0x3a2e2a),
        // rojo
        ['R'] = Hex(0x5a1410), ['S'] = Hex(0x9e2921), ['T'] = Hex(0xd24a3a), ['U'] = Hex(0xf07a5a),
        // ojo
        ['X'] = Hex(0xefe6d2), ['Z'] = Hex(0x1a1210), ['Q'] = Hex(0xffffff),
    };

    private static Color32 Hex(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

    // La vista Total: una hoja con renglones.
    private static readonly string[] ListRows =
    {
        ".BBBBBBBBBBBBBB.",
        "B88888888888887B",
        "B87777777777776B",
        "B87999999999976B",
        "B87777777777776B",
        "B87999999977776B",
        "B87777777777776B",
        "B87999999999976B",
        "B87777777777776B",
        "B87999977777776B",
        "B87777777777776B",
        "B87777777777776B",
        "B87999999999976B",
        "B87777777777776B",
        "B87999999999976B",
        "B86666666666665B",
        ".BBBBBBBBBBBBBB.",
    };

    // Siempre visible: el ojo abierto (prendido)…
    private static readonly string[] EyeRows =
    {
        ".......BBBBBB.......",
        "....BBBXXXXXXBBB....",
        "..BBXXXX1111XXXXBB..",
        ".BXXXX11222211XXXXB.",
        "BXXXX1223ZZ3221XXXXB",
        "BXXXX123QZZZ321XXXXB",
        "BXXXX123ZZZZ321XXXXB",
        "BXXXX1223ZZ3221XXXXB",
        ".B6XXX11222211XXX6B.",
        "..BB66XX1111XX66BB..",
        "....BBB666666BBB....",
        ".......BBBBBB.......",
    };

    // …y cerrado (apagado): la curva del párpado con filo claro y cuatro pestañas.
    private static readonly string[] EyeClosedRows =
    {
        "BB................BB",
        "B7BB............BB7B",
        "B777BBB......BBB777B",
        "BB77777BBBBBB77777BB",
        ".BBB777777777777BBB.",
        "...BBBB777777BBBB...",
        "......BBBBBBBB......",
        "....B..B....B..B....",
        "...B...B....B...B...",
    };

    // Marcar cofres: el pin rojo del mapa.
    private static readonly string[] PinRows =
    {
        "....BBBBBB....",
        "..BBTTTTTTBB..",
        ".BTTUUTTTTSSB.",
        ".BTUUTTTTTTSB.",
        "BTTUTTXXTTTSSB",
        "BTTTTXXXXTTSSB",
        "BTTTTXXXXTTSSB",
        "BTTTTTXXTTTSSB",
        ".BTTTTTTTTSSB.",
        ".BSTTTTTTSSSB.",
        "..BSSTTTSSSB..",
        "...BSSSSSSB...",
        "....BSSSSB....",
        ".....BSSB.....",
        "......BB......",
        "......BB......",
    };

    // Candado de latón: cerrado (el arco baja dos pixeles) y abierto.
    private static readonly string[] LockRows =
    {
        "................",
        "................",
        "....BBBBBBBB....",
        "...BNNNNNNNNB...",
        "..BNWBBBBBBMNB..",
        "..BNB......BNB..",
        "..BNB......BNB..",
        "..BMB......BMB..",
        "BBBBBBBBBBBBBBBB",
        "B44333333333332B",
        "B43222222222221B",
        "B322222ZZ222221B",
        "B32222ZZZZ22221B",
        "B32222ZZZZ22221B",
        "B322222ZZ222221B",
        "B322222ZZ222221B",
        "B322222ZZ222221B",
        "B32222222222221B",
        "B21111111111111B",
        "BBBBBBBBBBBBBBBB",
    };

    private static readonly string[] LockOpenRows =
    {
        "....BBBBBBBB....",
        "...BNNNNNNNNB...",
        "..BNWBBBBBBMNB..",
        "..BNB......BNB..",
        "..BMB......BNB..",
        "..BB.......BNB..",
        "...........BNB..",
        "...........BMB..",
        "BBBBBBBBBBBBBBBB",
        "B44333333333332B",
        "B43222222222221B",
        "B322222ZZ222221B",
        "B32222ZZZZ22221B",
        "B32222ZZZZ22221B",
        "B322222ZZ222221B",
        "B322222ZZ222221B",
        "B322222ZZ222221B",
        "B32222222222221B",
        "B21111111111111B",
        "BBBBBBBBBBBBBBBB",
    };

    // Vaciar la cola: bote con tapa y asa separadas del cuerpo, y rayas.
    private static readonly string[] TrashRows =
    {
        ".....BBBBBB.....",
        ".....BNNNNB.....",
        "BBBBBBBBBBBBBBBB",
        "BWNNNNNNNNNNNNNB",
        "BMMMMMMMMMMMMMMB",
        "BBBBBBBBBBBBBBBB",
        ".BNMNMNMNMNMNMB.",
        ".BNMNMNMNMNMNMB.",
        ".BNMNMNMNMNMNMB.",
        ".BNMNMNMNMNMNMB.",
        ".BNMNMNMNMNMNMB.",
        ".BNMNMNMNMNMNMB.",
        ".BNMNMNMNMNMNMB.",
        ".BNMNMNMNMNMNMB.",
        ".BOMOMOMOMOMOMB.",
        "..BBBBBBBBBBBB..",
    };

    // La esquina: tres puntos de papel, uno sobre otro.
    private static readonly string[] DotsRows =
    {
        ".BBB.", "B887B", "B876B", "B766B", ".BBB.", ".....", ".....", ".....",
        ".BBB.", "B887B", "B876B", "B766B", ".BBB.", ".....", ".....", ".....",
        ".BBB.", "B887B", "B876B", "B766B", ".BBB.",
    };

    // Confirmar: palomita dorada (relleno con bisel; el contorno y el aro se agregan al dibujarla).
    private static readonly string[] CheckFill =
    {
        "............432",
        "...........432.",
        "..........432..",
        ".........432...",
        "432.....432....",
        ".432...432.....",
        "..432.432......",
        "...43332.......",
        "....432........",
        ".....3.........",
    };

    // Bolsa (si el juego no da la suya): un morral de cuero atado.
    private static readonly string[] BagRows =
    {
        "......BBBBBB......",
        ".....BLLLLLLB.....",
        "......BDDDDB......",
        ".....BBBBBBBB.....",
        "....BDDDDDDDDB....",
        "...BDCCCCCCCCDB...",
        "..BDCCCCCCCCCCDB..",
        "..BCCCCCCCCCCCCB..",
        ".BCCCCCCCCCCCCCCB.",
        ".BCCCCCCCCCCCCCCB.",
        ".BJCCCCCCCCCCCCJB.",
        ".BJJCCCCCCCCCCJJB.",
        "..BJJJJJJJJJJJJB..",
        "...BBBBBBBBBBBB...",
    };

    // Cofre (si el juego no da el suyo), ya con su aro.
    private static readonly string[] ChestRows =
    {
        "...AAAAAAAAAAAAAAAA...",
        "..ABBBBBBBBBBBBBBBBA..",
        ".ABLDDDDDDMNDDDDDDLBA.",
        "ABDCCCCCCCMNCCCCCCCDBA",
        "ABCCCCCCCCMNCCCCCCCCBA",
        "ABJCCCCCCCMNCCCCCCCJBA",
        "ABFJJJJJJJMNJJJJJJJFBA",
        "ABBBBBBBBBBBBBBBBBBBBA",
        "ABLDDDDDDOMMODDDDDDLBA",
        "ABDCCCCCCMNNMCCCCCCDBA",
        "ABCCCCCCCMHHMCCCCCCCBA",
        "ABCCCCCCCMNNMCCCCCCCBA",
        "ABJCCCCCCOMMOCCCCCCJBA",
        "ABJJCCCCCCCCCCCCCCJJBA",
        "ABFJJJJJJJJJJJJJJJJFBA",
        "ABEFFFFFFFFFFFFFFFFEBA",
        "ABBBBBBBBBBBBBBBBBBBBA",
        ".AAAAAAAAAAAAAAAAAAAA.",
    };

    private static string[] Recolor(string[] rows, string from, string to)
    {
        string[] r = new string[rows.Length];
        for (int y = 0; y < rows.Length; y++)
        {
            char[] c = rows[y].ToCharArray();
            for (int x = 0; x < c.Length; x++)
            {
                int i = from.IndexOf(c[x]);
                if (i >= 0)
                    c[x] = to[i];
            }
            r[y] = new string(c);
        }
        return r;
    }

    // Un pixel alrededor del dibujo: 'mark' donde un vecino (arriba, abajo, a los lados) cumple 'next'.
    private static string[] Grow(string[] rows, char mark, Func<char, bool> next)
    {
        int w = rows[0].Length + 2, h = rows.Length + 2;
        char[][] g = new char[h][];
        for (int y = 0; y < h; y++)
            g[y] = new string('.', w).ToCharArray();
        for (int y = 0; y < rows.Length; y++)
        {
            if (rows[y].Length != w - 2)
                throw new ArgumentException($"renglón de ancho distinto: '{rows[y]}'");
            for (int x = 0; x < rows[y].Length; x++)
                g[y + 1][x + 1] = rows[y][x];
        }
        string[] result = new string[h];
        for (int y = 0; y < h; y++)
        {
            char[] row = new char[w];
            for (int x = 0; x < w; x++)
            {
                char c = g[y][x];
                if (c == '.' && ((x > 0 && next(g[y][x - 1])) || (x < w - 1 && next(g[y][x + 1]))
                                 || (y > 0 && next(g[y - 1][x])) || (y < h - 1 && next(g[y + 1][x]))))
                    c = mark;
                row[x] = c;
            }
            result[y] = new string(row);
        }
        return result;
    }

    // Aro claro de 1 pixel alrededor del contorno oscuro (como la bolsa del juego).
    private static string[] Ring(string[] rows) => Grow(rows, 'A', c => c == 'B');

    // Contorno oscuro alrededor de un relleno.
    private static string[] Outline(string[] rows) => Grow(rows, 'B', c => c != '.' && c != 'B');

    private static Sprite Draw(string[] rows)
    {
        int w = rows[0].Length, h = rows.Length;
        Color32[] px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[(h - 1 - y) * w + x] = Pal.TryGetValue(rows[y][x], out Color32 c) ? c : new Color32(0, 0, 0, 0);
        return Make(w, h, px);
    }

    private enum DrawnPlate { Cell, Frame, Shade }

    // Celda, marco y sombra propios (26 × 26, esquinas recortadas), por si el juego no da los suyos.
    private static Sprite Plate(DrawnPlate kind)
    {
        const int n = 26;
        Color32[] px = new Color32[n * n];
        Color32 clear = new Color32(0, 0, 0, 0);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int edge = Math.Min(Math.Min(x, y), Math.Min(n - 1 - x, n - 1 - y));
                bool corner = (Math.Min(x, n - 1 - x) + Math.Min(y, n - 1 - y)) < 2;
                Color32 c = clear;
                if (!corner)
                {
                    if (kind == DrawnPlate.Cell)
                        c = edge == 0 ? Hex(0x1b1a22) : edge == 1 ? Hex(0x3a3844) : Hex(0x282632);
                    else if (kind == DrawnPlate.Frame)
                        c = edge == 0 ? Hex(0x6b4a14) : edge == 1 ? Hex(0xe8b040) : clear;
                    else
                        c = Hex(0x000000);
                }
                px[(n - 1 - y) * n + x] = c;
            }
        return Make(n, n, px);
    }

    private static Sprite Make(int w, int h, Color32[] px)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f);
    }
}
