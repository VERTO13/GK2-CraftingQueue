using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CraftQueue;

// El plan de la cola: reparte lo que tienes entre las tareas, en el orden del panel (la de más
// arriba toma primero), y calcula qué falta de cada cosa.
//  - Una tarea de objeto pide TENER N: lo que ya tienes cuenta, y solo lo que falta se craftea.
//  - Lo que falta se baja por sus recetas (la elegida con ◂ ▸): cada ingrediente toma de lo que
//    quede libre, y lo que falte de él baja por su receta, y así (aunque esté plegado en el panel).
//  - Construcciones: cada material, igual.
// Cada renglón se identifica con la misma ruta que usa el panel ("tabla/tronco", "Build:x/clavo").
internal static class Plan
{
    internal sealed class Row
    {
        public string id;
        public int want;   // cuánto pide ese renglón
        public int avail;  // cuánto quedaba libre para él (puede ser más de lo que pide)
        public bool fuel;
        public int Missing => fuel ? 0 : Mathf.Max(0, want - avail);
    }

    internal sealed class Group
    {
        public string item;                  // tarea de objeto: el objeto (null en construcciones)
        public QueueView.Entry build;        // construcción u obra del pueblo
        public int total;                    // objetos: cuántos pide en total
        public int preferred = -1;           // receta con la que se agregó desde una mesa
        public readonly List<QueueTask> tasks = new List<QueueTask>();
        public readonly List<string> ids = new List<string>();
        public string Key => item != null ? "i:" + item : build.id;
    }

    public static readonly Dictionary<string, Row> Rows = new Dictionary<string, Row>();
    public static readonly Dictionary<string, int> Crafts = new Dictionary<string, int>(); // veces a craftear por ruta
    public static readonly List<Group> Groups = new List<Group>();

    private static readonly Dictionary<string, int> pool = new Dictionary<string, int>();

    public static string HeaderPath(string item) => "#" + item;

    // Tareas de un mismo objeto juntas (agregadas como objeto o desde una mesa); construcciones aparte.
    private static void GroupTasks(List<QueueView.Entry> items)
    {
        Groups.Clear();
        Dictionary<string, Group> byItem = new Dictionary<string, Group>();
        foreach (QueueView.Entry e in items)
        {
            string item = null;
            int preferred = -1;
            if (!e.isBuild)
                item = e.id;
            else if (e.id.StartsWith("craft:") && e.iconItem != null)
            {
                item = e.iconItem;
                CraftDef craft = GameBalance.Me?.GetDataOrNull<CraftDef>(e.id.Substring("craft:".Length));
                if (craft != null)
                    preferred = GameData.OptionsFor(item).FindIndex(o => o.craft == craft); // su primera estación
            }
            if (item == null)
            {
                Group b = new Group { build = e };
                b.tasks.Add(e.raw);
                b.ids.Add(e.id);
                Groups.Add(b);
                continue;
            }
            if (!byItem.TryGetValue(item, out Group g))
            {
                g = new Group { item = item };
                byItem[item] = g;
                Groups.Add(g);
            }
            g.total += e.need;
            if (g.preferred < 0)
                g.preferred = preferred;
            g.tasks.Add(e.raw);
            g.ids.Add(e.id);
        }
    }

    public static void Compute(List<QueueView.Entry> items)
    {
        GroupTasks(items);
        Rows.Clear();
        Crafts.Clear();
        pool.Clear();
        foreach (Group g in Groups)
        {
            if (g.item != null)
            {
                Row head = Take(HeaderPath(g.item), g.item, g.total);
                if (head.Missing > 0)
                    Expand(g.item, head.Missing, g.item, 1, g.preferred);
                continue;
            }
            QueueView.Entry e = g.build;
            foreach ((string pid, int per) in e.parts)
            {
                string path = e.id + "/" + pid;
                Row r = Take(path, pid, per * e.need);
                if (r.Missing > 0)
                    Expand(pid, r.Missing, path, 2, -1);
            }
        }
    }

    private static Row Take(string path, string id, int want)
    {
        Row r = new Row { id = id, want = want, fuel = GameData.IsFuel(id) };
        if (!r.fuel)
        {
            if (!pool.TryGetValue(id, out int free))
                free = GameData.Owned(id);
            r.avail = free;
            pool[id] = free - Mathf.Min(free, want);
        }
        Rows[path] = r;
        return r;
    }

    private static void Expand(string id, int missing, string path, int depth, int preferred)
    {
        if (depth > Prefs.MaxDepth + 2)
            return;
        List<GameData.Recipe> recipes = GameData.OptionsFor(id);
        if (recipes.Count == 0)
            return;
        GameData.Recipe recipe = recipes[Prefs.SelectedRecipe(id, recipes, Mathf.Max(1, missing), preferred)];
        int output = Mathf.Max(1, GameData.OutputCount(recipe, id));
        int crafts = Mathf.CeilToInt(missing / (float)output);
        Crafts[path] = crafts;
        string[] above = path.Split('/');
        foreach ((string nid, int n) in GameData.Needs(recipe))
        {
            string child = path + "/" + nid;
            Row r = Take(child, nid, n * crafts);
            if (r.Missing > 0 && !above.Contains(nid))
                Expand(nid, r.Missing, child, depth + 1, -1);
        }
    }

    // Cuántas veces hay que craftear la receta de esa ruta (0 si ya no falta nada).
    public static int CraftsAt(string path) => Crafts.TryGetValue(path, out int c) ? c : 0;

    // ---------- Vista Total ----------
    // Los renglones "hoja" del plan (lo que ya no se desglosa: materiales sin receta, lo que ya
    // tienes, o donde se llegó al fondo), sumados por material en el orden de la cola.
    // Necesitas = lo que pide cada renglón; tienes = lo que le tocó del reparto.

    internal sealed class Total
    {
        public string id;
        public int want, have;
        public bool fuel;
    }

    public static List<Total> Totals()
    {
        HashSet<string> parents = new HashSet<string>();
        foreach (string path in Rows.Keys)
        {
            int slash = path.LastIndexOf('/');
            if (slash > 0)
                parents.Add(path.Substring(0, slash));
        }
        List<Total> list = new List<Total>();
        Dictionary<string, Total> byId = new Dictionary<string, Total>();
        foreach (KeyValuePair<string, Row> kv in Rows)
        {
            // "#objeto" es la barra de una tarea: sus ingredientes cuelgan de "objeto/…".
            string own = kv.Key[0] == '#' ? kv.Key.Substring(1) : kv.Key;
            if (parents.Contains(own))
                continue; // se desglosa: cuentan sus ingredientes
            Row r = kv.Value;
            if (!byId.TryGetValue(r.id, out Total t))
            {
                t = new Total { id = r.id, fuel = r.fuel };
                byId[r.id] = t;
                list.Add(t);
            }
            t.want += r.want;
            if (!r.fuel)
                t.have += Mathf.Min(r.avail, r.want);
        }
        return list;
    }

    // Lo que se marca en los cofres para un grupo: el objeto (o los materiales de la construcción);
    // de lo que falte, sus ingredientes; más abajo, solo lo que tengas desplegado en el panel.
    public static HashSet<string> MarksFor(Group g)
    {
        HashSet<string> m = new HashSet<string>();
        string root = g.item ?? g.build.id;
        if (g.item != null)
            m.Add(g.item);
        string prefix = root + "/";
        foreach (KeyValuePair<string, Row> kv in Rows)
        {
            if (!kv.Key.StartsWith(prefix, System.StringComparison.Ordinal))
                continue;
            string parent = kv.Key.Substring(0, kv.Key.LastIndexOf('/'));
            if (parent == root || Prefs.Expanded.Contains(parent))
                m.Add(kv.Value.id);
        }
        return m;
    }

    // ---------- Tareas que se completan solas ----------
    // Una tarea de objeto se quita sola cuando pasa de "falta" a "completa" sin que hayas cambiado
    // de zona: la crafteaste, la recogiste o la compraste. Si la agregas cuando ya la tienes, o
    // llegas a una zona donde ya había suficiente, se queda en verde (no desaparece de golpe).

    private static readonly Dictionary<string, bool> wasComplete = new Dictionary<string, bool>();
    private static string lastZone, lastSlot;
    private static int lastVersion = -1, lastCounting = -1;
    private static float nextCheck;

    public static void Tick()
    {
        if (Time.unscaledTime < nextCheck)
            return;
        nextCheck = Time.unscaledTime + 0.5f;
        if (!Queue.HasSlot || MainGame.PlayerData == null)
            return;
        long t = Perf.Start();
        try
        {
            string zone = MainGame.PlayerData.CurrentWorldZoneData?.id;
            string slot = GameState.Slot;
            // Cambiar de zona o de partida, editar la cola (+, −, agregar…) o cambiar qué se cuenta
            // (la bolsa y el cofre de la barra) no completa nada: solo toma la foto nueva.
            int counting = (Plugin.CountCarried ? 1 : 0) | (Plugin.CountChests ? 2 : 0);
            bool sameContext = zone == lastZone && slot == lastSlot && Queue.Version == lastVersion && counting == lastCounting;
            lastZone = zone;
            lastSlot = slot;
            lastVersion = Queue.Version;
            lastCounting = counting;

            Compute(QueueView.Items());
            List<QueueTask> done = new List<QueueTask>();
            HashSet<string> seen = new HashSet<string>();
            foreach (Group g in Groups)
            {
                if (g.item == null)
                    continue;
                seen.Add(g.item);
                bool complete = Rows.TryGetValue(HeaderPath(g.item), out Row r) && r.Missing == 0;
                if (sameContext && complete && wasComplete.TryGetValue(g.item, out bool before) && !before)
                    done.AddRange(g.tasks);
                wasComplete[g.item] = complete;
            }
            foreach (string gone in wasComplete.Keys.Where(k => !seen.Contains(k)).ToList())
                wasComplete.Remove(gone);
            if (done.Count > 0)
            {
                Plugin.Log.LogInfo("Tareas completadas: " + string.Join(", ", done.Select(d => d.id).Distinct()));
                Queue.RemoveAll(done);
                lastVersion = Queue.Version;
            }
        }
        finally { Perf.Stop("cola: completadas", t); } // va aparte de "partida y botones": cuenta en el cuadro
    }
}
