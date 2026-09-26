using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LazyBearTechnology;

namespace CraftQueue;

// Solo con Diagnóstico → MedirRendimiento: al cargar una partida escribe "diagnostico_recetas.txt"
// en la carpeta de datos del mod, con las recetas cuya cantidad o probabilidad de salida depende de
// una fórmula (talentos, ventajas, mejoras…) y la lista de talentos, ventajas e inspiraciones.
// Sirve para saber qué cambia cuánto rinde una receta sin adivinar. No cambia nada del juego.
internal static class Diagnostics
{
    private static bool written;

    public static void DumpRecipesOnce(string folder)
    {
        if (written || !Perf.On || GameBalance.Me == null || folder == null)
            return;
        written = true;
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Recetas con salida variable (id receta | objeto | cantidad | mín | máx | probabilidad)");
            foreach (CraftDef craft in GameBalance.Me.craftDefs)
            {
                if (craft?.outputItems == null)
                    continue;
                IEnumerable<ChanceOutputItem> outs = (craft.outputItems.chanceOutputItems ?? new List<ChanceOutputItem>())
                    .Concat((craft.outputItems.groupChanceOutputItems ?? new List<GroupChanceOutputItem>())
                        .SelectMany(g => g.chanceItems ?? new List<ChanceOutputItem>()));
                foreach (ChanceOutputItem o in outs)
                {
                    if (o == null)
                        continue;
                    string count = Raw(o.count), min = Raw(o.minValue), max = Raw(o.maxValue), chance = Raw(o.chance);
                    if (!IsFormula(count) && !IsFormula(min) && !IsFormula(max) && !IsFormula(chance))
                        continue;
                    sb.AppendLine($"{craft.id} | {o.id} ({Name(o.id)}) | {count} | {min} | {max} | {chance}");
                }
            }
            sb.AppendLine();
            sb.AppendLine("# Talentos / perks (id | nombre | inicio | progreso | maestría)");
            foreach (PerkDef p in GameBalance.Me.perkDefs.Where(p => p != null))
                sb.AppendLine($"{p.id} | {Name(p.id)} | {p.craftStartTicks} | {p.craftTotalProgressTicksBonus} | {p.craftMasteryBonus}");
            sb.AppendLine();
            sb.AppendLine("# Ventajas (talentDefs) e inspiraciones (id | nombre)");
            foreach (TalentDef t in GameBalance.Me.talentDefs.Where(t => t != null))
                sb.AppendLine($"talento {t.id} | {Name(t.id)}");
            foreach (InspirationDef i in GameBalance.Me.inspirationDefs.Where(i => i != null))
                sb.AppendLine($"inspiración {i.id} | {Name(i.id)}");
            string path = Path.Combine(folder, "diagnostico_recetas.txt");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Plugin.Log.LogInfo("Diagnóstico de recetas escrito en " + path);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Diagnóstico de recetas: " + e.Message);
        }
    }

    private static string Raw(LazyExpression e)
    {
        try { return e?.GetRawExpressionString() ?? ""; }
        catch { return ""; }
    }

    // Una fórmula (no solo un número fijo).
    private static bool IsFormula(string s) => !string.IsNullOrEmpty(s) && s.Any(char.IsLetter);

    private static string Name(string id)
    {
        try { return LLBase.HasL(id) ? GameData.Plain(LLBase.L(id)) : ""; }
        catch { return ""; }
    }
}
