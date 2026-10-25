using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RelationBasedTrading
{
    [HarmonyPatch]
    public static class StockGenerator_Patch
    {
        private const int MaxRefillPasses = 2;

        // >0 while we re-run a generator ourselves to refill stock; inner calls pass through untouched.
        [ThreadStatic] private static int refillDepth;

        // Exact signature of the base method, so an extra overload in some mod can't make the lookup ambiguous.
        private static readonly Type[] GenerateThingsParams =
            AccessTools.Method(typeof(StockGenerator), nameof(StockGenerator.GenerateThings))
                ?.GetParameters().Select(p => p.ParameterType).ToArray();

        public static IEnumerable<MethodBase> TargetMethods()
        {
            // Only methods actually declared by a concrete StockGenerator type, each exactly once (a concrete method
            // inherited from an abstract base, e.g. StockGenerator_MarketValue -> StockGenerator_MiscItems, counts too).
            var seen = new HashSet<MethodBase>();
            foreach (Type type in GenTypes.AllTypes)
            {
                if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters || !typeof(StockGenerator).IsAssignableFrom(type))
                    continue;
                MethodInfo method = null;
                try
                {
                    method = AccessTools.Method(type, nameof(StockGenerator.GenerateThings), GenerateThingsParams);
                }
                catch (Exception e)
                {
                    Log.Warning($"[Relation Based Trading] Skipping {type.FullName}: {e.Message}");
                }
                if (method == null || method.IsAbstract)
                    continue;
                if (seen.Add(method))
                    yield return method;
            }
        }

        // Arguments are read positionally through __args: GenerateThings(tile, faction). This keeps the patch
        // independent of the tile type (int up to 1.5, PlanetTile from 1.6 on) and of parameter names in modded overrides.
        public static IEnumerable<Thing> Postfix(IEnumerable<Thing> __result,
            StockGenerator __instance, object[] __args, MethodBase __originalMethod)
        {
            RelationBasedTradingSettings settings = RelationBasedTradingMod.Settings;
            Faction faction = __args != null && __args.Length > 1 ? __args[1] as Faction : null;
            // Orbital trade ships can carry a faction (TradeShip passes it to the stock maker); they stay unfiltered.
            bool orbital = __instance?.trader != null && __instance.trader.orbital;
            // Limits are worked out once per generator call, not per item.
            TradeLimits limits = refillDepth > 0 || orbital || settings == null || !settings.enabled ? null : TradingUtility.LimitsFor(faction);
            if (limits == null)
            {
                foreach (Thing thing in __result)
                    yield return thing;
                yield break;
            }

            var kept = new List<Thing>();
            int rejected = 0;
            foreach (Thing thing in __result)
            {
                if (TradingUtility.Allows(limits, thing, allowDowngrade: true))
                    kept.Add(thing);
                else
                {
                    rejected++;
                    Discard(thing);
                }
            }

            int refilled = 0;
            if (rejected > 0 && !limits.NoTrade && settings.refillFilteredStock && __originalMethod != null)
            {
                refillDepth++;
                try
                {
                    for (int pass = 0; pass < MaxRefillPasses && refilled < rejected; pass++)
                    {
                        if (!(__originalMethod.Invoke(__instance, __args) is IEnumerable extra))
                            break;
                        foreach (object o in extra)
                        {
                            // Stop as soon as the slots are filled; most generators are lazy, so nothing more gets made.
                            if (refilled >= rejected)
                                break;
                            if (o is Pawn extraPawn)
                            {
                                // Only things are refilled; discard extra pawns properly so they don't linger in relations.
                                if (!extraPawn.Destroyed && Find.WorldPawns != null && !Find.WorldPawns.Contains(extraPawn))
                                    Find.WorldPawns.PassToWorld(extraPawn, PawnDiscardDecideMode.Discard);
                                continue;
                            }
                            if (!(o is Thing t))
                                continue;
                            if (TradingUtility.Allows(limits, t, allowDowngrade: true))
                            {
                                kept.Add(t);
                                refilled++;
                            }
                            else
                                Discard(t);
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.WarningOnce($"[Relation Based Trading] Refill failed for {__instance?.GetType().Name}: {e.Message}", 0x5EB7A0 ^ (__instance?.GetType().GetHashCode() ?? 0));
                }
                finally
                {
                    refillDepth--;
                }
            }

            if (settings.verboseLogging && (rejected > 0 || refilled > 0))
                Log.Message($"[Relation Based Trading] {faction.Name} ({limits}) / {__instance.GetType().Name}: kept {kept.Count - refilled}, filtered {rejected}, refilled {refilled}.");

            foreach (Thing thing in kept)
                yield return thing;
        }

        /// <summary>
        /// Destroys a generated thing that won't be sold. Freshly generated, never-spawned things only. This releases
        /// what they hold (e.g. art tale references), the same way the game destroys unsold settlement stock.
        /// </summary>
        internal static void Discard(Thing thing)
        {
            // Buildings (minified or not yet minified) are left alone: some (HSK lockers, end tables) throw in Destroy()
            // when never spawned.
            if (thing == null || thing is Pawn || thing is MinifiedThing || thing.def.category == ThingCategory.Building || thing.Destroyed || thing.Spawned || thing.holdingOwner != null || !thing.def.destroyable)
                return;
            try
            {
                thing.Destroy(DestroyMode.Vanish);
            }
            catch (Exception e)
            {
                if (RelationBasedTradingMod.Settings?.verboseLogging == true)
                    Log.Message($"[Relation Based Trading] Could not discard filtered {thing.def.defName}: {e.Message}");
            }
        }
    }

    // The TraderKindDef.WillTrade postfix from the 1.5 release was removed on purpose (TPS drop, wrong faction's
    // goodwill, emptied orbital traders, players unable to sell). Stock is filtered at generation time instead.

    /// <summary>
    /// Keeps settlement stock in line with goodwill, without tiers: when goodwill drops, items above the new limits
    /// leave the stock at once; when goodwill has risen by "restock rise" or more since the stock was made, the stock is
    /// regenerated. Otherwise the game's normal 30-day restock applies.
    /// </summary>
    public static class StockSync
    {
        // Goodwill each settlement's current stock was made (or last filtered) for. Transient: after loading, the first
        // check only records the current goodwill. Nothing of the mod is saved, so it stays safe to remove.
        private static readonly Dictionary<int, int> stockGoodwill = new Dictionary<int, int>();
        // Settlements whose update waited for an open trade window with them.
        private static readonly HashSet<Settlement> pending = new HashSet<Settlement>();
        private static Game stateGame;

        private static void EnsureGame()
        {
            if (stateGame == Current.Game)
                return;
            stateGame = Current.Game;
            stockGoodwill.Clear();
            pending.Clear();
        }

        public static bool HasPending => pending.Count > 0;

        private static readonly FieldInfo stockField = AccessTools.Field(typeof(Settlement_TraderTracker), "stock");
        private static bool HasStock(Settlement s) => s?.trader != null && stockField?.GetValue(s.trader) != null;

        /// <summary>After the game generated a settlement's stock.</summary>
        public static void Notify_StockGenerated(Settlement s)
        {
            if (s?.Faction == null)
                return;
            EnsureGame();
            int? gw = TradingUtility.PlayerGoodwill(s.Faction);
            if (gw.HasValue)
                stockGoodwill[s.ID] = gw.Value;
        }

        /// <summary>
        /// True while a trade window with this settlement is open. TradeSession.Active alone is not enough: the game
        /// never calls TradeSession.Close(), so TradeSession.trader keeps pointing at the last trader afterwards.
        /// </summary>
        public static bool TradeOpenWith(Settlement settlement)
        {
            if (!TradeSession.Active || !ReferenceEquals(TradeSession.trader, settlement))
                return false;
            WindowStack windows = Find.WindowStack;
            if (windows == null)
                return false;
            for (int i = 0; i < windows.Count; i++)
                if (IsTradeWindow(windows[i]))
                    return true;
            return false;
        }

        private static readonly Dictionary<Type, bool> tradeWindowTypes = new Dictionary<Type, bool>();

        // Dialog_Trade, or a replacement trade window from another mod (Dynamic Trade Interface closes Dialog_Trade
        // right away and opens its own Window_DynamicTrade).
        private static bool IsTradeWindow(Window window)
        {
            if (window == null)
                return false;
            if (window is Dialog_Trade)
                return true;
            Type type = window.GetType();
            if (!tradeWindowTypes.TryGetValue(type, out bool trade))
                tradeWindowTypes[type] = trade = type.Name.IndexOf("Trade", StringComparison.OrdinalIgnoreCase) >= 0;
            return trade;
        }

        /// <summary>Checks every stocked settlement of this faction against its current goodwill.</summary>
        public static void CheckFaction(Faction npc)
        {
            RelationBasedTradingSettings settings = RelationBasedTradingMod.Settings;
            if (settings == null || !settings.enabled || !TradingUtility.IsFilterable(npc))
                return;
            int? gw = TradingUtility.PlayerGoodwill(npc);
            if (!gw.HasValue)
                return;
            EnsureGame();
            TradeLimits limits = null;
            foreach (Settlement s in Find.WorldObjects.Settlements)
            {
                if (s.Faction != npc || !HasStock(s))
                    continue;
                if (TradeOpenWith(s))
                {
                    // Don't change the stock under an open trade window (trading itself changes goodwill).
                    pending.Add(s);
                    continue;
                }
                limits ??= TradingUtility.LimitsFor(gw.Value, TradingUtility.FactionTechOf(npc), settings);
                Apply(s, gw.Value, limits, settings);
            }
        }

        private static void Apply(Settlement s, int gw, TradeLimits limits, RelationBasedTradingSettings settings)
        {
            if (!stockGoodwill.TryGetValue(s.ID, out int made))
            {
                stockGoodwill[s.ID] = gw; // first look after loading: take the stock as it is
                return;
            }
            if (gw >= made + Math.Max(1, settings.restockRise))
            {
                DestroyStock(s); // regenerated with the better limits on the next visit
                stockGoodwill.Remove(s.ID);
                if (settings.verboseLogging)
                    Log.Message($"[Relation Based Trading] {s.LabelCap}: goodwill {made} -> {gw}, stock will be regenerated.");
            }
            else if (gw < made)
            {
                int removed = FilterStock(s, limits);
                stockGoodwill[s.ID] = gw;
                if (settings.verboseLogging)
                    Log.Message($"[Relation Based Trading] {s.LabelCap}: goodwill {made} -> {gw}, removed {removed} items above the new limits.");
            }
        }

        /// <summary>Removes stock items that the limits no longer allow (no downgrading of existing stock).</summary>
        public static int FilterStock(Settlement s, TradeLimits limits)
        {
            ThingOwner stock = s?.trader?.GetDirectlyHeldThings();
            if (stock == null)
                return 0;
            int removed = 0;
            for (int i = stock.Count - 1; i >= 0; i--)
            {
                Thing thing = stock[i];
                if (thing is Pawn || TradingUtility.Allows(limits, thing, allowDowngrade: false))
                    continue;
                stock.Remove(thing);
                removed++;
                if (thing.Destroyed)
                    continue;
                try { thing.Destroy(); }
                catch (Exception e)
                {
                    if (RelationBasedTradingMod.Settings?.verboseLogging == true)
                        Log.Message($"[Relation Based Trading] {thing.def.defName} threw while leaving the stock: {e.Message}");
                }
            }
            return removed;
        }

        /// <summary>Runs the updates that waited for a trade window.</summary>
        public static void TryRunPending()
        {
            if (pending.Count == 0)
                return;
            EnsureGame();
            foreach (Settlement s in pending.ToList())
            {
                if (s == null || s.Destroyed || s.trader == null) { pending.Remove(s); continue; }
                if (TradeOpenWith(s))
                    continue;
                pending.Remove(s);
                RunFor(s);
            }
        }

        /// <summary>Fallback for one settlement, right before its stock is read.</summary>
        public static void TryRunPending(Settlement s)
        {
            if (pending.Count == 0 || s == null || !pending.Contains(s) || TradeOpenWith(s))
                return;
            EnsureGame();
            pending.Remove(s);
            RunFor(s);
        }

        private static void RunFor(Settlement s)
        {
            RelationBasedTradingSettings settings = RelationBasedTradingMod.Settings;
            int? gw = TradingUtility.PlayerGoodwill(s.Faction);
            if (settings == null || !gw.HasValue || !HasStock(s))
                return;
            Apply(s, gw.Value, TradingUtility.LimitsFor(gw.Value, TradingUtility.FactionTechOf(s.Faction), settings), settings);
        }

        /// <summary>
        /// Settlement_TraderTracker.TryDestroyStock, but one item that throws in Destroy() (HSK's minified clothes
        /// locker does when unspawned) can't abort the reset or escape into the goodwill change that triggered it.
        /// </summary>
        public static void DestroyStock(Settlement settlement)
        {
            Settlement_TraderTracker trader = settlement?.trader;
            if (trader == null)
                return;
            ThingOwner stock = trader.GetDirectlyHeldThings();
            if (stock != null)
            {
                for (int i = stock.Count - 1; i >= 0; i--)
                {
                    Thing thing = stock[i];
                    stock.Remove(thing);
                    if (thing is Pawn || thing.Destroyed)
                        continue;
                    try { thing.Destroy(); }
                    catch (Exception e)
                    {
                        if (RelationBasedTradingMod.Settings?.verboseLogging == true)
                            Log.Message($"[Relation Based Trading] {thing.def.defName} threw while destroying old stock: {e.Message}");
                    }
                }
            }
            try
            {
                trader.TryDestroyStock(); // stock is empty now; this sets it to null so it regenerates on the next visit
            }
            catch (Exception e)
            {
                Log.WarningOnce($"[Relation Based Trading] Could not reset stock of {settlement.LabelCap}: {e.Message}", 0x5EB7A2);
            }
        }
    }

    [HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith))]
    public static class Faction_TryAffectGoodwillWith_Patch
    {
        public static Faction OtherThanPlayer(Faction instance, Faction other)
        {
            if (instance == null || other == null)
                return null;
            if (instance.IsPlayer && !other.IsPlayer)
                return other;
            if (other.IsPlayer && !instance.IsPlayer)
                return instance;
            return null;
        }

        // The game calls this both ways (npc.TryAffectGoodwillWith(player) and player.TryAffectGoodwillWith(npc)).
        public static void Postfix(Faction __instance, Faction other)
        {
            try
            {
                Faction npc = OtherThanPlayer(__instance, other);
                if (npc != null)
                    StockSync.CheckFaction(npc);
            }
            catch (Exception e)
            {
                // Never break the goodwill change (trade, gift, quest) that called us.
                Log.WarningOnce("[Relation Based Trading] Stock update after goodwill change failed: " + e, 0x5EB7A3);
            }
        }
    }

    // Runs every 1000 ticks for each faction with goodwill (GoodwillSituationManager). Catches goodwill changes from
    // the Ideology goodwill cap, dev tools and mods that set goodwill directly.
    [HarmonyPatch(typeof(Faction), "Notify_GoodwillSituationsChanged")]
    public static class Faction_Notify_GoodwillSituationsChanged_Patch
    {
        public static bool Prepare() => AccessTools.Method(typeof(Faction), "Notify_GoodwillSituationsChanged") != null;

        public static void Postfix(Faction __instance, object[] __args)
        {
            Faction other = __args != null && __args.Length > 0 ? __args[0] as Faction : null;
            Faction npc = Faction_TryAffectGoodwillWith_Patch.OtherThanPlayer(__instance, other);
            if (npc == null)
                return;
            try { StockSync.CheckFaction(npc); }
            catch (Exception e) { Log.WarningOnce("[Relation Based Trading] Goodwill check failed: " + e, 0x5EB7A4); }
        }
    }

    // ThingSetMaker_TraderStock calls PostGeneratedForTrader on every item after the stock generators ran, and
    // CompQuality rerolls the quality there. Cap it again, or a filtered Normal item could come out Legendary.
    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.PostGeneratedForTrader))]
    public static class ThingWithComps_PostGeneratedForTrader_Patch
    {
        public static void Postfix(ThingWithComps __instance, object[] __args)
        {
            try
            {
                RelationBasedTradingSettings settings = RelationBasedTradingMod.Settings;
                if (settings == null || !settings.enabled || !settings.limitQuality || __args == null || __args.Length < 3)
                    return;
                if (__args[0] is TraderKindDef trader && trader.orbital)
                    return;
                if (!__instance.TryGetQuality(out QualityCategory q))
                    return;
                TradeLimits limits = TradingUtility.LimitsFor(__args[2] as Faction);
                if (limits != null && q > limits.MaxQuality)
                    __instance.TryGetComp<CompQuality>()?.SetQuality(limits.MaxQuality, ArtGenerationContext.Outsider);
            }
            catch (Exception e) { Log.WarningOnce("[Relation Based Trading] Quality cap failed: " + e, 0x5EB7A8); }
        }
    }

    // Records the goodwill a settlement's new stock was made for.
    [HarmonyPatch(typeof(Settlement_TraderTracker), "RegenerateStock")]
    public static class Settlement_TraderTracker_RegenerateStock_Patch
    {
        public static void Postfix(Settlement_TraderTracker __instance)
        {
            try { StockSync.Notify_StockGenerated(__instance?.settlement); }
            catch (Exception e) { Log.WarningOnce("[Relation Based Trading] Recording new stock failed: " + e, 0x5EB7A7); }
        }
    }

    // Runs the waiting updates when the trade window closes, whichever window class it is (vanilla or a trade UI mod).
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.TryRemove), new[] { typeof(Window), typeof(bool) })]
    public static class WindowStack_TryRemove_Patch
    {
        public static void Postfix()
        {
            if (!StockSync.HasPending)
                return;
            try { StockSync.TryRunPending(); }
            catch (Exception e) { Log.WarningOnce("[Relation Based Trading] Stock update after trade failed: " + e, 0x5EB7A5); }
        }
    }

    [HarmonyPatch(typeof(Settlement_TraderTracker), nameof(Settlement_TraderTracker.StockListForReading), MethodType.Getter)]
    public static class Settlement_TraderTracker_Stock_Patch
    {
        public static void Prefix(Settlement_TraderTracker __instance)
        {
            try { StockSync.TryRunPending(__instance?.settlement); }
            catch (Exception e) { Log.WarningOnce("[Relation Based Trading] Pending stock update failed: " + e, 0x5EB7A6); }
        }
    }
}
