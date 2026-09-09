using System.Collections.Generic;
using LivingDiorama.Core;
using LivingDiorama.Data;
using LivingDiorama.Save;
using UnityEngine;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// The collection screen: every species in the game, discovered ones in colour and
    /// the rest as silhouettes. Undiscovered entries still show their rarity border,
    /// because knowing a Legendary exists is half the reason to keep opening boxes.
    /// </summary>
    public sealed class CollectionPanel
    {
        readonly VisualElement _root;
        readonly GameController _game;
        readonly GameUI _ui;

        readonly VisualElement _grid;
        readonly Label _progress;
        readonly VisualElement _detail;
        readonly Label _detailName, _detailFlavour, _detailTags;
        readonly Button _placeButton;
        readonly VisualElement _viewport;

        CreatureDefinition _selected;
        CreatureStage _stage;

        Vector2 _dragFrom;
        bool _dragging;

        // Pointer id -> where that finger is. Two of them make a pinch.
        readonly Dictionary<int, Vector2> _touches = new(2);
        float _pinchFrom;

        public VisualElement Root => _root;

        public CollectionPanel(VisualElement root, GameController game, GameUI ui)
        {
            _root = root;
            _game = game;
            _ui = ui;

            _grid = root.Q<VisualElement>("collection-grid-inner");
            _progress = root.Q<Label>("collection-progress");
            _detail = root.Q<VisualElement>("collection-detail");
            _detailName = root.Q<Label>("collection-detail-name");
            _detailFlavour = root.Q<Label>("collection-detail-flavour");
            _detailTags = root.Q<Label>("collection-detail-tags");
            _placeButton = root.Q<Button>("btn-place");
            _viewport = root.Q<VisualElement>("collection-viewport");

            HookViewport();

            _placeButton.clicked += OnPlace;
            root.Q<Button>("btn-collection-close").clicked += ui.CloseModals;
        }

        /// <summary>Give the panel the turntable to draw into. Supplied after construction
        /// because the stage needs the creature factory, which the world owns.</summary>
        public void AttachStage(CreatureStage stage)
        {
            _stage = stage;
            if (_stage != null) _viewport.style.backgroundImage = Background.FromRenderTexture(_stage.Texture);
        }

        /// <summary>Drag turns the model, pinch pushes in. The viewport is doing the same
        /// job as the diorama camera, so it answers to the same gestures.</summary>
        void HookViewport()
        {
            _viewport.RegisterCallback<PointerDownEvent>(e =>
            {
                _touches[e.pointerId] = e.position;

                if (_touches.Count == 1)
                {
                    _dragging = true;
                    _dragFrom = e.position;
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
                    // Pinch to zoom.
                    //
                    // This was a mouse wheel, which a phone does not have -- so the hint
                    // under the model invited a gesture that did nothing at all.
                    float span = PinchSpan();
                    if (_pinchFrom > 1f && span > 1f)
                    {
                        _stage.Zoom = Mathf.Clamp(_stage.Zoom * (span / _pinchFrom), 0.6f, 3.2f);
                    }
                    _pinchFrom = span;
                    return;
                }

                if (!_dragging) return;

                Vector2 delta = (Vector2)e.position - _dragFrom;
                _dragFrom = e.position;

                _stage.Yaw -= delta.x * 0.5f;
                _stage.Pitch = Mathf.Clamp(_stage.Pitch + delta.y * 0.25f, -18f, 42f);
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
                _stage.Zoom = Mathf.Clamp(_stage.Zoom - e.delta.y * 0.06f, 0.6f, 3.2f);
            });
        }

        /// <summary>Distance between the first two fingers on the viewport.</summary>
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

        /// <summary>Called every frame while the collection is open.</summary>
        public void Tick()
        {
            if (_selected != null) _stage?.Render();
        }

        public void Refresh()
        {
            _grid.Clear();
            _detail.AddToClassList("hidden");
            _selected = null;

            _progress.text = $"{_game.State.DiscoveredCount} of {_game.State.SpeciesTotal} discovered  -  " +
                             $"{_game.State.PlacedCount}/{_game.State.PopulationCap} placed";

            foreach (CreatureDefinition def in _game.Database.creatures)
            {
                if (def != null) _grid.Add(BuildCard(def));
            }
        }

        VisualElement BuildCard(CreatureDefinition def)
        {
            bool known = _game.State.IsDiscovered(def.id);

            var card = new VisualElement();
            card.AddToClassList("card");
            card.AddToClassList("card--" + def.rarity.ToString().ToLowerInvariant());
            if (!known) card.AddToClassList("card--locked");

            var portrait = new VisualElement();
            portrait.AddToClassList("card__portrait");
            if (known && def.icon != null)
            {
                portrait.style.backgroundImage = new StyleBackground(def.icon);
            }
            else if (!known)
            {
                portrait.AddToClassList("card__portrait--unknown");
            }
            else
            {
                // No portrait asset: fall back to the rarity colour so the grid still reads.
                portrait.style.backgroundColor = BoxPanel.RarityColour(def.rarity) * 0.5f;
            }
            card.Add(portrait);

            var name = new Label(known ? def.displayName : "???");
            name.AddToClassList("card__name");
            card.Add(name);

            int copies = _game.State.CopiesOf(def.id);
            var count = new Label(known ? (copies > 1 ? $"x{copies}" : "owned") : "undiscovered");
            count.AddToClassList("card__count");
            card.Add(count);

            if (known)
            {
                card.RegisterCallback<ClickEvent>(_ => Select(def));
            }

            return card;
        }

        void Select(CreatureDefinition def)
        {
            _selected = def;
            _detail.RemoveFromClassList("hidden");

            if (_stage != null)
            {
                _stage.Yaw = 150f;
                _stage.Pitch = 8f;
                _stage.Zoom = 1f;
                _ = _stage.Show(def);
            }

            _detailName.text = def.displayName;
            _detailFlavour.text = string.IsNullOrWhiteSpace(def.flavourText)
                ? $"{def.rarity} - {def.diet}, {def.activity}"
                : def.flavourText;

            _detailTags.text = def.tags is { Length: > 0 }
                ? "Traits: " + string.Join(", ", def.tags)
                : "";

            SavedCreature spare = FindUnplaced(def.id);
            bool canPlace = spare != null && _game.State.HasRoomToPlace;

            _placeButton.text = spare == null
                ? "All copies already placed"
                : _game.State.HasRoomToPlace
                    ? "Place in diorama"
                    : "Diorama is full - expand first";

            _placeButton.SetEnabled(canPlace);
        }

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
