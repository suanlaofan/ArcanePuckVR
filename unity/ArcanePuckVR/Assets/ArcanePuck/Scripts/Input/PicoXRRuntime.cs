using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;

namespace ArcanePuck
{
    public static class PicoXRRuntime
    {
        // The PICO loader is configured for Android only. Desktop keeps its existing controls.
        public static bool IsRunning => Application.platform == RuntimePlatform.Android && XRSettings.enabled;

        public static Camera CreateCamera(TableConfig config)
        {
            foreach (var existing in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                existing.gameObject.SetActive(false);
            }

            var origin = new GameObject("PICO XR Origin");
            // Emulator/headset tracking is applied to the child HMD camera. Keep
            // the authored comfort pose on its parent: farther back from the
            // player's table edge and pitched down toward the play surface.
            origin.transform.SetPositionAndRotation(
                new Vector3(0f, 0f, -4.4f),
                Quaternion.Euler(12f, 0f, 0f));
            origin.AddComponent<PXR_Manager>();
            var offset = new GameObject("Camera Offset").transform;
            offset.SetParent(origin.transform, false);
            offset.localPosition = new Vector3(0f, 1.6f, 0f);

            var cameraObject = new GameObject("PICO HMD Camera", typeof(Camera), typeof(AudioListener), typeof(TrackedPoseDriver));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(offset, false);
            var camera = cameraObject.GetComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 70f;
            var driver = cameraObject.GetComponent<TrackedPoseDriver>();
            driver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            return camera;
        }
    }
}
