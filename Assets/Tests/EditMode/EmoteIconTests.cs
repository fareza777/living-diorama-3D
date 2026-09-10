using LivingDiorama.Presentation;
using LivingDiorama.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace LivingDiorama.Tests
{
    /// <summary>
    /// The mood icons are generated, not drawn, so nothing about them is verified by
    /// looking at an asset. Both faults they have shipped with were invisible to every
    /// other check: first every icon rendered as a filled square, then the heart
    /// rendered upside down. Both are shape faults, so these tests probe the shape.
    /// </summary>
    public sealed class EmoteIconTests
    {
        static bool Inside(Mood mood, float x, float y)
            => EmoteIcons.SignedDistance(mood, new Vector2(x, y)) < 0f;

        /// <summary>
        /// A heart is two lobes above a single point below. Sampling the four places
        /// that distinguish it from its mirror image pins the orientation: flip the
        /// shape and all four answers invert.
        /// </summary>
        [Test]
        public void Heart_PointsDownwards()
        {
            Assert.IsTrue(Inside(Mood.Social, 0f, -0.5f), "no point at the bottom");
            Assert.IsTrue(Inside(Mood.Social, -0.33f, 0.42f), "no left lobe at the top");
            Assert.IsTrue(Inside(Mood.Social, 0.33f, 0.42f), "no right lobe at the top");

            Assert.IsFalse(Inside(Mood.Social, 0f, 0.55f), "no cleft between the lobes");
            Assert.IsFalse(Inside(Mood.Social, -0.33f, -0.42f), "solid where the point tapers");
            Assert.IsFalse(Inside(Mood.Social, 0.33f, -0.42f), "solid where the point tapers");
        }

        /// <summary>An exclamation mark: bar above, dot below, a gap between them.</summary>
        [Test]
        public void Exclamation_HasABarAboveItsDot()
        {
            Assert.IsTrue(Inside(Mood.Scared, 0f, 0.5f), "no bar");
            Assert.IsTrue(Inside(Mood.Scared, 0f, -0.55f), "no dot");
            Assert.IsFalse(Inside(Mood.Scared, 0f, -0.35f), "bar and dot have run together");
        }

        /// <summary>
        /// Every icon has to be a shape rather than a fill: something inside it, and
        /// clear air at the corners. A solid square passes no part of this.
        /// </summary>
        [Test]
        public void EveryMood_IsAShapeAndNotAFilledTile()
        {
            foreach (Mood mood in System.Enum.GetValues(typeof(Mood)))
            {
                Assert.IsTrue(Inside(mood, 0f, 0f) || Inside(mood, 0f, 0.3f) || Inside(mood, 0.2f, 0f),
                    $"{mood} is empty in the middle");

                Assert.IsFalse(Inside(mood, -0.97f, -0.97f), $"{mood} fills its bottom-left corner");
                Assert.IsFalse(Inside(mood, 0.97f, 0.97f), $"{mood} fills its top-right corner");
            }
        }
    }
}
