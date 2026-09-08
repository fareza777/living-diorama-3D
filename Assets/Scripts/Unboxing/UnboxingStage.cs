using LivingDiorama.Data;
using LivingDiorama.Diorama;
using LivingDiorama.Presentation;
using UnityEngine;

namespace LivingDiorama.Unboxing
{
    /// <summary>
    /// The physical set the unboxing happens on: a chest on a floating plinth, lit by its
    /// own two-point rig, with the light shaft and particle systems it needs to open.
    ///
    /// It lives well above the diorama rather than inside it. That keeps the reveal free
    /// of whatever the creatures happen to be doing, lets the rig be lit for drama without
    /// touching the world's lighting, and means the camera cut reads as going somewhere
    /// rather than zooming in.
    /// </summary>
    public sealed class UnboxingStage : MonoBehaviour
    {
        public Transform ChestRoot { get; private set; }
        public Transform LidRoot { get; private set; }
        public Transform CreatureAnchor { get; private set; }
        public Vector3 StagePosition => transform.position;

        Renderer _seamRenderer;
        Renderer _beamRenderer;
        Material _seamMaterial;
        Material _beamMaterial;
        Light _keyLight;
        Light _rimLight;
        ParticleSystem _burst;
        ParticleSystem _motes;
        Transform _plinth;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        public static UnboxingStage Create(Transform parent, Vector3 position)
        {
            var go = new GameObject("UnboxingStage");
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            var stage = go.AddComponent<UnboxingStage>();
            stage.Build();
            stage.SetVisible(false);
            return stage;
        }

        void Build()
        {
            Shader toon = Shader.Find("Living Diorama/Ground");
            Shader additive = Shader.Find("Living Diorama/Additive");

            var propMaterial = new Material(toon) { name = "ChestMaterial" };
            if (propMaterial.HasProperty("_EdgeDarken")) propMaterial.SetFloat("_EdgeDarken", 0f);
            if (propMaterial.HasProperty("_CliffStart")) propMaterial.SetFloat("_CliffStart", 1.1f);
            if (propMaterial.HasProperty("_DetailStrength")) propMaterial.SetFloat("_DetailStrength", 0.03f);

            BuildPlinth(propMaterial);
            BuildChest(propMaterial, additive);
            BuildBeam(additive);
            BuildLights();
            BuildParticles(additive);

            CreatureAnchor = new GameObject("CreatureAnchor").transform;
            CreatureAnchor.SetParent(transform, false);
            CreatureAnchor.localPosition = new Vector3(0f, 0.5f, 0f);
        }

        void BuildPlinth(Material material)
        {
            var go = new GameObject("Plinth");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, -0.14f, 0f);
            _plinth = go.transform;

            var b = new ProceduralMeshes.Builder();
            var stone = new Color(0.20f, 0.19f, 0.24f);
            var trim = new Color(0.58f, 0.45f, 0.26f);

            ProceduralMeshes.Cylinder(b, new Vector3(0f, 0f, 0f), 0.78f, 0.70f, 0.10f, 12, stone);
            ProceduralMeshes.Cylinder(b, new Vector3(0f, 0.10f, 0f), 0.70f, 0.66f, 0.035f, 12, trim);

            go.AddComponent<MeshFilter>().sharedMesh = b.Bake("Plinth");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void BuildChest(Material propMaterial, Shader additive)
        {
            var wood = new Color(0.42f, 0.27f, 0.16f);
            var metal = new Color(0.34f, 0.33f, 0.36f);

            ChestRoot = new GameObject("Chest").transform;
            ChestRoot.SetParent(transform, false);

            var body = new GameObject("Body");
            body.transform.SetParent(ChestRoot, false);
            body.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.ChestBody(wood, metal);
            body.AddComponent<MeshRenderer>().sharedMaterial = propMaterial;

            LidRoot = new GameObject("Lid").transform;
            LidRoot.SetParent(ChestRoot, false);
            LidRoot.localPosition = ProceduralMeshes.ChestLidAnchor;

            var lid = new GameObject("LidMesh");
            lid.transform.SetParent(LidRoot, false);
            lid.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.ChestLid(wood, metal);
            lid.AddComponent<MeshRenderer>().sharedMaterial = propMaterial;

            var seam = new GameObject("Seam");
            seam.transform.SetParent(ChestRoot, false);
            seam.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.ChestSeam();

            _seamMaterial = new Material(additive) { name = "SeamGlow" };
            _seamMaterial.SetFloat("_UseVertexColor", 0f);
            _seamRenderer = seam.AddComponent<MeshRenderer>();
            _seamRenderer.sharedMaterial = _seamMaterial;
            _seamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>The shaft of light that erupts when the lid goes. A tapered cylinder
        /// with a vertical fade sells volumetric light for the price of eight triangles.</summary>
        void BuildBeam(Shader additive)
        {
            var go = new GameObject("Beam");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.42f, 0f);

            var b = new ProceduralMeshes.Builder();
            ProceduralMeshes.Cylinder(b, Vector3.zero, 0.34f, 0.95f, 3.4f, 10, Color.white);
            go.AddComponent<MeshFilter>().sharedMesh = b.Bake("Beam");

            _beamMaterial = new Material(additive) { name = "BeamGlow" };
            _beamMaterial.SetFloat("_UseVertexColor", 0f);
            _beamMaterial.SetFloat("_SoftFade", 0.30f);

            _beamRenderer = go.AddComponent<MeshRenderer>();
            _beamRenderer.sharedMaterial = _beamMaterial;
            _beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _beamRenderer.enabled = false;
        }

        void BuildLights()
        {
            var keyGo = new GameObject("KeyLight");
            keyGo.transform.SetParent(transform, false);
            keyGo.transform.localPosition = new Vector3(1.4f, 2.1f, -1.7f);

            _keyLight = keyGo.AddComponent<Light>();
            _keyLight.type = LightType.Point;
            _keyLight.range = 8f;
            _keyLight.intensity = 3.2f;
            _keyLight.color = new Color(1f, 0.95f, 0.86f);
            _keyLight.shadows = LightShadows.None;

            var rimGo = new GameObject("RimLight");
            rimGo.transform.SetParent(transform, false);
            rimGo.transform.localPosition = new Vector3(-1.5f, 1.1f, 1.9f);

            _rimLight = rimGo.AddComponent<Light>();
            _rimLight.type = LightType.Point;
            _rimLight.range = 7f;
            _rimLight.intensity = 2.4f;
            _rimLight.shadows = LightShadows.None;
        }

        void BuildParticles(Shader additive)
        {
            _burst = MakeParticles("Burst", additive, SparkTexture.Twinkle(), 160, 0.09f, 3.6f, 1.5f);
            _motes = MakeParticles("Motes", additive, SparkTexture.Dot(), 40, 0.05f, 0.35f, 4.0f);

            // Motes drift upward continuously once the box is on stage: the set never
            // looks completely still, even while nothing is happening.
            ParticleSystem.EmissionModule emission = _motes.emission;
            emission.rateOverTime = 7f;

            ParticleSystem.ShapeModule shape = _motes.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.6f, 0.1f, 1.6f);
        }

        ParticleSystem MakeParticles(string name, Shader additive, Texture texture,
                                     int maxParticles, float size, float speed, float lifetime)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.35f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.duration = 2f;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = maxParticles;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.gravityModifier = 0.28f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.22f;

            ParticleSystem.SizeOverLifetimeModule sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            ParticleSystem.ColorOverLifetimeModule colourOverLife = ps.colorOverLifetime;
            colourOverLife.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
            colourOverLife.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var material = new Material(additive) { name = name + "Material" };
            material.SetTexture("_MainTex", texture);
            material.SetFloat("_UseVertexColor", 1f);
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return ps;
        }

        // ---- presentation control -------------------------------------------

        public void SetVisible(bool visible) => gameObject.SetActive(visible);

        /// <summary>Tint the whole rig to the rarity being revealed. Doing this on the
        /// lights and the glow rather than on the chest means one prop serves every tier
        /// and the difference still reads instantly.</summary>
        public void ApplyRarity(Rarity rarity)
        {
            Color colour = RarityColour(rarity);

            _rimLight.color = colour;
            _rimLight.intensity = 2.0f + (int)rarity * 0.6f;

            _seamMaterial.SetColor(ColorId, colour);
            _beamMaterial.SetColor(ColorId, colour);

            SetParticleColour(_burst, colour);
            SetParticleColour(_motes, Color.Lerp(colour, Color.white, 0.55f));

            // Rarer pulls get a fuller sky of sparks.
            ParticleSystem.EmissionModule emission = _burst.emission;
            emission.SetBurst(0, new ParticleSystem.Burst(0f, 40 + (int)rarity * 32));
        }

        static void SetParticleColour(ParticleSystem ps, Color colour)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startColor = colour;
        }

        public static Color RarityColour(Rarity rarity) => rarity switch
        {
            Rarity.Uncommon => new Color(0.49f, 0.90f, 0.56f),
            Rarity.Rare => new Color(0.40f, 0.70f, 1f),
            Rarity.Epic => new Color(0.76f, 0.50f, 1f),
            Rarity.Legendary => new Color(1f, 0.78f, 0.32f),
            _ => new Color(0.78f, 0.80f, 0.88f),
        };

        public void SetSeamGlow(float intensity) => _seamMaterial.SetFloat(IntensityId, intensity);

        public void SetBeam(float intensity)
        {
            bool on = intensity > 0.01f;
            _beamRenderer.enabled = on;
            if (on) _beamMaterial.SetFloat(IntensityId, intensity);
        }

        public void EmitBurst() => _burst.Play();

        public void StartMotes() => _motes.Play();

        public void StopMotes() => _motes.Stop();

        public float KeyIntensity
        {
            get => _keyLight.intensity;
            set => _keyLight.intensity = value;
        }

        void Update()
        {
            // A slow counter-rotation on the plinth keeps the set alive between beats.
            if (_plinth != null) _plinth.Rotate(Vector3.up, -6f * Time.unscaledDeltaTime, Space.Self);
        }
    }
}
