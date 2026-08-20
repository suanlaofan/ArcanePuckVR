using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcanePuck
{
    public sealed class DesktopMalletInput : MonoBehaviour, IMalletInput
    {
        private TableConfig config;
        private Camera observationCamera;
        private Transform tableSpace;
        private Plane tablePlane;
        private Vector3 target;
        private float chargeSeconds;
        private bool charging;
        private bool released;
        private bool inputEnabled;

        public Vector3 TargetPosition => target;
        public float Charge01 => config == null ? 0f : Mathf.Clamp01(chargeSeconds / config.chargeSeconds);
        public bool IsCharging => charging;

        public void Initialize(TableConfig tableConfig, Camera camera, Transform playSpace = null)
        {
            config = tableConfig;
            observationCamera = camera;
            tableSpace = playSpace;
            RebuildTablePlane();
            ResetInput();
        }

        private void Update()
        {
            if (!inputEnabled || config == null || observationCamera == null)
            {
                return;
            }

            RebuildTablePlane();

            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (!mouse.rightButton.isPressed)
                {
                    var ray = observationCamera.ScreenPointToRay(mouse.position.ReadValue());
                    if (tablePlane.Raycast(ray, out var distance))
                    {
                        var tableLocalTarget = config.ToTablePosition(tableSpace, ray.GetPoint(distance));
                        target = config.ClampMalletTarget(tableLocalTarget, true);
                    }
                }

                if (mouse.leftButton.wasPressedThisFrame)
                {
                    charging = true;
                    released = false;
                }

                if (mouse.leftButton.wasReleasedThisFrame && charging)
                {
                    charging = false;
                    released = true;
                }
            }

            if (charging)
            {
                chargeSeconds = Mathf.Min(config.chargeSeconds, chargeSeconds + Time.unscaledDeltaTime);
            }
        }

        public bool ConsumeChargeRelease(out float charge01)
        {
            charge01 = 0f;
            if (!released || chargeSeconds <= 0f)
            {
                return false;
            }

            charge01 = Charge01;
            released = false;
            chargeSeconds = 0f;
            return true;
        }

        public void SetEnabled(bool enabled)
        {
            inputEnabled = enabled;
            if (!enabled)
            {
                charging = false;
                released = false;
            }
        }

        public void ResetInput()
        {
            target = config == null ? new Vector3(0f, 0f, -1f) : new Vector3(0f, 0f, -config.length * 0.28f);
            chargeSeconds = 0f;
            charging = false;
            released = false;
        }

        private void RebuildTablePlane()
        {
            var normal = tableSpace != null ? tableSpace.up : Vector3.up;
            var point = tableSpace != null ? tableSpace.position : Vector3.zero;
            tablePlane = new Plane(normal, point);
        }
    }
}
