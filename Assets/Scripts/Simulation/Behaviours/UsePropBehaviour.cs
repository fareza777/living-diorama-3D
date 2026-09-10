using LivingDiorama.Diorama;
using UnityEngine;

namespace LivingDiorama.Simulation.Behaviours
{
    /// <summary>
    /// Walk to something the player put down, and use it.
    ///
    /// One behaviour for all four objects rather than four nearly identical ones. What
    /// differs between sleeping in a bed and hitting a training dummy is which need it
    /// serves, which animation plays and how long it takes -- all of that is data, and
    /// none of it is control flow. A fifth object is a row in a table, not a new class.
    ///
    /// The part that matters for how it feels is the noticing. A creature that turns and
    /// looks at the drum for a beat before walking over reads as having decided; one that
    /// simply starts walking reads as having been told. It is one second of standing
    /// still, and it is most of the difference between a simulation and a character.
    /// </summary>
    public abstract class UsePropBehaviour : CreatureBehaviour
    {
        protected abstract PlaceableRole Role { get; }

        /// <summary>How badly the creature wants this, 0 to 1, before distance and mood.
        /// </summary>
        protected abstract float Appetite(in BehaviourContext ctx);

        /// <summary>Animator state, and the procedural fallback for creatures without
        /// clips.</summary>
        protected abstract string Clip { get; }

        protected virtual float Seconds => 12f;

        /// <summary>What using it does to the creature, per in-game hour spent.</summary>
        protected abstract void Benefit(CreatureAgent self, float hours);

        public override float Score(in BehaviourContext ctx)
        {
            if (ctx.Placements == null) return 0f;

            float appetite = Appetite(in ctx);
            if (appetite <= 0.01f) return 0f;

            Placement target = ctx.Placements.FindFree(Role, ctx.Self.Position, ctx.Self.InstanceId);
            if (target == null) return 0f;

            // Closer is more tempting, but only mildly: a creature that ignores the bed
            // across the garden in favour of standing where it is has no inner life.
            float distance = Vector3.Distance(ctx.Self.Position, target.Position);
            float reach = Mathf.Clamp01(1f - distance / Mathf.Max(0.1f, target.Definition.Draw) * 0.45f);

            return Mathf.Clamp01(appetite * reach);
        }

        public override void OnEnter(CreatureAgent self, in BehaviourContext ctx)
        {
            Placement target = ctx.Placements?.FindFree(Role, self.Position, self.InstanceId);
            if (target == null)
            {
                self.BehaviourTimer = 0f;
                return;
            }

            ctx.Placements.Claim(target, self.InstanceId);
            self.TargetPlacement = target;
            self.TargetPoint = target.UseSpot;
            self.BehaviourTimer = Seconds;

            // Look at it first. The walk starts a beat later, from Tick.
            self.Halt();
            self.FaceWhileStill(target.Position);
            _noticing = NoticeSeconds;
        }

        /// <summary>The pause before setting off. Short enough not to read as hesitation,
        /// long enough to read as a decision.</summary>
        const float NoticeSeconds = 0.75f;

        float _noticing;

        public override void Tick(CreatureAgent self, in BehaviourContext ctx, float dt)
        {
            Placement target = self.TargetPlacement;
            if (target == null) return;

            if (_noticing > 0f)
            {
                _noticing -= dt;
                self.Halt();
                self.FaceWhileStill(target.Position);
                return;
            }

            if (!self.AtTarget(ctx.Def.bodyRadius + 0.28f))
            {
                self.MoveTowards(target.UseSpot, ctx.Def.walkSpeed);
                self.View?.SetGait(1f);
                return;
            }

            self.Halt();
            self.View?.SetGait(0f);
            self.FaceWhileStill(target.Position);

            if (!_using)
            {
                _using = true;
                self.View?.SetBehaviour(Clip);
                SimEventBus.Publish(new SimEvent(SimEventKind.UsedPlacement, self, null,
                    target.Position, ctx.World.Clock.TotalHours));
            }

            Benefit(self, dt * ctx.World.Clock.HoursPerRealSecond);
        }

        bool _using;

        public override void OnExit(CreatureAgent self)
        {
            _using = false;
            _noticing = 0f;

            self.World?.Placements?.Release(self.InstanceId);
            self.TargetPlacement = null;
            self.ClearFacing();
            self.View?.SetSleeping(false);
        }
    }

    /// <summary>A bed. Recovers energy far faster than sleeping on the ground, which is
    /// the whole reason to build one.</summary>
    public sealed class SleepInBedBehaviour : UsePropBehaviour
    {
        public override string Id => "sleep_bed";
        protected override PlaceableRole Role => PlaceableRole.Rest;
        protected override string Clip => "sleep";
        protected override float Seconds => 60f;

        protected override float Appetite(in BehaviourContext ctx)
        {
            if (ctx.Threat != null && ctx.ThreatDistance < ctx.Settings.baseSightRadius * 0.4f) return 0f;

            // Once in bed, stay in bed -- the same hysteresis the ground sleep needed
            // after creatures spent whole nights flickering awake and back.
            if (ctx.Self.Current is SleepInBedBehaviour) return ctx.Self.Energy < 0.95f ? 1f : 0f;

            float tired = Mathf.Max(0f, (1f - ctx.Self.Energy) - 0.40f) * 2.2f;
            return Mathf.Clamp01(tired + (1f - ctx.Activity) * 0.45f);
        }

        protected override void Benefit(CreatureAgent self, float hours)
        {
            self.View?.SetSleeping(true);
            self.Energy = Mathf.Clamp01(self.Energy + hours * 0.9f);
        }
    }

    /// <summary>A bowl someone keeps filled. Beats foraging, and never runs out the way a
    /// bush does.</summary>
    public sealed class EatFromBowlBehaviour : UsePropBehaviour
    {
        public override string Id => "eat_bowl";
        protected override PlaceableRole Role => PlaceableRole.Eat;
        protected override string Clip => "eat";
        protected override float Seconds => 14f;

        protected override float Appetite(in BehaviourContext ctx)
        {
            float hunger = 1f - ctx.Self.Fullness;
            return Mathf.Clamp01((hunger - 0.30f) * 2.4f);
        }

        protected override void Benefit(CreatureAgent self, float hours)
        {
            self.Feed(hours * 1.6f);
            self.View?.PlayEat();
        }
    }

    /// <summary>The training dummy. Costs energy, and is the only thing in the diorama a
    /// creature can visibly get better at.</summary>
    public sealed class TrainAtDummyBehaviour : UsePropBehaviour
    {
        public override string Id => "train";
        protected override PlaceableRole Role => PlaceableRole.Train;
        protected override string Clip => "attack";
        protected override float Seconds => 16f;

        protected override float Appetite(in BehaviourContext ctx)
        {
            // Too small to swing at anything, too tired, or too hungry: not now.
            if (ctx.Self.Stage == LifeStage.Baby) return 0f;
            if (ctx.Self.Energy < 0.4f || ctx.Self.Fullness < 0.35f) return 0f;

            return Mathf.Clamp01(ctx.Def.aggression * 0.6f + (1f - ctx.Self.Fun) * 0.5f) * ctx.Activity;
        }

        protected override void Benefit(CreatureAgent self, float hours)
        {
            self.Energy = Mathf.Clamp01(self.Energy - hours * 0.35f);
            self.Fun = Mathf.Clamp01(self.Fun + hours * 0.8f);
            self.View?.PlayAttack();
        }
    }

    /// <summary>The drum. Pure fun, and the one thing a creature will do purely because it
    /// wants to.</summary>
    public sealed class PlayDrumBehaviour : UsePropBehaviour
    {
        public override string Id => "drum";
        protected override PlaceableRole Role => PlaceableRole.Play;
        protected override string Clip => "celebrate";
        protected override float Seconds => 13f;

        protected override float Appetite(in BehaviourContext ctx)
        {
            if (ctx.Self.Energy < 0.25f) return 0f;

            float bored = 1f - ctx.Self.Fun;
            return Mathf.Clamp01((bored - 0.25f) * 1.9f + ctx.Def.curiosity * 0.25f) * ctx.Activity;
        }

        protected override void Benefit(CreatureAgent self, float hours)
        {
            self.Fun = Mathf.Clamp01(self.Fun + hours * 1.4f);
            self.Social = Mathf.Clamp01(self.Social + hours * 0.3f);
        }
    }
}
