using LivingDiorama.Data;
using UnityEngine;

namespace LivingDiorama.Simulation.Behaviours
{
    /// <summary>Walk to the nearest edible thing and eat it.</summary>
    public sealed class SeekFoodBehaviour : CreatureBehaviour
    {
        public override float CooldownAfterExit => 6f;
        public override string Id => "seek_food";

        public override float Score(in BehaviourContext ctx)
        {
            if (ctx.Def.diet == Diet.None) return 0f;
            if (ctx.Food == null) return 0f;

            float hunger = 1f - ctx.Self.Fullness;
            float threshold = 1f - ctx.Settings.hungerSeekThreshold;
            float need = Mathf.Clamp01((hunger - threshold) * 2.2f);
            if (need <= 0f) return 0f;

            float reachable = 0.55f + 0.45f * BehaviourContext.Proximity(
                ctx.FoodDistance, ctx.Settings.baseSightRadius * 2f);
            return Mathf.Clamp01(need * reachable);
        }

        public override void OnEnter(CreatureAgent self, in BehaviourContext ctx)
        {
            self.TargetFood = ctx.Food;
            self.BehaviourTimer = 20f;
        }

        public override void Tick(CreatureAgent self, in BehaviourContext ctx, float dt)
        {
            if (self.TargetFood == null || !self.TargetFood.HasFood)
            {
                self.TargetFood = ctx.Food;
                if (self.TargetFood == null) return;
            }

            Vector3 foodPos = self.TargetFood.Position;
            float dist = self.DistanceTo(foodPos);

            if (dist > 0.55f)
            {
                // Trot to food when properly hungry, amble when merely peckish.
                float speed = self.Fullness < 0.2f ? ctx.Def.runSpeed * 0.8f : ctx.Def.walkSpeed;
                self.MoveTowards(foodPos, speed);
                self.View?.SetGait(speed / Mathf.Max(0.01f, ctx.Def.walkSpeed));
                return;
            }

            self.Halt();
            self.FaceWhileStill(foodPos);
            self.View?.SetGait(0f);

            if (self.ActionCooldown > 0f) return;

            if (self.TargetFood.Consume(ctx.World.Clock.TotalHours))
            {
                self.Feed(ctx.Settings.foodRestoreAmount);
                self.ActionCooldown = 1.2f;
                self.View?.PlayEat();
                SimEventBus.Publish(new SimEvent(SimEventKind.Ate, self, null,
                    self.Position, ctx.World.Clock.TotalHours));
            }
            else
            {
                self.TargetFood = null;
            }
        }

        public override void OnExit(CreatureAgent self)
        {
            self.TargetFood = null;
            self.ClearFacing();
        }
    }

    /// <summary>Put distance between yourself and whatever is frightening you.</summary>
    public sealed class FleeBehaviour : CreatureBehaviour
    {
        public override string Id => "flee";

        public override float Score(in BehaviourContext ctx)
        {
            if (ctx.Threat == null || ctx.Threat.IsKnockedOut) return 0f;

            float weight = ctx.ThreatStance switch
            {
                Stance.Fearful => 1f,
                Stance.Predatory => 1f,   // being hunted is terrifying regardless of tags
                Stance.Wary => 0.45f,
                Stance.Hostile => 0.6f,
                _ => 0f,
            };
            if (weight <= 0f) return 0f;

            float panic = BehaviourContext.Proximity(ctx.ThreatDistance, ctx.Settings.baseSightRadius);
            // Brave creatures hold their nerve a little longer.
            float nerve = 1f - ctx.Def.bravery * 0.35f;
            return Mathf.Clamp01(weight * Mathf.Pow(panic, 0.7f) * nerve * 1.15f);
        }

        public override void OnEnter(CreatureAgent self, in BehaviourContext ctx)
        {
            self.TargetAgent = ctx.Threat;
            self.BehaviourTimer = 8f;
            self.View?.PlayStartle();

            if (ctx.Threat != null)
            {
                SimEventBus.Publish(new SimEvent(SimEventKind.Intimidated, ctx.Threat, self,
                    self.Position, ctx.World.Clock.TotalHours));
            }
        }

        public override void Tick(CreatureAgent self, in BehaviourContext ctx, float dt)
        {
            CreatureAgent threat = self.TargetAgent != null && self.TargetAgent.IsActive
                ? self.TargetAgent
                : ctx.Threat;

            if (threat == null)
            {
                self.Halt();
                return;
            }

            Vector3 away = self.Position - threat.Position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.001f) away = Random.insideUnitSphere;
            away.Normalize();

            // Aim past the safe distance so the creature keeps running rather than
            // stopping the instant it is technically clear.
            Vector3 goal = self.Position + away * (ctx.Settings.fleeSafeDistance * 0.6f);
            goal = ctx.Surface.ClampInside(goal);

            self.MoveTowards(goal, ctx.Def.runSpeed);
            self.View?.SetGait(ctx.Def.runSpeed / Mathf.Max(0.01f, ctx.Def.walkSpeed));

            if (self.DistanceTo(threat) > ctx.Settings.fleeSafeDistance)
            {
                SimEventBus.Publish(new SimEvent(SimEventKind.EscapedChase, self, threat,
                    self.Position, ctx.World.Clock.TotalHours));
                self.TargetAgent = null;
                self.BehaviourTimer = 0f;
            }
        }

        public override void OnExit(CreatureAgent self) => self.TargetAgent = null;
    }
}
