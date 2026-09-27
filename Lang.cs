using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    // Barra del panel, botones y menú Mods (0.5.0): mismo orden que MoreKeys.
    private static readonly string[] MoreKeys =
    {
        "clear", "clear_confirm", "clear_n", "queue_empty", "no_game", "total", "view_total", "view_tasks",
        "lock_tip", "pin_tip", "eye_tip", "pin_task_tip", "btn_minus", "btn_plus", "btn_up", "btn_down", "btn_remove"
    };

    private static readonly Dictionary<string, string[]> More = new Dictionary<string, string[]>
    {
        ["en"] = new[]
        {
            "Clear the queue", "Click again to clear the queue ({0})", "Clear the queue ({0})",
            "The queue is empty", "Load a game first", "Total",
            "Total: everything the queue needs", "Back to the tasks", "Unlock to move the panel (title) and resize it (corner)",
            "Mark on the chests where your queued materials are", "Always visible: don't move or fold the panel for chests and stations", "Mark this task's materials on the chests",
            "-1 · Shift: -10", "+1 · Shift: +10", "Move up · Shift: to the top",
            "Move down · Shift: to the bottom", "Remove from the queue"
        },
        ["es"] = new[]
        {
            "Vaciar la cola", "Clic otra vez para vaciar la cola ({0})", "Vaciar la cola ({0})",
            "La cola está vacía", "Carga una partida primero", "Total",
            "Total: todo lo que pide la cola", "Volver a las tareas", "Ábrelo para mover el panel (título) y cambiar su tamaño (esquina)",
            "Marca en los cofres dónde están los materiales de tu cola", "Siempre visible: no se recorre ni se pliega con cofres y mesas", "Marca en los cofres los materiales de esta tarea",
            "-1 · Shift: -10", "+1 · Shift: +10", "Subir · Shift: hasta arriba",
            "Bajar · Shift: hasta abajo", "Quitar de la cola"
        },
        ["de"] = new[]
        {
            "Warteschlange leeren", "Nochmal klicken zum Leeren ({0})", "Warteschlange leeren ({0})",
            "Die Warteschlange ist leer", "Lade zuerst einen Spielstand", "Gesamt",
            "Gesamt: alles, was die Warteschlange braucht", "Zurück zu den Aufgaben", "Entsperren, um das Panel zu verschieben (Titel) und die Größe zu ändern (Ecke)",
            "Zeigt an den Truhen, wo deine Materialien liegen", "Immer sichtbar: bei Truhen und Stationen nicht verschieben oder einklappen", "Zeigt die Materialien dieser Aufgabe an den Truhen",
            "-1 · Umschalt: -10", "+1 · Umschalt: +10", "Nach oben · Umschalt: ganz nach oben",
            "Nach unten · Umschalt: ganz nach unten", "Aus der Warteschlange entfernen"
        },
        ["fr"] = new[]
        {
            "Vider la file", "Clique encore pour vider la file ({0})", "Vider la file ({0})",
            "La file est vide", "Charge d'abord une partie", "Total",
            "Total : tout ce dont la file a besoin", "Retour aux tâches", "Déverrouille pour déplacer le panneau (titre) et le redimensionner (coin)",
            "Indique sur les coffres où sont les matériaux de ta file", "Toujours visible : ne pas déplacer ni replier le panneau devant les coffres et établis", "Indique sur les coffres les matériaux de cette tâche",
            "-1 · Maj : -10", "+1 · Maj : +10", "Monter · Maj : tout en haut",
            "Descendre · Maj : tout en bas", "Retirer de la file"
        },
        ["pt-br"] = new[]
        {
            "Esvaziar a fila", "Clique de novo para esvaziar a fila ({0})", "Esvaziar a fila ({0})",
            "A fila está vazia", "Carregue um jogo primeiro", "Total",
            "Total: tudo o que a fila precisa", "Voltar às tarefas", "Destrave para mover o painel (título) e mudar o tamanho (canto)",
            "Marca nos baús onde estão os materiais da sua fila", "Sempre visível: não mover nem recolher o painel com baús e bancadas", "Marca nos baús os materiais desta tarefa",
            "-1 · Shift: -10", "+1 · Shift: +10", "Subir · Shift: para o topo",
            "Descer · Shift: para o fim", "Remover da fila"
        },
        ["ru"] = new[]
        {
            "Очистить очередь", "Нажмите ещё раз, чтобы очистить очередь ({0})", "Очистить очередь ({0})",
            "Очередь пуста", "Сначала загрузите игру", "Итого",
            "Итого: всё, что нужно очереди", "Назад к задачам", "Откройте замок, чтобы двигать панель (заголовок) и менять размер (угол)",
            "Отмечает на сундуках, где лежат материалы очереди", "Всегда видна: не сдвигать и не сворачивать панель у сундуков и станков", "Отмечает на сундуках материалы этой задачи",
            "-1 · Shift: -10", "+1 · Shift: +10", "Выше · Shift: в самый верх",
            "Ниже · Shift: в самый низ", "Убрать из очереди"
        },
        ["uk-ua"] = new[]
        {
            "Очистити чергу", "Натисніть ще раз, щоб очистити чергу ({0})", "Очистити чергу ({0})",
            "Черга порожня", "Спершу завантажте гру", "Разом",
            "Разом: усе, що потрібно черзі", "Назад до завдань", "Відкрийте замок, щоб рухати панель (заголовок) і змінювати розмір (кут)",
            "Позначає на скринях, де лежать матеріали черги", "Завжди видима: не зсувати й не згортати панель біля скринь і верстатів", "Позначає на скринях матеріали цього завдання",
            "-1 · Shift: -10", "+1 · Shift: +10", "Вище · Shift: на самий верх",
            "Нижче · Shift: у самий низ", "Прибрати з черги"
        },
        ["it"] = new[]
        {
            "Svuota la coda", "Clicca di nuovo per svuotare la coda ({0})", "Svuota la coda ({0})",
            "La coda è vuota", "Carica prima una partita", "Totale",
            "Totale: tutto ciò che serve alla coda", "Torna alle attività", "Sblocca per spostare il pannello (titolo) e ridimensionarlo (angolo)",
            "Segna sui forzieri dove sono i materiali della coda", "Sempre visibile: non spostare né ripiegare il pannello con forzieri e banchi", "Segna sui forzieri i materiali di questa attività",
            "-1 · Maiusc: -10", "+1 · Maiusc: +10", "Su · Maiusc: in cima",
            "Giù · Maiusc: in fondo", "Rimuovi dalla coda"
        },
        ["pl"] = new[]
        {
            "Wyczyść kolejkę", "Kliknij jeszcze raz, aby wyczyścić kolejkę ({0})", "Wyczyść kolejkę ({0})",
            "Kolejka jest pusta", "Najpierw wczytaj grę", "Razem",
            "Razem: wszystko, czego potrzebuje kolejka", "Wróć do zadań", "Odblokuj, aby przesuwać panel (tytuł) i zmieniać rozmiar (róg)",
            "Zaznacza na skrzyniach, gdzie są materiały z kolejki", "Zawsze widoczny: nie przesuwaj ani nie zwijaj panelu przy skrzyniach i stołach", "Zaznacza na skrzyniach materiały tego zadania",
            "-1 · Shift: -10", "+1 · Shift: +10", "W górę · Shift: na samą górę",
            "W dół · Shift: na sam dół", "Usuń z kolejki"
        },
        ["tr"] = new[]
        {
            "Sırayı temizle", "Sırayı temizlemek için tekrar tıkla ({0})", "Sırayı temizle ({0})",
            "Sıra boş", "Önce bir oyun yükle", "Toplam",
            "Toplam: sıranın ihtiyacı olan her şey", "Görevlere dön", "Paneli taşımak (başlık) ve boyutlandırmak (köşe) için kilidi aç",
            "Sıradaki malzemelerin hangi sandıklarda olduğunu gösterir", "Her zaman görünür: sandık ve tezgâhlarda paneli kaydırma veya katlama", "Bu görevin malzemelerini sandıklarda gösterir",
            "-1 · Shift: -10", "+1 · Shift: +10", "Yukarı · Shift: en üste",
            "Aşağı · Shift: en alta", "Sıradan çıkar"
        },
        ["ja"] = new[]
        {
            "キューを空にする", "もう一度クリックでキューを空にする({0})", "キューを空にする({0})",
            "キューは空です", "先にゲームをロードしてください", "合計",
            "合計：キューに必要なものすべて", "タスクに戻る", "ロックを外すとパネルを移動(タイトル)・サイズ変更(角)できます",
            "キューの素材がどのチェストにあるか表示", "常に表示：チェストや作業台を開いてもパネルを動かさず畳まない", "このタスクの素材をチェストに表示",
            "-1 · Shift: -10", "+1 · Shift: +10", "上へ · Shift: 一番上へ",
            "下へ · Shift: 一番下へ", "キューから削除"
        },
        ["zh_cn"] = new[]
        {
            "清空队列", "再次点击以清空队列({0})", "清空队列({0})",
            "队列为空", "请先载入游戏", "总计",
            "总计：队列所需的全部材料", "返回任务", "解锁后可拖动标题移动面板，拖动角落调整大小",
            "在箱子上标出队列材料所在位置", "始终可见：打开箱子和工作台时不移动也不折叠面板", "在箱子上标出此任务的材料",
            "-1 · Shift: -10", "+1 · Shift: +10", "上移 · Shift: 移到最上",
            "下移 · Shift: 移到最下", "从队列移除"
        },
        ["zh_cht"] = new[]
        {
            "清空佇列", "再按一次以清空佇列({0})", "清空佇列({0})",
            "佇列是空的", "請先載入遊戲", "總計",
            "總計：佇列所需的全部材料", "返回任務", "解鎖後可拖曳標題移動面板，拖曳角落調整大小",
            "在箱子上標出佇列材料所在位置", "永遠可見：開啟箱子和工作台時不移動也不收合面板", "在箱子上標出此任務的材料",
            "-1 · Shift: -10", "+1 · Shift: +10", "上移 · Shift: 移到最上",
            "下移 · Shift: 移到最下", "從佇列移除"
        },
        ["ko"] = new[]
        {
            "대기열 비우기", "한 번 더 클릭하면 대기열을 비웁니다 ({0})", "대기열 비우기 ({0})",
            "대기열이 비어 있습니다", "먼저 게임을 불러오세요", "합계",
            "합계: 대기열에 필요한 모든 것", "작업으로 돌아가기", "잠금을 풀면 패널을 이동(제목)하고 크기를 조절(모서리)할 수 있습니다",
            "대기열 재료가 있는 상자를 표시합니다", "항상 표시: 상자나 작업대를 열어도 패널을 옮기거나 접지 않습니다", "이 작업의 재료를 상자에 표시합니다",
            "-1 · Shift: -10", "+1 · Shift: +10", "위로 · Shift: 맨 위로",
            "아래로 · Shift: 맨 아래로", "대기열에서 제거"
        },
        ["th"] = new[]
        {
            "ล้างคิว", "คลิกอีกครั้งเพื่อล้างคิว ({0})", "ล้างคิว ({0})",
            "คิวว่างอยู่", "โหลดเกมก่อน", "รวม",
            "รวม: ทุกอย่างที่คิวต้องการ", "กลับไปที่งาน", "ปลดล็อกเพื่อย้ายแผง (หัวข้อ) และปรับขนาด (มุม)",
            "แสดงบนหีบว่าวัตถุดิบในคิวอยู่ที่ไหน", "แสดงตลอด: ไม่ย้ายหรือพับแผงเมื่อเปิดหีบและโต๊ะทำงาน", "แสดงวัตถุดิบของงานนี้บนหีบ",
            "-1 · Shift: -10", "+1 · Shift: +10", "เลื่อนขึ้น · Shift: ไปบนสุด",
            "เลื่อนลง · Shift: ไปล่างสุด", "นำออกจากคิว"
        },
        ["vn"] = new[]
        {
            "Xóa hàng đợi", "Nhấp lần nữa để xóa hàng đợi ({0})", "Xóa hàng đợi ({0})",
            "Hàng đợi trống", "Hãy tải một ván chơi trước", "Tổng",
            "Tổng: mọi thứ hàng đợi cần", "Quay lại nhiệm vụ", "Mở khóa để di chuyển bảng (tiêu đề) và đổi kích thước (góc)",
            "Đánh dấu trên rương nơi có nguyên liệu trong hàng đợi", "Luôn hiển thị: không dời hay thu gọn bảng khi mở rương và bàn chế tạo", "Đánh dấu trên rương nguyên liệu của nhiệm vụ này",
            "-1 · Shift: -10", "+1 · Shift: +10", "Lên · Shift: lên đầu",
            "Xuống · Shift: xuống cuối", "Xóa khỏi hàng đợi"
        },
    };

    // Se juntan con los de arriba al cargar la clase (después de los inicializadores).
    static Lang()
    {
        foreach (KeyValuePair<string, string[]> kv in More)
            if (BuiltIn.TryGetValue(kv.Key, out Dictionary<string, string> table))
                for (int i = 0; i < MoreKeys.Length && i < kv.Value.Length; i++)
                    table[MoreKeys[i]] = kv.Value[i];
    }

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
            foreach (string k in Keys.Concat(MoreKeys))
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
