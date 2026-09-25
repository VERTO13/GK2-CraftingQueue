using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LazyBearTechnology;

namespace CraftQueue;

// Textos propios del complemento en el idioma que el jugador tiene elegido en el juego.
// Trae los 16 idiomas oficiales; para un idioma de un mod de idioma (o para corregir una
// traducción) basta con poner "lang/<código>.txt" junto a la DLL, con líneas "clave = texto".
// Al arrancar se escribe "lang/_plantilla_en.txt" con todas las claves para traducir.
internal static class Lang
{
    // Debe ir ANTES de BuiltIn: los inicializadores estáticos corren en orden y Make() la usa.
    private static readonly string[] Keys = { "recipe", "more_recipes", "right_click", "more_rows", "no_recipe", "yields", "queue", "empty" };

    private static readonly Dictionary<string, Dictionary<string, string>> BuiltIn = new Dictionary<string, Dictionary<string, string>>
    {
        ["en"] = Make("Recipe {0}", "+{0} more recipes", "right-click", "… and {0} more", "You don't know a recipe for this", "yields {0}", "Queue", "Ctrl + right-click something to add it"),
        ["es"] = Make("Receta {0}", "+{0} recetas más", "clic derecho", "… y {0} más", "No conoces una receta para esto", "rinde {0}", "Cola", "Ctrl + clic derecho sobre algo para agregarlo"),
        ["de"] = Make("Rezept {0}", "+{0} weitere Rezepte", "Rechtsklick", "… und {0} weitere", "Du kennst dafür kein Rezept", "ergibt {0}", "Warteschlange", "Strg + Rechtsklick auf etwas, um es hinzuzufügen"),
        ["fr"] = Make("Recette {0}", "+{0} autres recettes", "clic droit", "… et {0} de plus", "Tu ne connais aucune recette pour ça", "donne {0}", "File", "Ctrl + clic droit sur un objet pour l'ajouter"),
        ["pt-br"] = Make("Receita {0}", "+{0} receitas a mais", "clique direito", "… e mais {0}", "Você não conhece uma receita para isso", "rende {0}", "Fila", "Ctrl + clique direito em algo para adicionar"),
        ["ru"] = Make("Рецепт {0}", "ещё рецептов: {0}", "правый клик", "… и ещё {0}", "Вы не знаете рецепта для этого", "даёт {0}", "Очередь", "Ctrl + правый клик, чтобы добавить"),
        ["uk-ua"] = Make("Рецепт {0}", "ще рецептів: {0}", "правий клік", "… і ще {0}", "Ви не знаєте рецепта для цього", "дає {0}", "Черга", "Ctrl + правий клік, щоб додати"),
        ["it"] = Make("Ricetta {0}", "+{0} altre ricette", "clic destro", "… e altri {0}", "Non conosci una ricetta per questo", "produce {0}", "Coda", "Ctrl + clic destro su qualcosa per aggiungerlo"),
        ["pl"] = Make("Przepis {0}", "+{0} przepisów więcej", "prawy przycisk", "… i {0} więcej", "Nie znasz przepisu na to", "daje {0}", "Kolejka", "Ctrl + prawy przycisk, aby dodać"),
        ["tr"] = Make("Tarif {0}", "+{0} tarif daha", "sağ tık", "… ve {0} tane daha", "Bunun için bir tarif bilmiyorsun", "{0} adet verir", "Sıra", "Eklemek için Ctrl + sağ tık"),
        ["ja"] = Make("レシピ{0}", "他{0}件のレシピ", "右クリック", "…他{0}件", "レシピを知りません", "{0}個できる", "キュー", "Ctrl + 右クリックで追加"),
        ["zh_cn"] = Make("配方{0}", "还有{0}个配方", "右键", "…还有{0}项", "你还不知道它的配方", "产出{0}", "队列", "Ctrl + 右键 添加"),
        ["zh_cht"] = Make("配方{0}", "還有{0}個配方", "右鍵", "…還有{0}項", "你還不知道它的配方", "產出{0}", "佇列", "Ctrl + 右鍵 加入"),
        ["ko"] = Make("레시피 {0}", "레시피 {0}개 더", "우클릭", "… 외 {0}개", "알고 있는 레시피가 없습니다", "{0}개 생산", "대기열", "Ctrl + 우클릭으로 추가"),
        ["th"] = Make("สูตร {0}", "อีก {0} สูตร", "คลิกขวา", "… และอีก {0}", "คุณยังไม่รู้สูตรนี้", "ได้ {0}", "คิว", "Ctrl + คลิกขวาเพื่อเพิ่ม"),
        ["vn"] = Make("Công thức {0}", "+{0} công thức khác", "chuột phải", "… và {0} mục khác", "Bạn chưa biết công thức này", "ra {0}", "Hàng đợi", "Ctrl + chuột phải để thêm"),
    };

    private static Dictionary<string, string> Make(params string[] values)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i < Keys.Length; i++)
            d[Keys[i]] = values[i];
        return d;
    }

    private static string folder;
    private static readonly Dictionary<string, Dictionary<string, string>> fromFiles = new Dictionary<string, Dictionary<string, string>>();

    public static void Init(string pluginFolder)
    {
        folder = Path.Combine(pluginFolder, "lang");
        try
        {
            Directory.CreateDirectory(folder);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Copy this file as <language code>.txt (e.g. pt-pt.txt, the code of your language mod) and translate the right side.");
            sb.AppendLine("# {0} is replaced by a number. Official codes: " + string.Join(", ", BuiltIn.Keys));
            foreach (string k in Keys)
                sb.AppendLine(k + " = " + BuiltIn["en"][k]);
            File.WriteAllText(Path.Combine(folder, "_plantilla_en.txt"), sb.ToString(), new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("No se pudo preparar la carpeta de idiomas: " + e.Message);
        }
    }

    // Idioma actual del juego (incluye los de mods de idioma, que traen su propio código).
    private static string Current
    {
        get
        {
            try { return (LLBase.CurrentLang ?? "en").ToLowerInvariant(); }
            catch { return "en"; }
        }
    }

    private static Dictionary<string, string> FileTable(string code)
    {
        if (folder == null)
            return null;
        if (fromFiles.TryGetValue(code, out var cached))
            return cached;
        Dictionary<string, string> table = null;
        try
        {
            string path = Path.Combine(folder, code + ".txt");
            if (File.Exists(path))
            {
                table = new Dictionary<string, string>();
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (line.Length == 0 || line.StartsWith("#") || eq <= 0)
                        continue;
                    table[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                Plugin.Log.LogInfo($"Traducción cargada: lang/{code}.txt ({table.Count} textos)");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"No se pudo leer lang/{code}.txt: " + e.Message);
        }
        fromFiles[code] = table;
        return table;
    }

    // Busca en: archivo del idioma > traducción incluida > idioma base (p. ej. "pt" -> "pt-br") > inglés.
    public static string T(string key)
    {
        string code = Current;
        if (FileTable(code) is { } f && f.TryGetValue(key, out string s))
            return s;
        if (BuiltIn.TryGetValue(code, out var b) && b.TryGetValue(key, out s))
            return s;
        string baseCode = code.Split('-', '_')[0];
        foreach (var kv in BuiltIn)
            if (kv.Key.Split('-', '_')[0] == baseCode && kv.Value.TryGetValue(key, out s))
                return s;
        return BuiltIn["en"].TryGetValue(key, out s) ? s : key;
    }

    public static string T(string key, int n) => T(key).Replace("{0}", n.ToString());
}
