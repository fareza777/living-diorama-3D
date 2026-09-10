using System.Collections.Generic;
using LivingDiorama.Core;
using LivingDiorama.Data;
using LivingDiorama.Save;
using UnityEngine;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// One creature, as large as the screen will allow, turnable in the hand.
    ///
    /// This used to be a 250-pixel strip inside the collection card, under a grid and
    /// above three lines of text and a button. Pinching something that small mostly moves
    /// it out of frame, and there was never enough of the creature on screen to be worth
    /// turning -- which is the opposite of the point. Owning a creature you modelled in
    /// three dimensions should be worth looking at.
    /// </summary>
    public sealed class InspectPanel
    {
        const float MinZoom = 0.55f;
        const float MaxZoom = 5f;

        readonly VisualElement _root;
        readonly GameController _game;
        readonly GameUI _ui;

        readonly VisualElement _viewport;
        readonly Label _name, _flavour, _tags;
        readonly Button _place;

        CreatureStage _stage;
        CreatureDefinition _selected;

        Vector2 _dragFrom;
        bool _dragging;

        // Pointer id -> where that finger is. Two of them make a pinch.
        readonly Dictionary<int, Vector2> _touches = new(2);
        float _pinchFrom;
        float _lastTapAt = -1f;

        public VisualElement Root => _root;

        public InspectPanel(VisualElement root, GameController game, GameUI ui)
        {
            _root = root;
            _game = game;
            _ui = ui;

            _viewport = root.Q<VisualElement>("inspect-viewport");
            _name = root.Q<Label>("inspect-name");
            _flavour = root.Q<Label>("inspect-flavour");
            _tags = root.Q<Label>("inspect-tags");
            _place = root.Q<Button>("btn-inspect-place");

            HookViewport();

            _place.clicked += OnPlace;
            root.Q<Button>("btn-inspect-back").clicked += ui.OpenCollection;
        }

        /// <summary>Give the panel the turntable to draw into. Supplied after construction
        /// because the stage needs the creature factory, which the world owns.</summary>
        public void AttachStage(CreatureStage stage)
        {
            _stage = stage;
            if (_stage != null) _viewport.style.backgroundImage = Background.FromRenderTexture(_stage.Texture);
        }

        public void Show(CreatureDefinition def)
        {
            _selected = def;

            if (_stage != null)
            {
                _stage.Yaw = 150f;
                _stage.Pitch = 8f;
                _stage.Zoom = 1f;
                _ = _stage.Show(def);
            }

            _name.text = def.displayName;
            _flavour.text = string.IsNullOrWhiteSpace(def.flavourText)
                ? $"{def.rarity} - {def.diet}, {def.activity}"
                : def.flavourText;

            _tags.text = def.tags is { Length: > 0 } ? "Traits: " + string.Join(", ", def.tags) : "";

            SavedCreature spare = FindUnplaced(def.id);

            _place.text = spare == null
                ? "All copies already placed"
                : _game.State.HasRoomToPlace
                    ? "Place in diorama"
                    : "Diorama is full - expand first";

            _place.SetEnabled(spare != null && _game.State.HasRoomToPlace);
        }

        /// <summary>Called every frame while the inspector is on screen.</summary>
        public void Tick()
        {
            if (_selected == null || _stage == null) return;

            // The render texture is stretched to fill whatever the layout gave the
            // viewport, so the camera has to be told that shape or the creature comes out
            // squeezed. Reading it per frame costs nothing and survives a rotation.
            float width = _viewport.resolvedStyle.width;
            float height = _viewport.resolvedStyle.height;
            if (width > 1f && height > 1f) _stage.ViewportAspect = width / height;

            _stage.Render();
        }

        // ---- gestures --------------------------------------------------------

        void HookViewport()
        {
            // The hint sits inside the viewport and would otherwise be the thing a finger
            // lands on. Events bubble, so it mostly works anyway -- but a label that can be
            // picked is one more way for a gesture to go missing, and the last time this
            // screen shipped, pinch did nothing at all.
            foreach (Label hint in _viewport.Query<Label>().ToList())
            {
                hint.pickingMode = PickingMode.Ignore;
            }

            _viewport.RegisterCallback<PointerDownEvent>(e =>
            {
                _touches[e.pointerId] = e.position;

                if (_touches.Count == 1)
                {
                    _dragging = true;
                    _dragFrom = e.position;
                    HandleDoubleTap();
                }
                else
                {
                    // A second finger ends the turn and starts a pinch.
                    _dragging = false;
                    _pinchFrom = PinchSpan();
                }

                _viewport.CapturePointer(e.pointerId);
            });

            _viewport.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_stage == null) return;
                if (_touches.ContainsKey(e.pointerId)) _touches[e.pointerId] = e.position;

                if (_touches.Count >= 2)
                {
                    float span = PinchSpan();
                    if (_pinchFrom > 1f && span > 1f)
                    {
                        _stage.Zoom = Mathf.Clamp(_stage.Zoom * (span / _pinchFrom), MinZoom, MaxZoom);
                    }
                    _pinchFrom = span;
                    return;
                }

                if (!_dragging) return;

                Vector2 delta = (Vector2)e.position - _dragFrom;
                _dragFrom = e.position;

                _stage.Yaw -= delta.x * 0.5f;
                _stage.Pitch = Mathf.Clamp(_stage.Pitch + delta.y * 0.25f, -25f, 55f);
            });

            _viewport.RegisterCallback<PointerUpEvent>(e =>
            {
                _touches.Remove(e.pointerId);
                _dragging = false;
                _viewport.ReleasePointer(e.pointerId);
            });

            _viewport.RegisterCallback<PointerCancelEvent>(e =>
            {
                _touches.Remove(e.pointerId);
                _dragging = false;
            });

            // Kept for the editor and anything with a wheel.
            _viewport.RegisterCallback<WheelEvent>(e =>
            {
                if (_stage == null) return;
                _stage.Zoom = Mathf.Clamp(_stage.Zoom - e.delta.y * 0.08f, MinZoom, MaxZoom);
            });
        }

        /// <summary>Zoomed in and lost, with no way back short of closing the screen, is
        /// the usual end state of a free camera. A double tap puts it back.</summary>
        void HandleDoubleTap()
        {
            float now = Time.unscaledTime;
            bool quick = now - _lastTapAt < 0.32f;
            _lastTapAt = now;

            if (!quick || _stage == null) return;

            _stage.Zoom = 1f;
            _stage.Pitch = 8f;
            _stage.Yaw = 150f;
            _lastTapAt = -1f;
        }

        float PinchSpan()
        {
            if (_touches.Count < 2) return 0f;

            Vector2 a = default, b = default;
            int n = 0;
            foreach (KeyValuePair<int, Vector2> touch in _touches)
            {
                if (n == 0) a = touch.Value;
                else if (n == 1) b = touch.Value;
                else break;
                n++;
            }

            return Vector2.Distance(a, b);
        }

        // ---- placing ---------------------------------------------------------

        SavedCreature FindUnplaced(string speciesId)
        {
            foreach (SavedCreature c in _game.State.Data.creatures)
            {
                if (c.speciesId == speciesId && !c.placed) return c;
            }
            return null;
        }

        void OnPlace()
        {
            if (_selected == null) return;

            SavedCreature spare = FindUnplaced(_selected.id);
            if (spare == null) return;

            if (_game.PlaceFromCollection(spare.instanceId))
            {
                _ui.RefreshAll();
                _ui.CloseModals();
            }
        }
    }
}
