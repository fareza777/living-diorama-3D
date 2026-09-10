using System;
using LivingDiorama.Core;
using LivingDiorama.Diorama;
using LivingDiorama.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// Putting furniture down.
    ///
    /// Deliberately not a grid editor. The diorama is a place rather than a floor plan,
    /// and asking the player to line things up on a lattice would make it a spreadsheet
    /// with grass on it. You pick a thing, drag your finger over the ground, and a ghost
    /// follows -- green where it will go, red where it will not, with the reason written
    /// underneath. Lift your finger and it is built.
    ///
    /// The validity check runs continuously rather than on release. Finding out why you
    /// could not build only after committing is the difference between a tool that
    /// teaches its own rules and one that just says no.
    /// </summary>
    public sealed class BuildMode
    {
        readonly GameController _game;
        readonly DioramaCamera _camera;
        readonly PlacementService _placements;
        readonly VisualElement _root;
        readonly Label _hint;

        GameObject _ghost;
        Renderer[] _ghostRenderers;
        Material _ghostOk, _ghostBad;

        string _selected;
        bool _valid;
        Vector3 _at;
        float _yaw;

        public bool Active { get; private set; }

        /// <summary>Raised when something is built or the mode closes, so the tray and the
        /// palette can catch up.</summary>
        public event Action Changed;

        public BuildMode(GameController game, DioramaCamera camera, PlacementService placements,
                         VisualElement root)
        {
            _game = game;
            _camera = camera;
            _placements = placements;
            _root = root;
            _hint = root.Q<Label>("build-hint");

            BuildGhostMaterials();
        }

        void BuildGhostMaterials()
        {
            Shader shader = Shader.Find("Living Diorama/Creature");

            _ghostOk = new Material(shader) { name = "GhostOk" };
            _ghostBad = new Material(shader) { name = "GhostBad" };

            foreach ((Material material, Color tint) in new[]
                     {
                         (_ghostOk, new Color(0.45f, 1f, 0.55f, 1f)),
                         (_ghostBad, new Color(1f, 0.42f, 0.36f, 1f)),
                     })
            {
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
                if (material.HasProperty("_RimStrength")) material.SetFloat("_RimStrength", 0.5f);
                if (material.HasProperty("_SSSStrength")) material.SetFloat("_SSSStrength", 0f);
            }
        }

        // ---- entering and leaving --------------------------------------------

        public void Begin(string placeableId)
        {
            if (!Placeables.TryGet(placeableId, out Placeable definition)) return;
            if (_game.State.StockOf(placeableId) <= 0) return;

            _selected = placeableId;
            Active = true;
            _yaw = 0f;

            _root.RemoveFromClassList("hidden");
            BuildGhost(definition);

            // The camera keeps working -- you have to be able to look around while
            // deciding where something goes -- but taps belong to this now.
            _camera.InputBlocked = false;
        }

        public void Cancel()
        {
            Active = false;
            _selected = null;

            _root.AddToClassList("hidden");
            DestroyGhost();
            Changed?.Invoke();
        }

        void BuildGhost(Placeable definition)
        {
            DestroyGhost();

            Mesh mesh = _placements.MeshFor(definition.Id);
            if (mesh == null) return;

            _ghost = new GameObject("BuildGhost");
            _ghost.transform.localScale = Vector3.one * definition.Height;
            _ghost.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = _ghost.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _ghostOk;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _ghostRenderers = new[] { (Renderer)renderer };
        }

        void DestroyGhost()
        {
            if (_ghost != null) UnityEngine.Object.Destroy(_ghost);
            _ghost = null;
            _ghostRenderers = null;
        }

        // ---- the drag ---------------------------------------------------------

        /// <summary>Called every frame while the mode is open.</summary>
        public void Tick(IWorldSurfaceProvider surface)
        {
            if (!Active || _ghost == null) return;

            Vector2 pointer = PointerPosition();
            if (!_camera.TryPickGround(pointer, surface.Surface, out Vector3 ground))
            {
                SetHint("Point at the diorama", false);
                return;
            }

            _at = ground;
            Placeables.TryGet(_selected, out Placeable definition);

            _valid = _placements.CanPlaceAt(_at, definition.Radius, out string reason);
            _ghost.transform.position = _at;
            _ghost.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            foreach (Renderer renderer in _ghostRenderers)
            {
                renderer.sharedMaterial = _valid ? _ghostOk : _ghostBad;
            }

            SetHint(_valid ? "Release to build the " + definition.DisplayName.ToLowerInvariant() : reason, _valid);
        }

        static Vector2 PointerPosition()
        {
            if (Input.touchCount > 0) return Input.GetTouch(0).position;
            return Input.mousePosition;
        }

        void SetHint(string text, bool ok)
        {
            if (_hint == null) return;

            _hint.text = text;
            _hint.EnableInClassList("build__hint--bad", !ok);
        }

        /// <summary>Turn the thing being placed. A bed against a wall and a bed across the
        /// path are different decisions.</summary>
        public void Rotate() => _yaw = Mathf.Repeat(_yaw + 45f, 360f);

        /// <summary>Commit. Returns false when the spot was not legal, in which case the
        /// mode stays open rather than throwing away the player's aim.</summary>
        public bool Confirm()
        {
            if (!Active || !_valid || _selected == null) return false;
            if (!_game.State.TakeStock(_selected)) return false;

            Placement placement = _placements.Place(_selected, _at, _yaw);
            if (placement == null) return false;

            _game.State.Data.placements.Add(new Save.SavedPlacement
            {
                id = placement.Id,
                placeableId = placement.Definition.Id,
                position = placement.Position,
                yaw = placement.Yaw,
            });

            Cancel();
            return true;
        }
    }

    /// <summary>Lets the build mode ask for the terrain without owning the world.</summary>
    public interface IWorldSurfaceProvider
    {
        Simulation.IWorldSurface Surface { get; }
    }
}
