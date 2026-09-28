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
        "lock_tip", "pin_tip", "eye_tip", "pin_task_tip", "btn_minus", "btn_plus", "btn_up", "btn_down", "btn_remove",
        "count_bag_tip", "count_chests_tip", "bar_show", "bar_hide", "bar_turn",
        // opciones del menú Mods (Plugin.BindChoices)
        "opt_by_task", "opt_detailed", "opt_compact", "opt_count_both", "opt_count_carried", "opt_count_chests", "opt_marks_all",
        "opt_marks_pinned", "opt_with_pin", "opt_no_pin", "opt_hint", "opt_hide", "opt_win_work", "opt_win_none",
        "opt_win_all", "opt_aside", "opt_over", "opt_out", "opt_tucked", "opt_side", "opt_top",
        "opt_movable", "opt_fixed", "opt_right", "opt_left",
        // tamaño desde el panel (Ctrl / Shift + rueda) y el globo del agarre
        "size_icons", "size_text", "grip_tip"
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
            "Move down · Shift: to the bottom", "Remove from the queue",
            "Count what you carry", "Count the chests of this zone",
            "Show the buttons", "Hide the buttons", "Right-click: vertical or horizontal",
            "By task", "Detailed", "Compact", "What you carry and the chests", "Only what you carry",
            "Only the chests", "Whole queue", "Only pinned tasks", "With pin", "Without pin",
            "Shows how to add", "Hidden", "With chests and stations", "With none", "With all",
            "Moves aside", "Stays on top", "Out", "Tucked away", "Down the side",
            "Across the top", "Movable", "Fixed", "Right", "Left",
            "Icons {0}", "Text {0}", "Drag to resize · Ctrl + wheel: icons · Shift + wheel: text"
        },
        ["es"] = new[]
        {
            "Vaciar la cola", "Clic otra vez para vaciar la cola ({0})", "Vaciar la cola ({0})",
            "La cola está vacía", "Carga una partida primero", "Total",
            "Total: todo lo que pide la cola", "Volver a las tareas", "Ábrelo para mover el panel (título) y cambiar su tamaño (esquina)",
            "Marca en los cofres dónde están los materiales de tu cola", "Siempre visible: no se recorre ni se pliega con cofres y mesas", "Marca en los cofres los materiales de esta tarea",
            "-1 · Shift: -10", "+1 · Shift: +10", "Subir · Shift: hasta arriba",
            "Bajar · Shift: hasta abajo", "Quitar de la cola",
            "Contar lo que llevas encima", "Contar los cofres de esta zona",
            "Mostrar los botones", "Ocultar los botones", "Clic derecho: vertical u horizontal",
            "Por tarea", "Detalladas", "Compactas", "Lo que llevas y los cofres", "Solo lo que llevas",
            "Solo los cofres", "De toda la cola", "Solo de tareas con pin", "Con pin", "Sin pin",
            "Dice cómo agregar", "Se oculta", "Con cofres y mesas", "Con ninguna", "Con todas",
            "Se hace a un lado", "Se queda encima", "Afuera", "Guardada", "Por el costado",
            "Por encima del panel", "Se puede mover", "Fijo", "Derecha", "Izquierda",
            "Íconos {0}", "Letra {0}", "Arrastra para cambiar el tamaño · Ctrl + rueda: íconos · Shift + rueda: letra"
        },
        ["de"] = new[]
        {
            "Warteschlange leeren", "Nochmal klicken zum Leeren ({0})", "Warteschlange leeren ({0})",
            "Die Warteschlange ist leer", "Lade zuerst einen Spielstand", "Gesamt",
            "Gesamt: alles, was die Warteschlange braucht", "Zurück zu den Aufgaben", "Entsperren, um das Panel zu verschieben (Titel) und die Größe zu ändern (Ecke)",
            "Zeigt an den Truhen, wo deine Materialien liegen", "Immer sichtbar: bei Truhen und Stationen nicht verschieben oder einklappen", "Zeigt die Materialien dieser Aufgabe an den Truhen",
            "-1 · Umschalt: -10", "+1 · Umschalt: +10", "Nach oben · Umschalt: ganz nach oben",
            "Nach unten · Umschalt: ganz nach unten", "Aus der Warteschlange entfernen",
            "Mitgeführtes zählen", "Truhen dieser Zone zählen",
            "Knöpfe zeigen", "Knöpfe ausblenden", "Rechtsklick: senkrecht oder waagrecht",
            "Nach Aufgabe", "Ausführlich", "Kompakt", "Mitgeführtes und Truhen", "Nur Mitgeführtes",
            "Nur Truhen", "Ganze Warteschlange", "Nur Aufgaben mit Pin", "Mit Pin", "Ohne Pin",
            "Zeigt, wie man hinzufügt", "Ausgeblendet", "Bei Truhen und Stationen", "Bei keinem", "Bei allen",
            "Weicht aus", "Bleibt darüber", "Ausgeklappt", "Eingeklappt", "Seitlich",
            "Oberhalb", "Beweglich", "Fest", "Rechts", "Links",
            "Symbole {0}", "Schrift {0}", "Ziehen: Größe · Strg + Mausrad: Symbole · Umschalt + Mausrad: Schrift"
        },
        ["fr"] = new[]
        {
            "Vider la file", "Clique encore pour vider la file ({0})", "Vider la file ({0})",
            "La file est vide", "Charge d'abord une partie", "Total",
            "Total : tout ce dont la file a besoin", "Retour aux tâches", "Déverrouille pour déplacer le panneau (titre) et le redimensionner (coin)",
            "Indique sur les coffres où sont les matériaux de ta file", "Toujours visible : ne pas déplacer ni replier le panneau devant les coffres et établis", "Indique sur les coffres les matériaux de cette tâche",
            "-1 · Maj : -10", "+1 · Maj : +10", "Monter · Maj : tout en haut",
            "Descendre · Maj : tout en bas", "Retirer de la file",
            "Compter ce que tu portes", "Compter les coffres de cette zone",
            "Afficher les boutons", "Masquer les boutons", "Clic droit : vertical ou horizontal",
            "Par tâche", "Détaillées", "Compactes", "Ce que tu portes et les coffres", "Seulement ce que tu portes",
            "Seulement les coffres", "Toute la file", "Seulement les tâches épinglées", "Avec épingle", "Sans épingle",
            "Indique comment ajouter", "Masqué", "Avec coffres et établis", "Avec aucune", "Avec toutes",
            "Se décale", "Reste au-dessus", "Sortie", "Rangée", "Sur le côté",
            "Au-dessus du panneau", "Déplaçable", "Fixe", "Droite", "Gauche",
            "Icônes {0}", "Texte {0}", "Glisser : taille · Ctrl + molette : icônes · Maj + molette : texte"
        },
        ["pt-br"] = new[]
        {
            "Esvaziar a fila", "Clique de novo para esvaziar a fila ({0})", "Esvaziar a fila ({0})",
            "A fila está vazia", "Carregue um jogo primeiro", "Total",
            "Total: tudo o que a fila precisa", "Voltar às tarefas", "Destrave para mover o painel (título) e mudar o tamanho (canto)",
            "Marca nos baús onde estão os materiais da sua fila", "Sempre visível: não mover nem recolher o painel com baús e bancadas", "Marca nos baús os materiais desta tarefa",
            "-1 · Shift: -10", "+1 · Shift: +10", "Subir · Shift: para o topo",
            "Descer · Shift: para o fim", "Remover da fila",
            "Contar o que você carrega", "Contar os baús desta zona",
            "Mostrar os botões", "Ocultar os botões", "Clique direito: vertical ou horizontal",
            "Por tarefa", "Detalhadas", "Compactas", "O que você carrega e os baús", "Só o que você carrega",
            "Só os baús", "Fila inteira", "Só tarefas com pin", "Com pin", "Sem pin",
            "Mostra como adicionar", "Fica oculto", "Com baús e bancadas", "Com nenhuma", "Com todas",
            "Sai da frente", "Fica por cima", "Aberta", "Guardada", "Pela lateral",
            "Por cima do painel", "Móvel", "Fixo", "Direita", "Esquerda",
            "Ícones {0}", "Texto {0}", "Arraste: tamanho · Ctrl + roda: ícones · Shift + roda: texto"
        },
        ["ru"] = new[]
        {
            "Очистить очередь", "Нажмите ещё раз, чтобы очистить очередь ({0})", "Очистить очередь ({0})",
            "Очередь пуста", "Сначала загрузите игру", "Итого",
            "Итого: всё, что нужно очереди", "Назад к задачам", "Откройте замок, чтобы двигать панель (заголовок) и менять размер (угол)",
            "Отмечает на сундуках, где лежат материалы очереди", "Всегда видна: не сдвигать и не сворачивать панель у сундуков и станков", "Отмечает на сундуках материалы этой задачи",
            "-1 · Shift: -10", "+1 · Shift: +10", "Выше · Shift: в самый верх",
            "Ниже · Shift: в самый низ", "Убрать из очереди",
            "Считать то, что при себе", "Считать сундуки этой зоны",
            "Показать кнопки", "Скрыть кнопки", "Правый клик: вертикально или горизонтально",
            "По задачам", "Подробные", "Краткие", "При себе и сундуки", "Только при себе",
            "Только сундуки", "Вся очередь", "Только задачи с меткой", "С меткой", "Без метки",
            "Подсказывает, как добавить", "Скрыта", "С сундуками и станками", "Ни с одним", "Со всеми",
            "Отодвигается", "Остаётся сверху", "Открыта", "Убрана", "Сбоку",
            "Над панелью", "Можно двигать", "Закреплена", "Справа", "Слева",
            "Значки {0}", "Текст {0}", "Тяните: размер · Ctrl + колесо: значки · Shift + колесо: текст"
        },
        ["uk-ua"] = new[]
        {
            "Очистити чергу", "Натисніть ще раз, щоб очистити чергу ({0})", "Очистити чергу ({0})",
            "Черга порожня", "Спершу завантажте гру", "Разом",
            "Разом: усе, що потрібно черзі", "Назад до завдань", "Відкрийте замок, щоб рухати панель (заголовок) і змінювати розмір (кут)",
            "Позначає на скринях, де лежать матеріали черги", "Завжди видима: не зсувати й не згортати панель біля скринь і верстатів", "Позначає на скринях матеріали цього завдання",
            "-1 · Shift: -10", "+1 · Shift: +10", "Вище · Shift: на самий верх",
            "Нижче · Shift: у самий низ", "Прибрати з черги",
            "Рахувати те, що при собі", "Рахувати скрині цієї зони",
            "Показати кнопки", "Сховати кнопки", "Правий клік: вертикально чи горизонтально",
            "За завданнями", "Докладні", "Стислі", "При собі та скрині", "Лише при собі",
            "Лише скрині", "Уся черга", "Лише завдання з міткою", "З міткою", "Без мітки",
            "Підказує, як додати", "Прихована", "Зі скринями й верстатами", "З жодним", "З усіма",
            "Відсувається", "Лишається зверху", "Відкрита", "Прибрана", "Збоку",
            "Над панеллю", "Можна рухати", "Закріплена", "Праворуч", "Ліворуч",
            "Значки {0}", "Текст {0}", "Тягніть: розмір · Ctrl + коліщатко: значки · Shift + коліщатко: текст"
        },
        ["it"] = new[]
        {
            "Svuota la coda", "Clicca di nuovo per svuotare la coda ({0})", "Svuota la coda ({0})",
            "La coda è vuota", "Carica prima una partita", "Totale",
            "Totale: tutto ciò che serve alla coda", "Torna alle attività", "Sblocca per spostare il pannello (titolo) e ridimensionarlo (angolo)",
            "Segna sui forzieri dove sono i materiali della coda", "Sempre visibile: non spostare né ripiegare il pannello con forzieri e banchi", "Segna sui forzieri i materiali di questa attività",
            "-1 · Maiusc: -10", "+1 · Maiusc: +10", "Su · Maiusc: in cima",
            "Giù · Maiusc: in fondo", "Rimuovi dalla coda",
            "Conta ciò che porti con te", "Conta i forzieri di questa zona",
            "Mostra i pulsanti", "Nascondi i pulsanti", "Clic destro: verticale o orizzontale",
            "Per attività", "Dettagliate", "Compatte", "Ciò che porti e i forzieri", "Solo ciò che porti",
            "Solo i forzieri", "Tutta la coda", "Solo attività con puntina", "Con puntina", "Senza puntina",
            "Spiega come aggiungere", "Nascosto", "Con forzieri e banchi", "Con nessuna", "Con tutte",
            "Si sposta", "Resta sopra", "Aperta", "Riposta", "Sul lato",
            "Sopra il pannello", "Spostabile", "Fisso", "Destra", "Sinistra",
            "Icone {0}", "Testo {0}", "Trascina: dimensione · Ctrl + rotellina: icone · Maiusc + rotellina: testo"
        },
        ["pl"] = new[]
        {
            "Wyczyść kolejkę", "Kliknij jeszcze raz, aby wyczyścić kolejkę ({0})", "Wyczyść kolejkę ({0})",
            "Kolejka jest pusta", "Najpierw wczytaj grę", "Razem",
            "Razem: wszystko, czego potrzebuje kolejka", "Wróć do zadań", "Odblokuj, aby przesuwać panel (tytuł) i zmieniać rozmiar (róg)",
            "Zaznacza na skrzyniach, gdzie są materiały z kolejki", "Zawsze widoczny: nie przesuwaj ani nie zwijaj panelu przy skrzyniach i stołach", "Zaznacza na skrzyniach materiały tego zadania",
            "-1 · Shift: -10", "+1 · Shift: +10", "W górę · Shift: na samą górę",
            "W dół · Shift: na sam dół", "Usuń z kolejki",
            "Licz to, co nosisz", "Licz skrzynie w tej strefie",
            "Pokaż przyciski", "Ukryj przyciski", "Prawy przycisk: pionowo lub poziomo",
            "Według zadań", "Szczegółowe", "Zwięzłe", "Przy sobie i skrzynie", "Tylko przy sobie",
            "Tylko skrzynie", "Cała kolejka", "Tylko zadania z pinezką", "Z pinezką", "Bez pinezki",
            "Pokazuje, jak dodać", "Ukryty", "Przy skrzyniach i stołach", "Przy żadnym", "Przy wszystkich",
            "Odsuwa się", "Zostaje na wierzchu", "Wysunięty", "Schowany", "Z boku",
            "Nad panelem", "Ruchomy", "Przypięty", "Z prawej", "Z lewej",
            "Ikony {0}", "Tekst {0}", "Przeciągnij: rozmiar · Ctrl + kółko: ikony · Shift + kółko: tekst"
        },
        ["tr"] = new[]
        {
            "Sırayı temizle", "Sırayı temizlemek için tekrar tıkla ({0})", "Sırayı temizle ({0})",
            "Sıra boş", "Önce bir oyun yükle", "Toplam",
            "Toplam: sıranın ihtiyacı olan her şey", "Görevlere dön", "Paneli taşımak (başlık) ve boyutlandırmak (köşe) için kilidi aç",
            "Sıradaki malzemelerin hangi sandıklarda olduğunu gösterir", "Her zaman görünür: sandık ve tezgâhlarda paneli kaydırma veya katlama", "Bu görevin malzemelerini sandıklarda gösterir",
            "-1 · Shift: -10", "+1 · Shift: +10", "Yukarı · Shift: en üste",
            "Aşağı · Shift: en alta", "Sıradan çıkar",
            "Üzerindekileri say", "Bu bölgedeki sandıkları say",
            "Düğmeleri göster", "Düğmeleri gizle", "Sağ tık: dikey veya yatay",
            "Göreve göre", "Ayrıntılı", "Kısa", "Üzerindekiler ve sandıklar", "Sadece üzerindekiler",
            "Sadece sandıklar", "Tüm sıra", "Sadece iğneli görevler", "İğneli", "İğnesiz",
            "Nasıl ekleneceğini gösterir", "Gizlenir", "Sandık ve tezgâhlarla", "Hiçbiriyle", "Hepsiyle",
            "Kenara çekilir", "Üstte kalır", "Açık", "Toplu", "Yandan",
            "Panelin üstünden", "Taşınabilir", "Sabit", "Sağ", "Sol",
            "Simgeler {0}", "Yazı {0}", "Sürükle: boyut · Ctrl + tekerlek: simgeler · Shift + tekerlek: yazı"
        },
        ["ja"] = new[]
        {
            "キューを空にする", "もう一度クリックでキューを空にする({0})", "キューを空にする({0})",
            "キューは空です", "先にゲームをロードしてください", "合計",
            "合計：キューに必要なものすべて", "タスクに戻る", "ロックを外すとパネルを移動(タイトル)・サイズ変更(角)できます",
            "キューの素材がどのチェストにあるか表示", "常に表示：チェストや作業台を開いてもパネルを動かさず畳まない", "このタスクの素材をチェストに表示",
            "-1 · Shift: -10", "+1 · Shift: +10", "上へ · Shift: 一番上へ",
            "下へ · Shift: 一番下へ", "キューから削除",
            "所持品を数える", "このエリアのチェストを数える",
            "ボタンを表示", "ボタンを隠す", "右クリック：縦／横",
            "タスクごと", "詳細", "簡潔", "所持品とチェスト", "所持品のみ",
            "チェストのみ", "キュー全体", "ピン付きのタスクのみ", "ピンあり", "ピンなし",
            "追加方法を表示", "隠す", "チェストと作業台で表示", "表示しない", "常に表示",
            "横によける", "上に表示したまま", "出す", "しまう", "横に",
            "パネルの上に", "移動できる", "固定", "右", "左",
            "アイコン {0}", "文字 {0}", "ドラッグ: サイズ · Ctrl + ホイール: アイコン · Shift + ホイール: 文字"
        },
        ["zh_cn"] = new[]
        {
            "清空队列", "再次点击以清空队列({0})", "清空队列({0})",
            "队列为空", "请先载入游戏", "总计",
            "总计：队列所需的全部材料", "返回任务", "解锁后可拖动标题移动面板，拖动角落调整大小",
            "在箱子上标出队列材料所在位置", "始终可见：打开箱子和工作台时不移动也不折叠面板", "在箱子上标出此任务的材料",
            "-1 · Shift: -10", "+1 · Shift: +10", "上移 · Shift: 移到最上",
            "下移 · Shift: 移到最下", "从队列移除",
            "计算随身携带的物品", "计算此区域的箱子",
            "显示按钮", "隐藏按钮", "右键：竖排／横排",
            "按任务", "详细", "简洁", "随身物品和箱子", "仅随身物品",
            "仅箱子", "整个队列", "仅带图钉的任务", "带图钉", "不带图钉",
            "提示如何添加", "隐藏", "箱子和工作台时显示", "都不显示", "都显示",
            "让开", "留在上面", "展开", "收起", "侧边",
            "面板上方", "可移动", "固定", "右侧", "左侧",
            "图标 {0}", "文字 {0}", "拖动：大小 · Ctrl + 滚轮：图标 · Shift + 滚轮：文字"
        },
        ["zh_cht"] = new[]
        {
            "清空佇列", "再按一次以清空佇列({0})", "清空佇列({0})",
            "佇列是空的", "請先載入遊戲", "總計",
            "總計：佇列所需的全部材料", "返回任務", "解鎖後可拖曳標題移動面板，拖曳角落調整大小",
            "在箱子上標出佇列材料所在位置", "永遠可見：開啟箱子和工作台時不移動也不收合面板", "在箱子上標出此任務的材料",
            "-1 · Shift: -10", "+1 · Shift: +10", "上移 · Shift: 移到最上",
            "下移 · Shift: 移到最下", "從佇列移除",
            "計算隨身攜帶的物品", "計算此區域的箱子",
            "顯示按鈕", "隱藏按鈕", "右鍵：直排／橫排",
            "按任務", "詳細", "簡潔", "隨身物品和箱子", "僅隨身物品",
            "僅箱子", "整個佇列", "僅有圖釘的任務", "有圖釘", "沒有圖釘",
            "提示如何加入", "隱藏", "箱子和工作台時顯示", "都不顯示", "都顯示",
            "讓開", "留在上面", "展開", "收起", "側邊",
            "面板上方", "可移動", "固定", "右側", "左側",
            "圖示 {0}", "文字 {0}", "拖曳：大小 · Ctrl + 滾輪：圖示 · Shift + 滾輪：文字"
        },
        ["ko"] = new[]
        {
            "대기열 비우기", "한 번 더 클릭하면 대기열을 비웁니다 ({0})", "대기열 비우기 ({0})",
            "대기열이 비어 있습니다", "먼저 게임을 불러오세요", "합계",
            "합계: 대기열에 필요한 모든 것", "작업으로 돌아가기", "잠금을 풀면 패널을 이동(제목)하고 크기를 조절(모서리)할 수 있습니다",
            "대기열 재료가 있는 상자를 표시합니다", "항상 표시: 상자나 작업대를 열어도 패널을 옮기거나 접지 않습니다", "이 작업의 재료를 상자에 표시합니다",
            "-1 · Shift: -10", "+1 · Shift: +10", "위로 · Shift: 맨 위로",
            "아래로 · Shift: 맨 아래로", "대기열에서 제거",
            "소지품 세기", "이 구역의 상자 세기",
            "버튼 보이기", "버튼 숨기기", "우클릭: 세로 / 가로",
            "작업별", "자세히", "간단히", "소지품과 상자", "소지품만",
            "상자만", "대기열 전체", "핀 고정한 작업만", "핀 고정", "핀 없음",
            "추가 방법 안내", "숨김", "상자와 작업대에서 표시", "표시 안 함", "항상 표시",
            "옆으로 비킴", "위에 그대로", "펼침", "접음", "옆으로",
            "패널 위로", "이동 가능", "고정", "오른쪽", "왼쪽",
            "아이콘 {0}", "글자 {0}", "드래그: 크기 · Ctrl + 휠: 아이콘 · Shift + 휠: 글자"
        },
        ["th"] = new[]
        {
            "ล้างคิว", "คลิกอีกครั้งเพื่อล้างคิว ({0})", "ล้างคิว ({0})",
            "คิวว่างอยู่", "โหลดเกมก่อน", "รวม",
            "รวม: ทุกอย่างที่คิวต้องการ", "กลับไปที่งาน", "ปลดล็อกเพื่อย้ายแผง (หัวข้อ) และปรับขนาด (มุม)",
            "แสดงบนหีบว่าวัตถุดิบในคิวอยู่ที่ไหน", "แสดงตลอด: ไม่ย้ายหรือพับแผงเมื่อเปิดหีบและโต๊ะทำงาน", "แสดงวัตถุดิบของงานนี้บนหีบ",
            "-1 · Shift: -10", "+1 · Shift: +10", "เลื่อนขึ้น · Shift: ไปบนสุด",
            "เลื่อนลง · Shift: ไปล่างสุด", "นำออกจากคิว",
            "นับของที่พกติดตัว", "นับหีบในโซนนี้",
            "แสดงปุ่ม", "ซ่อนปุ่ม", "คลิกขวา: แนวตั้งหรือแนวนอน",
            "ตามงาน", "ละเอียด", "กะทัดรัด", "ของที่พกและหีบ", "เฉพาะของที่พก",
            "เฉพาะหีบ", "ทั้งคิว", "เฉพาะงานที่ปักหมุด", "ปักหมุด", "ไม่ปักหมุด",
            "บอกวิธีเพิ่ม", "ซ่อน", "กับหีบและโต๊ะทำงาน", "ไม่แสดงเลย", "แสดงทั้งหมด",
            "หลบไปด้านข้าง", "อยู่ด้านบน", "กางออก", "เก็บไว้", "ด้านข้าง",
            "เหนือแผง", "ย้ายได้", "ตรึงไว้", "ขวา", "ซ้าย",
            "ไอคอน {0}", "ตัวอักษร {0}", "ลาก: ขนาด · Ctrl + ล้อเมาส์: ไอคอน · Shift + ล้อเมาส์: ตัวอักษร"
        },
        ["vn"] = new[]
        {
            "Xóa hàng đợi", "Nhấp lần nữa để xóa hàng đợi ({0})", "Xóa hàng đợi ({0})",
            "Hàng đợi trống", "Hãy tải một ván chơi trước", "Tổng",
            "Tổng: mọi thứ hàng đợi cần", "Quay lại nhiệm vụ", "Mở khóa để di chuyển bảng (tiêu đề) và đổi kích thước (góc)",
            "Đánh dấu trên rương nơi có nguyên liệu trong hàng đợi", "Luôn hiển thị: không dời hay thu gọn bảng khi mở rương và bàn chế tạo", "Đánh dấu trên rương nguyên liệu của nhiệm vụ này",
            "-1 · Shift: -10", "+1 · Shift: +10", "Lên · Shift: lên đầu",
            "Xuống · Shift: xuống cuối", "Xóa khỏi hàng đợi",
            "Đếm đồ mang theo", "Đếm rương trong khu vực này",
            "Hiện các nút", "Ẩn các nút", "Chuột phải: dọc hoặc ngang",
            "Theo nhiệm vụ", "Chi tiết", "Gọn", "Đồ mang theo và rương", "Chỉ đồ mang theo",
            "Chỉ rương", "Cả hàng đợi", "Chỉ nhiệm vụ có ghim", "Có ghim", "Không ghim",
            "Chỉ cách thêm", "Ẩn", "Với rương và bàn chế tạo", "Không với cửa sổ nào", "Với mọi cửa sổ",
            "Tránh sang bên", "Nằm trên", "Mở ra", "Cất đi", "Bên cạnh",
            "Phía trên bảng", "Di chuyển được", "Cố định", "Phải", "Trái",
            "Biểu tượng {0}", "Chữ {0}", "Kéo: kích thước · Ctrl + con lăn: biểu tượng · Shift + con lăn: chữ"
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
