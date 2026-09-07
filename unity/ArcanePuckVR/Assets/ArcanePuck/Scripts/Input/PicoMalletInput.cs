using System;
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
        private VRControlConfig controls;
        private readonly VRMalletMapping mapping = new();
        private bool hasUsableTracking;
        private bool trackingPauseRequested;
        private bool hasPoseSample;
        private Vector3 lastPoseSample;
        private float lastPoseTime;
        private float stableSince = -1f;
        private float missingSince = -1f;
        private bool suppressChargeUntilRelease;
        private Vector2 menuNavigation;
        private float nextMenuNavigationTime;
        public event Action ContinuityReset;
        public bool HasUsableTracking => hasUsableTracking;
        private const float TrackingStableSeconds = 0.12f;
        private const float TrackingPauseSeconds = 0.5f;
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
#if DEVELOPMENT_BUILD
        private float nextInputDiagnostic;
#endif
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
                                              (PicoXRRuntime.IsEditorMousePreview && activeSource == InputSource.Mouse) ||
                                              (PicoXRRuntime.IsEmulator &&
                                               activeSource == InputSource.Mouse &&
                                               emulatorMouseObserved);

        public void Initialize(TableConfig tableConfig, Camera camera, Transform playSpace = null)
        {
            config = tableConfig;
            observationCamera = camera;
            tableSpace = playSpace;
            trackingOrigin = PicoXRRuntime.TrackingSpace != null
                ? PicoXRRuntime.TrackingSpace : camera != null ? camera.transform.parent : null;
            controls = PicoXRRuntime.Controls;
            if (controls != null) controls.Changed += OnControlsChanged;
            if (PicoXRRuntime.Calibration != null) PicoXRRuntime.Calibration.Recentered += OnRecentered;
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
            if (hasTrackedController || PicoXRRuntime.IsEditorMousePreview)
            {
                UpdateControllerShortcuts(sourceChanged);
            }

            UpdateTrackingAvailability(source, hasTrackedController, controllerPosition, sourceChanged);
#if DEVELOPMENT_BUILD
            if (PicoXRRuntime.IsEmulator && Time.unscaledTime >= nextInputDiagnostic)
            {
                nextInputDiagnostic = Time.unscaledTime + 3f;
                var pointer = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
                controller.TryGetFeatureValue(XRCommonUsages.deviceRotation, out var aim);
                controller.TryGetFeatureValue(XRCommonUsages.triggerButton, out var triggerDown);
                Debug.Log($"Arcane Puck emulator input: source={source}, tracked={hasTrackedController}, pose={controllerPosition:F3}, aim={aim.eulerAngles:F1}, trigger={triggerDown}, mouse={pointer:F1}, usable={hasUsableTracking}, target={target:F3}.");
            }
#endif
            if (!inputEnabled || !hasUsableTracking) return;

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
                Recalibrate();
            }
        }

        public void ResetInput()
        {
            target = config == null ? new Vector3(0f, 0f, -1f) : VRMalletMapping.Center(config);
            Recalibrate();
            startRequested = false;
            pauseRequested = false;
            menuNavigation = Vector2.zero;
            trackingPauseRequested = false;
            missingSince = -1f;
        }

        public void Recalibrate(bool center = false)
        {
            if (center && config != null) target = VRMalletMapping.Center(config);
            mapping.Clear();
            hasEmulatorPoseAnchor = false;
            if (hasUsableTracking && hasPoseSample && activeSource == InputSource.TrackedController)
            {
                if (PicoXRRuntime.IsEmulator)
                {
                    emulatorPoseAnchor = lastPoseSample;
                    emulatorTargetAnchor = target;
                    hasEmulatorPoseAnchor = true;
                }
                else
                {
                    var world = trackingOrigin != null ? trackingOrigin.TransformPoint(lastPoseSample) : lastPoseSample;
                    mapping.Anchor(config.ToTablePosition(tableSpace, world), target);
                }
            }
            CancelCharge();
            suppressChargeUntilRelease = true;
            ContinuityReset?.Invoke();
        }

        public bool ConsumeTrackingPauseRequest()
        {
            var requested = trackingPauseRequested;
            trackingPauseRequested = false;
            return requested;
        }

        public Vector2 ConsumeMenuNavigation()
        {
            var navigation = menuNavigation;
            menuNavigation = Vector2.zero;
            return navigation;
        }

        private void OnControlsChanged() => Recalibrate();
        private void OnRecentered()
        {
            hasPoseSample = false;
            stableSince = -1f;
            Recalibrate();
        }

        private void OnDestroy()
        {
            if (controls != null) controls.Changed -= OnControlsChanged;
            if (PicoXRRuntime.Calibration != null) PicoXRRuntime.Calibration.Recentered -= OnRecentered;
        }

        private void UpdateTrackingAvailability(InputSource source, bool tracked, Vector3 pose, bool sourceChanged)
        {
            var now = Time.unscaledTime;
            var usable = source == InputSource.Mouse && Mouse.current != null &&
                         (!PicoXRRuntime.IsRunning || PicoXRRuntime.IsEmulator || PicoXRRuntime.IsEditorMousePreview);
            if (source == InputSource.TrackedController && tracked)
            {
                var elapsed = Mathf.Max(0.001f, now - lastPoseTime);
                // A large discontinuity is not a legitimate swing. Ordinary fast
                // movement retains the direct path; only a pose teleport re-anchors.
                var jumped = hasPoseSample && !sourceChanged &&
                    (pose - lastPoseSample).magnitude > 0.45f + 6f * elapsed;
                if (jumped)
                {
                    stableSince = -1f;
                    Recalibrate();
                }
                lastPoseSample = pose;
                lastPoseTime = now;
                hasPoseSample = true;
                if (stableSince < 0f) stableSince = now;
                usable = now - stableSince >= TrackingStableSeconds;
            }
            else
            {
                hasPoseSample = false;
                stableSince = -1f;
            }
            var calibration = PicoXRRuntime.Calibration;
            usable &= calibration == null || (calibration.IsReady && !calibration.IsCalibrating && calibration.HasTrackedHead);
            if (!usable)
            {
                if (hasUsableTracking) Recalibrate();
                if (missingSince < 0f) missingSince = now;
                if (inputEnabled && now - missingSince >= TrackingPauseSeconds)
                    trackingPauseRequested = true;
            }
            else
            {
                if (!hasUsableTracking) Recalibrate();
                missingSince = -1f;
            }
            hasUsableTracking = usable;
        }

        public bool TryGetEmulatorMenuPointer(out Ray ray, out bool pressed)
        {
            ray = default;
            pressed = false;
            if (!PicoXRRuntime.IsEmulator || emulatorMouseObserved ||
                !TryGetTrackedController(out var position) || !TryCreateControllerRay(position, out ray)) return false;
            controller.TryGetFeatureValue(XRCommonUsages.triggerButton, out pressed);
            if (!pressed && controller.TryGetFeatureValue(XRCommonUsages.trigger, out var trigger))
                pressed = trigger > 0.5f;
            return true;
        }

        private bool TryCreateControllerRay(Vector3 position, out Ray ray)
        {
            ray = default;
            if (!controller.TryGetFeatureValue(XRCommonUsages.deviceRotation, out var rotation)) return false;
            var origin = trackingOrigin != null ? trackingOrigin.TransformPoint(position) : position;
            var direction = trackingOrigin != null
                ? trackingOrigin.TransformDirection(rotation * Vector3.forward) : rotation * Vector3.forward;
            ray = new Ray(origin, direction);
            return true;
        }

        private bool TryGetTrackedController(out Vector3 controllerPosition)
        {
#if UNITY_EDITOR
            if (PicoXRRuntime.IsEditorPreview)
            {
                controllerPosition = PicoXRRuntime.EditorControllerPosition;
                return PicoXRRuntime.EditorSimulateController && PicoXRRuntime.EditorControllerTracked;
            }
#endif
            // Deliberately resolve both hands every frame. An InputDevice can remain
            // valid while losing tracking, so checking only isValid would prevent the
            // mouse fallback in the emulator and during controller hand-off.
            var preferred = controls != null && controls.LeftHanded
                ? InputDeviceCharacteristics.Left : InputDeviceCharacteristics.Right;
            var fallback = preferred == InputDeviceCharacteristics.Left
                ? InputDeviceCharacteristics.Right : InputDeviceCharacteristics.Left;
            if (TryGetTrackedController(
                    InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Controller | preferred,
                    out controllerPosition))
            {
                return true;
            }

            if (TryGetTrackedController(
                    InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Controller | fallback,
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
            mapping.Clear();
            hasPoseSample = false;
            stableSince = -1f;
            suppressChargeUntilRelease = true;
            ContinuityReset?.Invoke();
            return true;
        }

        private void UpdateControllerShortcuts(bool sourceChanged)
        {
#if UNITY_EDITOR
            if (PicoXRRuntime.IsEditorPreview)
            {
                var keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    if (keyboard.enterKey.wasPressedThisFrame) startRequested = true;
                    if (keyboard.upArrowKey.wasPressedThisFrame) menuNavigation = Vector2.up;
                    if (keyboard.downArrowKey.wasPressedThisFrame) menuNavigation = Vector2.down;
                    if (keyboard.leftArrowKey.wasPressedThisFrame) menuNavigation = Vector2.left;
                    if (keyboard.rightArrowKey.wasPressedThisFrame) menuNavigation = Vector2.right;
                }
                return;
            }
#endif
            controller.TryGetFeatureValue(XRCommonUsages.primaryButton, out var primaryPressed);
            controller.TryGetFeatureValue(XRCommonUsages.secondaryButton, out var secondaryPressed);
            if (!sourceChanged)
            {
                if (primaryPressed && !primaryWasPressed) startRequested = true;
                if (secondaryPressed && !secondaryWasPressed) pauseRequested = true;
            }

            primaryWasPressed = primaryPressed;
            secondaryWasPressed = secondaryPressed;
            controller.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out var axis);
            if (axis.magnitude < 0.35f)
            {
                nextMenuNavigationTime = 0f;
            }
            else if (Time.unscaledTime >= nextMenuNavigationTime && axis.magnitude >= 0.55f)
            {
                menuNavigation = Mathf.Abs(axis.y) >= Mathf.Abs(axis.x)
                    ? new Vector2(0f, Mathf.Sign(axis.y)) : new Vector2(Mathf.Sign(axis.x), 0f);
                nextMenuNavigationTime = Time.unscaledTime + 0.25f;
            }
        }

        private void UpdateControllerTarget(Vector3 controllerPosition, bool sourceChanged)
        {
            if (PicoXRRuntime.IsEmulator)
            {
                // Current PICO images expose the host pointer as controller aim,
                // with position fixed near the eye. Project that aim onto the table.
                if (TryCreateControllerRay(controllerPosition, out var pointerRay) &&
                    VRMalletMapping.TryProjectPointer(pointerRay, tableSpace, config, out var pointerTarget))
                {
                    target = pointerTarget;
                    hasEmulatorPoseAnchor = false;
                    return;
                }
                // Older images expose translation rather than aim. Retain their
                // XY path if no forward table intersection is available.
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
            // Head and hands use the SAME tracking-space transform, including the
            // Device-mode offset. Projection occurs in table coordinates, not world Y.
            var worldPosition = trackingOrigin != null
                ? trackingOrigin.TransformPoint(controllerPosition) : controllerPosition;
            var local = config.ToTablePosition(tableSpace, worldPosition);
            target = mapping.Map(local, target, config,
                controls != null ? controls.GainX : 2.5f,
                controls != null ? controls.GainZ : 2.5f);
        }

        private void UpdateCharge(bool isPressed, bool pressedThisFrame, bool sourceChanged)
        {
            if (suppressChargeUntilRelease)
            {
                triggerWasPressed = isPressed;
                if (!isPressed) suppressChargeUntilRelease = false;
                return;
            }
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
