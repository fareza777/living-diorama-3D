using System.Collections.Generic;
using UnityEngine;

namespace LivingDiorama.Presentation.Rigging
{
    /// <summary>What a creature is doing right now, over and above walking.</summary>
    public enum RigAction { None, Eat, Attack, Startle }

    /// <summary>Everything the rig needs to strike a pose for one frame.</summary>
    public struct RigPose
    {
        /// <summary>Gait cycle, in radians. One full turn is one stride.</summary>
        public float Phase;
        /// <summary>Speed as a multiple of walking pace. 0 is standing.</summary>
        public float Gait;
        public float Sleep;
        public float KnockOut;
        public RigAction Action;
        /// <summary>0..1 through the current action, shaped so it peaks in the middle.</summary>
        public float ActionPulse;
    }

    /// <summary>
    /// Poses a generated skeleton.
    ///
    /// Everything is driven off the roles in the plan rather than off bone names, so one
    /// set of rules covers two legs, four legs and none: a hip swings, a knee bends
    /// behind it, a tail trails, a wing beats. The result is not a hand-animated walk
    /// cycle, but it is a walk -- the legs carry the body rather than the body carrying
    /// the legs, which is the whole difference the player sees.
    /// </summary>
    public sealed class CreatureRig : MonoBehaviour
    {
        RigPlan _plan;
        Transform[] _bones;
        Vector3[] _rest;

        /// <summary>Foot bones, and how far below each one the sole was when the mesh was
        /// bound. Together they say where the ground is.</summary>
        int[] _feet;
        float[] _soles;

        public RigShape Shape => _plan != null ? _plan.Shape : RigShape.None;

        /// <summary>Whether this creature has feet to stand on. A blob does not.</summary>
        public bool HasFeet => _feet != null && _feet.Length > 0;

        public void Initialise(RigPlan plan, Transform[] bones)
        {
            _plan = plan;
            _bones = bones;
            _rest = new Vector3[bones.Length];

            for (int i = 0; i < bones.Length; i++) _rest[i] = bones[i].localPosition;

            var feet = new List<int>(4);
            var soles = new List<float>(4);

            for (int i = 0; i < plan.Count; i++)
            {
                if (plan.Joints[i].Role != JointRole.Foot) continue;

                feet.Add(i);
                soles.Add(plan.Joints[i].Position.y - plan.Bounds.min.y);
            }

            _feet = feet.ToArray();
            _soles = soles.ToArray();
        }

        static float SwingAngle(float gait) => Mathf.Min(gait, 2f) * 17f;

        // ---- posing ---------------------------------------------------------

        public void Pose(in RigPose pose)
        {
            if (_plan == null || _bones == null) return;

            float gait = pose.Gait;
            float swing = SwingAngle(gait);
            float wake = 1f - Mathf.Clamp01(pose.Sleep + pose.KnockOut);

            for (int i = 0; i < _bones.Length; i++)
            {
                RigJoint joint = _plan.Joints[i];
                float phase = pose.Phase + LimbOffset(joint);

                Vector3 euler = Moving(joint, phase, swing * wake, gait * wake, pose);
                euler += Acting(joint, pose) * wake;

                Vector3 settled = Resting(joint);
                euler = Vector3.Lerp(euler, settled, Mathf.Clamp01(pose.Sleep + pose.KnockOut * 0.7f));

                _bones[i].localRotation = Quaternion.Euler(euler);
                _bones[i].localPosition = _rest[i] + Wobble(joint, pose);
            }

            PlantFeet(wake);
        }

        /// <summary>
        /// Drop the whole skeleton until its lowest foot touches the floor.
        ///
        /// A leg swung out from the hip reaches the ground short of where it did hanging
        /// straight down, so a creature whose body stays at one height rises onto its toes
        /// with every stride -- the hover you see the moment you look at a creature's
        /// feet. Working the amount out from the swing angle gets close and no closer,
        /// because the knee, the ankle and the lean all move the foot as well. Reading the
        /// bones back after posing them is the exact answer, and costs four transform
        /// lookups. It is also, not by coincidence, the real reason walking bobs.
        /// </summary>
        void PlantFeet(float wake)
        {
            if (_feet.Length == 0) return;

            float lowest = float.MaxValue;
            for (int i = 0; i < _feet.Length; i++)
            {
                Vector3 local = transform.InverseTransformPoint(_bones[_feet[i]].position);
                lowest = Mathf.Min(lowest, local.y - _soles[i]);
            }

            // Fade out as the creature curls up. A sleeping animal tucks its feet under
            // its belly, and insisting those feet still touch the floor drives the rest of
            // it underground -- which measured as a wolf a quarter of a unit into the
            // grass, asleep.
            float drop = (lowest - _plan.Bounds.min.y) * wake;

            // And never more than a fraction of a stride's worth, whatever the pose does.
            float limit = _plan.Bounds.size.y * 0.12f;

            Transform root = _bones[0];
            Vector3 position = root.localPosition;
            position.y -= Mathf.Clamp(drop, -limit, limit);
            root.localPosition = position;
        }

        /// <summary>
        /// Which half of the stride a limb is in.
        ///
        /// Two legs alternate. Four legs trot: the diagonal pairs move together, which is
        /// what stops a quadruped looking like it is bunny-hopping. Multiplying the side
        /// by the end of the body says exactly that -- front-left and back-right agree,
        /// and the other two agree with each other.
        /// </summary>
        static float LimbOffset(RigJoint joint)
        {
            switch (joint.Role)
            {
                case JointRole.Hip:
                case JointRole.Knee:
                case JointRole.Foot:
                    if (joint.Along != 0f) return joint.Side * joint.Along > 0f ? Mathf.PI : 0f;
                    return joint.Side > 0f ? Mathf.PI : 0f;

                // Arms swing against the leg on the same side.
                case JointRole.Shoulder:
                case JointRole.Elbow:
                case JointRole.Hand:
                    return joint.Side > 0f ? 0f : Mathf.PI;

                default:
                    return 0f;
            }
        }

        Vector3 Moving(RigJoint joint, float phase, float swing, float gait, in RigPose pose)
        {
            float cycle = Mathf.Sin(phase);

            switch (joint.Role)
            {
                case JointRole.Hip:
                    return new Vector3(cycle * swing, 0f, 0f);

                // Knees only fold one way. Bending on the back half of the swing is what
                // makes a leg lift and clear the ground instead of scything through it.
                case JointRole.Knee:
                    return new Vector3(-Mathf.Max(0f, -cycle) * swing * 1.6f, 0f, 0f);

                case JointRole.Foot:
                    return new Vector3(-cycle * swing * 0.45f, 0f, 0f);

                case JointRole.Shoulder:
                    return new Vector3(cycle * swing * 0.75f, 0f, 0f);

                case JointRole.Elbow:
                    return new Vector3(-Mathf.Max(0f, cycle) * swing * 0.5f, 0f, 0f);

                case JointRole.Spine:
                    // A small counter-rotation through the body, twice a stride.
                    return new Vector3(0f, Mathf.Sin(phase) * 2.2f * gait, Mathf.Sin(phase * 2f) * 1.6f * gait);

                case JointRole.Head:
                    // The head stays level while the body works underneath it.
                    return new Vector3(-Mathf.Sin(phase * 2f) * 1.8f * gait, Mathf.Sin(phase * 0.5f) * 4f, 0f);

                case JointRole.Tail:
                    return new Vector3(Mathf.Sin(phase * 0.7f) * 4f, Mathf.Sin(phase * 0.9f) * (7f + gait * 9f), 0f);

                case JointRole.Wing:
                {
                    // Wings beat whether or not the creature is going anywhere: it is what
                    // keeps it in the air.
                    float beat = Mathf.Sin(pose.Phase * 2f) * (16f + gait * 12f);
                    return new Vector3(0f, 0f, beat * -joint.Side);
                }

                case JointRole.WingTip:
                {
                    float beat = Mathf.Sin(pose.Phase * 2f - 0.7f) * (13f + gait * 10f);
                    return new Vector3(0f, 0f, beat * -joint.Side);
                }

                default:
                    return Vector3.zero;
            }
        }

        /// <summary>The pose a creature settles into when it stops being awake: limbs
        /// folded under it, back curled, head down.</summary>
        static Vector3 Resting(RigJoint joint) => joint.Role switch
        {
            JointRole.Hip => new Vector3(38f, 0f, 0f),
            JointRole.Knee => new Vector3(-72f, 0f, 0f),
            JointRole.Foot => new Vector3(24f, 0f, 0f),
            JointRole.Shoulder => new Vector3(22f, 0f, 12f * -joint.Side),
            JointRole.Elbow => new Vector3(-46f, 0f, 0f),
            JointRole.Spine => new Vector3(9f, 0f, 0f),
            JointRole.Head => new Vector3(16f, 0f, 0f),
            JointRole.Tail => new Vector3(6f, 14f, 0f),
            JointRole.Wing => new Vector3(0f, 0f, 26f * -joint.Side),
            JointRole.WingTip => new Vector3(0f, 0f, 34f * -joint.Side),
            _ => Vector3.zero,
        };

        /// <summary>One-shot reactions, layered over whatever the creature is doing.</summary>
        static Vector3 Acting(RigJoint joint, in RigPose pose)
        {
            if (pose.Action == RigAction.None || pose.ActionPulse <= 0.001f) return Vector3.zero;

            float p = pose.ActionPulse;

            switch (pose.Action)
            {
                case RigAction.Eat:
                    return joint.Role switch
                    {
                        JointRole.Head => new Vector3(p * 34f, 0f, 0f),
                        JointRole.Spine => new Vector3(p * 9f, 0f, 0f),
                        JointRole.Shoulder => new Vector3(p * 26f, 0f, 0f),
                        JointRole.Elbow => new Vector3(-p * 40f, 0f, 0f),
                        _ => Vector3.zero,
                    };

                case RigAction.Attack:
                    return joint.Role switch
                    {
                        JointRole.Shoulder => new Vector3(-p * 62f, 0f, 0f),
                        JointRole.Elbow => new Vector3(-p * 24f, 0f, 0f),
                        JointRole.Spine => new Vector3(-p * 8f, 0f, 0f),
                        JointRole.Head => new Vector3(-p * 6f, 0f, 0f),
                        JointRole.Wing => new Vector3(0f, 0f, -p * 30f * -joint.Side),
                        _ => Vector3.zero,
                    };

                case RigAction.Startle:
                    return joint.Role switch
                    {
                        JointRole.Head => new Vector3(-p * 22f, 0f, 0f),
                        JointRole.Spine => new Vector3(-p * 10f, 0f, 0f),
                        JointRole.Shoulder => new Vector3(-p * 30f, 0f, p * 18f * -joint.Side),
                        JointRole.Wing => new Vector3(0f, 0f, -p * 40f * -joint.Side),
                        _ => Vector3.zero,
                    };

                default:
                    return Vector3.zero;
            }
        }

        /// <summary>
        /// Jelly. A limbless body has nothing to swing, so its shape has to do the work:
        /// four points around the waist rise and fall out of step with each other, which
        /// is what a blob does when it lands.
        /// </summary>
        Vector3 Wobble(RigJoint joint, in RigPose pose)
        {
            if (joint.Role != JointRole.Lobe || _plan.Shape != RigShape.Blob) return Vector3.zero;

            float lobe = joint.Side != 0f
                ? (joint.Side < 0f ? 0f : Mathf.PI)
                : (joint.Along < 0f ? Mathf.PI * 0.5f : Mathf.PI * 1.5f);

            float amount = _plan.Bounds.size.y * 0.07f * (0.45f + Mathf.Min(pose.Gait, 1.5f) * 0.55f);
            float travel = Mathf.Sin(pose.Phase * 2f + lobe) * amount * (1f - pose.Sleep * 0.7f);

            // Out sideways in both directions, but only ever upwards: a lobe that drops
            // pulls the bottom of the blob through the ground, which measured as four
            // centimetres of a slime buried in the grass.
            return new Vector3(joint.Side * travel * 0.6f,
                               Mathf.Max(0f, travel) * 0.7f,
                               joint.Along * travel * 0.6f);
        }

        /// <summary>Put every bone back where it was bound. Used when a creature stops
        /// being animated so it does not freeze mid-stride.</summary>
        public void ResetPose()
        {
            if (_bones == null) return;

            for (int i = 0; i < _bones.Length; i++)
            {
                _bones[i].localRotation = Quaternion.identity;
                _bones[i].localPosition = _rest[i];
            }
        }

        void OnDestroy() => _bones = null;
    }
}
