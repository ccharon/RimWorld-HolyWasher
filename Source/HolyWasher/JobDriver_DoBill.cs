using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HolyWasher
{
    // ReSharper disable once InconsistentNaming
    public abstract class JobDriver_DoBill : JobDriver
    {
        protected const TargetIndex TableTi = TargetIndex.A;
        private protected const TargetIndex ObjectTi = TargetIndex.B;
        private const TargetIndex HaulTi = TargetIndex.C;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TableTi);
            this.FailOnBurningImmobile(ObjectTi);
            this.FailOnDestroyedNullOrForbidden(ObjectTi);
            this.FailOnBurningImmobile(ObjectTi);
            yield return Toils_Reserve.Reserve(TableTi);
            yield return Toils_Reserve.Reserve(ObjectTi);
            yield return Toils_Goto.GotoThing(ObjectTi, PathEndMode.Touch);
            yield return Toils_Haul.StartCarryThing(ObjectTi);
            yield return Toils_Goto.GotoThing(TableTi, PathEndMode.InteractionCell);
            yield return Toils_Haul.PlaceHauledThingInCell(TableTi, null, false);
            yield return DoBill();
            yield return Store();
            yield return Toils_Reserve.Reserve(HaulTi);
            yield return Toils_Haul.CarryHauledThingToCell(HaulTi);
            yield return Toils_Haul.PlaceHauledThingInCell(HaulTi, null, false);
            yield return Toils_Reserve.Release(ObjectTi);
            yield return Toils_Reserve.Release(HaulTi);
            yield return Toils_Reserve.Release(TableTi);
        }

        protected abstract Toil DoBill();

        private Toil Store()
        {
            var toil = new Toil();
            toil.initAction = delegate
            {
                var actor = toil.actor;
                var curJob = actor.jobs.curJob;
                var objectThing = curJob.GetTarget(ObjectTi).Thing;

                if (curJob.bill.GetStoreMode() != BillStoreModeDefOf.DropOnFloor)
                {
                    if (StoreUtility.TryFindBestBetterStoreCellFor(objectThing, actor, actor.Map,
                        StoragePriority.Unstored, actor.Faction, out var vec))
                    {
                        actor.carryTracker.TryStartCarry(objectThing, 1);
                        curJob.SetTarget(HaulTi, vec);
                        curJob.count = 99999;
                        return;
                    }
                }

                actor.carryTracker.TryStartCarry(objectThing, 1);
                actor.carryTracker.TryDropCarriedThing(actor.Position, ThingPlaceMode.Near, out objectThing);

                actor.jobs.EndCurrentJob(JobCondition.Succeeded);
            };
            return toil;
        }

        public override string GetReport()
        {
            return pawn.jobs.curJob.RecipeDef != null ? base.ReportStringProcessed(pawn.jobs.curJob.RecipeDef.jobString) : base.GetReport();
        }
    }
}