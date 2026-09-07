#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ArcanePuck.Editor
{
    public static class VRPreviewTools
    {
        private const string ToggleMenu = "Arcane Puck/VR/Enable Editor Preview";
        private const string GameScenePath = "Assets/ArcanePuck/Scenes/ArcanePuck.unity";

        [MenuItem(ToggleMenu)]
        public static void ToggleEditorPreview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            PicoXRRuntime.EditorPreviewEnabled = !PicoXRRuntime.EditorPreviewEnabled;
            Debug.Log($"Arcane Puck VR Editor preview {(PicoXRRuntime.EditorPreviewEnabled ? "enabled" : "disabled")}. " +
                      "This uses a simulated head and the Game View mouse; it does not verify a PICO device.");
        }

        [MenuItem(ToggleMenu, true)]
        private static bool ValidateToggle()
        {
            Menu.SetChecked(ToggleMenu, PicoXRRuntime.EditorPreviewEnabled);
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem("Arcane Puck/VR/Start Editor Preview")]
        public static void StartEditorPreview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorSceneManager.GetActiveScene().path != GameScenePath)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath) == null)
                {
                    Debug.LogError($"Arcane Puck VR preview could not find the game scene: {GameScenePath}");
                    return;
                }

                // This menu opens the authored scene, including its adjusted model
                // anchors. An empty scene would silently exercise the procedural
                // fallback and provide misleading visual acceptance evidence.
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            }
            PicoXRRuntime.EditorPreviewEnabled = true;
            PicoXRRuntime.EditorSimulateController = false;
            EditorApplication.isPlaying = true;
        }
    }
}
#endif
