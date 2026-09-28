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
    // investigadas y tus estaciones. Una estación construida, mejorada o quitada cambia la cuenta de
    // objetos de la zona donde estás; cambiar de zona no cambia nada (las estaciones se buscan en todo
    // el mundo), así que solo importa si esa cuenta cambia sin haber cambiado de zona. Antes, cada cambio
    // de zona volvía a calcular todas las recetas de la cola justo al llegar.
    private static string stampZone;
    private static int stampZoneCount, stationEdits;

    public static int StationsStamp
    {
        get
        {
            GameSave save = MainGame.Instance?.GameSave;
            int perks = save?.perkSystemData?.activePerks?.Count ?? 0;
            int techs = save?.knowledgeSystem?.unlockedTechs?.Count ?? 0;
            WorldZoneData zone = MainGame.PlayerData?.CurrentWorldZoneData;
            int count = zone?.wgoDataList?.Count ?? 0;
            if (zone?.id == stampZone && count != stampZoneCount)
                stationEdits++;
            stampZone = zone?.id;
            stampZoneCount = count;
            return perks * 1000003 + techs * 1009 + stationEdits;
        }
    }

    // Solo talentos y tecnologías (sin los objetos de la zona, que cambian por cualquier cosa):
    // para que el panel se redibuje únicamente cuando de verdad pudo cambiar un ×N.
    public static int PerksStamp
    {
        get
        {
            GameSave save = MainGame.Instance?.GameSave;
            KnowledgeSystem ks = save?.knowledgeSystem;
            // Construcciones desbloqueadas también (por tecnología, misión o lo que sea): una
            // estación nueva aparece al momento como opción de receta.
            return ((save?.perkSystemData?.activePerks?.Count ?? 0) * 1009 + (ks?.unlockedTechs?.Count ?? 0)) * 1009
                   + (ks?.unlockedBuildings?.Count ?? 0) * 31 + (ks?.lockedBuildings?.Count ?? 0);
        }
    }

    // Se construyó o terminó una obra: recalcular ya (una mejora puede no cambiar la cuenta de objetos).
    public static void ResetStations()
    {
        stationCache.Clear();
        stationEdits++; // y las opciones de receta (su nombre lleva la mejora que tienes)
    }

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
            // La fórmula necesitaba algo que esa estación no tiene (p. ej. una de prueba): se
            // calcula con la receta base en vez de mostrar 1.
            if (station != null)
                return RawOutput(craft, key, null);
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
            try { count = n.GetCount(station); }
            catch
            {
                try { count = n.GetCount(); } catch { } // sin estación: la cantidad base
            }
            if (count > 0)
                result.Add((key, count));
        }
        return result;
    }

    // Cuánto da la receta por cada vez que se hace, con tu estación real (talentos y mejoras incluidos).
    public static int OutputCount(CraftDef craft, string key) => RawOutput(craft, key, StationOf(craft));

    // ---------- Opciones de receta: receta + estación ----------
    // Una receta del juego que se hace en varias estaciones da una opción por estación (se
    // cambian con ◂ ▸). Las mejoras de una misma estación (I, II, III…) cuentan como una sola,
    // y se usa la que tienes construida que más rinde. Cada opción calcula su propio ×N y lo que
    // pide con esa estación (talentos, mejoras…).
    internal sealed class Recipe
    {
        public CraftDef craft;
        public List<string> stations = new List<string>(); // ids de la estación y sus mejoras
        public string label;                                // nombre para mostrar
    }

    private static readonly Dictionary<string, (int stamp, List<Recipe> list)> optionsCache = new Dictionary<string, (int, List<Recipe>)>();

    public static List<Recipe> OptionsFor(string key)
    {
        int stamp = ((StationsStamp * 31 + KnowledgeStamp) * 31 + PerksStamp) * 31 + (LLBase.CurrentLang?.GetHashCode() ?? 0);
        if (optionsCache.TryGetValue(key, out var cached) && cached.stamp == stamp)
            return cached.list;
        List<Recipe> list = new List<Recipe>();
        foreach (CraftDef craft in RecipesFor(key))
        {
            // Agrupa las estaciones por nombre sin la mejora ("Mesa de montaje II" → "Mesa de montaje").
            List<Recipe> groups = new List<Recipe>();
            foreach (string id in (craft.craftsIn ?? new List<string>()).Where(s => !string.IsNullOrEmpty(s)).Distinct())
            {
                string baseName = WithoutTier(StationName(id));
                Recipe g = groups.FirstOrDefault(x => x.label == baseName);
                if (g == null)
                    groups.Add(g = new Recipe { craft = craft, label = baseName });
                g.stations.Add(id);
            }
            // Solo estaciones que tienes construidas o que ya puedes construir (como el menú de
            // construir: sin desbloqueo o desbloqueada, y no bloqueada). Ej.: el Yunque de acero no
            // aparece mientras no lo desbloquees en el árbol tecnológico.
            // Si no tienes ninguna estación donde hacerla, la receta no se muestra (como una
            // bloqueada); aparece sola cuando desbloqueas o construyes una de sus estaciones.
            groups = groups.Where(g => g.stations.Any(StationAvailable)).ToList();
            foreach (Recipe g in groups)
            {
                string built = BestBuilt(g)?.id;
                if (built != null)
                    g.label = StationName(built); // la mejora que tienes: "Mesa de montaje II"
            }
            list.AddRange(groups);
        }
        optionsCache[key] = (stamp, list);
        return list;
    }

    public static int OutputCount(Recipe r, string key) => RawOutput(r.craft, key, BestBuilt(r));

    public static List<(string key, int count)> Needs(Recipe r) => Needs(r.craft?.needItems, BestBuilt(r));

    public static string Station(Recipe r) => r?.label ?? "?";

    // La estación construida de esa opción (o sus mejoras) que más rinde; null si no tienes ninguna.
    private static WgoData BestBuilt(Recipe r)
    {
        if (r?.craft == null)
            return null;
        string cacheKey = r.craft.id + "@" + string.Join(",", r.stations);
        int stamp = StationsStamp;
        if (stamp != stationStamp)
        {
            stationStamp = stamp;
            stationCache.Clear();
        }
        if (stationCache.TryGetValue(cacheKey, out var cached))
            return cached.station;
        WgoData best = null;
        int bestOutput = int.MinValue;
        try
        {
            WorldData world = MainGame.WorldData;
            string output = MainOutput(r.craft);
            foreach (string stationId in r.stations)
                foreach (WgoData wgo in world?.GetWgoDataList(stationId) ?? new List<WgoData>())
                {
                    if (wgo == null)
                        continue;
                    int n = RawOutput(r.craft, output, wgo);
                    if (n > bestOutput)
                    {
                        best = wgo;
                        bestOutput = n;
                    }
                }
        }
        catch
        {
            best = null;
        }
        // Sin ninguna construida: el edificio BASE de esa opción (su primera mejora), sin nada
        // agregado, con solo lo tuyo (talentos, ventajas, pasivas). Lo que dependa de mejoras o
        // complementos del edificio se verá cuando lo construyas: entonces se usa el real, arriba.
        if (best == null)
        {
            string baseStation = r.stations.Where(StationAvailable).OrderBy(TierOf).FirstOrDefault()
                                 ?? r.stations.OrderBy(TierOf).FirstOrDefault();
            if (baseStation != null)
                best = PreviewStation(baseStation);
        }
        stationCache[cacheKey] = (Time.unscaledTime, best);
        return best;
    }

    // Nivel de mejora por el nombre: sin número o "I" = 1, "II" = 2, "III" = 3…
    private static int TierOf(string stationId)
    {
        string name = StationName(stationId);
        string[] tiers = { " V", " IV", " III", " II", " I" };
        int[] values = { 5, 4, 3, 2, 1 };
        for (int i = 0; i < tiers.Length; i++)
            if (name.EndsWith(tiers[i], StringComparison.Ordinal))
                return values[i];
        return 1;
    }

    // Estación "de prueba" para calcular fórmulas: vacía, solo con su tipo. No se pone en el mundo,
    // no se registra en ningún lado y no toca la partida (el constructor normal sí: suma calidad
    // del pueblo y la mete en grupos de personajes, por eso no se usa).
    private static readonly Dictionary<string, WgoData> previews = new Dictionary<string, WgoData>();

    private static WgoData PreviewStation(string stationId)
    {
        if (previews.TryGetValue(stationId, out WgoData w))
            return w;
        try
        {
            w = new WgoData { id = stationId, isTempObject = true };
            if (w.Definition == null)
                w = null;
        }
        catch
        {
            w = null;
        }
        previews[stationId] = w;
        return w;
    }

    // ¿Tienes esa estación o puedes construirla? Construida en cualquier lugar: sí. Si se construye
    // desde el menú, depende de si está desbloqueada. Si no se construye así (fijas del mundo, del
    // pueblo, de personajes), no hay forma de saberlo: se cuenta como disponible.
    private static bool StationAvailable(string wgoId)
    {
        try
        {
            if ((MainGame.WorldData?.GetWgoDataList(wgoId)?.Count ?? 0) > 0)
                return true;
            KnowledgeSystem ks = MainGame.Instance?.GameSave?.knowledgeSystem;
            List<BuildingDef> builders = BuildersOf(wgoId);
            if (ks == null || builders == null || builders.Count == 0)
                return true;
            return builders.Any(b => (!b.isNeedsUnlock || ks.unlockedBuildings.Contains(b.id)) && !ks.lockedBuildings.Contains(b.id));
        }
        catch
        {
            return true;
        }
    }

    // Las construcciones del menú de construir que colocan cada estación. Son datos fijos del juego: se
    // agrupan una vez (antes se recorrían todas las construcciones del juego por cada estación revisada).
    // Solo las que la colocan: las de "quitar" (steel_anvil_r) no necesitan desbloqueo y hacían parecer
    // disponible un yunque bloqueado. Misma regla que el menú de construir.
    private static Dictionary<string, List<BuildingDef>> buildersByStation;
    private static GameBalance buildersFrom;

    private static List<BuildingDef> BuildersOf(string wgoId)
    {
        GameBalance balance = GameBalance.Me;
        if (balance?.buildingDefs == null || string.IsNullOrEmpty(wgoId))
            return null;
        if (buildersByStation == null || !ReferenceEquals(balance, buildersFrom))
        {
            buildersFrom = balance;
            buildersByStation = new Dictionary<string, List<BuildingDef>>();
            foreach (BuildingDef b in balance.buildingDefs)
            {
                if (b == null || string.IsNullOrEmpty(b.wgoId)
                    || b.buildingMode == BuildingDef.BuildingMode.None || b.buildingMode == BuildingDef.BuildingMode.Remove)
                    continue;
                if (!buildersByStation.TryGetValue(b.wgoId, out List<BuildingDef> list))
                    buildersByStation[b.wgoId] = list = new List<BuildingDef>();
                list.Add(b);
            }
        }
        return buildersByStation.TryGetValue(wgoId, out List<BuildingDef> found) ? found : null;
    }

    private static string StationName(string id)
    {
        string name = Plain(LLBase.HasL(id) ? LLBase.L(id) : id);
        return name.Length > 0 ? name : id;
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

    // Cuánto tienes disponible. De fábrica, igual que el juego al craftear: tu inventario más los
    // almacenes de la zona donde estás (la estación puede tomar de ahí). La bolsa y el cofre de la
    // barra de botones eligen qué se cuenta: solo lo que llevas encima, solo los almacenes de la zona
    // (todo menos lo que llevas), o las dos cosas.
    public static int Owned(string key)
    {
        try
        {
            if (MainGame.PlayerData == null)
                return 0;
            bool carried = Plugin.CountCarried, chests = Plugin.CountChests;
            MultiInventory all = chests ? Counting(ref zoneInventory, ref zoneFrame, withZone: true) : null;
            MultiInventory bag = carried && chests ? null : Counting(ref bagInventory, ref bagFrame, withZone: false);
            int total = 0;
            foreach (string id in ItemsOf(key))
            {
                if (carried && chests)
                    total += all.GetTotalCount(id);
                else if (carried)
                    total += bag.GetTotalCount(id);
                else
                    total += Math.Max(0, all.GetTotalCount(id) - bag.GetTotalCount(id));
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }

    // Lo que le falta a un pedido del pueblo. Desde la 1.006 el juego toma la mercancía de las
    // tarimas del almacén (y de su sótano) en cuanto la pones y cada noche, no solo en Pride:
    // los pedidos normales se entregan por partes (VendorOrderData.Count lleva lo entregado) y los
    // urgentes completos o nada (mientras, la mercancía espera en la tarima).
    public static int OrderMissing(VendorOrderData order)
    {
        VendorOrderDef def = order?.Definition;
        if (def == null)
            return 0;
        int delivered = def.isUrgent ? 0 : order.Count;
        int onPallets = 0;
        try
        {
            foreach (string zone in new[] { "warehouse", "warehouse_cellar" })
                onPallets += MainGame.WorldData?.GetWorldZoneDataById(zone)?.CountItemsOnTownPalettes(def.itemId) ?? 0;
        }
        catch
        {
            onPallets = 0; // sin tarimas legibles: se pide lo que falta por entregar
        }
        return Math.Max(0, def.count - delivered - onPallets);
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

    // Lo que hay en los almacenes de OTRAS zonas (desde aquí no se puede usar): la zona que más
    // tiene y cuánto, para avisar en el panel "Patio: 7". Con el cofre de la barra apagado también
    // cuenta la zona donde estás (sus cofres no se suman). Lo que tienen se vuelve a contar cada 2 s.
    // Qué almacenes hay casi no cambia: esa lista se rearma solo si cambió la cuenta de objetos de
    // alguna zona (se construyó o se quitó algo), si cambiaste de zona o de qué se cuenta, o cada 30 s.
    // Antes se recorrían todos los objetos del mundo cada vez.
    private static float elsewhereAt = -10f, elsewhereListsAt = -100f;
    private static bool elsewhereWithHere;
    private static int elsewhereListsStamp;
    private static readonly List<(string zone, List<WgoData> storages)> otherZones = new List<(string, List<WgoData>)>();
    private static readonly Dictionary<string, (string zone, int count)> elsewhere = new Dictionary<string, (string, int)>();

    public static (string zone, int count) Elsewhere(string key)
    {
        try
        {
            bool withHere = !Plugin.CountChests;
            float now = Time.unscaledTime;
            if (now - elsewhereAt > 2f || withHere != elsewhereWithHere)
            {
                elsewhereAt = now;
                elsewhere.Clear();
                WorldZoneData here = MainGame.PlayerData?.CurrentWorldZoneData;
                List<GameSceneData> scenes = MainGame.WorldData?.gameSceneDataList ?? new List<GameSceneData>();
                int stamp = (withHere ? 1 : 0) * 31 + (here?.id?.GetHashCode() ?? 0);
                foreach (GameSceneData scene in scenes)
                    foreach (WorldZoneData z in scene?.worldZones ?? new List<WorldZoneData>())
                        stamp = stamp * 31 + (z?.wgoDataList?.Count ?? 0);
                if (stamp != elsewhereListsStamp || now - elsewhereListsAt > 30f || withHere != elsewhereWithHere)
                {
                    elsewhereListsStamp = stamp;
                    elsewhereListsAt = now;
                    otherZones.Clear();
                    foreach (GameSceneData scene in scenes)
                        foreach (WorldZoneData z in scene?.worldZones ?? new List<WorldZoneData>())
                        {
                            if (z == null || (!withHere && (z == here || (here != null && z.id == here.id))))
                                continue;
                            List<WgoData> storages = ZoneStorages(z).ToList();
                            if (storages.Count > 0)
                                otherZones.Add((ZoneName(z.id), storages));
                        }
                }
                elsewhereWithHere = withHere;
            }
            if (elsewhere.TryGetValue(key, out (string zone, int count) found))
                return found;
            found = (null, 0);
            foreach ((string zone, List<WgoData> storages) in otherZones)
            {
                int n = storages.Sum(w => CountIn(w.Inventory, key));
                if (n > found.count)
                    found = (zone, n);
            }
            elsewhere[key] = found;
            return found;
        }
        catch
        {
            return (null, 0);
        }
    }

    public static string ZoneName(string zone)
    {
        if (string.IsNullOrEmpty(zone))
            return "";
        return LLBase.HasL("wz_" + zone) ? Plain(LLBase.L("wz_" + zone)) : zone;
    }

    public static int CountIn(Inventory inv, string key) =>
        inv?.Data == null ? 0 : ItemsOf(key).Sum(id => inv.Data.GetTotalCountInInventory(id));

    // Inventario + almacenes de la zona, y solo el inventario: cada uno armado una sola vez por cuadro
    // (el panel pregunta por muchos materiales seguidos; armarlo para cada uno recorría la zona entera
    // cada vez).
    private static MultiInventory zoneInventory, bagInventory;
    private static int zoneFrame = -1, bagFrame = -1;

    private static MultiInventory Counting(ref MultiInventory inv, ref int frame, bool withZone)
    {
        if (inv == null || frame != Time.frameCount)
        {
            inv = new MultiInventory(MainGame.PlayerData, addCurrentPlayerWorldZone: withZone);
            frame = Time.frameCount;
        }
        return inv;
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
