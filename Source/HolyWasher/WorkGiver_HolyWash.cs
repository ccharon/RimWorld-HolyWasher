using RimWorld;
using Verse;
using Verse.AI;

namespace HolyWasher
{
    // ReSharper disable once InconsistentNaming
    // ReSharper disable once UnusedType.Global
    public class WorkGiver_HolyWash : WorkGiver_Scanner
    {
        private static readonly IntRange ReCheckFailedBillTicksRange = new IntRange(500, 600);

        private static string _missingSkillTranslated;
        private static string _missingMaterialsTranslated;

        private readonly JobDef _jobDef;

        public WorkGiver_HolyWash()
        {
            _missingSkillTranslated ??= "MissingSkill".Translate();
            _missingMaterialsTranslated ??= "MissingMaterials".Translate();
            _jobDef = HolyWasherMod.HolyWash;
        }
        
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override ThingRequest PotentialWorkThingRequest => def.fixedBillGiverDefs is { Count: 1 } ? ThingRequest.ForDef(def.fixedBillGiverDefs[0]) : ThingRequest.ForGroup(ThingRequestGroup.PotentialBillGiver);

        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (thing is not IBillGiver billGiver || !ThingIsUsableBillGiver(thing)) return null;
            if (thing.IsBurning() || thing.IsForbidden(pawn)) return null;
            
            if (!pawn.CanReserve(thing)) return null;
            if (!pawn.CanReach(thing.InteractionCell, PathEndMode.OnCell, Danger.Some)) return null;

            if (!billGiver.CurrentlyUsableForBills() || !billGiver.BillStack.AnyShouldDoNow) return null;

            billGiver.BillStack.RemoveIncompletableBills();
            return StartOrResumeBillJob(pawn, billGiver);
        }
        
         private bool ThingIsUsableBillGiver(Thing thing)
        {
            return def.fixedBillGiverDefs != null && def.fixedBillGiverDefs.Contains(thing.def);
        }

        private Job StartOrResumeBillJob(Pawn pawn, IBillGiver giver)
        {
            foreach (var bill in giver.BillStack)
            {
                // use HolyWasher.Worker as a filter so we can use the same tables.
                if (bill.recipe.workerClass != typeof(Worker)) continue;
                if (!bill.ShouldDoNow()) continue;
                if (!bill.PawnAllowedToStartAnew(pawn)) continue;
                
                var notTimeToTryAgain = Find.TickManager.TicksGame < bill.nextTickToSearchForIngredients + ReCheckFailedBillTicksRange.RandomInRange;
                if (notTimeToTryAgain && FloatMenuMakerMap.makingFor == null) continue;

                if (!bill.recipe.PawnSatisfiesSkillRequirements(pawn)) JobFailReason.Is(_missingSkillTranslated);
                else if (TryFindBestBillIngredients(bill, pawn, (Thing)giver, out var chosen)) return TryStartNewHolyWashJob(pawn, bill, giver, chosen);
            }

            return null;
        }
        
        private Job TryStartNewHolyWashJob(Pawn pawn, Bill bill, IBillGiver giver, Thing chosen)
        {
            var job = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, giver, null) 
                      ?? new Job(_jobDef, (Thing) giver, chosen)
            {
                count = 1,
                haulMode = HaulMode.ToCellNonStorage,
                bill = bill
            };

            return job;
        }

        private static bool TryFindBestBillIngredients(Bill bill, Pawn pawn, Thing billGiver, out Thing chosen)
        {
            var billGiverRootCell = GetBillGiverRootCell(billGiver, pawn);
            var regionIsInvalid = pawn.Map.regionGrid.GetValidRegionAt(billGiverRootCell) == null ;
            
            if (regionIsInvalid)
            {
                chosen = null;
                return false;
            }
            
            chosen = GenClosest.ClosestThingReachable(
                billGiverRootCell,
                pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                PathEndMode.Touch,
                TraverseParms.For(pawn),
                bill.ingredientSearchRadius,
                t => t.Spawned
                     && !t.IsForbidden(pawn)
                     && bill.recipe.fixedIngredientFilter.Allows(t)
                     && bill.ingredientFilter.Allows(t)
                     && bill.recipe.ingredients.Any(ingNeed => ingNeed.filter.Allows(t))
                     && pawn.CanReserve(t)
                     && (!bill.CheckIngredientsIfSociallyProper || t.IsSociallyProper(pawn))
            );

            return chosen != null;
        }

        private static IntVec3 GetBillGiverRootCell(Thing billGiver, Thing forPawn)
        {
            if (billGiver is not Building building) return billGiver.Position;
            if (building.def.hasInteractionCell) return building.InteractionCell;

            Log.Error("HolyWash :: Tried to find bill ingredients for " + billGiver + " which has no interaction cell.");
            return forPawn.Position;
        }
    }
}