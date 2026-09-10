using System.Collections.Generic;
using LivingDiorama.Data;
using UnityEngine;

namespace LivingDiorama.Presentation.Rigging
{
    /// <summary>
    /// Fits a skeleton to a mesh that arrived without one, and skins the mesh to it.
    ///
    /// The generated creatures come out of Meshy as a single rigid lump. Moving that
    /// lump around as a whole -- bobbing it, leaning it, squashing it -- is the best any
    /// amount of transform animation can do, and it reads exactly like what it is: a
    /// cardboard puppet slid across the ground. Nothing bends, so nothing walks.
    ///
    /// Meshy will rig a model, but only a humanoid one, and only sometimes: it rigged the
    /// goblin and the knight, and produced an empty file for the skeleton twice. A wolf
    /// and a slime it will not attempt at all. So the rest are rigged here instead, from
    /// the one thing every mesh does have -- its shape. The body plan says where the
    /// joints of such a creature go as fractions of its bounding box; each vertex is then
    /// bound to the one or two bones whose segment it lies nearest.
    ///
    /// It is not a substitute for an artist's rig, and it is not trying to be. It is the
    /// difference between a creature whose legs swing when it walks and one whose do not.
    /// </summary>
    public static class ProceduralRig
    {
        /// <summary>
        /// Which body plan a species has.
        ///
        /// Read off the locomotion style, which is already the closest thing the data has
        /// to a description of anatomy -- a creature that trots has four legs, one that
        /// hops in a single arc has none. Adding a second field to say the same thing
        /// again would only be one more thing to keep in step.
        /// </summary>
        public static RigShape ShapeFor(CreatureDefinition def)
        {
            if (def == null) return RigShape.None;

            // Already carries a skeleton and real clips: leave it be.
            if (def.riggedPrefab != null && def.animatorController != null) return RigShape.None;

            return def.locomotion switch
            {
                LocomotionStyle.Walk => RigShape.Biped,
                LocomotionStyle.Scurry => RigShape.Biped,
                LocomotionStyle.Stomp => RigShape.Biped,
                LocomotionStyle.Trot => RigShape.Quadruped,
                LocomotionStyle.Float => RigShape.Winged,
                LocomotionStyle.Hop => RigShape.Blob,
                // A slither has no joints worth guessing at, and the whole-body wave the
                // procedural animator already does for it is a better answer than a
                // skeleton invented for the occasion.
                _ => RigShape.None,
            };
        }

        // ---- layout ---------------------------------------------------------

        /// <summary>Lay a skeleton out inside a bounding box. Pure, so the placement rules
        /// can be checked without a mesh, a scene or a graphics device.</summary>
        public static RigPlan Build(RigShape shape, Bounds bounds) => shape switch
        {
            RigShape.Biped => new RigPlan(shape, Biped(bounds), bounds),
            RigShape.Quadruped => new RigPlan(shape, Quadruped(bounds, false), bounds),
            RigShape.Winged => new RigPlan(shape, Quadruped(bounds, true), bounds),
            RigShape.Blob => new RigPlan(shape, Blob(bounds), bounds),
            _ => null,
        };

        static RigJoint[] Biped(Bounds b)
        {
            float h = b.size.y, w = b.size.x, d = b.size.z;
            float y = b.min.y, x = b.center.x, z = b.center.z;

            Vector3 P(float fx, float fy, float fz) => new(x + w * fx, y + h * fy, z + d * fz);

            var joints = new List<RigJoint>(16)
            {
                new("Pelvis", -1, P(0f, 0.52f, 0f), JointRole.Root),
                new("Spine", 0, P(0f, 0.66f, 0f), JointRole.Spine),
                new("Chest", 1, P(0f, 0.78f, 0f), JointRole.Spine),
                new("Head", 2, P(0f, 0.90f, 0f), JointRole.Head),
            };

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                string tag = i == 0 ? "L" : "R";

                joints.Add(new RigJoint($"Hip{tag}", 0, P(0.17f * side, 0.50f, 0f), JointRole.Hip, side));
                joints.Add(new RigJoint($"Knee{tag}", joints.Count - 1, P(0.17f * side, 0.27f, 0.01f), JointRole.Knee, side));
                joints.Add(new RigJoint($"Foot{tag}", joints.Count - 1, P(0.17f * side, 0.04f, 0.05f), JointRole.Foot, side));
            }

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                string tag = i == 0 ? "L" : "R";

                joints.Add(new RigJoint($"Shoulder{tag}", 2, P(0.27f * side, 0.76f, 0f), JointRole.Shoulder, side));
                joints.Add(new RigJoint($"Elbow{tag}", joints.Count - 1, P(0.33f * side, 0.62f, 0f), JointRole.Elbow, side));
                joints.Add(new RigJoint($"Hand{tag}", joints.Count - 1, P(0.35f * side, 0.48f, 0f), JointRole.Hand, side));
            }

            return joints.ToArray();
        }

        /// <summary>Four legs along a horizontal spine, nose at +Z. Wings optional: a
        /// dragon is a quadruped that also flaps.</summary>
        static RigJoint[] Quadruped(Bounds b, bool winged)
        {
            float h = b.size.y, w = b.size.x, d = b.size.z;
            float y = b.min.y, x = b.center.x, z = b.center.z;

            Vector3 P(float fx, float fy, float fz) => new(x + w * fx, y + h * fy, z + d * fz);

            const int hips = 0, chest = 2;

            var joints = new List<RigJoint>(23)
            {
                new("Hips", -1, P(0f, 0.62f, -0.22f), JointRole.Root),
                new("Spine", hips, P(0f, 0.66f, 0.02f), JointRole.Spine),
                new("Chest", 1, P(0f, 0.66f, 0.26f), JointRole.Spine),
                new("Neck", chest, P(0f, 0.74f, 0.40f), JointRole.Spine),
                new("Head", 3, P(0f, 0.80f, 0.48f), JointRole.Head),
                new("Tail", hips, P(0f, 0.60f, -0.36f), JointRole.Tail),
                new("TailTip", 5, P(0f, 0.55f, -0.48f), JointRole.Tail),
            };

            // Front pair hangs off the chest, rear pair off the hips, which is what makes
            // the shoulders lead and the haunches follow when the spine moves.
            for (int pair = 0; pair < 2; pair++)
            {
                float along = pair == 0 ? 1f : -1f;
                float legZ = pair == 0 ? 0.26f : -0.24f;
                int attach = pair == 0 ? chest : hips;
                string end = pair == 0 ? "F" : "B";

                for (int i = 0; i < 2; i++)
                {
                    float side = i == 0 ? -1f : 1f;
                    string tag = end + (i == 0 ? "L" : "R");

                    joints.Add(new RigJoint($"Hip{tag}", attach, P(0.20f * side, 0.56f, legZ), JointRole.Hip, side, along));
                    joints.Add(new RigJoint($"Knee{tag}", joints.Count - 1, P(0.20f * side, 0.30f, legZ), JointRole.Knee, side, along));
                    joints.Add(new RigJoint($"Foot{tag}", joints.Count - 1, P(0.20f * side, 0.04f, legZ + 0.02f), JointRole.Foot, side, along));
                }
            }

            if (winged)
            {
                for (int i = 0; i < 2; i++)
                {
                    float side = i == 0 ? -1f : 1f;
                    string tag = i == 0 ? "L" : "R";

                    joints.Add(new RigJoint($"Wing{tag}", chest, P(0.16f * side, 0.78f, 0.06f), JointRole.Wing, side));
                    joints.Add(new RigJoint($"WingTip{tag}", joints.Count - 1, P(0.48f * side, 0.90f, -0.14f), JointRole.WingTip, side));
                }
            }

            return joints.ToArray();
        }

        /// <summary>A body with no limbs still needs somewhere to wobble from: a short
        /// vertical stack for squash, and four lobes around the waist that lag behind it.</summary>
        static RigJoint[] Blob(Bounds b)
        {
            float h = b.size.y, w = b.size.x, d = b.size.z;
            float y = b.min.y, x = b.center.x, z = b.center.z;

            Vector3 P(float fx, float fy, float fz) => new(x + w * fx, y + h * fy, z + d * fz);

            return new[]
            {
                new RigJoint("Base", -1, P(0f, 0.12f, 0f), JointRole.Root),
                new RigJoint("Body", 0, P(0f, 0.48f, 0f), JointRole.Spine),
                new RigJoint("Crown", 1, P(0f, 0.86f, 0f), JointRole.Head),
                new RigJoint("LobeL", 1, P(-0.40f, 0.40f, 0f), JointRole.Lobe, -1f),
                new RigJoint("LobeR", 1, P(0.40f, 0.40f, 0f), JointRole.Lobe, 1f),
                new RigJoint("LobeB", 1, P(0f, 0.40f, -0.40f), JointRole.Lobe, 0f, -1f),
                new RigJoint("LobeF", 1, P(0f, 0.40f, 0.40f), JointRole.Lobe, 0f, 1f),
            };
        }

        // ---- skinning -------------------------------------------------------

        /// <summary>
        /// How much further from a vertex the root counts as being, as a fraction of the
        /// model's height.
        ///
        /// The root sits in the middle of the body and its segment is a point, so on raw
        /// distance it wins the hip and shoulder vertices outright and the limbs come away
        /// weighted to a bone that never moves. Handicapping it makes it what it should
        /// be: the bone that holds everything no limb has claimed.
        /// </summary>
        const float RootHandicap = 0.16f;

        /// <summary>Bind one vertex to the two nearest bones. Two rather than four because
        /// a third influence on a stylised low-poly mesh only softens the silhouette;
        /// the weighting between the two is squared so the nearer bone dominates and
        /// limbs stay crisp.</summary>
        public static BoneWeight WeightFor(RigPlan plan, Vector3 vertex)
        {
            float handicap = plan.Bounds.size.y * RootHandicap;

            int best = 0, second = 0;
            float bestD = float.MaxValue, secondD = float.MaxValue;

            for (int i = 0; i < plan.Count; i++)
            {
                plan.SegmentOf(i, out Vector3 a, out Vector3 b);
                float d = DistanceToSegment(vertex, a, b);
                if (plan.Joints[i].Role == JointRole.Root) d += handicap;

                if (d < bestD)
                {
                    secondD = bestD;
                    second = best;
                    bestD = d;
                    best = i;
                }
                else if (d < secondD)
                {
                    secondD = d;
                    second = i;
                }
            }

            float w0 = secondD * secondD;
            float w1 = bestD * bestD;
            float total = w0 + w1;

            if (total < 1e-9f)
            {
                return new BoneWeight { boneIndex0 = best, weight0 = 1f };
            }

            return new BoneWeight
            {
                boneIndex0 = best,
                weight0 = w0 / total,
                boneIndex1 = second,
                weight1 = w1 / total,
            };
        }

        public static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float length = ab.sqrMagnitude;
            if (length < 1e-8f) return Vector3.Distance(p, a);

            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / length);
            return Vector3.Distance(p, a + ab * t);
        }

        // ---- building the thing ---------------------------------------------

        /// <summary>
        /// Give a model a skeleton and skin it to it.
        ///
        /// Returns the rig, or null when there is nothing to do -- an already-skinned
        /// model, a shape with no plan, or a mesh too small to fit anything to. Callers
        /// carry on regardless: a creature with no rig still moves, just less.
        /// </summary>
        public static CreatureRig Apply(GameObject instance, RigShape shape)
        {
            if (instance == null || shape == RigShape.None) return null;
            if (instance.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) return null;

            var filters = new List<MeshFilter>();
            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null && filter.sharedMesh.vertexCount > 0) filters.Add(filter);
            }
            if (filters.Count == 0) return null;

            Transform root = instance.transform;
            if (!MeasureInLocalSpace(filters, root, out Bounds bounds)) return null;
            if (bounds.size.y < 1e-4f) return null;

            RigPlan plan = Build(shape, bounds);
            if (plan == null) return null;

            Transform[] bones = CreateBones(plan, root);

            foreach (MeshFilter filter in filters)
            {
                Skin(filter, plan, bones, root);
            }

            var rig = instance.AddComponent<CreatureRig>();
            rig.Initialise(plan, bones);
            return rig;
        }

        static bool MeasureInLocalSpace(List<MeshFilter> filters, Transform root, out Bounds bounds)
        {
            bounds = default;
            bool first = true;

            foreach (MeshFilter filter in filters)
            {
                Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Bounds local = filter.sharedMesh.bounds;

                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? local.min.x : local.max.x,
                        (i & 2) == 0 ? local.min.y : local.max.y,
                        (i & 4) == 0 ? local.min.z : local.max.z);

                    Vector3 p = toRoot.MultiplyPoint3x4(corner);
                    if (first)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(p);
                    }
                }
            }

            return !first;
        }

        static Transform[] CreateBones(RigPlan plan, Transform root)
        {
            var bones = new Transform[plan.Count];

            for (int i = 0; i < plan.Count; i++)
            {
                RigJoint joint = plan.Joints[i];
                var go = new GameObject(joint.Name);
                Transform t = go.transform;

                t.SetParent(joint.Parent < 0 ? root : bones[joint.Parent], false);
                // Positions are in the model's space; a child's is relative to its parent.
                t.localPosition = joint.Parent < 0
                    ? joint.Position
                    : joint.Position - plan.Joints[joint.Parent].Position;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;

                bones[i] = t;
            }

            return bones;
        }

        /// <summary>
        /// Replace a rigid mesh renderer with a skinned one.
        ///
        /// The skinned renderer goes on a new child rather than on the original object,
        /// because Unity allows only one Renderer per GameObject and the rigid one is
        /// still there at this point. The child sits at identity, so it inherits exactly
        /// the transform the mesh already had.
        /// </summary>
        static void Skin(MeshFilter filter, RigPlan plan, Transform[] bones, Transform root)
        {
            Mesh mesh = filter.sharedMesh;
            var source = filter.GetComponent<MeshRenderer>();

            var go = new GameObject(filter.gameObject.name + "_Skinned");
            go.transform.SetParent(filter.transform, false);

            // The mesh is shared between every instance of a species, and so are its
            // weights: the plan comes from the mesh's own bounds, so a second goblin works
            // out exactly the same numbers. Recomputing them per creature would be pure
            // cost.
            if (mesh.bindposes == null || mesh.bindposes.Length != plan.Count)
            {
                Matrix4x4 meshToRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Vector3[] vertices = mesh.vertices;

                var weights = new BoneWeight[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    weights[i] = WeightFor(plan, meshToRoot.MultiplyPoint3x4(vertices[i]));
                }

                var bindposes = new Matrix4x4[plan.Count];
                for (int i = 0; i < plan.Count; i++)
                {
                    bindposes[i] = bones[i].worldToLocalMatrix * filter.transform.localToWorldMatrix;
                }

                mesh.boneWeights = weights;
                mesh.bindposes = bindposes;
            }

            var skinned = go.AddComponent<SkinnedMeshRenderer>();
            skinned.sharedMesh = mesh;
            skinned.bones = bones;
            skinned.rootBone = bones[0];
            skinned.quality = SkinQuality.Bone2;
            skinned.updateWhenOffscreen = false;
            // Room for the pose to move away from the bind bounds without the creature
            // popping out of existence at the edge of the screen.
            skinned.localBounds = new Bounds(mesh.bounds.center, mesh.bounds.size * 1.6f);

            if (source != null)
            {
                skinned.sharedMaterials = source.sharedMaterials;
                skinned.shadowCastingMode = source.shadowCastingMode;
                skinned.receiveShadows = source.receiveShadows;
                Object.Destroy(source);
            }

            Object.Destroy(filter);
        }
    }
}
