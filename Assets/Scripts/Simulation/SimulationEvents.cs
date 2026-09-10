using System;
using UnityEngine;

namespace LivingDiorama.Simulation
{
    public enum SimEventKind
    {
        Spawned,
        Ate,
        FellAsleep,
        WokeUp,
        StoleFood,
        StartedChase,
        EscapedChase,
        Attacked,
        KnockedOut,
        Recovered,
        MadeFriend,
        PlayedInWater,
        Intimidated,
        Discovered,

        /// <summary>Walked to something the player built, and used it.</summary>
        UsedPlacement,
    }

    /// <summary>
    /// A notable thing that happened. The UI turns these into toasts, the economy turns
    /// some of them into coins, and the "moments" feed uses them to tell the player what
    /// they missed. Kept as a struct so the hot path never allocates.
    /// </summary>
    public readonly struct SimEvent
    {
        public readonly SimEventKind Kind;
        public readonly CreatureAgent Actor;
        public readonly CreatureAgent Target;
        public readonly Vector3 Position;
        public readonly double AtHours;

        public SimEvent(SimEventKind kind, CreatureAgent actor, CreatureAgent target,
                        Vector3 position, double atHours)
        {
            Kind = kind;
            Actor = actor;
            Target = target;
            Position = position;
            AtHours = atHours;
        }

        /// <summary>Player-facing one-liner. Deliberately written like a nature documentary
        /// caption -- the joy of this game is reading what your creatures got up to.</summary>
        public string Describe()
        {
            string a = Actor != null ? Actor.DisplayName : "Something";
            string b = Target != null ? Target.DisplayName : "something";
            return Kind switch
            {
                SimEventKind.Spawned => $"{a} settled into the diorama.",
                SimEventKind.Ate => $"{a} found a meal.",
                SimEventKind.FellAsleep => $"{a} curled up for a nap.",
                SimEventKind.WokeUp => $"{a} woke up.",
                SimEventKind.StoleFood => $"{a} snatched food and bolted!",
                SimEventKind.StartedChase => $"{a} is chasing {b}!",
                SimEventKind.EscapedChase => $"{a} shook off {b}.",
                SimEventKind.Attacked => $"{a} struck {b}.",
                SimEventKind.KnockedOut => $"{b} was knocked out by {a}.",
                SimEventKind.Recovered => $"{a} dusted itself off.",
                SimEventKind.MadeFriend => $"{a} and {b} are getting along.",
                SimEventKind.PlayedInWater => $"{a} is splashing about.",
                SimEventKind.Intimidated => $"{b} fled from {a}.",
                SimEventKind.Discovered => $"{a} joined your collection!",
                _ => $"{a} did something.",
            };
        }

        /// <summary>Events the player would enjoy seeing, versus routine chatter.</summary>
        public bool IsNotable => Kind is SimEventKind.StoleFood or SimEventKind.StartedChase
            or SimEventKind.KnockedOut or SimEventKind.MadeFriend or SimEventKind.Intimidated
            or SimEventKind.PlayedInWater or SimEventKind.Discovered;
    }

    /// <summary>Static pub/sub for simulation events. One publisher, several listeners
    /// (UI toasts, economy, audio, analytics-free telemetry).</summary>
    public static class SimEventBus
    {
        public static event Action<SimEvent> Raised;

        public static void Publish(in SimEvent e) => Raised?.Invoke(e);

        /// <summary>Domain reloads are disabled in fast play mode, so listeners must be
        /// cleared explicitly when the simulation tears down.</summary>
        public static void Reset() => Raised = null;
    }
}
