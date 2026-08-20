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
            origin.transform.position = new Vector3(0f, 0f, -2.35f);
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
