using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace WeaponBlueprints
{
    // Research work: study a blueprint whose weapon the colony doesn't know yet.
    // A plain WorkGiver (not a scanner) because the scanner can't see books inside bookcases.
    public class WorkGiver_StudyBlueprint : WorkGiver
    {
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (WBSGameComponent.Instance == null || pawn.Map == null || !BookUtility.CanReadNow(pawn))
            {
                return true;
            }
            return false;
        }

        public override Job NonScanJob(Pawn pawn)
        {
            Book book = FindBlueprintToStudy(pawn);
            return book == null ? null : JobMaker.MakeJob(WBS_DefOf.WBS_StudyBlueprint, book);
        }

        public static Book FindBlueprintToStudy(Pawn pawn)
        {
            Map map = pawn.Map;
            if (map == null)
            {
                return null;
            }
            Book best = null;
            int bestDist = int.MaxValue;
            foreach (Thing t in Candidates(map))
            {
                if (!(t is Book book) || !IsStudyable(book, pawn))
                {
                    continue;
                }
                int dist = (t.PositionHeld - pawn.Position).LengthHorizontalSquared;
                if (dist < bestDist)
                {
                    best = book;
                    bestDist = dist;
                }
            }
            return best;
        }

        private static IEnumerable<Thing> Candidates(Map map)
        {
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Book))
            {
                yield return t;
            }
            foreach (Building_Bookcase shelf in map.listerThings.GetThingsOfType<Building_Bookcase>())
            {
                foreach (Book b in shelf.HeldBooks)
                {
                    yield return b;
                }
            }
        }

        // Same checks as vanilla BookUtility.IsValidBook, plus "the weapon is still locked".
        public static bool IsStudyable(Book book, Pawn pawn)
        {
            if (!WeaponGate.IsLocked(WeaponGate.WeaponFor(book.def)))
            {
                return false;
            }
            if (book.IsForbiddenHeld(pawn) || book.Fogged())
            {
                return false;
            }
            ReadingPolicy policy = pawn.reading?.CurrentPolicy;
            if (policy != null && (!policy.defFilter.Allows(book) || (policy.effectFilter != null && !policy.effectFilter.Allows(book))))
            {
                return false;
            }
            if (!BookUtility.CanReadBook(book, pawn, out _))
            {
                return false;
            }
            return pawn.CanReserveAndReach(book, PathEndMode.Touch, Danger.None);
        }
    }

    // Vanilla reading (chair search, bookcase pickup, haul back afterwards) with the reading toil
    // replaced: fixed length, no recreation, doesn't stop during work hours, ends once the weapon is learned.
    public class JobDriver_StudyBlueprint : JobDriver_Reading
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            List<Toil> toils = base.MakeNewToils().ToList();
            Toil read = toils[toils.Count - 1];
            int duration = job.def.joyDuration > 0 ? job.def.joyDuration : 5000;
            ThingDef weapon = WeaponGate.WeaponFor(Book?.def);

            read.defaultCompleteMode = ToilCompleteMode.Delay;
            read.defaultDuration = duration;
            read.tickIntervalAction = delegate (int delta)
            {
                if (job.GetTarget(TargetIndex.B).IsValid)
                {
                    pawn.rotationTracker.FaceCell(job.GetTarget(TargetIndex.B).Cell);
                }
                else if (Book.Spawned)
                {
                    pawn.rotationTracker.FaceCell(Book.Position);
                }
                Book.OnBookReadTick(pawn, delta, BookUtility.GetReadingBonus(pawn));
                pawn.skills?.Learn(SkillDefOf.Intellectual, 0.1f * delta);
                pawn.GainComfortFromCellIfPossible(delta);
                if (pawn.IsHashIntervalTick(600, delta))
                {
                    pawn.jobs.CheckForJobOverride(9.1f);
                }
            };
            read.finishActions?.Clear();
            read.AddFinishAction(delegate
            {
                if (Book != null)
                {
                    Book.IsOpen = false;
                }
            });
            read.AddEndCondition(() => WeaponGate.IsLocked(weapon) ? JobCondition.Ongoing : JobCondition.Succeeded);
            return toils;
        }
    }
}
