using UnityEngine;

namespace LivingDiorama.Simulation.Behaviours
{
    /// <summary>Stand around and look about. The floor of the utility system: it always
    /// scores something, so a creature is never without a valid thing to do.</summary>
    public sealed class IdleBehaviour : CreatureBehaviour
    {
        public override string Id => "idle";

        public override float Score(in BehaviourContext ctx)
        {
            // Slightly more appealing when the creature is out of step with its cycle.
            return 0.14f + (1f - ctx.Activity) * 0.10f;
        }

        public override void OnEnter(CreatureAgent self, in BehaviourContext ctx)
        {
            self.Halt();
            self.BehaviourTimer = Random.Range(1.5f, 4.5f);
            self.View?.SetGait(0f);
        }

        public override void Tick(CreatureAgent self, in BehaviourContext ctx, float dt)
        {
            self.Halt();

            // Glance at whatever is nearest and interesting.
            CreatureAgent focus = ctx.Companion != null ? ctx.Companion : ctx.Quarry;
            if (focus != null && ctx.Def.curiosity > 0.3f)
            {
                self.FaceWhileStill(focus.Position);
            }
            else
            {
                self.ClearFacing();
            }
        }

        public override void OnExit(CreatureAgent self) => self.ClearFacing();
    }

    /// <summary>Amble to a nearby point. The default "living" behaviour that makes the
    /// diorama read as inhabited rather than posed.</summary>
    public sealed class WanderBehaviour : CreatureBehaviour
    {
        public override string Id => "wander";

        public override float Score(in BehaviourContext ctx)
        {
            float restless = 0.22f + ctx.Def.curiosity * 0.24f;
            // Hungry or exhausted creatures have better things to do.
            float distraction = Mathf.Min(1f - ctx.Self.Fullness, 1f - ctx.Self.Energy);
            return Mathf.Clamp01(restless * ctx.Activity * (1f - distraction * 0.6f));
        }

        public override void OnEnter(CreatureAgent self, in BehaviourContext ctx)
        {
            PickDestination(self, ctx);
            self.BehaviourTimer = Random.Range(4f, 9f);
        }

        public override void Tick(CreatureAgent self, in BehaviourContext ctx, float dt)
        {
            if (self.AtTarget(0.35f) || self.BehaviourTimer <= 0f)
            {
                PickDestination(self, ctx);
                self.BehaviourTimer = Random.Range(4f, 9f);
            }

            self.MoveTowards(self.TargetPoint, ctx.Def.walkSpeed);
            self.View?.SetGait(1f);
        }

        static void PickDestination(CreatureAgent self, in BehaviourContext ctx)
        {
            float range = 2.5f + ctx.Def.curiosity * 3.5f;
            self.TargetPoint = ctx.Surface.RandomPoint(self.Position, range);
        }
    }

    /// <summary>Find a quiet spot and sleep. Nocturnal creatures run this in daylight,
    /// which is what makes a skeleton feel different from a goblin without extra code.</summary>
    public sealed class SleepBehaviour : CreatureBehaviour
    {
        public override float CooldownAfterExit => 12f;
        public override string Id => "sleep";

        public override float Score(in BehaviourContext ctx)
        {
            // Danger trumps tiredness -- nothing sleeps with a wolf breathing on it.
            //
            // The radius shrinks once the creature is already down, because a wolf
            // wandering back and forth across a fixed line woke it every time it crossed
            // inward and let it settle every time it crossed out. Waking needs something
            // genuinely close, not merely something in the neighbourhood.
            bool alreadyAsleep = ctx.Self.Current is SleepBehaviour;
            float alarm = ctx.Settings.baseSightRadius * (alreadyAsleep ? 0.3f : 0.6f);
            if (ctx.Threat != null && ctx.ThreatDistance < alarm) return 0f;

            // Once asleep, stay asleep until actually rested.
            //
            // Scoring purely on tiredness meant a creature woke the instant its energy
            // crossed back over the threshold it had fallen below, dozed off again a few
            // seconds later, and spent the night flickering between the two. Sleep is not
            // a thing you do for one second at a time.
            if (alreadyAsleep) return ctx.Self.Energy < 0.92f ? 1f : 0f;

            float tired = Mathf.Max(0f, (1f - ctx.Self.Energy) - 0.55f) * 2.2f;
            float offCycle = (1f - ctx.Activity) * 0.4f;
            return Mathf.Clamp01(tired + offCycle);
        }

        public override void OnEnter(CreatureAgent self, in BehaviourContext ctx)
        {
            // Sleep a little away from the crowd.
            self.TargetPoint = ctx.Surface.RandomPoint(self.Position, 2f);
            self.BehaviourTimer = 90f;
            SimEventBus.Publish(new SimEvent(SimEventKind.FellAsleep, self, null,
                self.Position, ctx.World.Clock.TotalHours));
        }

        public override void Tick(CreatureAgent self, in BehaviourContext ctx, float dt)
        {
            if (!self.AtTarget(0.3f))
            {
                self.MoveTowards(self.TargetPoint, ctx.Def.walkSpeed * 0.6f);
                self.View?.SetGait(0.6f);
                return;
            }

            self.Halt();
            self.ClearFacing();
            self.View?.SetGait(0f);
            self.View?.SetSleeping(true);
        }

        public override void OnExit(CreatureAgent self)
        {
            self.View?.SetSleeping(false);
            if (self.World != null)
            {
                SimEventBus.Publish(new SimEvent(SimEventKind.WokeUp, self, null,
                    self.Position, self.World.Clock.TotalHours));
            }
        }
    }
}
