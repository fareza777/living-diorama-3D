using LivingDiorama.Data;
using UnityEngine;

namespace LivingDiorama.Simulation
{
    /// <summary>
    /// Drives the day/night cycle. Time is stored as total in-game hours since the save
    /// was created, so offline progress is just arithmetic on the same number.
    /// </summary>
    public sealed class WorldClock
    {
        readonly SimulationSettings _settings;

        public WorldClock(SimulationSettings settings, double startHours = 6.0)
        {
            _settings = settings;
            TotalHours = startHours;
        }

        /// <summary>In-game hours elapsed since the save began. 24 hours per in-game day.</summary>
        public double TotalHours { get; private set; }

        /// <summary>0 at midnight, 0.5 at noon.</summary>
        public float NormalisedTime => (float)(TotalHours / 24.0 % 1.0);

        public int Day => (int)(TotalHours / 24.0) + 1;

        public float Daylight => _settings.DaylightAt(NormalisedTime);

        public bool IsNight => _settings.IsNight(NormalisedTime);

        /// <summary>In-game hours that pass per real second.</summary>
        public float HoursPerRealSecond => 24f / _settings.SecondsPerDay;

        public void Advance(float realDeltaSeconds)
        {
            TotalHours += realDeltaSeconds * HoursPerRealSecond;
        }

        public void AdvanceHours(double hours)
        {
            TotalHours += hours;
        }

        public void SetTotalHours(double hours)
        {
            TotalHours = hours;
        }

        /// <summary>Sun direction for the current time. Tilted so shadows stay readable
        /// from the diorama's fixed-ish camera rather than being physically correct.</summary>
        public Quaternion SunRotation
        {
            get
            {
                float t = NormalisedTime;
                float elevation = Mathf.Lerp(-12f, 78f, Mathf.Sin(t * Mathf.PI * 2f - Mathf.PI * 0.5f) * 0.5f + 0.5f);
                float azimuth = Mathf.Lerp(-40f, 220f, t);
                return Quaternion.Euler(elevation, azimuth, 0f);
            }
        }

        /// <summary>A short human label, e.g. "Day 3 - 14:20".</summary>
        public string Label
        {
            get
            {
                float hoursIntoDay = NormalisedTime * 24f;
                int h = Mathf.FloorToInt(hoursIntoDay);
                int m = Mathf.FloorToInt((hoursIntoDay - h) * 60f);
                return $"Day {Day} - {h:00}:{m:00}";
            }
        }
    }
}
