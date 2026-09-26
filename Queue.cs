using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CraftQueue;

internal enum TaskKind
{
    Item,   // "necesito tener N de este objeto"
    Craft,  // igual, agregada desde una mesa (recuerda la receta para mostrarla primero)
    Build,  // construcción (BuildingDef): colocar N
    Town    // obra del pueblo (TownBuildingDef): reparar/mejorar
}

[Serializable]
internal class QueueTask
{
    public TaskKind kind;
    public string id;       // clave de material, id de receta, de construcción o de obra
    public int count = 1;
    public string title;    // nombre al agregarla (las construcciones no siempre tienen uno traducible)
    public string icon;     // id de sprite (construcciones) o vacío
    public string zone;     // zona de la construcción, si aplica
    // Materiales de construcciones/obras, tal como se pedían al agregarlas (una mejora puede
    // cambiar lo que pide según el estado, así que se guarda la foto).
    public List<string> partKeys = new List<string>();
    public List<int> partCounts = new List<int>();

    public IEnumerable<(string key, int count)> Parts =>
        partKeys.Zip(partCounts, (k, c) => (k, c));
}

// La cola del jugador: una por partida guardada, en un archivo de texto propio del mod
// ("<partida>.txt"), junto con los pines de las tareas marcadas.
// Formato: una línea por dato, campos separados por tabulador (\t, \n y \\ escapados):
//   task <tipo> <id> <cantidad> <título> <ícono> <zona> <material:cantidad|material:cantidad…>
//   pin  <id de tarea en el panel>
// (No se usa JsonUtility de Unity: con las clases del mod guardaba el archivo sin las tareas.)
internal static class Queue
{
    private const string Header = "# Crafting Queue 1";

    public static readonly List<QueueTask> Tasks = new List<QueueTask>();
    public static readonly HashSet<string> Pins = new HashSet<string>(); // tareas marcadas con el pin
    public static int Version { get; private set; } // cambia con cada modificación (para redibujar)
    public static event Action Changed;

    private static string loadedSlot;
    private static string folder;

    public static void Init(string dataFolder)
    {
        folder = dataFolder;
        Directory.CreateDirectory(folder);
    }

    // Se llama seguido: si cambió la partida cargada, carga la cola de la nueva.
    public static void SyncSlot(string slot)
    {
        if (slot == loadedSlot)
            return;
        loadedSlot = slot;
        Tasks.Clear();
        Pins.Clear();
        if (slot != null)
        {
            try
            {
                string path = PathFor(slot);
                if (File.Exists(path))
                    Load(File.ReadAllLines(path));
                Plugin.Log.LogInfo($"Cola de la partida {slot}: {Tasks.Count} tareas, {Pins.Count} con pin.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("No se pudo leer la cola de la partida: " + e.Message);
            }
        }
        Touch(save: false);
    }

    private static string PathFor(string slot)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            slot = slot.Replace(c, '_');
        return Path.Combine(folder, slot + ".txt");
    }

    private static void Load(string[] lines)
    {
        foreach (string line in lines)
        {
            if (line.Length == 0 || line.StartsWith("#"))
                continue;
            string[] f = line.Split('\t').Select(Unescape).ToArray();
            if (f[0] == "pin" && f.Length >= 2)
            {
                Pins.Add(f[1]);
                continue;
            }
            if (f[0] != "task" || f.Length < 8 || !Enum.TryParse(f[1], out TaskKind kind) || !int.TryParse(f[3], out int count))
                continue;
            if (string.IsNullOrEmpty(f[2]) || count <= 0)
                continue;
            QueueTask t = new QueueTask { kind = kind, id = f[2], count = count, title = Null(f[4]), icon = Null(f[5]), zone = Null(f[6]) };
            foreach (string part in f[7].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = part.LastIndexOf(':');
                if (colon > 0 && int.TryParse(part.Substring(colon + 1), out int n))
                {
                    t.partKeys.Add(part.Substring(0, colon));
                    t.partCounts.Add(n);
                }
            }
            Tasks.Add(t);
        }
    }

    private static void Save()
    {
        if (loadedSlot == null || folder == null)
            return;
        try
        {
            List<string> lines = new List<string> { Header };
            foreach (QueueTask t in Tasks)
                lines.Add(string.Join("\t", new[]
                {
                    "task", t.kind.ToString(), t.id, t.count.ToString(), t.title ?? "", t.icon ?? "", t.zone ?? "",
                    string.Join("|", t.Parts.Select(p => p.key + ":" + p.count))
                }.Select(Escape)));
            foreach (string pin in Pins.OrderBy(p => p))
                lines.Add("pin\t" + Escape(pin));
            // Primero a un archivo temporal y luego se reemplaza: si el juego se cierra a medias,
            // no se pierde la cola anterior.
            string path = PathFor(loadedSlot), tmp = path + ".tmp";
            File.WriteAllLines(tmp, lines, new System.Text.UTF8Encoding(false));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("No se pudo guardar la cola: " + e.Message);
        }
    }

    private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "");

    private static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0)
            return s;
        System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char n = s[++i];
                sb.Append(n == 't' ? '\t' : n == 'n' ? '\n' : n);
            }
            else
                sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static string Null(string s) => string.IsNullOrEmpty(s) ? null : s;

    // Marcar/desmarcar el pin de una tarea del panel (se guarda con la partida). Devuelve si quedó marcado.
    public static bool TogglePin(string id)
    {
        if (!HasSlot || string.IsNullOrEmpty(id))
            return false;
        bool on = !Pins.Remove(id);
        if (on)
            Pins.Add(id);
        Touch();
        return on;
    }

    // El id con el que el panel identifica el bloque de esa tarea (el mismo que usa su pin):
    // objetos y recetas se juntan por objeto ("i:<objeto>"); construcciones y obras, por tarea.
    private static string PinIdFor(QueueTask t)
    {
        switch (t.kind)
        {
            case TaskKind.Item:
                return "i:" + t.id;
            case TaskKind.Craft:
                string output = OutputOf(t);
                return output != null ? "i:" + output : null;
            default:
                return t.kind + ":" + t.id;
        }
    }

    private static string OutputOf(QueueTask t)
    {
        CraftDef craft = GameBalance.Me?.GetDataOrNull<CraftDef>(t.id);
        return craft != null ? GameData.MainOutput(craft) : null;
    }

    public static void ClearPins()
    {
        if (Pins.Count == 0)
            return;
        Pins.Clear();
        Touch();
    }

    private static void Touch(bool save = true)
    {
        // Pines de tareas que ya no están en la cola (terminadas o quitadas): fuera.
        if (save && Pins.Count > 0)
        {
            HashSet<string> live = new HashSet<string>(Tasks.Select(PinIdFor).Where(p => p != null));
            Pins.RemoveWhere(p => !live.Contains(p));
        }
        Version++;
        if (save)
            Save();
        try { Changed?.Invoke(); } catch (Exception e) { Plugin.Log.LogWarning("Cola: " + e.Message); }
    }

    public static bool HasSlot => loadedSlot != null;

    // ---------- Agregar ----------

    // Si ya hay una tarea igual, le suma; si no, crea una nueva. Devuelve la tarea.
    // Las tareas de objetos piden TENER esa cantidad. Con oneMore (Ctrl + clic en un objeto o una
    // receta, sin cantidad pedida) se entiende "quiero N más de lo que ya tengo": si ya tienes 10
    // y agregas 1, pide 11.
    public static QueueTask Add(TaskKind kind, string id, int count, string title = null, string icon = null,
        string zone = null, IEnumerable<(string key, int count)> parts = null, bool oneMore = false)
    {
        if (!HasSlot || string.IsNullOrEmpty(id) || count <= 0)
            return null;
        if (oneMore && (kind == TaskKind.Item || kind == TaskKind.Craft))
        {
            string item = kind == TaskKind.Item ? id : OutputOf(new QueueTask { kind = kind, id = id });
            if (item != null)
            {
                int planned = Tasks.Where(t => ItemOf(t) == item).Sum(t => t.count);
                count = Math.Max(planned, GameData.Owned(item)) + count - planned;
            }
        }
        QueueTask task = Tasks.FirstOrDefault(t => t.kind == kind && t.id == id && (t.zone ?? "") == (zone ?? ""));
        if (task != null)
        {
            task.count += count;
        }
        else
        {
            task = new QueueTask { kind = kind, id = id, count = count, title = title, icon = icon, zone = zone };
            foreach ((string key, int n) in parts ?? Enumerable.Empty<(string, int)>())
            {
                task.partKeys.Add(key);
                task.partCounts.Add(n);
            }
            Tasks.Add(task);
            // Tarea nueva: su pin prendido, para ver al momento en qué cofres están sus materiales
            // (si ya estaba en la cola y solo se suma, se respeta su pin como esté).
            if (Plugin.PinNewTasks && PinIdFor(task) is string pin)
                Pins.Add(pin);
        }
        Touch();
        return task;
    }

    // ---------- Cambiar / quitar ----------

    public static void Change(QueueTask task, int delta)
    {
        if (task == null || !Tasks.Contains(task))
            return;
        task.count += delta;
        if (task.count <= 0)
            Tasks.Remove(task);
        Touch();
    }

    public static void Remove(QueueTask task)
    {
        if (task != null && Tasks.Remove(task))
            Touch();
    }

    public static void RemoveAll(IEnumerable<QueueTask> tasks)
    {
        HashSet<QueueTask> gone = new HashSet<QueueTask>(tasks);
        if (Tasks.RemoveAll(gone.Contains) > 0)
            Touch();
    }

    // Nuevo orden de la cola (el panel lo pide al subir o bajar una tarea). Las que no vengan en
    // la lista se quedan al final, en su orden.
    public static void SetOrder(IEnumerable<QueueTask> order)
    {
        List<QueueTask> sorted = order.Where(Tasks.Contains).Distinct().ToList();
        sorted.AddRange(Tasks.Where(t => !sorted.Contains(t)));
        if (sorted.SequenceEqual(Tasks))
            return;
        Tasks.Clear();
        Tasks.AddRange(sorted);
        Touch();
    }

    // El objeto que pide una tarea de objeto o de receta (null en construcciones).
    private static string ItemOf(QueueTask t) =>
        t.kind == TaskKind.Item ? t.id : t.kind == TaskKind.Craft ? OutputOf(t) : null;

    // ---------- Al craftear ----------
    // Ya no se descuenta: las tareas de objetos piden tener N, y lo crafteado sube lo que tienes.
    // El plan (Plan.Tick) quita la tarea cuando se completa.
    public static void OnCrafted(CraftDef craft, IEnumerable<(string item, int count)> produced)
    {
    }

    // Se colocó una construcción o se hizo una obra del pueblo.
    public static void OnBuilt(TaskKind kind, string id)
    {
        // Una estación nueva o mejorada puede cambiar lo que rinden las recetas: recalcular ya.
        GameData.ResetStations();
        QueueHud.Dirty = true;
        if (!HasSlot || string.IsNullOrEmpty(id))
            return;
        QueueTask task = Tasks.FirstOrDefault(t => t.kind == kind && t.id == id);
        if (task == null)
            return;
        task.count--;
        if (task.count <= 0)
            Tasks.Remove(task);
        Touch();
    }
}
