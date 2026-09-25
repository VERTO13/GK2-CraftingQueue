using System.Collections.Generic;
using System.Linq;
using LazyBearTechnology;
using UnityEngine;

namespace CraftQueue;

// La cola tal como la dibuja el panel: objetos, recetas agregadas desde una mesa ("craft:<id>",
// que producen un objeto) y construcciones/obras con sus materiales.
internal static class QueueView
{
    internal class Entry
    {
        public QueueTask raw;
        public string id;
        public int need;
        public bool isBuild;
        public string title;
        public string zone;
        public string iconItem;
        public Sprite buildIcon;
        public List<(string id, int n)> parts = new List<(string, int)>();
    }

    public static List<Entry> Items()
    {
        List<Entry> list = new List<Entry>();
        foreach (QueueTask t in Queue.Tasks)
        {
            switch (t.kind)
            {
                case TaskKind.Item:
                    list.Add(new Entry { raw = t, id = t.id, need = t.count });
                    break;
                case TaskKind.Craft:
                    CraftDef craft = GameBalance.Me?.GetDataOrNull<CraftDef>(t.id);
                    if (craft == null)
                        continue;
                    string output = GameData.MainOutput(craft);
                    list.Add(new Entry
                    {
                        raw = t, id = "craft:" + t.id, need = t.count, isBuild = true,
                        title = output != null ? GameData.Name(output) : t.title, iconItem = output,
                        parts = GameData.Needs(craft).Select(p => (p.key, p.count)).ToList()
                    });
                    break;
                default:
                    list.Add(new Entry
                    {
                        raw = t, id = t.kind + ":" + t.id, need = t.count, isBuild = true,
                        title = Title(t), zone = ZoneName(t.zone),
                        buildIcon = string.IsNullOrEmpty(t.icon) ? null : GameData.IconById(t.icon),
                        parts = t.Parts.Select(p => (p.key, p.count)).ToList()
                    });
                    break;
            }
        }
        return list;
    }

    private static string Title(QueueTask t)
    {
        if (LLBase.HasL(t.id))
            return GameData.Plain(LLBase.L(t.id));
        return string.IsNullOrEmpty(t.title) ? t.id : t.title;
    }

    private static string ZoneName(string zone)
    {
        if (string.IsNullOrEmpty(zone))
            return "";
        return LLBase.HasL("wz_" + zone) ? GameData.Plain(LLBase.L("wz_" + zone)) : zone;
    }
}
