using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;

namespace ArcanePuck
{
    public static class PicoXRRuntime
    {
        // The PICO loader is configured for Android only. Desktop keeps its existing controls.
        public static bool IsRunning => IsEditorPreview ||
            (Application.platform == RuntimePlatform.Android && XRSettings.enabled);

        public static bool IsEditorPreview
        {
            get
            {
#if UNITY_EDITOR
                return EditorPreviewEnabled;
#else
                return false;
#endif
            }
        }

        public static bool IsEditorMousePreview
        {
            get
            {
#if UNITY_EDITOR
                return IsEditorPreview && !EditorSimulateController;
#else
                return false;
#endif
            }
        }

#if UNITY_EDITOR
        public static bool EditorPreviewEnabled
        {
            get => UnityEditor.SessionState.GetBool("ArcanePuck.VR.EditorPreview", false);
            set => UnityEditor.SessionState.SetBool("ArcanePuck.VR.EditorPreview", value);
        }
        public static Vector3 EditorControllerPosition { get; set; } = new(0.25f, 1.20f, 0.35f);
        public static bool EditorControllerTracked { get; set; } = true;
        // Pose injection is opt-in for automated tracking tests. Interactive VR
        // preview uses the actual Game View mouse instead of a stationary hand.
        public static bool EditorSimulateController { get; set; }
#endif

        public static VRControlConfig Controls { get; private set; }
        public static PicoPlayerCalibration Calibration { get; private set; }
        public static Transform TrackingSpace { get; private set; }

        private static bool isEmulator;
        private static bool isEmulatorResolved;
        private static float nextEmulatorProbeTime;

        /// <summary>
        /// The PICO Android emulator advertises its Android Build.PRODUCT with an
        /// "emulator" suffix. SystemInfo.deviceModel intentionally remains a PICO
        /// model name, so it cannot distinguish host-pointer input from a headset.
        /// </summary>
        public static bool IsEmulator
        {
            get
            {
                if (Application.platform != RuntimePlatform.Android) return false;
                if (isEmulatorResolved) return isEmulator;
                if (Time.realtimeSinceStartup < nextEmulatorProbeTime) return isEmulator;

                try
                {
                    var product = PXR_Plugin.System.UPxr_GetProductName();
                    nextEmulatorProbeTime = Time.realtimeSinceStartup + 0.5f;
                    if (!string.IsNullOrEmpty(product))
                    {
                        isEmulator = product.IndexOf("emulator", System.StringComparison.OrdinalIgnoreCase) >= 0;
                        isEmulatorResolved = true;
                    }
                }
                catch
                {
                    // Keep normal controller input intact if PICO's Android bridge
                    // is not ready yet during startup; retry on a later frame.
                    nextEmulatorProbeTime = Time.realtimeSinceStartup + 0.5f;
                }

                return isEmulator;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            Controls = null;
            Calibration = null;
            TrackingSpace = null;
            isEmulator = false;
            isEmulatorResolved = false;
            nextEmulatorProbeTime = 0f;
#if UNITY_EDITOR
            EditorSimulateController = false;
            EditorControllerPosition = new Vector3(0.25f, 1.20f, 0.35f);
            EditorControllerTracked = true;
#endif
        }

        public static Camera CreateCamera(TableConfig config, Transform tableRoot = null)
        {
            foreach (var existing in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                existing.gameObject.SetActive(false);
            }

            var origin = new GameObject("PICO XR Origin");
            Controls = VRControlConfig.LoadRuntime();
            // This is only a loading pose. Calibration positions the actual HMD
            // after its first valid tracking sample and never adds artificial pitch.
            var nearEdge = tableRoot != null
                ? tableRoot.TransformPoint(new Vector3(0f, 0f, -config.HalfLength))
                : new Vector3(0f, 0f, -config.HalfLength);
            var forward = Vector3.ProjectOnPlane(tableRoot != null ? tableRoot.forward : Vector3.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            var loadingPosition = nearEdge - forward * Controls.StartDistance;
            loadingPosition.y = 0f;
            origin.transform.SetPositionAndRotation(loadingPosition, Quaternion.LookRotation(forward, Vector3.up));
            if (!IsEditorPreview) origin.AddComponent<PXR_Manager>();
            var offset = new GameObject("Shared Tracking Space").transform;
            offset.SetParent(origin.transform, false);
            TrackingSpace = offset;

            var cameraObject = new GameObject("PICO HMD Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(offset, false);
            var camera = cameraObject.GetComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 70f;
            if (IsEditorPreview)
            {
                // The head itself is tilted for visual inspection; the XR origin
                // remains upright just as it does with a real tracked headset.
                cameraObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
                cameraObject.transform.localRotation = Quaternion.Euler(30f, 0f, 0f);
                camera.fieldOfView = 80f;
            }
            else
            {
                var driver = cameraObject.AddComponent<TrackedPoseDriver>();
                driver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
                driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
                driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            }
            Calibration = origin.AddComponent<PicoPlayerCalibration>();
            Calibration.Initialize(config, Controls, tableRoot, offset, camera);
            return camera;
        }
    }
}
