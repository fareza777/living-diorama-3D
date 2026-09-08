using System;
using LivingDiorama.Data;
using LivingDiorama.Save;
using UnityEngine;

namespace LivingDiorama.Meta
{
    /// <summary>
    /// What happened while the player was away.
    ///
    /// Earnings are capped and discounted rather than paid in full: the game should
    /// reward coming back, not reward staying away. The clock still advances the whole
    /// elapsed time, so the diorama is genuinely at a different hour when reopened.
    /// </summary>
    public static class OfflineProgress
    {
        public readonly struct Report
        {
            public readonly double ElapsedHours;
            public readonly double CreditedHours;
            public readonly int Coins;
            public readonly bool WorthShowing;

            public Report(double elapsedHours, double creditedHours, int coins)
            {
                ElapsedHours = elapsedHours;
                CreditedHours = creditedHours;
                Coins = coins;
                // Under a couple of minutes away is not a "welcome back" moment.
                WorthShowing = coins > 0 && elapsedHours > 0.05;
            }

            public string ElapsedLabel
            {
                get
                {
                    if (ElapsedHours < 1.0) return $"{Mathf.RoundToInt((float)(ElapsedHours * 60.0))} minutes";
                    if (ElapsedHours < 48.0) return $"{ElapsedHours:0.#} hours";
                    return $"{ElapsedHours / 24.0:0.#} days";
                }
            }
        }

        /// <summary>
        /// Compute (but do not apply) what the player earned while away.
        /// </summary>
        /// <param name="nowUnix">Injected so this is testable without a clock.</param>
        public static Report Compute(SaveData data, GameDatabase db, long nowUnix)
        {
            if (data == null || db == null) return new Report(0, 0, 0);

            long elapsedSeconds = Math.Max(0, nowUnix - data.lastSeenUnix);
            double realHoursAway = elapsedSeconds / 3600.0;

            SimulationSettings sim = db.simulation;
            double creditedHours = Math.Min(realHoursAway, sim.offlineCapHours);

            // Idle income is expressed per in-game hour, so convert.
            double inGameHoursCredited = creditedHours * 3600.0 * (24.0 / sim.SecondsPerDay);

            float ratePerInGameHour = 0f;
            for (int i = 0; i < data.creatures.Count; i++)
            {
                SavedCreature c = data.creatures[i];
                if (!c.placed) continue;

                CreatureDefinition def = db.GetCreature(c.speciesId);
                if (def == null) continue;

                // Use the stored needs as a stand-in for wellbeing; a creature left hungry
                // earns less, which nudges the player to come back and expand their food.
                float wellbeing = Mathf.Clamp01(c.fullness * 0.5f + c.energy * 0.3f + c.social * 0.2f);
                ratePerInGameHour += def.coinsPerHour * Mathf.Lerp(0.15f, 1f, wellbeing);
            }

            var coins = (int)Math.Floor(ratePerInGameHour * inGameHoursCredited * sim.offlineEfficiency);
            return new Report(realHoursAway, creditedHours, Math.Max(0, coins));
        }

        /// <summary>Apply a report: pay out and roll the world clock forward.</summary>
        public static void Apply(SaveData data, GameDatabase db, in Report report, GameState state,
                                 float multiplier = 1f)
        {
            if (data == null) return;

            var coins = (int)Math.Round(report.Coins * Mathf.Max(1f, multiplier));
            if (coins > 0) state?.Grant(CurrencyKind.Coins, coins);

            // The clock advances by the real time away, uncapped: the player should come
            // back to a different time of day, even if the payout was capped.
            double inGameHours = report.ElapsedHours * 3600.0 * (24.0 / db.simulation.SecondsPerDay);
            data.clockHours += inGameHours;

            // Needs decay while away, but only down to a floor -- nothing should be at
            // death's door just because the player took a weekend off.
            DecayNeeds(data, db, inGameHours);

            data.lastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        static void DecayNeeds(SaveData data, GameDatabase db, double inGameHours)
        {
            const float floor = 0.25f;
            var hours = (float)Math.Min(inGameHours, 48.0);

            for (int i = 0; i < data.creatures.Count; i++)
            {
                SavedCreature c = data.creatures[i];
                CreatureDefinition def = db.GetCreature(c.speciesId);
                if (def == null) continue;

                c.fullness = Mathf.Max(floor, c.fullness - def.hungerRate * hours * 0.5f);
                c.social = Mathf.Max(floor, c.social - def.socialRate * hours * 0.5f);
                // Energy recovers while away: they had all that time to nap.
                c.energy = Mathf.Clamp01(c.energy + 0.05f * hours);
            }
        }
    }
}
