using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CraftQueue;

// Medición de rendimiento (se activa en la configuración: Diagnóstico → MedirRendimiento).
// Cada parte del mod mide cuánto tarda; cada 15 s se escribe en el log un resumen (promedio,
// máximo y veces por parte, más cuánto dura un cuadro del juego) y, si en un cuadro el mod
// tardó más de 3 ms, se anota ese cuadro con su desglose (máximo uno por segundo).
// Apagado no mide nada: cada punto de medición es solo una comparación.
internal static class Perf
{
    internal static bool On;

    private sealed class Stat
    {
        public double total, max;
        public int calls;
    }

    private static readonly Stopwatch clock = Stopwatch.StartNew();
    private static readonly Dictionary<string, Stat> window = new Dictionary<string, Stat>();
    private static readonly Dictionary<string, double> frame = new Dictionary<string, double>();
    private static double frameOurs;
    private static double windowStart = -1, lastSpikeLog;
    private static int frames, gcAtStart;
    private static double frameTimeTotal, frameTimeMax;
    private const double WindowSeconds = 15, SpikeMs = 3;

    public static long Start() => On ? clock.ElapsedTicks : 0;

    // top = true para las partes de primer nivel (las que suman al costo del cuadro).
    public static void Stop(string name, long started, bool top = true)
    {
        if (!On || started == 0)
            return;
        double ms = (clock.ElapsedTicks - started) * 1000.0 / Stopwatch.Frequency;
        if (!window.TryGetValue(name, out Stat s))
            window[name] = s = new Stat();
        s.total += ms;
        s.calls++;
        if (ms > s.max)
            s.max = ms;
        frame[name] = (frame.TryGetValue(name, out double f) ? f : 0) + ms;
        if (top)
            frameOurs += ms;
    }

    // Una vez por cuadro (desde el plugin): cierra el cuadro y, cada 15 s, escribe el resumen.
    public static void EndFrame(float unscaledDelta)
    {
        if (!On)
            return;
        double now = clock.Elapsed.TotalSeconds;
        if (windowStart < 0)
        {
            windowStart = now;
            gcAtStart = GC.CollectionCount(0);
        }
        frames++;
        double dt = unscaledDelta * 1000.0;
        frameTimeTotal += dt;
        if (dt > frameTimeMax)
            frameTimeMax = dt;

        if (frameOurs > SpikeMs && now - lastSpikeLog > 1)
        {
            lastSpikeLog = now;
            Plugin.Log.LogInfo($"[Rendimiento] cuadro lento del mod: {frameOurs:0.00} ms (cuadro del juego {dt:0.0} ms) → " +
                               string.Join(", ", frame.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:0.00}")));
        }
        frame.Clear();
        frameOurs = 0;

        if (now - windowStart < WindowSeconds)
            return;
        StringBuilder sb = new StringBuilder();
        sb.Append($"[Rendimiento] {frames} cuadros en {now - windowStart:0} s · cuadro del juego prom {frameTimeTotal / Math.Max(1, frames):0.0} ms, máx {frameTimeMax:0.0} ms · limpiezas de memoria {GC.CollectionCount(0) - gcAtStart}");
        foreach (KeyValuePair<string, Stat> kv in window.OrderByDescending(kv => kv.Value.total))
            sb.Append($"\n    {kv.Key,-22} prom/cuadro {kv.Value.total / Math.Max(1, frames):0.000} ms · máx {kv.Value.max:0.00} ms · {kv.Value.calls} veces");
        Plugin.Log.LogInfo(sb.ToString());
        window.Clear();
        frames = 0;
        frameTimeTotal = frameTimeMax = 0;
        windowStart = now;
        gcAtStart = GC.CollectionCount(0);
    }
}
