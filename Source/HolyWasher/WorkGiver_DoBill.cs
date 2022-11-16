using RimWorld;
using Verse;
using Verse.AI;

namespace HolyWasher
{
    // ReSharper disable once InconsistentNaming
    public abstract class WorkGiver_DoBill : WorkGiver_Scanner
    {
        private static readonly IntRange ReCheckFailedBillTicksRange = new IntRange(500, 600);

        private static string _missingSkillTranslated;
        private static string _missingMaterialsTranslated;

        private readonly JobDef _jobDef;

        protected WorkGiver_DoBill(JobDef job)
        {
            _missingSkillTranslated ??= "MissingSkill".Translate();
            _missingMaterialsTranslated ??= "MissingMaterials".Translate();
            _jobDef = job;
        }

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override ThingRequest PotentialWorkThingRequest => def.fixedBillGiverDefs is { Count: 1 } ? ThingRequest.ForDef(def.fixedBillGiverDefs[0]) : ThingRequest.ForGroup(ThingRequestGroup.PotentialBillGiver);

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t is not IBillGiver billGiver || !ThingIsUsableBillGiver(t) || !billGiver.CurrentlyUsableForBills() ||
                !billGiver.BillStack.AnyShouldDoNow || !pawn.CanReserve(t) || t.IsBurning() || t.IsForbidden(pawn))
            {
                return null;
            }

            if (!pawn.CanReach(t.InteractionCell, PathEndMode.OnCell, Danger.Some))
            {
                return null;
            }

            billGiver.BillStack.RemoveIncompletableBills();
            return StartOrResumeBillJob(pawn, billGiver);
        }

        private bool ThingIsUsableBillGiver(Thing thing)
        {
            if (def.fixedBillGiverDefs != null && def.fixedBillGiverDefs.Contains(thing.def))
            {
                return true;
            }

            return false;
        }

        private Job StartOrResumeBillJob(Pawn pawn, IBillGiver giver)
        {
            foreach (var bill in giver.BillStack)
            {
                // use HolyWasher.Worker as a filter so we can use the same tables.
                if (bill.recipe.workerClass != typeof(Worker))
                {
                    continue;
                }

                if (Find.TickManager.TicksGame < bill.nextTickToSearchForIngredients + ReCheckFailedBillTicksRange.RandomInRange &&
                    FloatMenuMakerMap.makingFor == null)
                {
                    continue;
                }

                if (!bill.ShouldDoNow())
                {
                    continue;
                }

                if (!bill.PawnAllowedToStartAnew(pawn))
                {
                    continue;
                }

                if (!bill.recipe.PawnSatisfiesSkillRequirements(pawn))
                {
                    JobFailReason.Is(_missingSkillTranslated);
                }
                else
                {
                    if (TryFindBestBillIngredients(bill, pawn, (Thing)giver, out var chosen))
                    {
                        return TryStartNewDoBillJob(pawn, bill, giver, chosen);
                    }
                }
            }

            return null;
        }

        private static bool TryFindBestBillIngredients(Bill bill, Pawn pawn, Thing billGiver,
            out Thing chosen)
        {
            var billGiverRootCell = GetBillGiverRootCell(billGiver, pawn);
            var validRegionAt = pawn.Map.regionGrid.GetValidRegionAt(billGiverRootCell);
            if (validRegionAt == null)
            {
                chosen = null;
                return false;
            }

            bool BaseValidator(Thing t)
            {
                return t.Spawned && !t.IsForbidden(pawn) && bill.recipe.fixedIngredientFilter.Allows(t) &&
                       bill.ingredientFilter.Allows(t) &&
                       bill.recipe.ingredients.Any(ingNeed => ingNeed.filter.Allows(t)) && pawn.CanReserve(t) &&
                       (!bill.CheckIngredientsIfSociallyProper || t.IsSociallyProper(pawn));
            }

            chosen = GenClosest.ClosestThingReachable(
                billGiverRootCell,
                pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                PathEndMode.Touch,
                TraverseParms.For(pawn),
                bill.ingredientSearchRadius,
                BaseValidator);

            return chosen != null;
        }

        private static IntVec3 GetBillGiverRootCell(Thing billGiver, Pawn forPawn)
        {
            if (billGiver is not Building building)
            {
                return billGiver.Position;
            }

            if (building.def.hasInteractionCell)
            {
                return building.InteractionCell;
            }

            Log.Error(
                "HollyWash :: Tried to find bill ingredients for " + billGiver + " which has no interaction cell.");
            return forPawn.Position;
        }

        private Job TryStartNewDoBillJob(Pawn pawn, Bill bill, IBillGiver giver, Thing chosen)
        {
            var job = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, giver, null);
            if (job != null)
            {
                return job;
            }

            var job2 = new Job(_jobDef, (Thing)giver, chosen)
            {
                count = 1,
                haulMode = HaulMode.ToCellNonStorage,
                bill = bill
            };
            return job2;
        }
    }
}