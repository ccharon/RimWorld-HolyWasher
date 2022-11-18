using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;

namespace HolyWasher
{
    // ReSharper disable once InconsistentNaming
    // ReSharper disable once UnusedType.Global
    public class JobDriver_HolyWash : JobDriver
    {
        private const TargetIndex TableTi = TargetIndex.A;
        private const TargetIndex ObjectTi = TargetIndex.B;
        private const TargetIndex HaulTi = TargetIndex.C;
        
        private readonly FieldInfo _apparelWornByCorpseInt = typeof(Apparel).GetField("wornByCorpseInt", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private float _workCycle;
        private float _workCycleProgress;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TableTi);
            this.FailOnBurningImmobile(TableTi);
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
        
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        private static Toil Store()
        {
            var toil = new Toil();
            toil.initAction = delegate
            {
                var actor = toil.actor;
                var curJob = actor.jobs.curJob;
                var objectThing = curJob.GetTarget(ObjectTi).Thing;

                var shouldNotBeDropped = curJob.bill.GetStoreMode() != BillStoreModeDefOf.DropOnFloor;
                var hasBetterPlace = StoreUtility.TryFindBestBetterStoreCellFor(objectThing, actor, actor.Map, StoragePriority.Unstored, actor.Faction, out var vec);
                
                if (shouldNotBeDropped && hasBetterPlace)
                {
                    actor.carryTracker.TryStartCarry(objectThing, 1);
                    curJob.SetTarget(HaulTi, vec); 
                    curJob.count = 99999;
                    return;
                }
                
                actor.carryTracker.TryStartCarry(objectThing, 1);
                actor.carryTracker.TryDropCarriedThing(actor.Position, ThingPlaceMode.Near, out objectThing);
                actor.jobs.EndCurrentJob(JobCondition.Succeeded);
            };
            return toil;
        }
        
        private Toil DoBill()
        {
            var actor = GetActor();
            var curJob = actor.jobs.curJob;
            var objectThing = curJob.GetTarget(ObjectTi).Thing;
            var tableThing = curJob.GetTarget(TableTi).Thing as Building_WorkTable;

            var toil = new Toil
            {
                initAction = delegate
                {
                    curJob.bill.Notify_DoBillStarted(actor);
                    _workCycleProgress = _workCycle = Math.Max(curJob.bill.recipe.workAmount, 10f);
                },
                tickAction = delegate
                {
                    if (objectThing == null || objectThing.Destroyed) actor.jobs.EndCurrentJob(JobCondition.Incompletable);

                    _workCycleProgress -= actor.GetStatValue(StatDefOf.WorkToMake);

                    tableThing?.UsedThisTick();

                    if (!(_workCycleProgress <= 0)) return;

                    var skillDef = curJob.RecipeDef.workSkill;
                    if (skillDef != null)
                    {
                        var skill = actor.skills.GetSkill(skillDef);
                        skill?.Learn(0.11f * curJob.RecipeDef.workSkillLearnFactor);
                    }

                    actor.GainComfortFromCellIfPossible();

                    if (objectThing is Apparel mendApparel)  _apparelWornByCorpseInt.SetValue(mendApparel, false);

                    var list = new List<Thing> { objectThing };
                    curJob.bill.Notify_IterationCompleted(actor, list);

                    _workCycleProgress = _workCycle;
                    ReadyForNextToil();
                },
                defaultCompleteMode = ToilCompleteMode.Never
            };
            
            toil.WithEffect(() => curJob.bill.recipe.effectWorking, TableTi);
            toil.PlaySustainerOrSound(() => toil.actor.CurJob.bill.recipe.soundWorking);
            toil.WithProgressBar(TableTi, () => objectThing.HitPoints / (float)objectThing.MaxHitPoints, false, 0.5f);
            toil.FailOn(() => curJob.bill.suspended 
                              || curJob.bill.DeletedOrDereferenced 
                              || (curJob.GetTarget(TableTi).Thing is IBillGiver billGiver && !billGiver.CurrentlyUsableForBills()));
            return toil;
        }
        
        public override string GetReport()
        {
            return pawn.jobs.curJob.RecipeDef != null ? base.ReportStringProcessed(pawn.jobs.curJob.RecipeDef.jobString) : base.GetReport();
        }
    }
}