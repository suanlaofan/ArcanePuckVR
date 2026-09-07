using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace ArcanePuck
{
    /// <summary>Pure calibration geometry, shared by the runtime and EditMode tests.</summary>
    public static class VRCalibrationGeometry
    {
        public static Pose CalculateOriginPose(Pose trackedHead, Vector3 trackingOffset,
            Vector3 currentOriginPosition, Vector3 nearEdgeWorld, Vector3 tableForward, float startDistance)
        {
            var forward = Vector3.ProjectOnPlane(tableForward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            var headForward = Vector3.ProjectOnPlane(trackedHead.rotation * Vector3.forward, Vector3.up).normalized;
            if (headForward.sqrMagnitude < 0.001f) headForward = Vector3.forward;
            var rotation = Quaternion.AngleAxis(Vector3.SignedAngle(headForward, forward, Vector3.up), Vector3.up);
            var desiredHead = nearEdgeWorld - forward * VRControlConfig.NormalizeStartDistance(startDistance);
            var rotatedHead = rotation * (trackingOffset + trackedHead.position);
            return new Pose(new Vector3(desiredHead.x - rotatedHead.x, currentOriginPosition.y,
                desiredHead.z - rotatedHead.z), rotation);
        }

        public static float TrackingHeightOffset(TrackingOriginModeFlags actualMode, bool seated)
        {
            // Floor poses already contain physical eye height. Device poses do not.
            return (actualMode & TrackingOriginModeFlags.Floor) != 0 ? 0f : seated ? 1.20f : 1.65f;
        }

        public static float SeatedTableHeight(float eyeWorldHeight, float standingTableHeight)
        {
            return Mathf.Clamp(eyeWorldHeight - 0.50f, 0.45f, standingTableHeight);
        }
    }

    /// <summary>
    /// Positions the player's actual tracked head by the near table edge. Head and
    /// controller poses share TrackingSpace; only yaw and horizontal origin position
    /// are authored. Calibration is requested by the match only when idle or paused.
    /// </summary>
    public sealed class PicoPlayerCalibration : MonoBehaviour
    {
        private const float FadeSeconds = 0.12f;
        private readonly List<XRInputSubsystem> inputSubsystems = new();
        private TableConfig tableConfig;
        private VRControlConfig controls;
        private Transform tableRoot;
        private Transform trackingSpace;
        private Camera hmdCamera;
        private XRInputSubsystem inputSubsystem;
        private float standingTableHeight;
        private Image fadeImage;
        private Canvas fadeCanvas;
        private Coroutine calibrationRoutine;
        private bool trackingOriginRecenterPending;
        private int trackingOriginChangedFrame;

        public bool IsReady { get; private set; }
        public bool IsCalibrating { get; private set; }
        public bool HasTrackedHead => hmdCamera != null && TryGetHeadPose(out _);
        public TrackingOriginModeFlags ActualTrackingOriginMode { get; private set; }
        public event Action Recentered;

        public void Initialize(TableConfig table, VRControlConfig controlSettings,
            Transform runtimeTable, Transform sharedTrackingSpace, Camera camera)
        {
            tableConfig = table;
            controls = controlSettings;
            tableRoot = runtimeTable;
            trackingSpace = sharedTrackingSpace;
            hmdCamera = camera;
            standingTableHeight = tableRoot != null ? tableRoot.position.y : table.height;
            BuildFade();
            RequestRecenter();
        }

        public bool RequestRecenter()
        {
            if (IsCalibrating || !isActiveAndEnabled || controls == null || trackingSpace == null || hmdCamera == null)
                return false;
            calibrationRoutine = StartCoroutine(Calibrate());
            return true;
        }

        private void Update()
        {
            if (!trackingOriginRecenterPending || IsCalibrating || Time.frameCount <= trackingOriginChangedFrame) return;
            trackingOriginRecenterPending = false;
            RequestRecenter();
        }

        private void OnTrackingOriginUpdated(XRInputSubsystem subsystem)
        {
            // Our own Floor request can raise this synchronously. In that case
            // the calibration already waits for a fresh pose and actual-mode readback.
            if (IsCalibrating) return;
            IsReady = false;
            trackingOriginRecenterPending = true;
            trackingOriginChangedFrame = Time.frameCount;
        }

        private IEnumerator Calibrate()
        {
            IsCalibrating = true;
            // Let the loader and its initial tracking-origin request settle. Do not
            // calibrate from the camera's untracked prefab pose on the first frame.
            yield return null;
            Pose headPose = default;
            while (true)
            {
                while (!TryResolveTrackingMode() || !TryGetHeadPose(out headPose)) yield return null;
                yield return FadeTo(1f);
                // Read again after fading: the player may have moved, or tracking
                // may have been lost. Never apply an old sample or leave the user
                // behind an opaque screen while waiting for tracking to recover.
                if (TryResolveTrackingMode() && TryGetHeadPose(out headPose)) break;
                yield return FadeTo(0f);
            }

            trackingSpace.localPosition = Vector3.up *
                VRCalibrationGeometry.TrackingHeightOffset(ActualTrackingOriginMode, controls.Seated);
            var nearEdge = tableRoot != null
                ? tableRoot.TransformPoint(new Vector3(0f, 0f, -tableConfig.HalfLength))
                : new Vector3(0f, tableConfig.height, -tableConfig.HalfLength);
            var originPose = VRCalibrationGeometry.CalculateOriginPose(headPose, trackingSpace.localPosition,
                transform.position, nearEdge, tableRoot != null ? tableRoot.forward : Vector3.forward, controls.StartDistance);
            transform.SetPositionAndRotation(originPose.position, originPose.rotation);

            if (tableRoot != null)
            {
                var position = tableRoot.position;
                var eyeHeight = trackingSpace.TransformPoint(headPose.position).y;
                position.y = controls.Seated
                    ? VRCalibrationGeometry.SeatedTableHeight(eyeHeight, standingTableHeight)
                    : standingTableHeight;
                // Rails, goal triggers, mallets, puck and their visual models all
                // live below Table Anchor, so moving it keeps one coherent table.
                tableRoot.position = position;
                Physics.SyncTransforms();
            }

            IsReady = true;
            Debug.Log($"Arcane Puck VR calibrated: tracking={ActualTrackingOriginMode}, " +
                      $"distance={controls.StartDistance:F2} m, seated={controls.Seated}, " +
                      $"trackingOffset={trackingSpace.localPosition.y:F2} m.");
            Recentered?.Invoke();
            yield return FadeTo(0f);
            IsCalibrating = false;
            calibrationRoutine = null;
        }

        private bool TryResolveTrackingMode()
        {
#if UNITY_EDITOR
            if (PicoXRRuntime.IsEditorPreview)
            {
                ActualTrackingOriginMode = TrackingOriginModeFlags.Floor;
                return true;
            }
#endif
            if (inputSubsystem == null || !inputSubsystem.running)
            {
                if (inputSubsystem != null) inputSubsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
                inputSubsystems.Clear();
                SubsystemManager.GetSubsystems(inputSubsystems);
                inputSubsystem = inputSubsystems.Find(subsystem => subsystem.running);
                if (inputSubsystem == null) return false;
                inputSubsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
                var supported = inputSubsystem.GetSupportedTrackingOriginModes();
                var requested = (supported & TrackingOriginModeFlags.Floor) != 0
                    ? TrackingOriginModeFlags.Floor
                    : TrackingOriginModeFlags.Device;
                if (!inputSubsystem.TrySetTrackingOriginMode(requested) && requested == TrackingOriginModeFlags.Floor)
                    inputSubsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Device);
            }

            // Read the actual subsystem mode instead of assuming a Floor request
            // succeeded. Unknown origins cannot safely supply a height offset.
            ActualTrackingOriginMode = inputSubsystem.GetTrackingOriginMode();
            return (ActualTrackingOriginMode & (TrackingOriginModeFlags.Floor | TrackingOriginModeFlags.Device)) != 0;
        }

        private bool TryGetHeadPose(out Pose headPose)
        {
            headPose = default;
#if UNITY_EDITOR
            if (PicoXRRuntime.IsEditorPreview && hmdCamera != null)
            {
                headPose = new Pose(hmdCamera.transform.localPosition, hmdCamera.transform.localRotation);
                return true;
            }
#endif
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid || !head.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) || !tracked ||
                !head.TryGetFeatureValue(CommonUsages.devicePosition, out var position) ||
                !head.TryGetFeatureValue(CommonUsages.deviceRotation, out var rotation)) return false;
            if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z) ||
                !IsFinite(rotation.x) || !IsFinite(rotation.y) || !IsFinite(rotation.z) || !IsFinite(rotation.w) ||
                Quaternion.Dot(rotation, rotation) < 0.5f) return false;
            headPose = new Pose(position, rotation.normalized);
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void BuildFade()
        {
            var fadeObject = new GameObject("VR Recenter Fade", typeof(RectTransform), typeof(Canvas), typeof(Image));
            fadeObject.transform.SetParent(hmdCamera.transform, false);
            fadeObject.transform.localPosition = Vector3.forward * (hmdCamera.nearClipPlane + 0.015f);
            var rect = fadeObject.GetComponent<RectTransform>();
            rect.sizeDelta = Vector2.one * 4f;
            fadeCanvas = fadeObject.GetComponent<Canvas>();
            fadeCanvas.renderMode = RenderMode.WorldSpace;
            fadeCanvas.worldCamera = hmdCamera;
            fadeCanvas.overrideSorting = true;
            fadeCanvas.sortingOrder = 32760;
            fadeImage = fadeObject.GetComponent<Image>();
            fadeImage.raycastTarget = false;
            fadeImage.color = Color.clear;
            fadeCanvas.enabled = false;
        }

        private IEnumerator FadeTo(float alpha)
        {
            if (fadeImage == null) yield break;
            fadeCanvas.enabled = true;
            var startAlpha = fadeImage.color.a;
            for (var elapsed = 0f; elapsed < FadeSeconds; elapsed += Time.unscaledDeltaTime)
            {
                fadeImage.color = new Color(0f, 0f, 0f, Mathf.Lerp(startAlpha, alpha, elapsed / FadeSeconds));
                yield return null;
            }
            fadeImage.color = new Color(0f, 0f, 0f, alpha);
            fadeCanvas.enabled = alpha > 0f;
        }

        private void OnDisable()
        {
            if (calibrationRoutine != null) StopCoroutine(calibrationRoutine);
            calibrationRoutine = null;
            IsCalibrating = false;
            if (fadeCanvas != null) fadeCanvas.enabled = false;
        }

        private void OnEnable()
        {
            if (tableConfig != null && !IsReady) RequestRecenter();
        }

        private void OnDestroy()
        {
            if (inputSubsystem != null) inputSubsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
        }
    }
}
