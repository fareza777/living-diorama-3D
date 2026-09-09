using UnityEngine;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// The vertical scrim that seats the title screen's menu over the live diorama.
    ///
    /// This has now failed twice as a picture. As a USS `linear-gradient` UI Toolkit
    /// rejected it outright and drew nothing; as a generated texture it worked in the
    /// editor and still came out blank on the phone. Both failures were silent, and both
    /// left the title sitting on bare sky.
    ///
    /// So it is not a picture any more. A stack of plain coloured bands uses only
    /// background-color, which is the most boring thing UI Toolkit can do and therefore
    /// the one thing that behaves identically everywhere. Forty bands over a phone's
    /// height is finer than the eye resolves against a dark ground.
    /// </summary>
    public static class Scrim
    {
        /// <summary>Fine enough that no step is visible. Forty banded plainly against the
        /// pale daytime sky the title screen is usually shown over.</summary>
        const int Bands = 96;

        /// <summary>Peak opacity at the very bottom.
        ///
        /// This reads far higher than it looks like it should. The project renders in
        /// linear colour, so the panel blends there too: 0.88 over a bright sky measured
        /// out at roughly two thirds of the coverage the number implies, and the menu was
        /// still sitting on grey. This is the value that actually lands on ink.</summary>
        const float Peak = 0.97f;

        /// <summary>Ink, matching the surface the rest of the interface is drawn on.</summary>
        static readonly Color Ink = new(6f / 255f, 7f / 255f, 14f / 255f);

        /// <summary>Heaviest at the bottom, gone before the halfway line. The falloff is
        /// smoothed rather than linear: a straight ramp reads as a wall with a visible
        /// edge across the middle of the screen, and the diorama behind the title is the
        /// best thing on it.</summary>
        public static void Fill(VisualElement host)
        {
            if (host == null || host.childCount > 0) return;

            host.style.flexDirection = FlexDirection.Column;

            for (int i = 0; i < Bands; i++)
            {
                // Children stack downwards, so the first band is the top of the screen
                // and t counts up from the bottom.
                float t = 1f - (i + 0.5f) / Bands;
                float k = Mathf.Clamp01(t / 0.58f);
                float alpha = Peak * (1f - k * k * (3f - 2f * k));

                var band = new VisualElement { pickingMode = PickingMode.Ignore };
                band.style.flexGrow = 1f;
                band.style.backgroundColor = new Color(Ink.r, Ink.g, Ink.b, alpha);
                host.Add(band);
            }
        }
    }
}
