using System;
using System.Linq;
using LazyBearTechnology;
using Rewired;
using UnityEngine;

namespace CraftQueue;

// Control (gamepad), todo con R3 (presionar el stick derecho):
//  En una ventana (cofre, mesa, construir, obras del pueblo…):
//    tocar R3    -> agrega a la cola lo seleccionado (como Ctrl + clic derecho)
//    mantener R3 -> muestra su receta mientras lo mantienes (como Alt)
//  Jugando (sin ventanas):
//    R3 -> entra al panel de la cola:
//      cruceta ↑↓ moverse · → abrir · ← cerrar · LB/RB receta anterior/siguiente
//      X restar · Y sumar · A pin (marcar en cofres) · B o R3 salir
// Los botones se leen igual que el juego (Rewired, jugador 0, mismos números de acción), así
// que respetan el control que el juego tenga activo. Mientras estás en el panel, el juego no
// recibe los botones (tu personaje no se mueve ni interactúa).
internal class GamepadInput : MonoBehaviour
{
    // Números de acción de Rewired que usa el juego (LazyBearTechnology.GamepadController).
    private const int X = 2, Y = 3, A = 4, B = 5, LB = 6, RB = 7, DUp = 12, DDown = 13, DLeft = 14, DRight = 15, R3 = 19;
    private const float HoldTime = 0.35f;

    internal static UIItemCell RecipeCell;   // mantener R3 en una ventana: su receta
    internal static bool InPanel;            // navegando el panel con el control

    private Player player;
    private float r3Since = -1f;
    private string lastError;
    private float nextRepeat;

    private void Update()
    {
        long t = Perf.Start();
        try
        {
            Tick();
            Perf.Stop("control", t);
        }
        catch (Exception e)
        {
            if (e.ToString() != lastError)
            {
                lastError = e.ToString();
                Plugin.Log.LogError("Control: " + e);
            }
            RecipeCell = null;
            ExitPanel();
        }
    }

    private bool Down(int action) => player.GetButtonDown(action);
    private bool Held(int action) => player.GetButton(action);
    private bool Up(int action) => player.GetButtonUp(action);

    private void Tick()
    {
        if (!ReInput.isReady)
            return;
        player ??= ReInput.players.GetPlayer(0);
        if (player == null)
            return;

        if (InPanel)
        {
            Panel();
            return;
        }

        if (GameState.InFight)
        {
            r3Since = -1f;
            RecipeCell = null;
            return;
        }

        bool inWindow = LazyWindowsStackController.ActiveWindow != null;
        if (Down(R3))
            r3Since = Time.unscaledTime;
        if (r3Since < 0f)
            return;

        // Mantener R3 en una ventana: la receta de lo seleccionado, mientras lo mantengas.
        if (Held(R3))
        {
            if (inWindow && Time.unscaledTime - r3Since >= HoldTime)
                RecipeCell = FocusedObject()?.GetComponentInParent<UIItemCell>();
            return;
        }
        if (!Up(R3))
            return;

        bool wasHold = Time.unscaledTime - r3Since >= HoldTime;
        r3Since = -1f;
        RecipeCell = null;
        if (wasHold || !Queue.HasSlot)
            return;
        if (inWindow)
        {
            GameObject focused = FocusedObject();
            bool added = QuickAdd.AddFrom(focused);
            Plugin.Log.LogInfo($"Control: R3 en ventana, seleccionado {(focused != null ? focused.name : "nada")}, agregado {added}");
            if (added)
            {
                QueueHud.ShowAfterAdd();
                Click();
            }
        }
        else if (QueueHud.Showing && QueueHud.Instance != null && QueueHud.Instance.GamepadEnter())
        {
            InPanel = true;
            // El juego deja de recibir botones mientras navegas el panel.
            LazyInput.ClearAllKeysDown();
            LazyInput.SetInputActivity(false);
            Plugin.Log.LogInfo("Control: navegando el panel de la cola");
            Click();
        }
    }

    private void Panel()
    {
        QueueHud hud = QueueHud.Instance;
        // Salir si se cerró/ocultó el panel, se abrió una ventana o empezó una escena.
        // (La pelea, sin esperar a que el panel se oculte: mientras navegas, el juego no recibe botones.)
        if (hud == null || !QueueHud.Showing || LazyWindowsStackController.ActiveWindow != null || GameState.InCutscene || GameState.InFight
            || Down(B) || Down(R3))
        {
            ExitPanel();
            return;
        }
        // Cruceta con repetición al mantener (como en los menús del juego).
        int move = Repeat(DUp) ? -1 : Repeat(DDown) ? 1 : 0;
        if (move != 0)
            hud.GamepadMove(move);
        if (Down(DRight))
            hud.GamepadFold(open: true);
        if (Down(DLeft))
            hud.GamepadFold(open: false);
        if (Down(LB))
            hud.GamepadCycle(-1);
        if (Down(RB))
            hud.GamepadCycle(1);
        if (Down(X))
            hud.GamepadChange(-1);
        if (Down(Y))
            hud.GamepadChange(1);
        if (Down(A))
            hud.GamepadPin();
    }

    private bool Repeat(int action)
    {
        if (Down(action))
        {
            nextRepeat = Time.unscaledTime + 0.4f;
            return true;
        }
        if (Held(action) && Time.unscaledTime >= nextRepeat)
        {
            nextRepeat = Time.unscaledTime + 0.1f;
            return true;
        }
        return false;
    }

    private static void ExitPanel()
    {
        if (!InPanel)
            return;
        InPanel = false;
        LazyInput.ClearAllKeysDown();
        LazyInput.SetInputActivity(true);
        QueueHud.Instance?.GamepadExit();
        Plugin.Log.LogInfo("Control: fuera del panel");
    }

    private void OnDisable() => ExitPanel(); // nunca dejar el juego sin controles

    // Lo seleccionado con el control en la ventana de arriba: el elemento enfocado por su
    // navegación (la misma que dibuja el cursor del control).
    private static GameObject FocusedObject()
    {
        LazyWidgetBase top = LazyWindowsStackController.ActiveWindow;
        GamepadNavigationController[] navs = FindObjectsByType<GamepadNavigationController>(FindObjectsSortMode.None)
            .Where(n => n != null && n.isActiveAndEnabled && n.IsEnabled && n.FocusedItem != null)
            .ToArray();
        GamepadNavigationController best = navs.FirstOrDefault(n => top != null && n.transform.IsChildOf(top.transform))
                                           ?? navs.FirstOrDefault();
        return best?.FocusedItem?.gameObject;
    }

    private static void Click()
    {
        try { LazyAudio.PlayAndForget("gui_click"); } catch { }
    }
}
