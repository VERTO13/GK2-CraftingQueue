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
// y al final "# end" (así se sabe que un archivo quedó completo).
// (No se usa JsonUtility de Unity: con las clases del mod guardaba el archivo sin las tareas.)
//
// El juego reusa los nombres de ranura: si borras Steam_1 y empiezas otra partida, la nueva
// también se llama Steam_1. Por eso la cola de una partida borrada, o la que ya hubiera con el
// nombre de una partida nueva, se aparta a "anteriores" (ver Plugin.SlotChanged) y no se hereda.
internal static class Queue
{
    private const string Header = "# Crafting Queue 1";
    private const string End = "# end";
    private const int KeepRetired = 20; // colas apartadas que se guardan en "anteriores"

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
                string[] lines = ReadSlot(slot);
                if (lines != null)
                    Load(lines);
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

    // El archivo de la partida. Un ".tmp" completo que quedó ahí es el último guardado que no
    // alcanzó a reemplazarlo (el juego se cerró justo en ese momento, u otro programa tenía abierto
    // el archivo): si es más nuevo, manda. Si falta el archivo, se recupera de la copia anterior.
    private static string[] ReadSlot(string slot)
    {
        string path = PathFor(slot), tmp = path + ".tmp", bak = path + ".bak";
        bool hasMain = File.Exists(path);
        if (File.Exists(tmp) && (!hasMain || File.GetLastWriteTimeUtc(tmp) > File.GetLastWriteTimeUtc(path))
            && ReadComplete(tmp) is string[] fresh)
        {
            Plugin.Log.LogWarning($"Cola de la partida {slot}: se tomó el último guardado sin terminar ({Path.GetFileName(tmp)}).");
            return fresh;
        }
        if (hasMain)
            return File.ReadAllLines(path);
        if (File.Exists(bak) && ReadComplete(bak) is string[] old)
        {
            Plugin.Log.LogWarning($"Cola de la partida {slot}: faltaba el archivo, se recuperó de {Path.GetFileName(bak)}.");
            return old;
        }
        return null;
    }

    // Las líneas de un archivo que terminó de escribirse (acaba en "# end"); null si quedó a medias.
    private static string[] ReadComplete(string file)
    {
        string[] lines = File.ReadAllLines(file);
        return lines.Length > 0 && lines[lines.Length - 1] == End ? lines : null;
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
            lines.Add(End);
            // Primero a un archivo temporal, que luego reemplaza al de la partida de una sola vez
            // (File.Replace): nunca queda la partida sin archivo, y el anterior se queda en ".bak".
            string path = PathFor(loadedSlot), tmp = path + ".tmp", bak = path + ".bak";
            File.WriteAllLines(tmp, lines, new System.Text.UTF8Encoding(false));
            if (!File.Exists(path))
                File.Move(tmp, path);
            else
            {
                try
                {
                    File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
                }
                catch (Exception e) when (!(e is UnauthorizedAccessException))
                {
                    // Otro programa tenía abierto el archivo (antivirus, un editor…): a la antigua,
                    // pero con la copia hecha antes; si se corta entre borrar y mover, ReadSlot
                    // recupera el ".tmp", que ya está completo.
                    File.Copy(path, bak, overwrite: true);
                    File.Delete(path);
                    File.Move(tmp, path);
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("No se pudo guardar la cola: " + e.Message);
        }
    }

    // La cola de esa ranura ya no es de ninguna partida (se borró, o su nombre va a ser el de una
    // partida nueva): se aparta a "anteriores/<ranura> <fecha>.txt" por si acaso, y con ella se
    // van su ".tmp" y su ".bak" (si no, ReadSlot la recuperaría para la partida nueva).
    public static void Retire(string slot, string why)
    {
        if (string.IsNullOrEmpty(slot) || folder == null)
            return;
        try
        {
            if (slot == loadedSlot)
                return; // la cola en uso nunca se aparta
            string path = PathFor(slot);
            string retired = null;
            if (File.Exists(path))
            {
                string old = Path.Combine(folder, "anteriores");
                Directory.CreateDirectory(old);
                retired = Path.Combine(old, $"{Path.GetFileNameWithoutExtension(path)} {DateTime.Now:yyyy-MM-dd HHmmss}.txt");
                File.Move(path, retired);
                File.SetLastWriteTime(retired, DateTime.Now); // para quedarse con las más recientes
                foreach (FileInfo f in new DirectoryInfo(old).GetFiles("*.txt").OrderByDescending(f => f.LastWriteTime).Skip(KeepRetired))
                    f.Delete();
            }
            foreach (string leftover in new[] { path + ".tmp", path + ".bak" })
                if (File.Exists(leftover))
                    File.Delete(leftover);
            if (retired != null)
                Plugin.Log.LogInfo($"Cola de {slot} apartada ({why}): {retired}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"No se pudo apartar la cola de {slot}: " + e.Message);
        }
    }

    // La partida cargada cambió de nombre al guardarse (las de la demo reciben uno nuevo): la cola
    // se va con ella.
    public static void Rename(string from, string to)
    {
        if (folder == null || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to)
            return;
        try
        {
            Retire(to, "el nombre pasa a otra partida"); // lo que hubiera con el nombre nuevo no es de esta
            string src = PathFor(from);
            if (File.Exists(src))
                File.Move(src, PathFor(to));
            foreach (string leftover in new[] { src + ".tmp", src + ".bak" })
                if (File.Exists(leftover))
                    File.Delete(leftover);
            if (loadedSlot == from)
                loadedSlot = to;
            Plugin.Log.LogInfo($"La partida {from} ahora se llama {to}: la cola la sigue.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"No se pudo mover la cola de {from} a {to}: " + e.Message);
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

    // Vaciar la cola entera (el bote de la barra del panel o el botón del menú Mods, los dos con
    // confirmación: ver ClearConfirm). La cola anterior se queda en el ".bak" de la partida.
    public static int Clear()
    {
        if (!HasSlot || Tasks.Count == 0)
            return 0;
        int n = Tasks.Count;
        Tasks.Clear();
        Pins.Clear();
        Touch();
        Plugin.Log.LogInfo($"Cola vaciada: {n} tareas.");
        return n;
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

// Vaciar la cola con confirmación: el primer toque la "arma" (el bote se pone rojo y avisa) y el
// segundo, antes de 4 segundos, la vacía. Lo usan el bote del panel y el botón del menú Mods.
internal static class ClearConfirm
{
    private const float Window = 4f;
    private static float armedUntil = -1f;

    public static bool Armed => Time.unscaledTime < armedUntil && Queue.HasSlot && Queue.Tasks.Count > 0;

    // Devuelve cuántas tareas quitó (0 si solo se armó o no había nada que quitar).
    public static int Press()
    {
        if (!Queue.HasSlot || Queue.Tasks.Count == 0)
        {
            armedUntil = -1f;
            return 0;
        }
        if (!Armed)
        {
            armedUntil = Time.unscaledTime + Window;
            return 0;
        }
        armedUntil = -1f;
        return Queue.Clear();
    }
}
