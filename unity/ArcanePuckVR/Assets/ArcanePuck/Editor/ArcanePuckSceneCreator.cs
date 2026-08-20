#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcanePuck.Editor
{
    [InitializeOnLoad]
    internal static class ArcanePuckSceneCreator
    {
        private const string ScenePath = "Assets/ArcanePuck/Scenes/ArcanePuck.unity";

        static ArcanePuckSceneCreator()
        {
            EditorApplication.delayCall += EnsureScene;
        }

        private static void EnsureScene()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                return;
            }

            CreateScene();
        }

        public static void CreateScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var bootstrap = new GameObject("Arcane Puck Bootstrap");
            bootstrap.AddComponent<ArcanePuckBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.Refresh();
        }
    }
}
#endif
