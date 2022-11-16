using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;

namespace HolyWasher
{
    // ReSharper disable once InconsistentNaming
    public class JobDriver_HolyWash : JobDriver_DoBill
    {
        private readonly FieldInfo _apparelWornByCorpseInt = typeof(Apparel).GetField("wornByCorpseInt",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private float _workCycle;
        private float _workCycleProgress;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override Toil DoBill()
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
                    if (objectThing == null || objectThing.Destroyed)
                    {
                        actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                    }

                    _workCycleProgress -= actor.GetStatValue(StatDefOf.WorkToMake);

                    tableThing?.UsedThisTick();

                    if (!(_workCycleProgress <= 0))
                    {
                        return;
                    }

                    var skillDef = curJob.RecipeDef.workSkill;
                    if (skillDef != null)
                    {
                        var skill = actor.skills.GetSkill(skillDef);

                        skill?.Learn(0.11f * curJob.RecipeDef.workSkillLearnFactor);
                    }

                    actor.GainComfortFromCellIfPossible();

                    if (objectThing is Apparel mendApparel)
                    {
                        _apparelWornByCorpseInt.SetValue(mendApparel, false);
                    }

                    var list = new List<Thing> { objectThing };
                    curJob.bill.Notify_IterationCompleted(actor, list);

                    _workCycleProgress = _workCycle;
                    ReadyForNextToil();
                },
                defaultCompleteMode = ToilCompleteMode.Never
            };


            toil.WithEffect(() => curJob.bill.recipe.effectWorking, TableTi);
            toil.PlaySustainerOrSound(() => toil.actor.CurJob.bill.recipe.soundWorking);
            toil.WithProgressBar(TableTi, () => objectThing.HitPoints / (float)objectThing.MaxHitPoints,
                false, 0.5f);
            toil.FailOn(() => curJob.bill.suspended || curJob.bill.DeletedOrDereferenced ||
                              curJob.GetTarget(TableTi).Thing is IBillGiver billGiver &&
                              !billGiver.CurrentlyUsableForBills());
            return toil;
        }
    }
}