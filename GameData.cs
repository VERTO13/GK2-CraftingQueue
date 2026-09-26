using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LazyBearTechnology;
using UnityEngine;

namespace CraftQueue;

// Todo lo que el mod lee de los datos del juego: recetas, nombres, íconos, cantidades.
// Un "material" se identifica con una clave: el id del objeto, o "any:<id>" cuando la receta
// acepta cualquier objeto de un grupo (p. ej. cualquier tipo de carne).
internal static class GameData
{
    private const string AnyPrefix = "any:";

    // ---------- Recetas ----------

    private static GameBalance indexedFrom;
    private static Dictionary<string, List<CraftDef>> recipesByOutput = new Dictionary<string, List<CraftDef>>();

    // Recetas que producen el material y que el jugador puede usar ahora mismo.
    public static List<CraftDef> RecipesFor(string key)
    {
        BuildIndex();
        List<CraftDef> result = new List<CraftDef>();
        foreach (string item in ItemsOf(key))
            if (recipesByOutput.TryGetValue(item, out List<CraftDef> list))
                foreach (CraftDef c in list)
                    if (!result.Contains(c) && Usable(c))
                        result.Add(c);
        return result;
    }

    private static void BuildIndex()
    {
        GameBalance balance = GameBalance.Me;
        if (balance == null || ReferenceEquals(balance, indexedFrom))
            return;
        indexedFrom = balance;
        recipesByOutput = new Dictionary<string, List<CraftDef>>();
        foreach (CraftDef craft in balance.craftDefs)
        {
            // Recetas "de verdad": en alguna estación, visibles y que piden algo.
            if (craft == null || craft.isHidden || craft.doNotShowInTooltips || craft.craftsIn == null || craft.craftsIn.Count == 0)
                continue;
            if (craft.needItems == null || craft.needItems.Count == 0)
                continue;
            // Igual que el índice del propio juego: las obras del pueblo (sueltan kits al terminar)
            // y las recetas de quitar/colocar no "hacen" objetos; solo los usan.
            if (craft.id.StartsWith("town_building_craft:") || craft.id.StartsWith("rem_") || craft.id.StartsWith("set_"))
                continue;
            HashSet<string> needs = new HashSet<string>(craft.needItems.Where(n => n != null && !string.IsNullOrEmpty(n.Id)).Select(n => n.Id));
            foreach (string output in OutputsOf(craft).Distinct())
            {
                if (needs.Contains(output)) // lo pide y lo devuelve: no es una receta de ese objeto
                    continue;
                if (!recipesByOutput.TryGetValue(output, out List<CraftDef> list))
                    recipesByOutput[output] = list = new List<CraftDef>();
                list.Add(craft);
            }
        }
    }

    public static IEnumerable<string> OutputsOf(CraftDef craft)
    {
        GameBalance balance = GameBalance.Me;
        OutputItems outputs = craft?.outputItems;
        if (balance == null || outputs == null)
            yield break;
        IEnumerable<ChanceOutputItem> all = (outputs.chanceOutputItems ?? new List<ChanceOutputItem>())
            .Concat((outputs.groupChanceOutputItems ?? new List<GroupChanceOutputItem>()).SelectMany(g => g.chanceItems ?? new List<ChanceOutputItem>()));
        foreach (ChanceOutputItem o in all)
        {
            if (o == null || string.IsNullOrEmpty(o.id))
                continue;
            if (!o.isStarGroup)
                yield return o.id;
            else if (balance.starGroupItemsCache.TryGetValue(o.id, out List<ItemDef> stars))
                foreach (ItemDef d in stars)
                    yield return d.id;
        }
    }

    // Como el juego al mostrar recetas en una mesa: conocida (si requiere desbloquearse),
    // no está en su lista negra y no es de una sola vez ya hecha.
    private static bool Usable(CraftDef c)
    {
        KnowledgeSystem ks = MainGame.Instance?.GameSave?.knowledgeSystem;
        if (ks == null)
            return true;
        if (c.isNeedsUnlock && !ks.unlockedCrafts.Contains(c.id))
            return false;
        return !ks.blackListCrafts.Contains(c.id) && !ks.IsOneTimeCraftCompleted(c);
    }

    // Cambia cuando desbloqueas (o se bloquea) una receta: el panel lo usa para redibujarse y
    // mostrar al momento las recetas nuevas de lo que ya tienes en la cola.
    public static int KnowledgeStamp
    {
        get
        {
            KnowledgeSystem ks = MainGame.Instance?.GameSave?.knowledgeSystem;
            return ks == null ? 0 : ks.unlockedCrafts.Count * 31 + ks.blackListCrafts.Count;
        }
    }

    // ---------- Estaciones reales (talentos y mejoras) ----------
    // Lo que rinde y lo que pide una receta se calcula con fórmulas que dependen de la estación
    // donde se hace: ahí entran tus talentos (p. ej. "más objetos del cubo de destilación") y las
    // mejoras de la estación. Se evalúan con tus estaciones construidas; si hay varias, la que más
    // rinde. Sin estación construida, queda el cálculo base.

    private static readonly Dictionary<string, (float time, WgoData station)> stationCache = new Dictionary<string, (float, WgoData)>();
    private static int stationStamp;

    // Huella barata de lo que cambia cuánto rinde una receta: talentos activos, tecnologías
    // investigadas y objetos de la zona (una estación nueva o mejorada cambia la lista).
    public static int StationsStamp
    {
        get
        {
            GameSave save = MainGame.Instance?.GameSave;
            int perks = save?.perkSystemData?.activePerks?.Count ?? 0;
            int techs = save?.knowledgeSystem?.unlockedTechs?.Count ?? 0;
            int zone = MainGame.PlayerData?.CurrentWorldZoneData?.wgoDataList?.Count ?? 0;
            return perks * 1000003 + techs * 1009 + zone;
        }
    }

    // Se construyó o terminó una obra: recalcular ya (una mejora puede no cambiar la cuenta de objetos).
    public static void ResetStations() => stationCache.Clear();

    private static WgoData StationOf(CraftDef craft)
    {
        if (craft == null)
            return null;
        // Se recalcula solo cuando algo pudo cambiar lo que rinde una receta: un talento nuevo,
        // una tecnología, o una estación construida/mejorada (ver StationsStamp y ResetStations).
        int stamp = StationsStamp;
        if (stamp != stationStamp)
        {
            stationStamp = stamp;
            stationCache.Clear();
        }
        if (stationCache.TryGetValue(craft.id, out var cached))
            return cached.station;
        long t = Perf.Start();
        try { return FindStation(craft); }
        finally { Perf.Stop("recetas: buscar estación", t, top: false); }
    }

    private static WgoData FindStation(CraftDef craft)
    {
        WgoData best = null;
        int bestOutput = int.MinValue;
        try
        {
            WorldData world = MainGame.WorldData;
            string output = MainOutput(craft);
            foreach (string stationId in craft.craftsIn ?? new List<string>())
            {
                if (world == null || string.IsNullOrEmpty(stationId))
                    continue;
                foreach (WgoData wgo in world.GetWgoDataList(stationId) ?? new List<WgoData>())
                {
                    if (wgo == null)
                        continue;
                    int n = RawOutput(craft, output, wgo);
                    if (n > bestOutput)
                    {
                        best = wgo;
                        bestOutput = n;
                    }
                }
            }
        }
        catch
        {
            best = null;
        }
        stationCache[craft.id] = (Time.unscaledTime, best);
        return best;
    }

    private static int RawOutput(CraftDef craft, string key, WgoData station)
    {
        try
        {
            OutputPreview p = craft.GetOutputPreview(station);
            if (p == null || p.count <= 0)
                return 1;
            if (key == null || p.itemId == null || ItemsOf(key).Contains(p.itemId))
                return p.count;
        }
        catch
        {
        }
        return 1;
    }

    // Lo que pide una receta, como (clave de material, cantidad por vez), con tu estación real.
    public static List<(string key, int count)> Needs(CraftDef craft) => Needs(craft?.needItems, StationOf(craft));

    public static List<(string key, int count)> Needs(List<NeedItemData> needs, WgoData station = null)
    {
        List<(string, int)> result = new List<(string, int)>();
        if (needs == null)
            return result;
        foreach (NeedItemData n in needs)
        {
            if (n == null || string.IsNullOrEmpty(n.Id))
                continue;
            string key;
            if (n.IsGroup)
            {
                if (!n.TryGetGroupItemDefs(out List<ItemDef> defs) || defs == null || defs.Count == 0)
                    continue;
                key = AnyPrefix + n.Id;
            }
            else
            {
                if (GameBalance.Me?.GetDataOrNull<ItemDef>(n.Id) == null)
                    continue;
                key = n.Id;
            }
            int count = 1;
            try { count = n.GetCount(station); } catch { }
            if (count > 0)
                result.Add((key, count));
        }
        return result;
    }

    // Cuánto da la receta por cada vez que se hace, con tu estación real (talentos y mejoras incluidos).
    public static int OutputCount(CraftDef craft, string key) => RawOutput(craft, key, StationOf(craft));

    // Dónde se hace, corto para el panel: la estación que tienes (la que más rinde) y cuántas más
    // hay, ej. "Yunque de madera +1". La lista completa, con StationList (al pasar el mouse / Alt).
    public static string Station(CraftDef craft)
    {
        List<string> names = StationNames(craft);
        if (names.Count == 0)
            return "?";
        return names.Count > 1 ? $"{names[0]} +{names.Count - 1}" : names[0];
    }

    // Todas las estaciones, separadas con " / ": "Yunque de madera / Yunque de hierro".
    public static string StationList(CraftDef craft)
    {
        List<string> names = StationNames(craft);
        return names.Count > 0 ? string.Join(" / ", names) : "?";
    }

    // Las estaciones donde se hace, con las mejoras de una misma estación (I, II, III…) juntas en
    // un solo nombre, y primero la que tienes construida y rinde más.
    private static List<string> StationNames(CraftDef craft)
    {
        List<string> ids = craft?.craftsIn?.Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList() ?? new List<string>();
        if (ids.Count == 0)
            return new List<string>();
        string built = StationOf(craft)?.id;
        if (built != null && ids.Remove(built))
            ids.Insert(0, built);
        List<string> names = new List<string>();
        foreach (string id in ids)
        {
            string name = Plain(LLBase.HasL(id) ? LLBase.L(id) : id);
            string baseName = WithoutTier(name);
            if (name.Length > 0 && !names.Any(n => WithoutTier(n) == baseName))
                names.Add(ids.Count > 1 && id != built ? baseName : name);
        }
        return names;
    }

    // "Mesa de montaje II" → "Mesa de montaje": las mejoras de una estación cuentan como una.
    private static string WithoutTier(string name)
    {
        string[] tiers = { " I", " II", " III", " IV", " V" };
        foreach (string t in tiers.OrderByDescending(t => t.Length))
            if (name.EndsWith(t, StringComparison.Ordinal))
                return name.Substring(0, name.Length - t.Length);
        return name;
    }

    // El objeto que produce una receta (para agregarla a la cola como "hacer esto").
    public static string MainOutput(CraftDef craft)
    {
        string id = craft?.outputItems?.chanceOutputItems?.Select(o => o?.id).FirstOrDefault(s => !string.IsNullOrEmpty(s));
        return ResolveStar(id);
    }

    // ---------- Objetos ----------

    public static bool IsAny(string key) => key.StartsWith(AnyPrefix);

    // Los objetos concretos que cumplen con la clave (uno, o todos los del grupo).
    // Los objetos de un grupo no cambian durante la partida: se calculan una vez y se guardan.
    private static readonly Dictionary<string, string[]> itemsCache = new Dictionary<string, string[]>();
    private static GameBalance itemsCacheFrom;

    public static IEnumerable<string> ItemsOf(string key)
    {
        if (string.IsNullOrEmpty(key))
            return Array.Empty<string>();
        if (!ReferenceEquals(GameBalance.Me, itemsCacheFrom))
        {
            itemsCache.Clear();
            itemsCacheFrom = GameBalance.Me;
        }
        if (itemsCache.TryGetValue(key, out string[] cached))
            return cached;
        string[] result;
        if (!IsAny(key))
            result = new[] { key };
        else
        {
            List<ItemDef> defs = null;
            try { new NeedItemData(key.Substring(AnyPrefix.Length), 1).TryGetGroupItemDefs(out defs); } catch { }
            result = (defs ?? new List<ItemDef>()).Where(d => d != null).Select(d => d.id).Distinct().ToArray();
        }
        itemsCache[key] = result;
        return result;
    }

    public static string DisplayItem(string key) => IsAny(key) ? ItemsOf(key).FirstOrDefault() ?? key.Substring(AnyPrefix.Length) : key;

    public static string Name(string key)
    {
        if (IsAny(key))
        {
            string group = key.Substring(AnyPrefix.Length);
            if (GameBalance.Me?.GetDataOrNull<ItemDef>(group) == null && LLBase.HasL(group))
                return Plain(LLBase.L(group));
        }
        ItemDef def = GameBalance.Me?.GetDataOrNull<ItemDef>(DisplayItem(key));
        return def != null ? Plain(def.GetHeader()) : key;
    }

    public static bool IsFuel(string key) =>
        !IsAny(key) && GameBalance.Me?.GetDataOrNull<ItemDef>(key)?.isFuel == true;

    public static Sprite Icon(string key)
    {
        try
        {
            ItemDef def = GameBalance.Me?.GetDataOrNull<ItemDef>(DisplayItem(key));
            return def == null ? null : LazySingletonSO<EasySpritesCollection>.Instance.GetSprite(def.iconId);
        }
        catch
        {
            return null;
        }
    }

    public static Sprite IconById(string iconId)
    {
        try { return LazySingletonSO<EasySpritesCollection>.Instance.GetSprite(iconId); }
        catch { return null; }
    }

    // Cuánto tienes disponible, contado igual que el juego al craftear: tu inventario más los
    // almacenes de la zona donde estás (la estación puede tomar de ahí).
    public static int Owned(string key)
    {
        try
        {
            if (MainGame.PlayerData == null)
                return 0;
            MultiInventory inv = CountingInventory();
            int total = 0;
            foreach (string id in ItemsOf(key))
                total += inv.GetTotalCount(id);
            return total;
        }
        catch
        {
            return 0;
        }
    }

    // Los almacenes de la zona donde estás que el juego suma al craftear (los mismos que Owned).
    public static IEnumerable<WgoData> ZoneStorages() => ZoneStorages(MainGame.PlayerData?.CurrentWorldZoneData);

    public static IEnumerable<WgoData> ZoneStorages(WorldZoneData zone)
    {
        WorldData world = MainGame.Instance?.GameSave?.worldData;
        if (zone?.wgoDataList == null || world == null)
            yield break;
        foreach (SGuid guid in zone.wgoDataList)
        {
            WgoData wgo = world.GetWgoData(guid);
            if (wgo?.Definition != null && wgo.Definition.inventorySize != 0 && wgo.Definition.OpenInMultiInventory && wgo.Inventory?.Data != null)
                yield return wgo;
        }
    }

    public static int CountIn(Inventory inv, string key) =>
        inv?.Data == null ? 0 : ItemsOf(key).Sum(id => inv.Data.GetTotalCountInInventory(id));

    // Inventario + almacenes de la zona, armado una sola vez por cuadro (el panel pregunta por
    // muchos materiales seguidos; armarlo para cada uno recorría la zona entera cada vez).
    private static MultiInventory countingInventory;
    private static int countingFrame = -1;

    private static MultiInventory CountingInventory()
    {
        if (countingInventory == null || countingFrame != Time.frameCount)
        {
            countingInventory = new MultiInventory(MainGame.PlayerData, addCurrentPlayerWorldZone: true);
            countingFrame = Time.frameCount;
        }
        return countingInventory;
    }

    private static string ResolveStar(string id)
    {
        if (string.IsNullOrEmpty(id) || GameBalance.Me?.GetDataOrNull<ItemDef>(id) != null)
            return id;
        if (GameBalance.Me?.starGroupItemsCache != null && GameBalance.Me.starGroupItemsCache.TryGetValue(id, out List<ItemDef> stars) && stars.Count > 0)
            return stars[0].id;
        return id;
    }

    public static string Plain(string s) => Regex.Replace(s ?? "", "<[^>]*>", "").Trim();
}
