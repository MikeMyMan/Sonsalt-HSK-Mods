# Relation Based Trading changelog

## 2.1.0 (2026-10-25)

New system: goodwill unlocks better goods gradually instead of fixed tech tiers. Factions advance in different ways,
so a faction's own tech level now sets the starting point, and relations decide how valuable, how good and how
dangerous the goods are.

How it works (t = (goodwill + 75) / 150, from 0 at -75 to 1 at 75)
- **Value:** the most expensive single item is 150 x 44.7^t silver: 150 at -75, about 530 at -25, 1,000 at 0, 1,900 at
  25, 3,550 at 50, 6,700 just below 75. From 75 (Ally) there is no limit. Rare and valuable goods appear gradually.
- **Danger:** weapons, ammo, shells, explosives and weapon blueprints (HSK Weapon Blueprints) only from goodwill 25,
  and they count 4x their value against the limit (about 470 silver at 25, 890 at 50, all at 75). Survival tools are
  work gear, not weapons.
- **Quality:** the best quality rises one step about every 37 goodwill (Normal at -75, Good from -56, Excellent from -19,
  Masterwork from 19, Legendary from 56). Higher-quality items are lowered to the limit instead of being removed, also
  after the game rerolls trader quality.
- **Tech:** relative to the faction's own tech level: two below at -75, one below up to 0, its own level up to 50, one
  above from 50. Archotech only from spacer factions and up, at Ally. Medicine up to the faction's own tech.
- **Always available:** food, raw materials and stuff, silver, animals, plants. Below -75: nothing.
- **Settlement stock follows goodwill:** when goodwill drops, items above the new limits leave the stock at once; when
  it has risen by 15 or more since the stock was made, the stock is regenerated. Changes during an open trade window
  (vanilla or Dynamic Trade Interface) wait until the window closes. A check every 1000 ticks catches goodwill changed
  by other mods or the Ideology goodwill cap.
- **Mod settings:** sliders for the start value, the value just below Ally, the weapon goodwill and factor, the rise
  that regenerates stock, quality and tech limits on/off, and a preview table.
- Removed: the seven fixed tech tiers and the tier-change reset.

Tested in game with the full HSK modlist (214 mods): 88 automated checks pass, including a goodwill sweep from -80 to
100 for an industrial and a tribal faction (every item under the limits), caravans, settlements, orbital and
factionless traders, drop/rise stock updates in both call directions, open trade windows, the periodic check and
save/load. No errors from the mod in the log. The 1.5 and 1.6 builds compile but were not tested in game.

## 2.0.1 (2026-10-25)

Bug fixes
- **About 150 items were never filtered** (with the HSK list mostly Combat Extended ammo, plus Core SK and 130 mm rocket items): some mods set up their items after the startup cache was built, so those items had no tech level and were always sold (e.g. Spacer charged ammo at any goodwill). The cache is rebuilt once the first time stock is generated in a game (about 25 ms).
- **Stock reset was skipped after any trade**: RimWorld never calls `TradeSession.Close()`, so after you traded with a settlement the game still counted that trade as open, and the settlement's stock never reset on later tier changes. The check now also requires a trade window to actually be open.
- **Tier reached during a trade was never applied**: trading and gifts raise goodwill while the trade window is open. The reset for that settlement was skipped and never caught up. It now runs as soon as the window closes.
- **Trade window mods**: Dynamic Trade Interface replaces the vanilla trade window with its own. Its window is now recognised, so a goodwill change during such a trade can't destroy the stock on screen either.
- **One broken item could abort a stock reset and break the goodwill change that caused it**: HSK's minified clothes locker throws when destroyed unspawned, which also happens in the game's own `TryDestroyStock`. Resets now destroy items one by one and never throw out of the goodwill patch.
- **No reset between "nothing" and "goods without research"** (goodwill -76 to -75 and back): tiers were compared by tech level, and both tiers use `Undefined`. They are now compared by tier.
- **Goodwill cap ignored**: filtering used the raw goodwill instead of the value the game shows, which includes the Ideology goodwill cap. Both now match.
- **Tier changes outside `TryAffectGoodwillWith`** (Ideology goodwill cap, mods or tools that set goodwill directly) did not reset stock. A light check every 1000 ticks (piggybacking on the game's own goodwill update) now catches them.
- **Filtered items were dropped without being destroyed**, so art items kept their tale references. Filtered items are now destroyed like unsold settlement stock (buildings are skipped, since some HSK buildings throw when destroyed unspawned).

Performance
- Goodwill and tier are looked up once per stock generator call instead of once per item.
- A refill pass stops generating as soon as the filtered slots are filled.
- The recipe and workbench index is released after startup caching.

Other
- Settings page shows the tier labels ("Goods without research", "Ultra and archotech") and notes that meals, raw food, animals and plants are always available.
- `Source/nuget.config` so `build.ps1` restores packages from nuget.org on machines without a NuGet source.

## 2.0.0 (2026-10-21)

Support
- Builds for RimWorld 1.5, 1.6 and 1.7 from one project (`Source/build.ps1` or `Source/build.sh`). The 1.4 assembly is unchanged.
- 1.7 build targets .NET 10. It references Harmony 2.4.1, so it binds to both HSK's bundled Harmony 3.0 and Workshop Harmony 2.4.x.
- `harmony.PatchAll()` now gets the assembly explicitly. Without it, nothing was patched on 1.7.
- Removed the use of `ThingDef.IsRawFood()`, which no longer exists in 1.6 and 1.7, and of `UnityEngine.RangeInt`.
- The stock patch binds the tile argument through `__args`, so it works with both `int` (1.5) and `PlanetTile` (1.6 and later).

Bug fixes
- **TPS drop** (Discord, June 2025: "removing relations based trading shot my TPS back to 900"): the `TraderKindDef.WillTrade` postfix ran constantly and did a faction lookup on every call. It has been removed.
- **Space traders with no stock** (Discord, July 2025): came from the same `WillTrade` patch, which applied the goodwill of the first faction of the trader kind's FactionDef to orbital traders. Trade ships also pass their faction into stock generation, so orbital trader kinds are now explicitly skipped (`trader.orbital`), as are factionless traders.
- **Weapons, armor and artifacts from MarketValue generators were never filtered on the reviewed path**: `StockGenerator_MarketValue` inherits `GenerateThings` from the abstract `StockGenerator_MiscItems`. Inherited concrete methods are now patched. Generic types and ambiguous overloads in other mods are skipped safely instead of breaking all patching.
- **Wrong faction's goodwill** for faction traders: also from `WillTrade` (`FactionUtility.DefaultFactionFrom`). Gone with that patch.
- **Players could not sell high-tech items** to low-goodwill traders: a side effect of `WillTrade` that was never intended. Gone with that patch.
- **Half-empty caravans** (Discord, Oct 2025): filtered items were dropped and never replaced. The stock generator is now rerolled up to twice to fill those slots with allowed items (setting: "Refill filtered stock").
- **Meals, pemmican, kibble and similar disappeared at neutral goodwill**: any item with a recipe was classified as at least Industrial, and the food check ran after that. Animals, plants, corpses and raw food are now always available, even when a base def gives them a tech level. Meals without their own tech level are always available, while meals that declare one (e.g. packaged survival meals) stay gated.
- **Recipe tech level**: only recipes whose single product is the item count, so recycling, byproduct and conversion recipes no longer make components or alloys look research-free. Recipes that no building can do are ignored. Of the remaining recipes, the cheapest way to make the item counts, using the higher of the recipe's research and the cheapest workbench's research. If no recipe qualifies, the old fallbacks apply (material guess, then Industrial).
- **Archotech items could never be bought**: they now unlock together with Ultra at 75+ goodwill.
- **Refill discarded pawns properly**: extra pawns from a refill pass are handed to `WorldPawns` for discard instead of being dropped.
- **Stock reset during an open trade** is skipped for that settlement, so a goodwill change mid-trade can't destroy items the trade dialog still holds.
- **Stock never reset on a tier change**: the goodwill patch only looked at `other`, but the game usually calls `npc.TryAffectGoodwillWith(player)`. Both directions are handled now, and the goodwill before the change is captured in a prefix instead of being rebuilt from `goodwillChange`, which is wrong when the change gets clamped.
- **Possible null reference** when resetting a settlement without a trader: fixed.
- **Same method patched several times**: generators that inherit `GenerateThings` without overriding it each added another copy of the filter. Each method is now patched once.
- **Minified furniture** was judged as `MinifiedThing` rather than by the item inside. Fixed.
- **Error log spam** from `GoodwillWith` for factions with no relation to the player: replaced with a null-safe lookup.
- **Slow startup with large mod lists**: the recipe search was O(things x recipes) and now uses a product index built once.
- **"Cached 0 items" on startup**: tech level caching now runs in a `[StaticConstructorOnStartup]`, after all defs are loaded, instead of a long event queued from the Mod constructor.

New
- Mod settings: Enabled, Refill filtered stock, Verbose logging, and the goodwill table.
