using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CraftQueue;

// Preferencias del jugador que se guardan entre sesiones: qué flechas dejó abiertas en el panel
// y qué receta eligió (◂ ▸) para cada material que se puede hacer de varias formas.
internal static class Prefs
{
    public const int MaxDepth = 4; // niveles de receta que se pueden desplegar

    // Rutas abiertas ("tarta/masa") o plegadas ("~tarta": las tareas empiezan abiertas).
    public static readonly HashSet<string> Expanded = new HashSet<string>();
    public static readonly Dictionary<string, int> Chosen = new Dictionary<string, int>();

    private static string expandedFile, chosenFile;

    public static void Load(string folder)
    {
        expandedFile = Path.Combine(folder, "abiertas.txt");
        chosenFile = Path.Combine(folder, "recetas.txt");
        try
        {
            if (File.Exists(expandedFile))
                Expanded.UnionWith(File.ReadAllLines(expandedFile).Where(l => l.Length > 0));
            if (File.Exists(chosenFile))
                foreach (string line in File.ReadAllLines(chosenFile))
                {
                    int eq = line.LastIndexOf('=');
                    if (eq > 0 && int.TryParse(line.Substring(eq + 1), out int i))
                        Chosen[line.Substring(0, eq)] = i;
                }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("No se pudieron leer las preferencias: " + e.Message);
        }
    }

    public static void Toggle(string path)
    {
        if (!Expanded.Remove(path))
            Expanded.Add(path);
        Write(expandedFile, Expanded.OrderBy(p => p));
    }

    // La elegida con ◂ ▸; si no, la que se usó al agregarla desde una mesa; si no, la mejor.
    public static int SelectedRecipe(string key, List<CraftDef> recipes, int need, int preferred = -1)
    {
        if (recipes.Count <= 1)
            return 0;
        if (Chosen.TryGetValue(key, out int i) && i >= 0 && i < recipes.Count)
            return i;
        if (preferred >= 0 && preferred < recipes.Count)
            return preferred;
        return BestRecipe(key, recipes, need);
    }

    // La que ya puedes completar; si ninguna, la que deja menos faltantes; en empate, la que da más.
    private static int BestRecipe(string key, List<CraftDef> recipes, int need)
    {
        int best = 0, bestMissing = int.MaxValue, bestOutput = 0;
        for (int r = 0; r < recipes.Count; r++)
        {
            int output = GameData.OutputCount(recipes[r], key);
            int times = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, need) / (float)Mathf.Max(1, output)));
            int missing = 0;
            foreach ((string k, int n) in GameData.Needs(recipes[r]))
                if (!GameData.IsFuel(k))
                    missing += Mathf.Max(0, n * times - GameData.Owned(k));
            if (missing < bestMissing || (missing == bestMissing && output > bestOutput))
            {
                best = r;
                bestMissing = missing;
                bestOutput = output;
            }
        }
        return best;
    }

    public static void CycleRecipe(string key, int current, int delta, int count)
    {
        if (count <= 1)
            return;
        Chosen[key] = ((current + delta) % count + count) % count;
        Write(chosenFile, Chosen.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value));
    }

    // "1/2 · Sierra circular ×4": en qué receta vas, en qué mesa se hace y cuánto da por vez.
    public static string RecipeCaption(int index, int count, int output, CraftDef craft) =>
        RecipePlace(index, count, craft) + Yield(output);

    // Solo "1/2 · Sierra circular" (el panel pone el "×4" aparte para que nunca se recorte).
    public static string RecipePlace(int index, int count, CraftDef craft) =>
        (count > 1 ? (index + 1) + "/" + count + " · " : "") + GameData.Station(craft);

    public static string Yield(int output) => output > 1 ? " ×" + output : "";

    private static void Write(string file, IEnumerable<string> lines)
    {
        if (file == null)
            return;
        try { File.WriteAllLines(file, lines); }
        catch (Exception e) { Plugin.Log.LogWarning("No se pudieron guardar las preferencias: " + e.Message); }
    }
}

// Dibujos pixelados de las flechas (▸ cerrado, ▾ abierto, ◂ anterior).
internal static class Arrows
{
    private static Sprite open, closed, left;
    private static readonly Color Fill = new Color(0.93f, 0.86f, 0.74f);
    private static readonly Color Outline = new Color(0.17f, 0.1f, 0.07f);

    public static Sprite Open() => open != null ? open : open = Make(new[]
    {
        "ooooooooo", "o#######o", ".o#####o.", "..o###o..", "...o#o...", "....o...."
    });

    public static Sprite Closed() => closed != null ? closed : closed = Make(new[]
    {
        "oo....", "o#o...", "o##o..", "o###o.", "o####o", "o###o.", "o##o..", "o#o...", "oo...."
    });

    public static Sprite Left() => left != null ? left : left = Make(new[]
    {
        "....oo", "...o#o", "..o##o", ".o###o", "o####o", ".o###o", "..o##o", "...o#o", "....oo"
    });

    private static Sprite Make(string[] rows)
    {
        int w = rows[0].Length, h = rows.Length;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, h - 1 - y, rows[y][x] == '#' ? Fill : rows[y][x] == 'o' ? Outline : Color.clear);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f);
    }
}
