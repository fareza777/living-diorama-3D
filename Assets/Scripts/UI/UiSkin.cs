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

            Vector4 border = sprite.border;
            element.style.unitySliceLeft = Mathf.RoundToInt(border.x);
            element.style.unitySliceBottom = Mathf.RoundToInt(border.y);
            element.style.unitySliceRight = Mathf.RoundToInt(border.z);
            element.style.unitySliceTop = Mathf.RoundToInt(border.w);

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

            foreach (VisualElement card in root.Query<VisualElement>(className: "modal__card").Build())
            {
                ApplyPlate(card, "panel_frame");
            }

            foreach (VisualElement chip in root.Query<VisualElement>(className: "chip").Build())
            {
                ApplyPlate(chip, "chip_plate");
            }

            foreach (Button button in root.Query<Button>(className: "button--primary").Build())
            {
                ApplyPlate(button, "button_primary");
                button.style.color = new Color(0.16f, 0.10f, 0.03f);
            }

            foreach (Button button in root.Query<Button>(className: "button--ghost").Build())
            {
                ApplyPlate(button, "button_ghost");
                // The generated secondary plate came out lighter than its brief, and pale
                // text on it is unreadable. Tinting the image down is a one-line fix that
                // does not require regenerating the art or inverting the type colour.
                button.style.unityBackgroundImageTintColor = new Color(0.44f, 0.41f, 0.47f);
                button.style.color = new Color(0.96f, 0.93f, 0.86f);
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
