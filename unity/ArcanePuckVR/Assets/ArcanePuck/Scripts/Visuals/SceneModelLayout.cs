using UnityEngine;

namespace ArcanePuck
{
    public sealed class SceneModelLayout : MonoBehaviour
    {
        public Transform tableModel;
        public Transform playerMalletModel;
        public Transform aiMalletModel;
        public Transform puckModel;

        public Transform TakeTableModel() => Take(ref tableModel);
        public Transform TakePlayerMalletModel() => Take(ref playerMalletModel);
        public Transform TakeAiMalletModel() => Take(ref aiMalletModel);
        public Transform TakePuckModel() => Take(ref puckModel);

        public Transform EnableRuntimeEnvironment()
        {
            var preview = transform.Find("Full Game Preview (Read Only)");
            if (preview == null) return null;
            preview.gameObject.SetActive(true);
            foreach (var previewObject in preview.GetComponentsInChildren<Transform>(true))
            {
                if (previewObject.name == "Player Goal Preview" ||
                    previewObject.name == "AI Goal Preview" ||
                    previewObject.name == "Wizard Robe" ||
                    previewObject.name == "Wizard Hood")
                    previewObject.gameObject.SetActive(false);
            }
            foreach (var light in preview.GetComponentsInChildren<Light>(true))
                light.enabled = true;
            return preview;
        }

        public Camera EnableRuntimeCamera()
        {
            var preview = transform.Find("Full Game Preview (Read Only)");
            if (preview == null) return null;
            var camera = preview.GetComponentInChildren<Camera>(true);
            if (camera == null) return null;
            camera.gameObject.SetActive(true);
            camera.enabled = true;
            camera.tag = "MainCamera";
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, 70f);
            if (camera.GetComponent<AudioListener>() == null)
                camera.gameObject.AddComponent<AudioListener>();
            return camera;
        }

        public void HideRuntimeWizardPreview()
        {
            foreach (var previewObject in GetComponentsInChildren<Transform>(true))
            {
                if (previewObject.name == "Wizard Robe" || previewObject.name == "Wizard Hood")
                    previewObject.gameObject.SetActive(false);
            }
        }

        private static Transform Take(ref Transform model)
        {
            var result = model;
            model = null;
            if (result != null)
            {
                var localPosition = result.localPosition;
                var localRotation = result.localRotation;
                var localScale = result.localScale;
                result.gameObject.SetActive(true);
                result.SetParent(null, false);
                result.SetLocalPositionAndRotation(localPosition, localRotation);
                result.localScale = localScale;
            }
            return result;
        }
    }
}
