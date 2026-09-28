using System;
using System.IO;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using LazyBearTechnology;
using TMPro;
using UnityEngine;

namespace CraftQueue;

// Vista previa en vivo con GK2 Mod Framework: mientras su ventana Mods muestra LA PÁGINA DE AJUSTES DE
// CRAFTING QUEUE, la ventana se achica lo justo para que el panel quepa a un lado y el panel se dibuja
// encima (así cada ajuste se ve al momento). Al salir de nuestra página, la escala vuelve a la que tenía
// el jugador. Las páginas de otros mods no se tocan.
// Todo sin referenciar el framework: su ventana se reconoce por el nombre de su tipo en la pila de
// ventanas del juego, nuestra página por su título ("Crafting Queue — …"), y la escala es una entrada
// normal de su .cfg ([UI] WindowScalePercent, 50–100) que el framework aplica en vivo al cambiarla.
// Si algo de eso cambia en otra versión del framework, simplemente no hay vista previa.
internal static class FrameworkPreview
{
    private const string FrameworkGuid = "ru.superman4eg.gk2.framework";
    private const string WindowType = "ModsMenuWindow";
    private const string TitleObject = "SettingsTitle";
    private const string OurName = "Crafting Queue";
    private const float BaseWindowWidth = 760f; // ancho base de la ventana del framework (en su escala 1)

    // La ventana Mods mientras muestra nuestra página (null si no).
    public static LazyWidgetBase Window { get; private set; }

    private static float nextCheck;
    private static int? original, applied;
    private static string backupFile;
    private static bool restoredBackup;
    // La ventana se corre hacia el lado contrario del panel (así se achica lo menos posible: el texto del
    // framework se ve borroso y chico con escalas bajas).
    private static RectTransform moved;
    private static float movedBy, fittedWidth;

    public static void Init(string dataFolder) => backupFile = Path.Combine(dataFolder, "framework_escala.txt");

    // Cada cuarto de segundo. Devuelve true si la vista previa empezó o terminó (para reacomodar el panel).
    public static bool Tick(float panelWidth, float canvasScale)
    {
        if (Time.unscaledTime < nextCheck)
            return false;
        nextCheck = Time.unscaledTime + 0.25f;
        try
        {
            RestoreBackupOnce();
            LazyWidgetBase w = GameWindows.FindOpen(WindowType);
            LazyWidgetBase ours = w != null && ShowsOurPage(w) ? w : null;
            if (ours == Window)
            {
                // Mismo estado; si cambió el ancho del panel (desde este mismo menú), se vuelve a acomodar.
                if (ours != null && Mathf.Abs(panelWidth - fittedWidth) > 0.5f)
                {
                    Fit(ours, panelWidth, canvasScale);
                    return true;
                }
                return false;
            }
            Window = ours;
            if (ours != null)
                Fit(ours, panelWidth, canvasScale);
            else
                Restore();
            Plugin.Log.LogInfo(ours != null
                ? "Ajustes de Crafting Queue abiertos en el menú Mods: vista previa del panel."
                : "Fin de la vista previa del panel en el menú Mods.");
            return true;
        }
        catch (Exception e)
        {
            Window = null;
            Plugin.Log.LogWarning("Vista previa con GK2 Mod Framework: " + e.Message);
            nextCheck = Time.unscaledTime + 10f;
            return false;
        }
    }

    private static bool ShowsOurPage(LazyWidgetBase w)
    {
        foreach (TMP_Text t in w.GetComponentsInChildren<TMP_Text>(false))
            if (t.name == TitleObject && t.isActiveAndEnabled)
                return t.text != null && t.text.StartsWith(OurName + " ", StringComparison.Ordinal);
        return false;
    }

    private static ConfigEntry<int> ScaleEntry()
    {
        if (!Chainloader.PluginInfos.TryGetValue(FrameworkGuid, out var info) || info.Instance == null)
            return null;
        return info.Instance.Config.TryGetEntry("UI", "WindowScalePercent", out ConfigEntry<int> e) ? e : null;
    }

    // Que el panel quepa junto a la ventana: primero se corre la ventana hacia el otro lado de la pantalla
    // y solo si aun así no cabe, se achica lo justo (partiendo siempre de la escala del jugador).
    private static void Fit(LazyWidgetBase w, float panelWidth, float canvasScale)
    {
        UnMove();
        fittedWidth = panelWidth;
        ConfigEntry<int> entry = ScaleEntry();
        float gameScale = LazyUI.ScaleFactor;
        if (entry == null || gameScale <= 0.001f || canvasScale <= 0f)
            return;
        int player = original ?? entry.Value;
        float margin = 6f * canvasScale;
        float panelPx = (panelWidth + ButtonBar.Slot + Plugin.HudSideOffset) * canvasScale + margin;
        float basePx = BaseWindowWidth * gameScale;
        // Ventana pegada al otro lado (con su margen) y el panel en el suyo.
        int fits = Mathf.Clamp(Mathf.FloorToInt((Screen.width - panelPx - 2f * margin) / basePx * 100f), 50, 100);
        int scale = Mathf.Min(player, fits);
        if (scale != entry.Value)
        {
            if (original == null)
            {
                original = entry.Value;
                try { File.WriteAllText(backupFile, original.Value.ToString()); } catch { }
            }
            applied = scale;
            entry.Value = scale; // el framework redimensiona su ventana al momento
        }
        // Centrada, ¿le queda al panel su espacio? Si no, se corre lo que falte (sin salirse de la pantalla).
        float windowPx = basePx * scale / 100f;
        float free = (Screen.width - windowPx) / 2f;
        if (free < panelPx)
        {
            float shift = Mathf.Min(panelPx - free, free - margin);
            if (shift > 0f && w.transform is RectTransform rt)
            {
                moved = rt;
                movedBy = Plugin.HudLeft ? shift : -shift;
                rt.position += new Vector3(movedBy, 0f, 0f);
            }
        }
    }

    private static void UnMove()
    {
        if (moved != null)
            moved.position -= new Vector3(movedBy, 0f, 0f);
        moved = null;
        movedBy = 0f;
    }

    // Vuelve la escala del jugador (si nadie la cambió mientras tanto) y la ventana a su lugar.
    private static void Restore()
    {
        UnMove();
        ConfigEntry<int> entry = ScaleEntry();
        if (entry != null && applied != null && original != null && entry.Value == applied.Value)
            entry.Value = original.Value;
        original = applied = null;
        try { if (File.Exists(backupFile)) File.Delete(backupFile); } catch { }
    }

    // Si el juego se cerró con la ventana achicada, al volver se deja como estaba.
    private static void RestoreBackupOnce()
    {
        if (restoredBackup || backupFile == null)
            return;
        restoredBackup = true;
        if (!File.Exists(backupFile))
            return;
        ConfigEntry<int> entry = ScaleEntry();
        if (entry != null && int.TryParse(File.ReadAllText(backupFile).Trim(), out int value))
        {
            entry.Value = Mathf.Clamp(value, 50, 100);
            Plugin.Log.LogInfo($"Escala de la ventana del framework restaurada a {entry.Value} %.");
        }
        File.Delete(backupFile);
    }
}
