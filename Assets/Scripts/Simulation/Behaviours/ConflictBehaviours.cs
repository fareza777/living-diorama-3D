using LivingDiorama.Data;
using UnityEngine;

namespace LivingDiorama.Simulation.Behaviours
{
    /// <summary>Close distance on something you want to catch. The wolf-after-goblin beat.</summary>
    public sealed class ChaseBehaviour : CreatureBehaviour
    {
        public override float CooldownAfterExit => 8f;
        public override string Id => "chase";

        public override float Score(in BehaviourContext ctx)
        {
            if (ctx.Quarry == null || ctx.Quarry.IsKnockedOut) return 0f;
            if (!RelationRuleSet.IsAggressive(ctx.QuarryStance)) return 0f;

            // Already close enough to swing -- let Attack take over.
            if (ctx.QuarryDistance <= ctx.Def.attackRange) return 0f;

            float drive = 0.4f + ctx.Def.aggression * 0.6f;
            float closeness = BehaviourContext.Proximity(ctx.QuarryDistance, ctx.Settings.baseSightRadius * 1.3f);

            // A well fed predator is a lazy predator.
            float appetite = ctx.QuarryStance == Stance.Predatory
                ? Mathf.Lerp(0.45f, 1f, 1f - ctx.Self.Fullness)
                : 1f;

            // A thief in sight is worth chasing even on a full stomach.
            if (ctx.Quarry.HasRuntimeTag("thief")) appetite = 1f;

            return Mathf.Clamp01(drive * closeness * appetite * ctx.Activity * 0.95f);
        }

        public override void OnEnter(CreatureAgent self, in BehaviourContext ctx)
        {
            self.TargetAgent = ctx.Quarry;
            self.BehaviourTimer = ctx.Settings.maxFightDuration;
            SimEventBus.Publish(new SimEvent(SimEventKind.StartedChase, self, ctx.Quarry,
                self.Position, ctx.World.Clock.TotalHours));
        }

        public override void Tick(CreatureAgent self, in BehaviourContext ctx, float dt)
        {
            CreatureAgent quarry = self.TargetAgent;
            if (quarry == null || !quarry.IsActive || quarry.IsKnockedOut || self.BehaviourTimer <= 0f)
            {
                self.Halt();
                self.TargetAgent = null;
                return;
            }

            // Lead the target slightly so the pursuit reads as intent, not lag.
            Vector3 lead = quarry.Position + quarry.Velocity * 0.35f;
            self.MoveTowards(lead, ctx.Def.runSpeed);
            self.View?.SetGait(ctx.Def.runSpeed / Mathf.Max(0.01f, ctx.Def.walkSpeed));
        }

        public override void OnExit(CreatureAgent self) => self.TargetAgent = null;
    }

    /// <summary>Trade blows at close range. Nothing dies -- the loser is knocked out,
    /// sulks for a bit and gets back up.</summary>
    public sealed class AttackBehaviour : CreatureBehaviour
    {
        public override float CooldownAfterExit => 5f;
        public override string Id => "attack";

        public override float Score(in BehaviourContext ctx)
        {
            if (ctx.Quarry == null || ctx.Quarry.IsKnockedOut) return 0f;
            if (!RelationRuleSet.IsAggressive(ctx.QuarryStance)) return 0f;
            if (ctx.QuarryDistance > ctx.Def.attackRange * 1.35f) return 0f;

            // Beats chase whenever the target is actually in reach.
            return Mathf.Clamp01(0.82f + ctx.Def.aggression * 0.18f);
        }

        public override void OnEnter(CreatureAgent self, in BehaviourContext ctx)
        {
            self.TargetAgent = ctx.Quarry;
            self.BehaviourTimer = ctx.Settings.maxFightDuration;
        }

        public override void Tick(CreatureAgent self, in BehaviourContext ctx, float dt)
        {
            CreatureAgent target = self.TargetAgent;
            if (target == null || !target.IsActive || target.IsKnockedOut || self.BehaviourTimer <= 0f)
            {
                self.Halt();
                self.TargetAgent = null;
                return;
            }

            float dist = self.DistanceTo(target);
            if (dist > ctx.Def.attackRange)
            {
                self.MoveTowards(target.Position, ctx.Def.runSpeed);
                self.View?.SetGait(1.5f);
                return;
            }

            self.Halt();
            self.FaceWhileStill(target.Position);
            self.View?.SetGait(0f);

            if (self.ActionCooldown > 0f) return;

            self.ActionCooldown = ctx.Def.attackInterval;
            self.View?.PlayAttack();
            target.TakeDamage(ctx.Def.attackDamage, self);

            SimEventBus.Publish(new SimEvent(SimEventKind.Attacked, self, target,
                self.Position, ctx.World.Clock.TotalHours));
        }

        public override void OnExit(CreatureAgent self)
        {
            self.TargetAgent = null;
            self.ClearFacing();
        }
    }
}
