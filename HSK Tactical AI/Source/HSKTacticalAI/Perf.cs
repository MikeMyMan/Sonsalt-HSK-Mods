using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// Lightweight profiler (playtest, 2026-10-23: "we also need to do FPS tests to check how the AI impacts the gameplay and
    /// what we can do to optimize it"). Active in the AI Lab or with the developer setting; otherwise every call is a
    /// single bool check. Sections: map-component ticks, every HSK job giver (via Harmony), and the CE hooks that run per
    /// projectile / per pawn tick.
    /// </summary>
    public static class Perf
    {
        public static bool On;
        private class Acc { public double ms, max; public long calls, spikes; public bool warm; }
        private static readonly Dictionary<string, Acc> acc = new Dictionary<string, Acc>();
        private static readonly double tickToMs = 1000.0 / Stopwatch.Frequency;

        public static long Start() => On ? Stopwatch.GetTimestamp() : 0L;

        public static void Stop(string key, long t0)
        {
            if (t0 == 0L) return;
            double ms = (Stopwatch.GetTimestamp() - t0) * tickToMs;
            if (!acc.TryGetValue(key, out var a)) acc[key] = a = new Acc();
            a.ms += ms;
            a.calls++;
            if (!a.warm) { a.warm = true; return; }   // first call: JIT compilation, not the steady cost
            if (ms > a.max) a.max = ms;
            if (ms > 2.0) a.spikes++;
        }

        public static double TotalMs => acc.Values.Sum(a => a.ms);

        /// <summary>"section=ms per tick (calls, max ms after the first call, calls over 2 ms)", most expensive first.</summary>
        public static string Report(long ticks)
        {
            if (ticks <= 0) return "";
            return string.Join(" ", acc.OrderByDescending(kv => kv.Value.ms)
                .Select(kv => kv.Key + "=" + (kv.Value.ms / ticks).ToString("0.0000") + "(" + kv.Value.calls + "," + kv.Value.max.ToString("0.00") + "," + kv.Value.spikes + ")"));
        }
    }

    /// <summary>
    /// Load-adaptive timing (CAI idea): every 120 ticks the real tick rate is compared with what the game speed asks for
    /// (60 x speed multiplier). Below 75 % of it, Load = 2: periodic checks run half as often and caches live twice as
    /// long. Fixed at 1 in the AI Lab (comparable runs).
    /// </summary>
    public static class Adaptive
    {
        public static int Load = 1;
        private static readonly Stopwatch sw = new Stopwatch();
        private static int lastTick = -1;

        public static void Tick()
        {
            int now = Find.TickManager.TicksGame;
            if (lastTick < 0 || !sw.IsRunning) { lastTick = now; sw.Restart(); return; }
            if (now - lastTick < 120) return;
            double secs = sw.Elapsed.TotalSeconds;
            float wanted = 60f * Find.TickManager.TickRateMultiplier;
            float actual = secs > 0 ? (float)((now - lastTick) / secs) : wanted;
            Load = AILab.Active || wanted <= 0f ? 1 : (actual < wanted * 0.75f ? 2 : 1);
            lastTick = now;
            sw.Restart();
        }
    }

    /// <summary>Times every HSK job giver's TryGiveJob.</summary>
    [HarmonyPatch]
    public static class Patch_PerfJobGivers
    {
        public static IEnumerable<MethodBase> TargetMethods() =>
            typeof(Perf).Assembly.GetTypes()
                .Where(t => typeof(ThinkNode_JobGiver).IsAssignableFrom(t) && !t.IsAbstract)
                .Select(t => AccessTools.DeclaredMethod(t, "TryGiveJob"))
                .Where(m => m != null);

        public static void Prefix(out long __state) { __state = Perf.Start(); }

        public static void Postfix(object __instance, long __state) { Perf.Stop("giver." + __instance.GetType().Name.Replace("JobGiver_HSKTAI_", ""), __state); }
    }
}
