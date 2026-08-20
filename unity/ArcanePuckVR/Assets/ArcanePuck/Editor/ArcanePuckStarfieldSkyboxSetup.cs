#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArcanePuck.Editor
{
    [InitializeOnLoad]
    internal static class ArcanePuckStarfieldSkyboxSetup
    {
        private const string ShaderName = "ArcanePuck/Starfield Skybox";
        private const string MaterialPath = "Assets/ArcanePuck/Scripts/Config/Resources/StarfieldSkybox.mat";
        private const string ScenePath = "Assets/ArcanePuck/Scenes/ArcanePuck.unity";

        static ArcanePuckStarfieldSkyboxSetup()
        {
            EditorApplication.delayCall += EnsureConfigured;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        [MenuItem("Arcane Puck/Configure Starfield Skybox")]
        public static void EnsureConfigured()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"Arcane Puck: waiting for skybox shader '{ShaderName}' to import.");
                return;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "StarfieldSkybox" };
                material.SetColor("_DeepSpaceColor", new Color(0.003f, 0.001f, 0.012f));
                material.SetColor("_HorizonColor", new Color(0.035f, 0.008f, 0.065f));
                material.SetColor("_NebulaColorA", new Color(0.30f, 0.025f, 0.50f));
                material.SetColor("_NebulaColorB", new Color(0.08f, 0.015f, 0.24f));
                material.SetColor("_StarColor", new Color(0.92f, 0.86f, 1f));
                material.SetFloat("_StarDensity", 0.90f);
                material.SetFloat("_StarIntensity", 4.2f);
                material.SetFloat("_NebulaIntensity", 0.68f);
                material.SetFloat("_Exposure", 1.15f);
                AssetDatabase.CreateAsset(material, MaterialPath);
                AssetDatabase.SaveAssets();
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
            }

            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath) return;
            if (RenderSettings.skybox == material && RenderSettings.ambientMode == AmbientMode.Trilight) return;

            RenderSettings.skybox = material;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            DynamicGI.UpdateEnvironment();
            SceneView.RepaintAll();
            Debug.Log("Arcane Puck: starfield skybox assigned to the scene.");
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += EnsureConfigured;
        }
    }
}
#endif
