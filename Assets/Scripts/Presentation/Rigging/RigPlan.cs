using UnityEngine;

namespace LivingDiorama.Presentation.Rigging
{
    /// <summary>Body plans the auto-rigger knows how to build.</summary>
    public enum RigShape
    {
        /// <summary>Not rigged: the model already carries a skeleton, or is too odd to fit.</summary>
        None,
        Biped,
        Quadruped,
        Winged,
        Blob,
    }

    /// <summary>What a joint is for. The animator poses by role, so it never has to know
    /// which body plan it is driving -- a leg is a leg whether there are two or four.</summary>
    public enum JointRole
    {
        Root,
        Spine,
        Head,
        Tail,
        Hip,
        Knee,
        Foot,
        Shoulder,
        Elbow,
        Hand,
        Wing,
        WingTip,

        /// <summary>A limbless body's wobble points, spaced around its waist.</summary>
        Lobe,
    }

    /// <summary>One bone, positioned in the model's own space with its feet at y = 0.</summary>
    public readonly struct RigJoint
    {
        public readonly string Name;
        /// <summary>Index into the plan, or -1 for the root.</summary>
        public readonly int Parent;
        public readonly Vector3 Position;
        public readonly JointRole Role;
        /// <summary>-1 left, +1 right, 0 on the centreline.</summary>
        public readonly float Side;
        /// <summary>+1 front pair, -1 rear pair, 0 for anything that is not a limb pair.</summary>
        public readonly float Along;

        public RigJoint(string name, int parent, Vector3 position, JointRole role,
                        float side = 0f, float along = 0f)
        {
            Name = name;
            Parent = parent;
            Position = position;
            Role = role;
            Side = side;
            Along = along;
        }
    }

    /// <summary>
    /// A skeleton laid out for one particular mesh.
    ///
    /// Kept as plain data rather than as Transforms so the layout can be built and
    /// checked without a scene: the placement rules are the part most likely to be wrong,
    /// and a bone in the wrong place is much easier to catch as a number than as a
    /// silhouette that looks slightly off.
    /// </summary>
    public sealed class RigPlan
    {
        public readonly RigShape Shape;
        public readonly RigJoint[] Joints;
        public readonly Bounds Bounds;

        public RigPlan(RigShape shape, RigJoint[] joints, Bounds bounds)
        {
            Shape = shape;
            Joints = joints;
            Bounds = bounds;
        }

        public int Count => Joints.Length;

        /// <summary>The segment a bone covers: from its parent to itself. Vertices are
        /// bound to whichever segment they sit closest to, which is what puts the thigh
        /// on the thigh bone rather than on the nearest joint centre.</summary>
        public void SegmentOf(int index, out Vector3 a, out Vector3 b)
        {
            RigJoint joint = Joints[index];
            b = joint.Position;

            if (joint.Parent < 0)
            {
                // The root has nothing above it, so it claims a small ball around itself
                // and everything the limbs and spine do not reach falls to it.
                a = joint.Position;
                return;
            }

            a = Joints[joint.Parent].Position;
        }
    }
}
