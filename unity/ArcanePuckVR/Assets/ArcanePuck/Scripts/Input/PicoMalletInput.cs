using UnityEngine;
using UnityEngine.XR;

namespace ArcanePuck
{
    /// <summary>
    /// Maps the dominant PICO controller onto the player's half of the table.
    /// This is deliberately a kinematic proxy: controller tracking never becomes
    /// a physics collider, so a tracking jump cannot launch the puck unrealistically.
    /// </summary>
    public sealed class PicoMalletInput : MonoBehaviour, IMalletInput
    {
        private TableConfig config;
        private Transform tableSpace;
        private Transform trackingOrigin;
        private InputDevice controller;
        private Vector3 target;
        private float chargeSeconds;
        private bool charging;
        private bool released;
        private bool inputEnabled;
        private bool startRequested;
        private bool pauseRequested;
        private bool primaryWasPressed;
        private bool secondaryWasPressed;

        public Vector3 TargetPosition => target;
        public float Charge01 => config == null ? 0f : Mathf.Clamp01(chargeSeconds / config.chargeSeconds);
        public bool IsCharging => charging;

        public void Initialize(TableConfig tableConfig, Camera camera, Transform playSpace = null)
        {
            config = tableConfig;
            tableSpace = playSpace;
            trackingOrigin = camera != null ? camera.transform.root : null;
            ResetInput();
            ResolveController();
        }

        private void Update()
        {
            if (config == null) return;
            if (!controller.isValid) ResolveController();

            // Start and pause are available outside active play. Movement and charge
            // remain gated below so the mallet cannot move during menus or pauses.
            var primaryPressed = false;
            var secondaryPressed = false;
            if (controller.isValid)
            {
                controller.TryGetFeatureValue(CommonUsages.primaryButton, out primaryPressed);
                controller.TryGetFeatureValue(CommonUsages.secondaryButton, out secondaryPressed);
            }
            if (primaryPressed && !primaryWasPressed) startRequested = true;
            if (secondaryPressed && !secondaryWasPressed) pauseRequested = true;
            primaryWasPressed = primaryPressed;
            secondaryWasPressed = secondaryPressed;

            if (!inputEnabled) return;

            if (controller.isValid &&
                controller.TryGetFeatureValue(CommonUsages.devicePosition, out var controllerPosition))
            {
                // InputDevices reports PICO controller poses in tracking-origin space.
                // Transform through the XR origin before converting to table-local metres.
                var worldPosition = trackingOrigin != null
                    ? trackingOrigin.TransformPoint(controllerPosition)
                    : controllerPosition;
                if (tableSpace != null)
                {
                    // PICO poses are in the XR origin's world space; project onto the table plane.
                    worldPosition.y = tableSpace.position.y;
                }

                var local = config.ToTablePosition(tableSpace, worldPosition);
                target = config.ClampMalletTarget(new Vector3(local.x, 0f, local.z), true);
            }

            var triggerPressed = false;
            if (controller.isValid)
            {
                controller.TryGetFeatureValue(CommonUsages.triggerButton, out triggerPressed);
            }

            if (triggerPressed && !charging)
            {
                charging = true;
                released = false;
            }
            else if (!triggerPressed && charging)
            {
                charging = false;
                released = true;
            }

            if (charging)
            {
                chargeSeconds = Mathf.Min(config.chargeSeconds, chargeSeconds + Time.unscaledDeltaTime);
            }

        }

        public bool ConsumeStartRequest()
        {
            if (!startRequested) return false;
            startRequested = false;
            return true;
        }

        public bool ConsumePauseRequest()
        {
            if (!pauseRequested) return false;
            pauseRequested = false;
            return true;
        }

        public bool ConsumeChargeRelease(out float charge01)
        {
            charge01 = 0f;
            if (!released || chargeSeconds <= 0f) return false;
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
            startRequested = false;
            pauseRequested = false;
            primaryWasPressed = false;
            secondaryWasPressed = false;
        }

        private void ResolveController()
        {
            var devices = new System.Collections.Generic.List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right,
                devices);
            if (devices.Count == 0)
            {
                InputDevices.GetDevicesWithCharacteristics(
                    InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left,
                    devices);
            }
            if (devices.Count > 0) controller = devices[0];
        }
    }
}
