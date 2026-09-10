using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// The interface's movement.
    ///
    /// Everything here works the same way: set the "from" state as an inline style,
    /// then clear it a frame later so the element animates back to whatever the
    /// stylesheet says. The transition itself is declared in USS, next to the thing it
    /// animates.
    ///
    /// It is done in that order on purpose. Declaring the entrance state in the
    /// stylesheet -- opacity zero, waiting to be switched on -- means that if the code
    /// that switches it on ever fails to run, the screen is simply invisible, silently,
    /// and only on device. Starting from the visible state and animating *into* it makes
    /// the worst case a screen that appears without moving.
    ///
    /// Not DOTween. It tweens Transforms, Materials and CanvasGroups; a VisualElement is
    /// none of those, and its geometry lives in the resolved style. UI Toolkit's own
    /// transitions run on the layout thread and cost nothing.
    /// </summary>
    public static class UiMotion
    {
        /// <summary>Long enough for the layout to settle and the "from" state to be
        /// painted once. A single frame is not reliably enough when the element was
        /// hidden a moment ago.</summary>
        const long Settle = 24;

        /// <summary>Rise into place: fade up, scale up slightly, drift up a few pixels.
        /// The workhorse, used by every panel and card.</summary>
        public static void Enter(VisualElement element, float lift = 12f, float from = 0.94f, long delayMs = 0)
        {
            if (element == null) return;

            Hold(element, lift, from);
            element.schedule.Execute(() => Release(element)).ExecuteLater(delayMs + Settle);
        }

        /// <summary>The state an element animates out of.</summary>
        static void Hold(VisualElement element, float lift, float from)
        {
            element.style.opacity = 0f;
            element.style.scale = new Scale(new Vector3(from, from, 1f));
            element.style.translate = new Translate(0f, lift, 0f);
        }

        /// <summary>Drop the inline overrides so the element settles onto whatever the
        /// stylesheet says, animating there through the transition declared on it.</summary>
        static void Release(VisualElement element)
        {
            element.style.opacity = StyleKeyword.Null;
            element.style.scale = StyleKeyword.Null;
            element.style.translate = StyleKeyword.Null;
        }

        /// <summary>
        /// The same, dealt out one after another.
        ///
        /// A grid of twelve cards appearing at once is a flash; the same twelve arriving
        /// forty milliseconds apart reads as the screen assembling itself. The total is
        /// capped so a long list never turns into a wait.
        /// </summary>
        public static void Stagger(VisualElement parent, long stepMs = 38, long maxMs = 460)
        {
            if (parent == null) return;

            var children = new List<VisualElement>(parent.childCount);
            foreach (VisualElement child in parent.Children())
            {
                Hold(child, 14f, 0.96f);
                children.Add(child);
            }

            if (children.Count == 0) return;

            // One scheduler walking the list, rather than one per child. Per-child
            // timers left the tail of a grid sitting at zero opacity -- cards built,
            // laid out, taking up their space, and invisible. A single cursor either
            // advances or it does not, which is a great deal easier to be sure of.
            int next = 0;
            IVisualElementScheduledItem cursor = null;
            cursor = parent.schedule.Execute(() =>
            {
                if (next < children.Count) Release(children[next]);

                next++;
                if (next >= children.Count) cursor.Pause();
            }).Every(stepMs);

            // And a net under it. Whatever happens to the scheduler -- a panel closed
            // mid-run, a list rebuilt underneath it -- everything is visible shortly
            // afterwards regardless. An entrance is worth having; a permanently blank
            // grid is not.
            parent.schedule.Execute(() =>
            {
                cursor.Pause();
                foreach (VisualElement child in children) Release(child);
            }).ExecuteLater(maxMs + stepMs * children.Count + 250);
        }

        /// <summary>
        /// Run a number up to its new value instead of swapping it.
        ///
        /// Earning coins is the reward; a label that simply changes hides it. The final
        /// frame writes the real number rather than the interpolated one, so the display
        /// can never disagree with the save file.
        /// </summary>
        public static void CountTo(Label label, int to, string format = "N0", long durationMs = 480)
        {
            if (label == null) return;

            if (!int.TryParse(label.text, System.Globalization.NumberStyles.Any,
                              System.Globalization.CultureInfo.CurrentCulture, out int from) || from == to)
            {
                label.text = to.ToString(format);
                return;
            }

            // Nothing to celebrate about a number going down, and nothing worth waiting
            // for on a big jump either.
            if (to < from || Mathf.Abs(to - from) > 100000)
            {
                label.text = to.ToString(format);
                return;
            }

            float started = Time.unscaledTime;
            float seconds = durationMs / 1000f;

            IVisualElementScheduledItem tick = null;
            tick = label.schedule.Execute(() =>
            {
                float t = Mathf.Clamp01((Time.unscaledTime - started) / seconds);
                float eased = 1f - Mathf.Pow(1f - t, 3f);

                label.text = Mathf.RoundToInt(Mathf.Lerp(from, to, eased)).ToString(format);

                if (t < 1f) return;

                label.text = to.ToString(format);
                tick.Pause();
            }).Every(16);
        }

        /// <summary>
        /// A slow breath, for a control that is waiting to be pressed.
        ///
        /// USS has no keyframes, so this alternates a class and lets the transition on
        /// it do the interpolation. Only ever used on a call to action, and stopped the
        /// moment it is acted on: an interface that animates permanently is a tiring one.
        /// </summary>
        public static void Breathe(VisualElement element, bool on)
        {
            if (element == null) return;

            if (Breathing.TryGetValue(element, out IVisualElementScheduledItem running))
            {
                running.Pause();
                Breathing.Remove(element);
            }

            element.RemoveFromClassList("is-breathing");
            if (!on) return;

            bool up = false;
            IVisualElementScheduledItem beat = element.schedule.Execute(() =>
            {
                up = !up;
                element.EnableInClassList("is-breathing", up);
            }).Every(900);

            Breathing[element] = beat;
        }

        static readonly Dictionary<VisualElement, IVisualElementScheduledItem> Breathing = new();

        /// <summary>Grow a bar to its value rather than snapping it. The width is a
        /// percentage, and the transition is declared on the element in USS.</summary>
        public static void FillTo(VisualElement fill, float fraction01)
        {
            if (fill == null) return;
            fill.style.width = Length.Percent(Mathf.Clamp01(fraction01) * 100f);
        }

        /// <summary>A short attention pulse: brighten, then settle. Used when a value
        /// changes for a reason the player should notice.</summary>
        public static void Flash(VisualElement element)
        {
            if (element == null) return;

            element.AddToClassList("is-flash");
            element.schedule.Execute(() => element.RemoveFromClassList("is-flash")).ExecuteLater(220);
        }
    }
}
