using System;
using UnityEngine;

namespace ArcanePuck
{
    [RequireComponent(typeof(Rigidbody), typeof(MeshCollider))]
    public sealed class PuckController : MonoBehaviour
    {
        private TableConfig config;
        private Rigidbody body;
        private float stationarySeconds;
        private float highlightSeconds;
        private float nextRailRecoveryTime;
        private Renderer[] visualRenderers;
        private Color[] baseColors;

        public event Action ResetRequired;
        public event Action<float> RailContact;
        public Rigidbody Body => body;
        public Vector3 PlanarVelocity => body == null || config == null
            ? Vector3.zero
            : config.ProjectWorldDirectionToTable(transform.parent, body.linearVelocity);
        public Vector3 TableVelocity => body == null || config == null
            ? Vector3.zero
            : config.ToTableDirection(transform.parent, PlanarVelocity);
        public float Speed => PlanarVelocity.magnitude;

        public void Initialize(TableConfig tableConfig)
        {
            config = tableConfig;
            body = GetComponent<Rigidbody>();
            body.mass = config.puckMass;
            body.linearDamping = config.puckLinearDamping;
            body.angularDamping = config.puckAngularDamping;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.maxLinearVelocity = config.puckMaxSpeed;

            var material = PhysicsMaterials.Create(
                "Puck Physics", config.puckFriction, config.puckRestitution, PhysicsMaterialCombine.Average);
            ConfigureDiscColliders(gameObject, config.puckRadius, config.puckHeight, Vector3.zero, material);

            visualRenderers = GetComponentsInChildren<Renderer>();
            baseColors = new Color[visualRenderers.Length];
            for (var i = 0; i < visualRenderers.Length; i++)
            {
                baseColors[i] = visualRenderers[i].material.color;
            }
        }

        private void FixedUpdate()
        {
            if (config == null || body == null || body.isKinematic)
            {
                return;
            }

            var tableLocalPosition = config.ToTablePosition(transform.parent, body.position);
            if (Mathf.Abs(tableLocalPosition.y - config.puckHeight * 0.5f) > 0.0001f)
            {
                tableLocalPosition.y = config.puckHeight * 0.5f;
                body.position = config.ToWorldPosition(transform.parent, tableLocalPosition);
            }

            var velocity = PlanarVelocity;
            var speed = velocity.magnitude;
            if (speed > config.puckMaxSpeed)
            {
                velocity *= config.puckMaxSpeed / speed;
            }

            if ((body.linearVelocity - velocity).sqrMagnitude > 0.000001f)
            {
                body.linearVelocity = velocity;
            }

            stationarySeconds = Speed < config.puckResetSpeedThreshold
                ? stationarySeconds + Time.fixedDeltaTime
                : 0f;

            if (stationarySeconds >= config.stationaryResetSeconds || IsOutOfBounds())
            {
                stationarySeconds = 0f;
                ResetRequired?.Invoke();
            }
        }

        private void Update()
        {
            if (highlightSeconds <= 0f || visualRenderers == null)
            {
                return;
            }

            highlightSeconds -= Time.deltaTime;
            var pulse = 1f + Mathf.Sin(Time.time * 45f) * 0.18f;
            for (var i = 0; i < visualRenderers.Length; i++)
            {
                var material = visualRenderers[i].material;
                material.color = Color.Lerp(baseColors[i], Color.white * pulse, Mathf.Clamp01(highlightSeconds * 4f));
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (body != null && !body.isKinematic && collision.gameObject.GetComponent<RailSurface>() != null)
            {
                RailContact?.Invoke(collision.relativeVelocity.magnitude);
            }

            RecoverFromRail(collision, false);
        }

        private void OnCollisionStay(Collision collision)
        {
            RecoverFromRail(collision, true);
        }

        private void RecoverFromRail(Collision collision, bool allowStationaryRecovery)
        {
            if (config == null || body == null || body.isKinematic ||
                collision.gameObject.GetComponent<RailSurface>() == null ||
                Time.time < nextRailRecoveryTime)
            {
                return;
            }

            var tableVelocity = TableVelocity;
            var bestNormal = Vector3.zero;
            var mostIncoming = 0f;
            foreach (var contact in collision.contacts)
            {
                var normal = config.ToTableDirection(transform.parent, contact.normal);
                normal.y = 0f;
                if (normal.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                normal.Normalize();
                var incoming = -Vector3.Dot(tableVelocity, normal);
                if (bestNormal == Vector3.zero || incoming > mostIncoming)
                {
                    bestNormal = normal;
                    mostIncoming = incoming;
                }
            }

            if (bestNormal == Vector3.zero)
            {
                return;
            }

            var alreadyLeaving = Vector3.Dot(tableVelocity, bestNormal);
            if (!allowStationaryRecovery && alreadyLeaving >= config.railMinimumReboundSpeed)
            {
                return;
            }

            if (alreadyLeaving < 0f)
            {
                tableVelocity = Vector3.Reflect(tableVelocity, bestNormal);
                alreadyLeaving = Vector3.Dot(tableVelocity, bestNormal);
            }

            var recoverySpeed = allowStationaryRecovery
                ? config.railMinimumReboundSpeed
                : Mathf.Min(config.railMinimumReboundSpeed, Mathf.Max(0f, mostIncoming));
            if (alreadyLeaving < recoverySpeed)
            {
                tableVelocity += bestNormal * (recoverySpeed - alreadyLeaving);
            }

            body.linearVelocity = config.ToWorldDirection(transform.parent, tableVelocity);
            stationarySeconds = 0f;
            nextRailRecoveryTime = Time.time + config.railRecoveryCooldownSeconds;
        }

        public void ResetPuck(bool towardAI, float speed)
        {
            if (body == null)
            {
                return;
            }

            body.position = config.ToWorldPosition(
                transform.parent,
                new Vector3(0f, config.puckHeight * 0.5f, 0f));
            body.rotation = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
            var tableLocalVelocity = new Vector3(
                UnityEngine.Random.Range(-0.18f, 0.18f) * speed,
                0f,
                (towardAI ? 1f : -1f) * speed);
            if (!body.isKinematic)
            {
                body.linearVelocity = config.ToWorldDirection(transform.parent, tableLocalVelocity);
                body.angularVelocity = Vector3.zero;
            }
            stationarySeconds = 0f;
        }

        public void SetSimulating(bool simulating)
        {
            if (body == null)
            {
                return;
            }

            if (!simulating)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                body.isKinematic = true;
                return;
            }

            body.isKinematic = false;
        }

        public void ApplyChargeImpulse(Vector3 direction, float charge01)
        {
            direction = config.ProjectWorldDirectionToTable(transform.parent, direction);
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = config.ToWorldDirection(transform.parent, Vector3.forward);
            }

            body.AddForce(direction.normalized * (charge01 * config.chargeImpulse), ForceMode.Impulse);
        }

        public void TriggerHighlight(float seconds)
        {
            highlightSeconds = Mathf.Max(highlightSeconds, seconds);
        }

        private bool IsOutOfBounds()
        {
            var p = config.ToTablePosition(transform.parent, body.position);
            return !float.IsFinite(p.x) || !float.IsFinite(p.z) ||
                   Mathf.Abs(p.x) > config.HalfWidth + config.outOfBoundsMargin ||
                   Mathf.Abs(p.z) > config.HalfLength + config.outOfBoundsMargin;
        }

        public static void ConfigureDiscColliders(
            GameObject owner,
            float radius,
            float height,
            Vector3 center,
            PhysicsMaterial material)
        {
            var colliders = owner.GetComponents<Collider>();
            var disc = colliders.Length == 1 && colliders[0] is MeshCollider existing
                ? existing
                : owner.AddComponent<MeshCollider>();
            foreach (var collider in colliders)
            {
                if (collider == disc)
                {
                    continue;
                }

                collider.enabled = false;
                Destroy(collider);
            }

            disc.sharedMesh = CreateDiscMesh(radius, height, center);
            disc.convex = true;
            disc.material = material;
        }

        private static Mesh CreateDiscMesh(float radius, float height, Vector3 center)
        {
            const int segments = 16;
            var halfHeight = Mathf.Max(0.001f, height) * 0.5f;
            var vertices = new Vector3[segments * 2 + 2];
            var triangles = new int[segments * 12];
            vertices[segments * 2] = center + Vector3.down * halfHeight;
            vertices[segments * 2 + 1] = center + Vector3.up * halfHeight;

            for (var i = 0; i < segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                var radial = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                vertices[i] = center + radial + Vector3.down * halfHeight;
                vertices[i + segments] = center + radial + Vector3.up * halfHeight;

                var next = (i + 1) % segments;
                var triangle = i * 12;
                triangles[triangle] = i;
                triangles[triangle + 1] = next + segments;
                triangles[triangle + 2] = i + segments;
                triangles[triangle + 3] = i;
                triangles[triangle + 4] = next;
                triangles[triangle + 5] = next + segments;
                triangles[triangle + 6] = segments * 2;
                triangles[triangle + 7] = next;
                triangles[triangle + 8] = i;
                triangles[triangle + 9] = segments * 2 + 1;
                triangles[triangle + 10] = i + segments;
                triangles[triangle + 11] = next + segments;
            }

            var mesh = new Mesh
            {
                name = $"Runtime Disc Collider {radius:0.###}x{height:0.###}",
                vertices = vertices,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    internal static class PhysicsMaterials
    {
        public static PhysicsMaterial Create(
            string name,
            float friction,
            float bounce,
            PhysicsMaterialCombine frictionCombine,
            PhysicsMaterialCombine? bounceCombine = null)
        {
            return new PhysicsMaterial(name)
            {
                dynamicFriction = friction,
                staticFriction = friction,
                bounciness = bounce,
                frictionCombine = frictionCombine,
                bounceCombine = bounceCombine ?? frictionCombine
            };
        }
    }

    internal sealed class RailSurface : MonoBehaviour
    {
    }
}
