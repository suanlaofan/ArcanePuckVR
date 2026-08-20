using UnityEngine;

namespace ArcanePuck
{
    public sealed class HitEffectController : MonoBehaviour
    {
        private ThemeConfig theme;
        private PuckController puck;
        private TrailRenderer trail;
        private EffectPool pool;
        private float trailSeconds;

        public void Initialize(ThemeConfig themeConfig, PuckController puckController, TrailRenderer puckTrail)
        {
            theme = themeConfig;
            puck = puckController;
            trail = puckTrail;
            pool = gameObject.AddComponent<EffectPool>();
            pool.Initialize(18, CreateEffect);
            HitEvents.Hit += OnHit;
            if (trail != null)
            {
                trail.emitting = false;
            }
        }

        private void OnDestroy()
        {
            HitEvents.Hit -= OnHit;
        }

        private void Update()
        {
            if (trail == null)
            {
                return;
            }

            if (trailSeconds <= 0f)
            {
                trail.emitting = false;
                return;
            }

            trailSeconds -= Time.deltaTime;
            trail.emitting = true;
        }

        private void OnHit(HitEvent hitEvent)
        {
            var playerHit = hitEvent.Side == HitSide.Player;
            var primary = playerHit ? theme.cyan : theme.enemyRed;
            var secondary = playerHit ? theme.gold : theme.ember;
            pool.Get().Play(hitEvent, primary, secondary);

            if (trail != null)
            {
                trail.startColor = primary;
                trail.endColor = new Color(secondary.r, secondary.g, secondary.b, 0f);
                trail.time = hitEvent.Strength switch
                {
                    HitStrength.Power => 0.48f,
                    HitStrength.Normal => 0.3f,
                    _ => 0.16f
                };
                trailSeconds = hitEvent.Strength switch
                {
                    HitStrength.Power => 0.55f,
                    HitStrength.Normal => 0.34f,
                    _ => 0.2f
                };
            }
            if (hitEvent.Strength == HitStrength.Power)
            {
                puck.TriggerHighlight(0.28f);
            }
        }

        private PooledEffect CreateEffect(Transform parent)
        {
            var root = new GameObject("Pooled Hit Effect");
            root.transform.SetParent(parent, false);

            var flashObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flashObject.name = "Contact Flash";
            flashObject.transform.SetParent(root.transform, false);
            Destroy(flashObject.GetComponent<Collider>());
            var flashRenderer = flashObject.GetComponent<Renderer>();
            flashRenderer.sharedMaterial = ProceduralTheme.CreateGlowMaterial(Color.white, true);

            var ringObject = new GameObject("Rune Shock Ring", typeof(MeshFilter), typeof(MeshRenderer));
            ringObject.name = "Rune Shock Ring";
            ringObject.transform.SetParent(root.transform, false);
            ringObject.transform.localPosition = Vector3.zero;
            ringObject.GetComponent<MeshFilter>().sharedMesh = CreateRingMesh();
            var ringRenderer = ringObject.GetComponent<Renderer>();
            ringRenderer.sharedMaterial = ProceduralTheme.CreateGlowMaterial(Color.white, true);

            var particles = new GameObject("Directional Sparks");
            particles.transform.SetParent(root.transform, false);
            var system = particles.AddComponent<ParticleSystem>();
            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 48;
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
            main.gravityModifier = 0.18f;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.035f;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ProceduralTheme.CreateParticleMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            var pooled = root.AddComponent<PooledEffect>();
            pooled.Initialize(pool, system, flashRenderer, ringRenderer);
            return pooled;
        }

        private static Mesh CreateRingMesh()
        {
            const int segments = 32;
            const float outerRadius = 0.5f;
            const float innerRadius = 0.36f;
            var vertices = new Vector3[segments * 2];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[segments * 6];

            for (var index = 0; index < segments; index++)
            {
                var angle = index * Mathf.PI * 2f / segments;
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[index * 2] = radial * innerRadius;
                vertices[index * 2 + 1] = radial * outerRadius;
                normals[index * 2] = Vector3.up;
                normals[index * 2 + 1] = Vector3.up;

                var next = (index + 1) % segments;
                var triangle = index * 6;
                triangles[triangle] = index * 2;
                triangles[triangle + 1] = next * 2 + 1;
                triangles[triangle + 2] = index * 2 + 1;
                triangles[triangle + 3] = index * 2;
                triangles[triangle + 4] = next * 2;
                triangles[triangle + 5] = next * 2 + 1;
            }

            var mesh = new Mesh
            {
                name = "Runtime Rune Shock Ring",
                vertices = vertices,
                normals = normals,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
