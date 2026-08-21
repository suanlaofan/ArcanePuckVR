using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

namespace ArcanePuck
{
    /// <summary>
    /// Maps a tracked PICO controller, or the emulator mouse when no controller is
    /// tracked, onto the player's half of the table. The controller is deliberately
    /// a kinematic proxy: controller tracking never becomes a physics collider, so a
    /// tracking jump cannot launch the puck unrealistically.
    /// </summary>
    public sealed class PicoMalletInput : MonoBehaviour, IMalletInput
    {
        private TableConfig config;
        private Camera observationCamera;
        private Transform tableSpace;
        private Transform trackingOrigin;
        private Plane tablePlane;
        private XRInputDevice controller;
        private readonly List<XRInputDevice> controllerCandidates = new();
        private Vector3 target;
        private float chargeSeconds;
        private bool charging;
        private bool released;
        private bool inputEnabled;
        private bool startRequested;
        private bool pauseRequested;
        private bool primaryWasPressed;
        private bool secondaryWasPressed;
        private bool triggerWasPressed;
        private bool hasEmulatorPoseAnchor;
        private Vector3 emulatorPoseAnchor;
        private Vector3 emulatorTargetAnchor;
        private bool hasMousePositionSample;
        private int mouseSampleFrames;
        private Vector2 lastMousePosition;
        private bool emulatorMouseObserved;
        private float mouseActivityUntil;
        private InputSource activeSource;
        private InputDeviceCharacteristics activeControllerHand;

        // PICO Emulator reports its controller pose from a 2D desktop interaction.
        // Keep the fallback responsive without allowing one pixel of desktop motion
        // to teleport the mallet across the table.
        // Keep the existing horizontal controller sensitivity: an emulator metre
        // remains one table metre. Only the missing vertical axis is remapped.
        private const float EmulatorMalletPositionScale = 1f;
        private const float EmulatorMouseActivityHoldSeconds = 0.25f;

        private enum InputSource
        {
            None,
            Mouse,
            TrackedController
        }

        public Vector3 TargetPosition => target;
        public float Charge01 => config == null ? 0f : Mathf.Clamp01(chargeSeconds / config.chargeSeconds);
        public bool IsCharging => charging;
        public bool IsUsingTrackedController => activeSource == InputSource.TrackedController;
        public bool IsUsingDirectPicoInput => activeSource == InputSource.TrackedController ||
                                              (PicoXRRuntime.IsEmulator &&
                                               activeSource == InputSource.Mouse &&
                                               emulatorMouseObserved);

        public void Initialize(TableConfig tableConfig, Camera camera, Transform playSpace = null)
        {
            config = tableConfig;
            observationCamera = camera;
            tableSpace = playSpace;
            trackingOrigin = camera != null ? camera.transform.root : null;
            RebuildTablePlane();
            ResetInput();
        }

        private void Update()
        {
            if (config == null) return;

            var hasTrackedController = TryGetTrackedController(out var controllerPosition);
            var mouse = Mouse.current;
            var hasEmulatorMouse = ObserveEmulatorMouse(mouse);
            // The PICO Emulator exposes a normal Input System mouse as well as a
            // virtual tracked controller. Prefer the actual pointer only after a
            // real position/delta event has arrived; some emulator builds expose
            // a static Mouse device while routing host motion to XR controller
            // pose. The pose fallback below keeps those builds usable too.
            var source = hasEmulatorMouse
                ? InputSource.Mouse
                : hasTrackedController
                    ? InputSource.TrackedController
                    : mouse != null ? InputSource.Mouse : InputSource.None;
            var sourceChanged = SetActiveSource(
                source,
                source == InputSource.TrackedController && hasTrackedController
                    ? controller.characteristics & (InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Right)
                    : InputDeviceCharacteristics.None);

            // Keep the controller's menu shortcuts available in the emulator even
            // when its raw mouse is driving the mallet position.
            if (hasTrackedController)
            {
                UpdateControllerShortcuts(sourceChanged);
            }

            if (!inputEnabled) return;

            if (source == InputSource.TrackedController && hasTrackedController)
            {
                UpdateControllerTarget(controllerPosition, sourceChanged);
            }
            else if (source == InputSource.Mouse)
            {
                UpdateMouseTarget();
            }

            // Mouse left-click and controller trigger both charge/release, but
            // only for the active position source. This prevents a virtual
            // controller trigger from accidentally charging while a real pointer
            // is steering the mallet.
            var mousePressed = source == InputSource.Mouse && mouse != null && mouse.leftButton.isPressed;
            var mousePressedThisFrame = source == InputSource.Mouse && mouse != null && mouse.leftButton.wasPressedThisFrame;
            var controllerTriggerPressed = false;
            if (hasTrackedController)
            {
                controller.TryGetFeatureValue(XRCommonUsages.triggerButton, out controllerTriggerPressed);
            }
            UpdateCharge(source == InputSource.Mouse ? mousePressed : controllerTriggerPressed,
                mousePressedThisFrame, sourceChanged);

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
                CancelCharge();
            }
        }

        public void ResetInput()
        {
            target = config == null ? new Vector3(0f, 0f, -1f) : new Vector3(0f, 0f, -config.length * 0.28f);
            CancelCharge();
            startRequested = false;
            pauseRequested = false;
            activeSource = InputSource.None;
            activeControllerHand = InputDeviceCharacteristics.None;
            controller = default;
            primaryWasPressed = false;
            secondaryWasPressed = false;
            triggerWasPressed = false;
            hasEmulatorPoseAnchor = false;
            hasMousePositionSample = false;
            mouseSampleFrames = 0;
            lastMousePosition = default;
            emulatorMouseObserved = false;
            mouseActivityUntil = 0f;
        }

        private bool TryGetTrackedController(out Vector3 controllerPosition)
        {
            // Deliberately resolve both hands every frame. An InputDevice can remain
            // valid while losing tracking, so checking only isValid would prevent the
            // mouse fallback in the emulator and during controller hand-off.
            if (TryGetTrackedController(
                    InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right,
                    out controllerPosition))
            {
                return true;
            }

            if (TryGetTrackedController(
                    InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left,
                    out controllerPosition))
            {
                return true;
            }

            controller = default;
            controllerPosition = default;
            return false;
        }

        private bool TryGetTrackedController(InputDeviceCharacteristics characteristics, out Vector3 controllerPosition)
        {
            controllerPosition = default;
            controllerCandidates.Clear();
            InputDevices.GetDevicesWithCharacteristics(characteristics, controllerCandidates);
            foreach (var candidate in controllerCandidates)
            {
                if (!candidate.isValid ||
                    !candidate.TryGetFeatureValue(XRCommonUsages.isTracked, out var isTracked) ||
                    !isTracked ||
                    !candidate.TryGetFeatureValue(XRCommonUsages.devicePosition, out controllerPosition))
                {
                    continue;
                }

                controller = candidate;
                return true;
            }

            return false;
        }

        private bool SetActiveSource(InputSource source, InputDeviceCharacteristics controllerHand)
        {
            if (activeSource == source && activeControllerHand == controllerHand) return false;

            activeSource = source;
            activeControllerHand = controllerHand;
            CancelCharge();
            primaryWasPressed = false;
            secondaryWasPressed = false;
            triggerWasPressed = false;
            hasEmulatorPoseAnchor = false;
            return true;
        }

        private void UpdateControllerShortcuts(bool sourceChanged)
        {
            controller.TryGetFeatureValue(XRCommonUsages.primaryButton, out var primaryPressed);
            controller.TryGetFeatureValue(XRCommonUsages.secondaryButton, out var secondaryPressed);
            if (!sourceChanged)
            {
                if (primaryPressed && !primaryWasPressed) startRequested = true;
                if (secondaryPressed && !secondaryWasPressed) pauseRequested = true;
            }

            primaryWasPressed = primaryPressed;
            secondaryWasPressed = secondaryPressed;
        }

        private void UpdateControllerTarget(Vector3 controllerPosition, bool sourceChanged)
        {
            if (PicoXRRuntime.IsEmulator)
            {
                // Android's emulator maps desktop horizontal/vertical controller
                // motion into X/Y. Turn that relative 2D motion into the table's
                // X/Z plane when no raw mouse pointer is available.
                if (sourceChanged || !hasEmulatorPoseAnchor)
                {
                    emulatorPoseAnchor = controllerPosition;
                    emulatorTargetAnchor = target;
                    hasEmulatorPoseAnchor = true;
                }

                var delta = controllerPosition - emulatorPoseAnchor;
                target = config.ClampMalletTarget(
                    new Vector3(
                        emulatorTargetAnchor.x + delta.x * EmulatorMalletPositionScale,
                        0f,
                        emulatorTargetAnchor.z + delta.y * EmulatorMalletPositionScale),
                    true);
                return;
            }

            hasEmulatorPoseAnchor = false;
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

        private void UpdateCharge(bool isPressed, bool pressedThisFrame, bool sourceChanged)
        {
            // A pointer move may select the direct-mouse route on the exact frame
            // its left button is pressed. Preserve that genuine click while still
            // suppressing a held controller trigger during an input-source swap.
            if ((isPressed && !triggerWasPressed && !sourceChanged) ||
                (sourceChanged && pressedThisFrame))
            {
                charging = true;
                released = false;
            }
            else if (!isPressed && triggerWasPressed && charging)
            {
                charging = false;
                released = true;
            }

            triggerWasPressed = isPressed;
        }

        private void UpdateMouseTarget()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // Read the pointer on every frame. This deliberately does not gate on
            // a right/side button: the screen coordinate itself is the mallet
            // control in the emulator.
            RebuildTablePlane();
            if (observationCamera != null)
            {
                var ray = observationCamera.ScreenPointToRay(mouse.position.ReadValue());
                if (tablePlane.Raycast(ray, out var distance))
                {
                    var tableLocalTarget = config.ToTablePosition(tableSpace, ray.GetPoint(distance));
                    target = config.ClampMalletTarget(tableLocalTarget, true);
                }
            }
        }

        private bool ObserveEmulatorMouse(Mouse mouse)
        {
            if (!PicoXRRuntime.IsEmulator || mouse == null)
            {
                return false;
            }

            var position = mouse.position.ReadValue();
            var delta = mouse.delta.ReadValue();
            var movedByDelta = delta.sqrMagnitude > 0.25f;
            var movedByPosition = false;
            if (!hasMousePositionSample)
            {
                hasMousePositionSample = true;
                lastMousePosition = position;
                mouseSampleFrames = 1;
            }
            else
            {
                mouseSampleFrames++;
                movedByPosition = mouseSampleFrames > 2 &&
                                   (position - lastMousePosition).sqrMagnitude > 0.25f;
                lastMousePosition = position;
            }

            // Buttons alone do not prove that the host pointer is reaching
            // Unity. Only a real coordinate/delta change may select the raw
            // screen-coordinate route; otherwise retain the virtual-controller
            // XY-to-XZ fallback.
            if (movedByPosition || movedByDelta)
            {
                mouseActivityUntil = Time.unscaledTime + EmulatorMouseActivityHoldSeconds;
                if (!emulatorMouseObserved)
                {
                    Debug.Log($"Arcane Puck emulator mouse active at {position}; using screen coordinates for mallet.");
                }
                emulatorMouseObserved = true;
            }
            else if (emulatorMouseObserved && mouse.leftButton.isPressed)
            {
                // Keep a verified direct-pointer source active while the player
                // holds a charge, even when the physical cursor is stationary.
                mouseActivityUntil = Time.unscaledTime + EmulatorMouseActivityHoldSeconds;
            }
            else if (emulatorMouseObserved && Time.unscaledTime > mouseActivityUntil)
            {
                emulatorMouseObserved = false;
            }

            return emulatorMouseObserved;
        }

        private void RebuildTablePlane()
        {
            var normal = tableSpace != null ? tableSpace.up : Vector3.up;
            var point = tableSpace != null ? tableSpace.position : Vector3.zero;
            tablePlane = new Plane(normal, point);
        }

        private void CancelCharge()
        {
            chargeSeconds = 0f;
            charging = false;
            released = false;
        }
    }
}
