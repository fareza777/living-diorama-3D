using System;
using System.Collections.Generic;
using LivingDiorama.Data;
using LivingDiorama.Simulation;
using UnityEngine;

namespace LivingDiorama.Meta
{
    /// <summary>
    /// Watches the diorama and records the first time each named moment happens.
    ///
    /// The simulation was always producing these -- a wolf running down a thief, a slime
    /// dancing in the shallows -- but nothing said any of it mattered, so the player had
    /// no reason to care which creatures they put together. Naming the good combinations
    /// and keeping a list of the ones still unseen is what turns watching into playing.
    ///
    /// The rewards are deliberately larger than idle income: coins accumulate whether or
    /// not you think about your cast, essence only arrives when you get one right.
    /// </summary>
    public sealed class ChronicleService : IDisposable
    {
        readonly GameState _state;
        readonly GameDatabase _db;
        readonly WorldClock _clock;

        /// <summary>Raised the first time a moment is witnessed, so the interface can
        /// make something of it.</summary>
        public event Action<MomentDefinition> MomentWitnessed;

        public ChronicleService(GameState state, GameDatabase db, WorldClock clock)
        {
            _state = state;
            _db = db;
            _clock = clock;

            SimEventBus.Raised += OnSimEvent;
        }

        public void Dispose() => SimEventBus.Raised -= OnSimEvent;

        public IReadOnlyList<MomentDefinition> All => _db.moments;

        public int WitnessedCount
        {
            get
            {
                int n = 0;
                foreach (MomentDefinition m in _db.moments)
                {
                    if (m != null && HasWitnessed(m.id)) n++;
                }
                return n;
            }
        }

        public bool HasWitnessed(string momentId) =>
            !string.IsNullOrEmpty(momentId) && _state.Data.witnessedMoments.Contains(momentId);

        void OnSimEvent(SimEvent e)
        {
            if (_db.moments == null) return;

            bool night = _clock != null && _clock.IsNight;

            foreach (MomentDefinition moment in _db.moments)
            {
                if (moment == null || HasWitnessed(moment.id)) continue;
                if (!moment.Matches(in e, night)) continue;

                _state.Data.witnessedMoments.Add(moment.id);
                if (moment.essence > 0) _state.Grant(CurrencyKind.Essence, moment.essence);

                MomentWitnessed?.Invoke(moment);

                // One event is one moment. Two moments firing off a single chase would
                // read as a bug even when both technically matched.
                return;
            }
        }
    }
}
