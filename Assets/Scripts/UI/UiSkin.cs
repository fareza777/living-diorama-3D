using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// Dresses the interface in the generated art.
    ///
    /// The layout, spacing and states all live in USS; this only swaps flat panels and
    /// buttons for the nine-sliced painted plates and drops the painted icons into the
    /// currency chips. Keeping it separate means the interface still lays out correctly
    /// with no art present at all -- useful while the art pipeline is running, and the
    /// reason a missing file is a plain panel rather than a broken screen.
    /// </summary>
    public static class UiSkin
    {
        const string ArtPath = "UI/Art/";

        static readonly Dictionary<string, Sprite> Cache = new(24);

        public static Sprite Sprite(string key)
        {
            if (Cache.TryGetValue(key, out Sprite cached)) return cached;

            var sprite = Resources.Load<Sprite>(ArtPath + key);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>Apply a nine-sliced plate, taking the slice insets from the sprite's
        /// own border so the ornament never stretches.</summary>
        public static void ApplyPlate(VisualElement element, string spriteKey)
        {
            Sprite sprite = Sprite(spriteKey);
            if (element == null || sprite == null) return;

            element.style.backgroundImage = new StyleBackground(sprite);
            element.style.backgroundColor = Color.clear;

            // A sprite with no border is stretched whole. Slicing is only right when the
            // ornament lives in the border; the pill plates carry theirs in the middle,
            // and slicing those tiles the ornament rather than the background.
            Vector4 border = sprite.border;
            if (border.sqrMagnitude > 0.5f)
            {
                element.style.unitySliceLeft = Mathf.RoundToInt(border.x);
                element.style.unitySliceBottom = Mathf.RoundToInt(border.y);
                element.style.unitySliceRight = Mathf.RoundToInt(border.z);
                element.style.unitySliceTop = Mathf.RoundToInt(border.w);
            }
            else
            {
                element.style.backgroundSize = new StyleBackgroundSize(
                    new BackgroundSize(Length.Percent(100), Length.Percent(100)));
            }

            // The painted frame is the border now, so drop the drawn one.
            element.style.borderLeftWidth = 0;
            element.style.borderRightWidth = 0;
            element.style.borderTopWidth = 0;
            element.style.borderBottomWidth = 0;
        }

        /// <summary>Put a painted icon into an element that was a coloured dot.</summary>
        public static void ApplyIcon(VisualElement element, string spriteKey, float size = 0f)
        {
            Sprite sprite = Sprite(spriteKey);
            if (element == null || sprite == null) return;

            element.style.backgroundImage = new StyleBackground(sprite);
            element.style.backgroundColor = Color.clear;
            element.style.borderTopLeftRadius = 0;
            element.style.borderTopRightRadius = 0;
            element.style.borderBottomLeftRadius = 0;
            element.style.borderBottomRightRadius = 0;

            if (size > 0f)
            {
                element.style.width = size;
                element.style.height = size;
            }
        }

        /// <summary>
        /// Put an icon next to a button's label.
        ///
        /// A Button paints its own text across its whole box rather than through a child,
        /// so simply inserting an icon lays it straight over the words. The text has to be
        /// moved into a real child label first, and only then can the two sit side by side.
        /// </summary>
        public static void PrefixIcon(Button button, string spriteKey, float size = 22f)
        {
            Sprite sprite = Sprite(spriteKey);
            if (button == null || sprite == null) return;
            if (button.Q<VisualElement>("skin-icon") != null) return;

            string caption = button.text;
            button.text = string.Empty;

            var icon = new VisualElement { name = "skin-icon", pickingMode = PickingMode.Ignore };
            icon.style.backgroundImage = new StyleBackground(sprite);
            icon.style.width = size;
            icon.style.height = size;
            icon.style.marginRight = 7;
            icon.style.flexShrink = 0;

            var label = new Label(caption) { name = "skin-label", pickingMode = PickingMode.Ignore };
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = button.resolvedStyle.fontSize > 0
                ? button.resolvedStyle.fontSize
                : 15f;
            label.style.color = button.resolvedStyle.color;
            label.style.flexShrink = 0;

            button.style.flexDirection = FlexDirection.Row;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
            button.style.flexShrink = 0;

            button.Add(icon);
            button.Add(label);
        }

        /// <summary>
        /// Walk a subtree and skin anything recognisable. Safe to run repeatedly, which is
        /// what lets dynamically built lists get the same treatment as the authored markup.
        /// </summary>
        public static void SkinAll(VisualElement root)
        {
            if (root == null) return;

            // Modal cards are deliberately not framed. An ornate border around a dense
            // list fights the content for attention and eats the padding the text needs;
            // the fantasy is carried by the buttons and the title face instead, and the
            // card itself stays a quiet sheet of dark glass that words sit cleanly on.

            // Chips are deliberately not plated. The painted pill is a 21:9 image and a
            // currency chip is nearer 2:1, so stretching it crushes the brass rim into a
            // blob; clean dark glass with a bright icon reads better and stays legible at
            // any width the number happens to need.

            foreach (Button button in root.Query<Button>(className: "button--primary").Build())
            {
                ApplyPlate(button, "button_primary");
                // The plate's centre is dark navy under a brass bezel, so the label is
                // light on dark like everything else rather than dark on gold.
                button.style.color = new Color(1f, 0.93f, 0.78f);
            }

            foreach (Button button in root.Query<Button>(className: "button--ghost").Build())
            {
                ApplyPlate(button, "button_ghost");
                button.style.color = new Color(0.97f, 0.94f, 0.88f);
            }

            SkinCurrencyIcons(root);
        }

        static void SkinCurrencyIcons(VisualElement root)
        {
            Skin("chip__icon--coins", "icon_coin");
            Skin("chip__icon--essence", "icon_essence");
            Skin("chip__icon--keys", "icon_key");

            void Skin(string className, string spriteKey)
            {
                foreach (VisualElement element in root.Query<VisualElement>(className: className).Build())
                {
                    ApplyIcon(element, spriteKey, 20f);
                }
            }
        }

        /// <summary>Icon-only HUD buttons: the glyph in the markup is a fallback for when
        /// the painted art has not been generated.</summary>
        public static void SkinIconButton(Button button, string spriteKey)
        {
            Sprite sprite = Sprite(spriteKey);
            if (button == null || sprite == null) return;

            button.text = string.Empty;
            button.style.backgroundImage = new StyleBackground(sprite);
            button.style.backgroundColor = Color.clear;
            button.style.unityBackgroundImageTintColor = Color.white;
        }
    }
}
