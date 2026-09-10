using LivingDiorama.Presentation.Rigging;
using NUnit.Framework;
using UnityEngine;

namespace LivingDiorama.Tests
{
    /// <summary>
    /// The auto-rigger decides where a creature's joints are and which bone each vertex
    /// follows. Both are easy to get wrong in ways that are invisible until a creature
    /// walks: a hip an inch too high, a thigh bound to the pelvis, a weight that does not
    /// add up. None of that throws, and none of it shows in a still. So it is measured.
    /// </summary>
    public sealed class ProceduralRigTests
    {
        /// <summary>A creature-sized box standing on the ground, as every model is after
        /// the factory normalises it.</summary>
        static Bounds Body(float height = 0.8f, float width = 0.4f, float depth = 0.5f)
            => new(new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, depth));

        static readonly RigShape[] Shapes =
        {
            RigShape.Biped, RigShape.Quadruped, RigShape.Winged, RigShape.Blob,
        };

        [Test]
        public void EveryPlan_IsAWellFormedHierarchy([ValueSource(nameof(Shapes))] RigShape shape)
        {
            RigPlan plan = ProceduralRig.Build(shape, Body());

            Assert.IsNotNull(plan, $"{shape} has no plan");
            Assert.Greater(plan.Count, 2, $"{shape} is too small to be a skeleton");

            int roots = 0;
            for (int i = 0; i < plan.Count; i++)
            {
                RigJoint joint = plan.Joints[i];

                if (joint.Parent < 0) roots++;

                // A bone list is walked front to back when it is built, so a child that
                // came before its parent would be positioned against a bone that does not
                // exist yet.
                Assert.Less(joint.Parent, i, $"{joint.Name} is declared before its parent");
                Assert.IsFalse(float.IsNaN(joint.Position.x + joint.Position.y + joint.Position.z),
                    $"{joint.Name} is at no position at all");
            }

            Assert.AreEqual(1, roots, $"{shape} should have exactly one root");
        }

        /// <summary>Bones outside the mesh drag the surface with them and tear the
        /// silhouette apart.</summary>
        [Test]
        public void EveryJoint_SitsInsideTheBody([ValueSource(nameof(Shapes))] RigShape shape)
        {
            Bounds body = Body();
            RigPlan plan = ProceduralRig.Build(shape, body);

            Bounds allowed = body;
            allowed.Expand(0.02f);

            foreach (RigJoint joint in plan.Joints)
            {
                Assert.IsTrue(allowed.Contains(joint.Position),
                    $"{joint.Name} at {joint.Position} is outside the body {body}");
            }
        }

        [Test]
        public void Legs_AreSymmetricAndReachTheFloor()
        {
            Bounds body = Body();
            RigPlan plan = ProceduralRig.Build(RigShape.Biped, body);

            float left = float.NaN, right = float.NaN;
            int feet = 0;

            foreach (RigJoint joint in plan.Joints)
            {
                if (joint.Role != JointRole.Foot) continue;

                feet++;
                Assert.Less(joint.Position.y - body.min.y, body.size.y * 0.1f,
                    $"{joint.Name} is nowhere near the ground");

                if (joint.Side < 0f) left = joint.Position.x;
                else right = joint.Position.x;
            }

            Assert.AreEqual(2, feet, "a biped has two feet");
            Assert.AreEqual(-left, right, 1e-4f, "the feet are not a mirror image");
        }

        [Test]
        public void Quadruped_HasFourFeetAtBothEnds()
        {
            RigPlan plan = ProceduralRig.Build(RigShape.Quadruped, Body());

            int front = 0, back = 0;
            foreach (RigJoint joint in plan.Joints)
            {
                if (joint.Role != JointRole.Foot) continue;
                if (joint.Along > 0f) front++;
                else if (joint.Along < 0f) back++;
            }

            Assert.AreEqual(2, front, "two feet at the front");
            Assert.AreEqual(2, back, "two feet at the back");
        }

        [Test]
        public void Dragon_HasWingsAndTheWolfDoesNot()
        {
            int Wings(RigShape shape)
            {
                int n = 0;
                foreach (RigJoint joint in ProceduralRig.Build(shape, Body()).Joints)
                {
                    if (joint.Role is JointRole.Wing or JointRole.WingTip) n++;
                }
                return n;
            }

            Assert.AreEqual(4, Wings(RigShape.Winged));
            Assert.AreEqual(0, Wings(RigShape.Quadruped));
        }

        // ---- skinning --------------------------------------------------------

        [Test]
        public void EveryWeight_SumsToOneAndPointsAtRealBones([ValueSource(nameof(Shapes))] RigShape shape)
        {
            Bounds body = Body();
            RigPlan plan = ProceduralRig.Build(shape, body);

            var random = new System.Random(4242);

            for (int i = 0; i < 400; i++)
            {
                var v = new Vector3(
                    Mathf.Lerp(body.min.x, body.max.x, (float)random.NextDouble()),
                    Mathf.Lerp(body.min.y, body.max.y, (float)random.NextDouble()),
                    Mathf.Lerp(body.min.z, body.max.z, (float)random.NextDouble()));

                BoneWeight w = ProceduralRig.WeightFor(plan, v);

                Assert.AreEqual(1f, w.weight0 + w.weight1, 1e-4f, $"weights at {v} do not add up");
                Assert.GreaterOrEqual(w.weight0, w.weight1, "the nearer bone should carry more");
                Assert.IsTrue(w.boneIndex0 >= 0 && w.boneIndex0 < plan.Count, "bone 0 is off the end");
                Assert.IsTrue(w.boneIndex1 >= 0 && w.boneIndex1 < plan.Count, "bone 1 is off the end");
            }
        }

        /// <summary>
        /// The fault this is really guarding against: the root sits in the middle of the
        /// body with a segment no longer than a point, so on raw distance it wins the
        /// limbs outright and a walking creature's legs stay welded to its hips.
        /// </summary>
        [Test]
        public void LegVertices_FollowTheLegAndNotThePelvis()
        {
            Bounds body = Body();
            RigPlan plan = ProceduralRig.Build(RigShape.Biped, body);

            // Half way down the left shin.
            var shin = new Vector3(-body.size.x * 0.17f, body.min.y + body.size.y * 0.16f, 0f);
            BoneWeight w = ProceduralRig.WeightFor(plan, shin);

            RigJoint bound = plan.Joints[w.boneIndex0];
            Assert.IsTrue(bound.Role is JointRole.Knee or JointRole.Foot,
                $"the shin is bound to the {bound.Role} ({bound.Name})");
            Assert.Less(bound.Side, 0f, "the left shin is bound to a right-hand bone");
        }

        [Test]
        public void HeadVertices_FollowTheHead()
        {
            Bounds body = Body();
            RigPlan plan = ProceduralRig.Build(RigShape.Biped, body);

            var crown = new Vector3(0f, body.min.y + body.size.y * 0.94f, 0f);
            RigJoint bound = plan.Joints[ProceduralRig.WeightFor(plan, crown).boneIndex0];

            Assert.AreEqual(JointRole.Head, bound.Role, $"the crown is bound to {bound.Name}");
        }

        /// <summary>A species that already has a real skeleton must not have a second one
        /// fitted over the top of it.</summary>
        [Test]
        public void Shape_IsNoneForAModelThatIsAlreadyRigged()
        {
            var def = ScriptableObject.CreateInstance<Data.CreatureDefinition>();
            def.locomotion = Data.LocomotionStyle.Walk;
            Assert.AreEqual(RigShape.Biped, ProceduralRig.ShapeFor(def));

            def.riggedPrefab = new GameObject("rigged");
            def.animatorController = new AnimatorOverrideController();
            Assert.AreEqual(RigShape.None, ProceduralRig.ShapeFor(def));

            Object.DestroyImmediate(def.riggedPrefab);
            Object.DestroyImmediate(def);
        }
    }
}
