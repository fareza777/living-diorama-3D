using System.Collections.Generic;
using UnityEngine;

namespace LivingDiorama.Diorama
{
    /// <summary>What a placed object offers the creature that walks up to it.</summary>
    public enum PlaceableRole { Rest, Eat, Train, Play }

    /// <summary>
    /// One thing the player can put down.
    ///
    /// A table in code rather than a ScriptableObject per item, for the same reason the
    /// prop library is: these are read once at startup and never edited by hand, and four
    /// asset files plus their meta files is more to keep in step than four entries here.
    /// It moves to assets the moment a designer other than the code needs to change them.
    /// </summary>
    public readonly struct Placeable
    {
        public readonly string Id;
        public readonly string DisplayName;
        /// <summary>File in StreamingAssets/Props, without the extension.</summary>
        public readonly string Model;
        public readonly PlaceableRole Role;
        /// <summary>World height the model is scaled to, and the radius creatures walk
        /// around. Both matter to the simulation, so they live with the definition.</summary>
        public readonly float Height;
        public readonly float Radius;
        /// <summary>How far a creature will travel to use it, in world units.</summary>
        public readonly float Draw;
        public readonly string Verb;

        public Placeable(string id, string name, string model, PlaceableRole role,
                         float height, float radius, float draw, string verb)
        {
            Id = id;
            DisplayName = name;
            Model = model;
            Role = role;
            Height = height;
            Radius = radius;
            Draw = draw;
            Verb = verb;
        }
    }

    public static class Placeables
    {
        /// <summary>
        /// The four the vertical slice ships with.
        ///
        /// One per need, deliberately: sleep, eat, train, play. Eight was the original
        /// list and half of them were variations on the same interaction -- a chair and a
        /// campfire are both "sit somewhere". Four different *reasons* to cross the
        /// diorama is more behaviour than eight ways to stand still.
        /// </summary>
        public static readonly Placeable[] All =
        {
            new("bed", "Straw Bed", "bed", PlaceableRole.Rest, 0.42f, 0.42f, 9f, "sleeping"),
            new("food_bowl", "Food Bowl", "food_bowl", PlaceableRole.Eat, 0.22f, 0.28f, 10f, "eating"),
            new("sword_dummy", "Training Dummy", "sword_dummy", PlaceableRole.Train, 0.95f, 0.32f, 8f, "training"),
            new("drum", "Drum", "drum", PlaceableRole.Play, 0.40f, 0.30f, 8f, "drumming"),
        };

        static readonly Dictionary<string, Placeable> Index = Build();

        static Dictionary<string, Placeable> Build()
        {
            var map = new Dictionary<string, Placeable>(All.Length);
            foreach (Placeable p in All) map[p.Id] = p;
            return map;
        }

        public static bool TryGet(string id, out Placeable placeable) => Index.TryGetValue(id, out placeable);
    }

    /// <summary>One object standing in the diorama, and the thing a creature walks to.</summary>
    public sealed class Placement
    {
        public string Id;
        public Placeable Definition;
        public Vector3 Position;
        public float Yaw;
        public GameObject Visual;

        /// <summary>Which creature has claimed it right now, so two do not walk to the
        /// same bed and stand inside one another.</summary>
        public string ClaimedBy;

        /// <summary>Where a creature actually stands to use it -- beside it, not inside
        /// it. Facing the object, so the animation reads as being aimed at something.</summary>
        public Vector3 UseSpot
        {
            get
            {
                float radians = Yaw * Mathf.Deg2Rad;
                var offset = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                return Position - offset * (Definition.Radius + 0.22f);
            }
        }
    }
}
