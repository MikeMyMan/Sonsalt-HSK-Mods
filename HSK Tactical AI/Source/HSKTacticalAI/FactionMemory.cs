using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    /// <summary>
    /// What one faction has learned about the player's colony from its earlier raids (playtest, 2026-10-25: "add faction
    /// memory between raids ... adapt approach and loadout within budget after a raid fails, only if someone escaped").
    /// Idea from Raid Tactics (research/newmods/raid_tactics.md), rebuilt on our plan: intel only comes home with
    /// survivors; which approach failed; what downed the raiders; how well armoured the colonists were. Everything fades
    /// by half every memoryHalfLifeDays.
    /// </summary>
    public class FactionRaidMemory : IExposable
    {
        public int raids, lastRaidTick = -1, lastDecayTick = -1;
        public float intel;                                          // 0..1, only from survivors
        public float failWalk, failBreach, failSiege, failStandoff;  // 0..1 per approach
        public float hitTurret, hitTrap, hitExplosion, hitMelee, hitLongRange, hitShot;   // raiders downed, by cause
        public float defArmour;                                      // colonists' mean sharp armour (last seen)
        public string lastLesson = "";

        public void ExposeData()
        {
            Scribe_Values.Look(ref raids, "raids");
            Scribe_Values.Look(ref lastRaidTick, "lastRaidTick", -1);
            Scribe_Values.Look(ref lastDecayTick, "lastDecayTick", -1);
            Scribe_Values.Look(ref intel, "intel");
            Scribe_Values.Look(ref failWalk, "failWalk");
            Scribe_Values.Look(ref failBreach, "failBreach");
            Scribe_Values.Look(ref failSiege, "failSiege");
            Scribe_Values.Look(ref failStandoff, "failStandoff");
            Scribe_Values.Look(ref hitTurret, "hitTurret");
            Scribe_Values.Look(ref hitTrap, "hitTrap");
            Scribe_Values.Look(ref hitExplosion, "hitExplosion");
            Scribe_Values.Look(ref hitMelee, "hitMelee");
            Scribe_Values.Look(ref hitLongRange, "hitLongRange");
            Scribe_Values.Look(ref hitShot, "hitShot");
            Scribe_Values.Look(ref defArmour, "defArmour");
            Scribe_Values.Look(ref lastLesson, "lastLesson", "");
        }

        /// <summary>Fade everything (but the armour reading) by half per memoryHalfLifeDays since the last decay.</summary>
        public void Decay(int now)
        {
            if (lastDecayTick < 0) { lastDecayTick = now; return; }
            float days = (now - lastDecayTick) / (float)GenDate.TicksPerDay;
            if (days <= 0f) return;
            float f = Mathf.Pow(0.5f, days / Mathf.Max(1f, TacticalAIMod.Settings.memoryHalfLifeDays));
            intel *= f; failWalk *= f; failBreach *= f; failSiege *= f; failStandoff *= f;
            hitTurret *= f; hitTrap *= f; hitExplosion *= f; hitMelee *= f; hitLongRange *= f; hitShot *= f;
            lastDecayTick = now;
        }

        public float Fail(PlanMode m) => m == PlanMode.Walk ? failWalk : m == PlanMode.Breach ? failBreach : m == PlanMode.Siege ? failSiege : failStandoff;

        public void AddFail(PlanMode m, float d)
        {
            float Clamp(float v) => Mathf.Clamp01(v + d);
            switch (m)
            {
                case PlanMode.Walk: failWalk = Clamp(failWalk); break;
                case PlanMode.Breach: failBreach = Clamp(failBreach); break;
                case PlanMode.Siege: failSiege = Clamp(failSiege); break;
                default: failStandoff = Clamp(failStandoff); break;
            }
        }

        public string Describe() =>
            "raids=" + raids + " intel=" + intel.ToString("0.00") + " fail[walk " + failWalk.ToString("0.00") + ", breach " + failBreach.ToString("0.00")
            + ", siege " + failSiege.ToString("0.00") + ", standoff " + failStandoff.ToString("0.00") + "] downedBy[turret " + hitTurret.ToString("0.#")
            + ", trap " + hitTrap.ToString("0.#") + ", explosion " + hitExplosion.ToString("0.#") + ", melee " + hitMelee.ToString("0.#")
            + ", longRange " + hitLongRange.ToString("0.#") + ", shot " + hitShot.ToString("0.#") + "] armour " + defArmour.ToString("0.0")
            + (lastLesson.NullOrEmpty() ? "" : " | " + lastLesson);
    }

    /// <summary>Saved with the game: one memory per hostile faction.</summary>
    public class GameComponent_RaidMemory : GameComponent
    {
        private Dictionary<int, FactionRaidMemory> byFaction = new Dictionary<int, FactionRaidMemory>();
        private List<int> keys;
        private List<FactionRaidMemory> vals;

        public GameComponent_RaidMemory(Game game) { }

        public static GameComponent_RaidMemory Instance => Current.Game?.GetComponent<GameComponent_RaidMemory>();

        public FactionRaidMemory For(Faction f, bool create)
        {
            if (f == null) return null;
            if (!byFaction.TryGetValue(f.loadID, out var m) && create) byFaction[f.loadID] = m = new FactionRaidMemory();
            m?.Decay(Find.TickManager.TicksGame);
            return m;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref byFaction, "factions", LookMode.Value, LookMode.Deep, ref keys, ref vals);
            if (byFaction == null) byFaction = new Dictionary<int, FactionRaidMemory>();
        }
    }

    public static class FactionMemory
    {
        public static int Learned, NothingLearned, ApproachChanges, WeaponSwaps, AmmoSwaps, GrenadeSwaps;
        public static string SwapDebug = "";
        private static readonly Dictionary<int, (string threat, List<string> changes)> adapted = new Dictionary<int, (string, List<string>)>();

        public static FactionRaidMemory Read(Faction f) => GameComponent_RaidMemory.Instance?.For(f, false);

        // ---------------- during the raid ----------------

        /// <summary>A raider of a planned raid went down: remember what did it (once per raider).</summary>
        public static void RecordCasualty(Pawn p, DamageInfo dinfo)
        {
            AssaultPlan plan = AssaultPlanner.PlanOf(p) ?? AssaultPlanner.Plans.Values.FirstOrDefault(pl => pl.members.Contains(p));
            if (plan == null || plan.memoryDone || plan.downedBy.ContainsKey(p)) return;
            plan.downedBy[p] = Cause(p, dinfo);   // (no Dictionary.TryAdd on .NET Framework: the 1.6 build)
        }

        private static string Cause(Pawn p, DamageInfo dinfo)
        {
            Thing by = dinfo.Instigator;
            if (by is Building_Turret) return "turret";
            if (by is Building_Trap || (by is Building && !(by is Building_Turret))) return "trap";
            if (dinfo.Def != null && dinfo.Def.isExplosive) return "explosion";
            bool ranged = (dinfo.Weapon != null && dinfo.Weapon.IsRangedWeapon) || (dinfo.Def != null && dinfo.Def.isRanged);
            if (!ranged) return "melee";
            if (by != null && by.Spawned && p.Spawned && by.Position.DistanceTo(p.Position) > 35f) return "longRange";
            return "shot";
        }

        // ---------------- end of the raid ----------------

        /// <summary>
        /// Every member dead, downed or gone from the map: write what the faction learned. Only survivors who left the map
        /// alive (carried wounded too) bring intel home; with none, the faction learns nothing.
        /// </summary>
        public static void TryFinish(AssaultPlan plan, bool force = false)
        {
            var s = TacticalAIMod.Settings;
            if (!s.raidMemory || plan.memoryDone || plan.faction == null || plan.map == null || plan.members.Count == 0) return;
            if (!force && plan.members.Any(m => !m.Dead && !m.Downed && m.Spawned && m.Map == plan.map && m.Faction == plan.faction && !m.IsPrisoner)) return;
            plan.memoryDone = true;
            var comp = GameComponent_RaidMemory.Instance;
            if (comp == null) return;
            var mem = comp.For(plan.faction, true);
            int n = plan.members.Count;
            int escaped = plan.members.Count(m => !m.Dead && m.Faction == plan.faction && !m.IsPrisoner && m.MapHeld != plan.map);
            int lost = plan.members.Count(m => m.Dead || m.IsPrisoner || m.Faction != plan.faction || (m.Downed && m.MapHeld == plan.map));
            int defNow = Suppression.Defenders(plan.map, plan.objective, 40f).Count;   // same radius as defendersAtStart
            float defDown = plan.defendersAtStart > 0 ? Mathf.Clamp01(1f - defNow / (float)plan.defendersAtStart) : 0f;
            bool success = defDown >= 0.5f;
            bool failed = !success && (lost >= n * 0.4f || plan.phase == PlanPhase.Withdraw);
            mem.raids++;
            mem.lastRaidTick = Find.TickManager.TicksGame;
            plan.memoryOutcome = (success ? "success" : failed ? "failed" : "inconclusive") + ", " + plan.mode + ", lost " + lost + "/" + n + ", escaped " + escaped
                                 + ", defenders down " + Mathf.RoundToInt(defDown * 100f) + "%";
            if (escaped == 0)
            {
                NothingLearned++;
                mem.lastLesson = "raid " + mem.raids + ": no survivors, nothing learned";
                return;
            }
            Learned++;
            mem.intel = Mathf.Clamp01(mem.intel + 0.2f + 0.1f * Mathf.Min(3, escaped));
            // scouts and saboteurs came for something else: no lesson about the approach; scouts who watched long bring more
            if (plan.mode == PlanMode.Scout)
                mem.intel = Mathf.Clamp01(mem.intel + 0.3f * Mathf.Clamp01(plan.observedTicks / (float)Mathf.Max(1, s.scoutObserveTicks)));
            else if (plan.mode != PlanMode.Sabotage)
            {
                if (failed) mem.AddFail(plan.mode, 0.6f);
                else if (success) mem.AddFail(plan.mode, -0.3f);
            }
            foreach (var c in plan.downedBy.Values)
            {
                switch (c)
                {
                    case "turret": mem.hitTurret++; break;
                    case "trap": mem.hitTrap++; break;
                    case "explosion": mem.hitExplosion++; break;
                    case "melee": mem.hitMelee++; break;
                    case "longRange": mem.hitLongRange++; break;
                    default: mem.hitShot++; break;
                }
            }
            if (plan.defArmour > 0f) mem.defArmour = plan.defArmour;
            mem.lastLesson = "raid " + mem.raids + " (" + plan.memoryOutcome + ")";
        }

        // ---------------- the next raid ----------------

        public enum Threat { None, Turrets, Killbox, Snipers, Melee, Armour }

        /// <summary>What hurt this faction most, from its memory.</summary>
        public static Threat MainThreat(FactionRaidMemory m)
        {
            var s = TacticalAIMod.Settings;
            var opts = new List<(Threat t, float w)>
            {
                (Threat.Turrets, m.hitTurret + m.hitExplosion * 0.5f),
                (Threat.Killbox, m.hitTrap + m.failWalk * 2f),
                (Threat.Snipers, m.hitLongRange),
                (Threat.Melee, m.hitMelee),
                (Threat.Armour, m.defArmour >= s.memoryArmourHigh ? 1.5f + m.hitShot * 0.25f : 0f),
            };
            var best = opts.OrderByDescending(o => o.w).First();
            return best.w >= 1f ? best.t : Threat.None;
        }

        public static string ThreatPhrase(Threat t) =>
            t == Threat.Turrets ? "your turrets" : t == Threat.Killbox ? "your killbox" : t == Threat.Snipers ? "your snipers"
            : t == Threat.Melee ? "your close-combat fighters" : t == Threat.Armour ? "your colonists' armour" : "your defences";

        /// <summary>
        /// Approach from memory (raids with intel only): a walk-in that failed -> breach; a failed breach -> siege if the
        /// faction can. Returns the forced mode or null.
        /// </summary>
        public static PlanMode? PreferredMode(Faction f, bool canWalk, out string why)
        {
            why = null;
            var s = TacticalAIMod.Settings;
            var m = Read(f);
            if (!s.raidMemory || m == null || m.intel < s.memoryIntelMin) return null;
            if (m.failBreach >= 0.5f && f.def.canSiege) { why = "remembers a breach that failed -> siege"; return PlanMode.Siege; }
            if (canWalk && m.failWalk >= 0.5f) { why = "remembers a walk-in that failed -> breach"; return PlanMode.Breach; }
            return null;
        }

        /// <summary>
        /// Loadout against what hurt them last time, within the raid's budget: up to memoryShare of the gunners swap their
        /// gun for one their pawn kind may carry, worth no more than the old one (x1.1) - ammo swapped, not added; carried
        /// grenades swapped (frag -> EMP vs turrets, half to smoke vs snipers); AP ammo vs armour. Never extra gear.
        /// </summary>
        public static void AdaptLoadouts(Lord lord, AssaultPlan plan, List<Pawn> pawns)
        {
            var s = TacticalAIMod.Settings;
            if (!s.raidMemory || !s.adaptiveLoadouts || lord?.faction == null) return;
            var m = Read(lord.faction);
            if (m == null || m.intel < s.memoryIntelMin || !Competence.Knows(plan, Doctrine.Memory)) return;   // a dull command ignores what the survivors told
            if (adapted.TryGetValue(lord.loadID, out var done0))
            {
                // the same raid planned again: what it brought stays
                plan.memoryThreat = done0.threat;
                plan.memoryAdaptations = done0.changes;
                return;
            }
            Threat t = MainThreat(m);
            plan.memoryThreat = t.ToString();
            adapted[lord.loadID] = (plan.memoryThreat, plan.memoryAdaptations);
            if (t == Threat.None) return;
            TechLevel tech = lord.faction.def.techLevel;
            var gunners = pawns.Where(p => p.RaceProps.Humanlike && p.equipment?.Primary != null && p.equipment.Primary.def.IsRangedWeapon
                                           && !(plan.roleOf.TryGetValue(p, out var r) && (r == RaidRole.Medic || r == RaidRole.Breacher))
                                           && !(p.equipment.Primary.TryGetComp<CompBiocodable>()?.Biocoded ?? false))
                               .OrderBy(p => p.thingIDNumber).ToList();
            int budget = Mathf.Max(1, Mathf.FloorToInt(pawns.Count(p => p.RaceProps.Humanlike) * s.memoryShare * Competence.Of(plan)));   // sharper commands adapt more of the raid
            raidTags = pawns.Where(p => p.kindDef?.weaponTags != null).SelectMany(p => p.kindDef.weaponTags).Distinct().ToList();
            var changes = new List<string>();
            int done = 0;
            foreach (var p in gunners)
            {
                if (done >= budget) break;
                bool changed = false;
                switch (t)
                {
                    case Threat.Turrets:
                    case Threat.Killbox:
                        changed = SwapGrenades(p, "Weapon_GrenadeEMP", t == Threat.Turrets ? 1f : 0f, changes)
                                  | (done < 2 && SwapWeapon(p, tech, d => Explosive(d), "launcher", changes));
                        break;
                    case Threat.Snipers:
                        changed = SwapGrenades(p, "CE_Weapon_GrenadeSmoke", 0.5f, changes) | SwapWeapon(p, tech, d => Range(d) >= 40f && !Explosive(d), "long rifle", changes);
                        break;
                    case Threat.Melee:
                        changed = SwapWeapon(p, tech, d => Range(d) <= 22f && !Explosive(d) && (Burst(d) >= 3 || d.defName.IndexOf("shotgun", StringComparison.OrdinalIgnoreCase) >= 0), "shotgun/SMG", changes);
                        break;
                    case Threat.Armour:
                        changed = SwapToAP(p, changes);
                        break;
                }
                if (changed) done++;
            }
            plan.memoryAdaptations.Clear();
            plan.memoryAdaptations.AddRange(changes);
            string phrase = ThreatPhrase(t);
            plan.capability += "; memory: " + phrase + " (" + (changes.Count == 0 ? "no matching gear" : string.Join(", ", changes.Distinct())) + ")";
            if (s.memoryMessages && lord.Map != null)
                Messages.Message(lord.faction.Name + " remember " + phrase + (changes.Count > 0 ? ": this raid brings " + string.Join(", ", changes.Distinct()) + "." : "."),
                    new LookTargets(pawns), MessageTypeDefOf.ThreatSmall, false);
        }

        // ---------------- gear swaps ----------------

        private static float Range(ThingDef d) => d.Verbs?.FirstOrDefault(v => !v.IsMeleeAttack)?.range ?? 0f;
        private static int Burst(ThingDef d) => d.Verbs?.FirstOrDefault(v => !v.IsMeleeAttack)?.burstShotCount ?? 1;
        private static readonly Dictionary<ThingDef, bool> explosive = new Dictionary<ThingDef, bool>();

        /// <summary>Fires explosive rounds: the verb's projectile, or under CE any projectile of its ammo set.</summary>
        private static bool Explosive(ThingDef d)
        {
            if (explosive.TryGetValue(d, out bool e)) return e;
            e = d.Verbs?.Any(v => (v.defaultProjectile?.projectile?.explosionRadius ?? 0f) > 0f) ?? false;
            if (!e && d.comps != null)
                foreach (var cp in d.comps)
                {
                    if (cp.GetType().Name != "CompProperties_AmmoUser") continue;
                    object set = cp.GetType().GetField("ammoSet")?.GetValue(cp);
                    if (set?.GetType().GetField("ammoTypes")?.GetValue(set) is System.Collections.IList links)
                        foreach (var link in links)
                            if (link?.GetType().GetField("projectile")?.GetValue(link) is ThingDef proj && (proj.projectile?.explosionRadius ?? 0f) > 0f) { e = true; break; }
                }
            explosive[d] = e;
            return e;
        }

        // HSK pawn kinds carry narrow weapon tags (Mercenary_Gunner: PT/SMG/ST, Mercenary_Sniper: RF, Heavy: MG): a raider
        // may also take what another pawn kind of the same raid carries - what this faction fields - within his own budget
        private static List<string> raidTags = new List<string>();

        /// <summary>
        /// A weapon his own pawn kind may carry (else one another kind of this raid carries), at the faction's tech, within
        /// his kind's weapon money (at least the old gun's value).
        /// </summary>
        private static bool SwapWeapon(Pawn p, TechLevel tech, Func<ThingDef, bool> wanted, string label, List<string> changes)
        {
            var old = p.equipment.Primary;
            var own = p.kindDef?.weaponTags;
            var tags = (own ?? new List<string>()).Concat(raidTags).Distinct().ToList();
            if (tags == null || tags.Count == 0 || wanted(old.def))
            {
                if (SwapDebug.Length < 600) SwapDebug += p.kindDef?.defName + ":" + (tags == null || tags.Count == 0 ? "noTags" : "alreadyFits(" + old.def.defName + ")") + " ";
                return false;
            }
            // within budget: what this kind of pawn may be generated with (its weapon money), at least the old gun's value
            float cap = Mathf.Max(old.MarketValue * 1.1f, p.kindDef?.weaponMoney.max ?? 0f);
            var tagged = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.IsRangedWeapon && d.equipmentType == EquipmentType.Primary && d.weaponTags != null && d.weaponTags.Any(tags.Contains)).ToList();
            var inTech = tagged.Where(d => d.techLevel <= tech && !d.destroyOnDrop).ToList();
            var inBudget = inTech.Where(d => d.BaseMarketValue <= cap).ToList();
            // his own kind's first; then ordinary weapons over spacer-tagged oddities below spacer tech (mem3: Dead Space's
            // contact beam was picked as a "launcher"); launchers proper (grenade / rocket) first among explosives
            bool Launcherish(ThingDef d) => d.weaponTags.Any(t => t.IndexOf("Launcher", StringComparison.OrdinalIgnoreCase) >= 0 || t.StartsWith("GLT"))
                                            || d.defName.IndexOf("Launcher", StringComparison.OrdinalIgnoreCase) >= 0 || d.defName.IndexOf("RPG", StringComparison.OrdinalIgnoreCase) >= 0;
            var cand = inBudget.Where(wanted).OrderBy(d => own != null && d.weaponTags.Any(own.Contains) ? 0 : 1)
                               .ThenBy(d => tech < TechLevel.Spacer && d.weaponTags.Contains("SpacerGun") ? 1 : 0)
                               .ThenBy(d => Launcherish(d) ? 0 : 1)
                               .ThenByDescending(d => d.BaseMarketValue).ThenBy(d => d.defName).FirstOrDefault();
            // diagnostics for the Lab (first few raiders): where the search comes up empty
            if (SwapDebug.Length < 600)
                SwapDebug += p.kindDef?.defName + "[" + string.Join("|", tags) + "] tagged " + tagged.Count + " tech " + inTech.Count + " budget<=" + cap.ToString("0") + " " + inBudget.Count
                             + " wanted " + inBudget.Count(wanted) + (cand != null ? " -> " + cand.defName : "") + "; ";
            if (cand == null) return false;
            int oldRounds = RemoveAmmoFor(p, old, out float ammoValue);
            ThingDef stuff = cand.MadeFromStuff ? (old.Stuff != null && cand.stuffCategories != null && old.Stuff.stuffProps?.categories?.Any(cand.stuffCategories.Contains) == true ? old.Stuff : GenStuff.DefaultStuffFor(cand)) : null;
            var gun = (ThingWithComps)ThingMaker.MakeThing(cand, stuff);
            if (old.TryGetQuality(out QualityCategory q)) gun.TryGetComp<CompQuality>()?.SetQuality(q, ArtGenerationContext.Outsider);
            p.equipment.DestroyEquipment(old);
            p.equipment.AddEquipment(gun);
            GiveAmmo(p, gun, ammoValue, oldRounds);
            WeaponSwaps++;
            changes.Add(label);
            return true;
        }

        /// <summary>CE ammo of a gun (its ammo set's types), or empty without CE.</summary>
        private static List<ThingDef> AmmoTypes(ThingWithComps gun, out object comp, out int magazine)
        {
            comp = null; magazine = 0;
            var list = new List<ThingDef>();
            foreach (var c in gun.AllComps)
            {
                if (c.GetType().Name != "CompAmmoUser") continue;
                comp = c;
                object props = c.props;
                magazine = (props?.GetType().GetField("magazineSize")?.GetValue(props) as int?) ?? 0;
                object set = props?.GetType().GetField("ammoSet")?.GetValue(props);
                if (set?.GetType().GetField("ammoTypes")?.GetValue(set) is System.Collections.IList types)
                    foreach (var at in types)
                        if (at?.GetType().GetField("ammo")?.GetValue(at) is ThingDef a) list.Add(a);
                break;
            }
            return list;
        }

        private static int RemoveAmmoFor(Pawn p, ThingWithComps gun, out float value)
        {
            value = 0f;
            var types = AmmoTypes(gun, out _, out _);
            int rounds = 0;
            foreach (var t in p.inventory.innerContainer.Where(x => types.Contains(x.def)).ToList())
            {
                rounds += t.stackCount;
                value += t.MarketValue * t.stackCount;
                p.inventory.innerContainer.Remove(t);
                t.Destroy();
            }
            return rounds;
        }

        /// <summary>Ammo for the new gun worth about what was taken (2 to 8 magazines).</summary>
        private static void GiveAmmo(Pawn p, ThingWithComps gun, float value, int oldRounds)
        {
            var types = AmmoTypes(gun, out _, out int mag);
            if (types.Count == 0) return;
            ThingDef ammo = types[0];
            float unit = Mathf.Max(0.01f, ammo.BaseMarketValue);
            int rounds = value > 0f ? Mathf.RoundToInt(value / unit) : oldRounds;
            int m = Mathf.Max(1, mag);
            rounds = Mathf.Clamp(rounds, m * 2, m * 8);
            Thing t = ThingMaker.MakeThing(ammo);
            t.stackCount = rounds;
            p.inventory.innerContainer.TryAdd(t);
        }

        /// <summary>Armour-piercing rounds in place of the same number of the ones carried (CE ammo set), selected for reload.</summary>
        private static bool SwapToAP(Pawn p, List<string> changes)
        {
            var gun = p.equipment.Primary;
            var types = AmmoTypes(gun, out object comp, out _);
            ThingDef ap = types.FirstOrDefault(a => a.defName.EndsWith("_AP") || a.defName.Contains("_AP_"));
            if (ap == null) return false;
            int rounds = 0;
            foreach (var t in p.inventory.innerContainer.Where(x => types.Contains(x.def) && x.def != ap).ToList())
            {
                rounds += t.stackCount;
                p.inventory.innerContainer.Remove(t);
                t.Destroy();
            }
            if (rounds == 0) return false;
            Thing apStack = ThingMaker.MakeThing(ap);
            apStack.stackCount = rounds;
            p.inventory.innerContainer.TryAdd(apStack);
            try { comp?.GetType().GetProperty("SelectedAmmo")?.SetValue(comp, ap); } catch { }
            AmmoSwaps++;
            changes.Add("armour-piercing ammo");
            return true;
        }

        /// <summary>Carried frag grenades exchanged for another kind (share of the stack); none carried: nothing.</summary>
        private static bool SwapGrenades(Pawn p, string toDef, float share, List<string> changes)
        {
            if (share <= 0f) return false;
            ThingDef to = DefDatabase<ThingDef>.GetNamedSilentFail(toDef);
            if (to == null) return false;
            Thing frag = Grenades.CarriedFrag(p);
            if (frag == null) return false;
            int n = Mathf.Max(1, Mathf.RoundToInt(frag.stackCount * share));
            Thing taken = frag.SplitOff(Mathf.Min(n, frag.stackCount));
            taken.Destroy();
            Thing g = ThingMaker.MakeThing(to);
            g.stackCount = Mathf.Min(n, to.stackLimit);
            p.inventory.innerContainer.TryAdd(g);
            GrenadeSwaps++;
            changes.Add(to.label);
            return true;
        }
    }

    /// <summary>What downed a raider of a planned raid (raid memory).</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.PostApplyDamage))]
    public static class Patch_RaidMemoryCasualty
    {
        public static void Postfix(Pawn ___pawn, DamageInfo dinfo)
        {
            if (___pawn == null || !(___pawn.Dead || ___pawn.Downed) || ___pawn.Faction == null || ___pawn.Faction == Faction.OfPlayer) return;
            if (!TacticalAIMod.Settings.raidMemory || !___pawn.RaceProps.Humanlike) return;
            FactionMemory.RecordCasualty(___pawn, dinfo);
        }
    }
}
