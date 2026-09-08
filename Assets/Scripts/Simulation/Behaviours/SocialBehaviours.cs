using UnityEngine;

namespace LivingDiorama.Simulation.Behaviours
{
    /// <summary>Hang around another creature you get on with.</summary>
    public sealed class SocialiseBehaviour : CreatureBehaviour
    {
        public override float CooldownAfterExit => 15f;
        public override string Id => "socialise";

        public override float Score(in BehaviourContext ctx)
        {
            if (ctx.Companion == null || ctx.Companion.IsKnockedOut) return 0f;

            float lonely = 1f - ctx.Self.Social;
            float closeness = BehaviourContext.Proximity(ctx.CompanionDistance, ctx.Settings.baseSightRadius);
            return Mathf.Clamp01(lonely * ctx.Def.sociability * closeness * ctx.Activity * 0.85f);
        }

        public override void OnEnter(CreatureAgent self, in BehaviourContext ctx)
        {
            self.TargetAgent = ctx.Companion;
            self.BehaviourTimer = Random.Range(5f, 11f);
            SimEventBus.Publish(new SimEvent(SimEventKind.MadeFriend, self, ctx.Companion,
                self.Position, ctx.World.Clock.TotalHours));
        }

        public override void Tick(CreatureAgent self, in BehaviourContext ctx, float dt)
        {
            CreatureAgent friend = self.TargetAgent;
            if (friend == null || !friend.IsActive || self.BehaviourTimer <= 0f)
            {
                self.Halt();
                return;
            }

            float dist = self.DistanceTo(friend);
            float comfortable = (ctx.Def.bodyRadius + friend.Definition.bodyRadius) * 2.4f;

            if (dist > comfortable)
            {
                self.MoveTowards(friend.Position, ctx.Def.walkSpeed);
                self.View?.SetGait(1f);
                return;
            }

            self.Halt();
            self.FaceWhileStill(friend.Position);
            self.View?.SetGait(0f);
            self.AddSocial(0.22f * dt);
            self.AddFun(0.10f * dt);
            self.View?.PlayEmote(Mood.Social);
        }

        public override void OnExit(CreatureAgent self)
        {
            self.TargetAgent = null;
            self.ClearFacing();
        }
    }

    /// <summary>Splash about at the water's edge. The slime's signature idle.</summary>
    public sealed class PlayInWaterBehaviour : CreatureBehaviour
    {
        public override float CooldownAfterExit => 20f;
        public override string Id => "play_water";

        public override float Score(in BehaviourContext ctx)
        {
            if (!ctx.HasWater || ctx.Def.lovesWater <= 0.05f) return 0f;
            if (ctx.Threat != null && ctx.ThreatDistance < ctx.Settings.baseSightRadius * 0.7f) return 0f;

            float bored = 0.35f + 0.65f * (1f - ctx.Self.Fun);
            float reach = BehaviourContext.Proximity(ctx.WaterDistance, ctx.Settings.baseSightRadius * 1.6f);
            return Mathf.Clamp01(ctx.Def.lovesWater * bored * reach * ctx.Activity);
        }

        public override void OnEnter(CreatureAgent self, in BehaviourContext ctx)
        {
            self.TargetPoint = ctx.WaterPoint;
            self.BehaviourTimer = Random.Range(6f, 14f);
            SimEventBus.Publish(new SimEvent(SimEventKind.PlayedInWater, self, null,
                self.Position, ctx.World.Clock.TotalHours));
        }

        public override void Tick(CreatureAgent self, in BehaviourContext ctx, float dt)
        {
            if (!self.AtTarget(0.4f))
            {
                self.MoveTowards(self.TargetPoint, ctx.Def.walkSpeed * 1.15f);
                self.View?.SetGait(1.2f);
                return;
            }

            // Bounce around a small area rather than standing in the shallows.
            if (self.BehaviourTimer % 2.5f < dt)
            {
                self.TargetPoint = ctx.Surface.ClampInside(
                    self.Position + Random.insideUnitSphere.normalized * 0.8f);
            }

            self.MoveTowards(self.TargetPoint, ctx.Def.walkSpeed * 0.9f);
            self.AddFun(0.28f * dt);
            self.View?.SetGait(0.9f);
            self.View?.PlaySplash();
        }
    }

    /// <summary>
    /// Raid the tile stockpile and leg it. Publishes a theft event and picks up a
    /// transient "thief" tag, which is what makes the knight go hostile and the wolf
    /// give chase -- none of that is hard-coded against goblins.
    /// </summary>
    public sealed class StealFoodBehaviour : CreatureBehaviour
    {
        const float StolenTagHours = 0.6f;

        public override float CooldownAfterExit => 25f;
        public override string Id => "steal";

        public override float Score(in BehaviourContext ctx)
        {
            if (ctx.Def.mischief < 0.15f) return 0f;
            if (ctx.Self.CarryingStolenFood) return 0.75f;   // finish the getaway

            FoodNode stockpile = ctx.World.FindStockpile(ctx.Self.Position, out float dist);
            if (stockpile == null || !stockpile.HasFood) return 0f;

            float greed = 0.35f + 0.65f * (1f - ctx.Self.Fullness);
            float reach = BehaviourContext.Proximity(dist, ctx.Settings.baseSightRadius * 2f);
            return Mathf.Clamp01(ctx.Def.mischief * greed * reach * ctx.Activity * 1.05f);
        }

        public override void OnEnter(CreatureAgent self, in BehaviourContext ctx)
        {
            self.TargetFood = ctx.World.FindStockpile(self.Position, out _);
            self.BehaviourTimer = 25f;
        }

        public override void Tick(CreatureAgent self, in BehaviourContext ctx, float dt)
        {
            if (self.CarryingStolenFood)
            {
                RunAway(self, ctx);
                return;
            }

            if (self.TargetFood == null || !self.TargetFood.HasFood)
            {
                self.TargetFood = ctx.World.FindStockpile(self.Position, out _);
                if (self.TargetFood == null)
                {
                    self.Halt();
                    return;
                }
            }

            Vector3 target = self.TargetFood.Position;
            if (self.DistanceTo(target) > 0.6f)
            {
                // Sneak in, sprint out.
                self.MoveTowards(target, ctx.Def.walkSpeed * 0.85f);
                self.View?.SetGait(0.8f);
                return;
            }

            self.Halt();
            self.FaceWhileStill(target);

            if (self.ActionCooldown > 0f) return;

            if (self.TargetFood.Consume(ctx.World.Clock.TotalHours))
            {
                self.CarryingStolenFood = true;
                self.Feed(ctx.Settings.foodRestoreAmount * 0.8f);
                self.ActionCooldown = 0.8f;
                self.AddRuntimeTag("thief", StolenTagHours);
                self.View?.PlaySteal();
                self.TargetPoint = ctx.Surface.RandomPoint(self.Position, 6f);

                SimEventBus.Publish(new SimEvent(SimEventKind.StoleFood, self, null,
                    self.Position, ctx.World.Clock.TotalHours));
            }
            else
            {
                self.TargetFood = null;
            }
        }

        static void RunAway(CreatureAgent self, in BehaviourContext ctx)
        {
            self.ClearFacing();
            if (self.AtTarget(0.5f) || self.BehaviourTimer <= 0f)
            {
                self.CarryingStolenFood = false;
                self.AddFun(0.35f);
                self.Halt();
                return;
            }

            self.MoveTowards(self.TargetPoint, ctx.Def.runSpeed);
            self.View?.SetGait(1.6f);
        }

        public override void OnExit(CreatureAgent self)
        {
            self.TargetFood = null;
            self.CarryingStolenFood = false;
            self.ClearFacing();
        }
    }
}
