#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ArcanePuck.Editor
{
    internal static class ArcanePuckRenderPipelineSetup
    {
        private static readonly string[] RequiredShaderNames =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Particles/Unlit"
        };

        private const string SettingsFolder = "Assets/ArcanePuck/Settings";
        private const string RendererPath = SettingsFolder + "/ArcanePuck_Renderer.asset";
        private const string PipelinePath = SettingsFolder + "/ArcanePuck_URP.asset";

        [InitializeOnLoadMethod]
        private static void QueueSetup()
        {
            EditorApplication.delayCall += EnsureConfigured;
        }

        [MenuItem("Arcane Puck/Configure URP")]
        public static void EnsureConfigured()
        {
            EnsureFolder("Assets/ArcanePuck", "Settings");

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "ArcanePuck_Renderer";
                ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }

            else
            {
                ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "ArcanePuck_URP";
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            pipeline.supportsCameraDepthTexture = false;
            pipeline.supportsCameraOpaqueTexture = false;
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 4;
            pipeline.renderScale = 1f;
            pipeline.shadowDistance = 22f;
            pipeline.shadowCascadeCount = 2;
            pipeline.maxAdditionalLightsCount = 4;
            pipeline.useSRPBatcher = true;
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            EnsureRequiredShadersIncluded();
            var previousQuality = QualitySettings.GetQualityLevel();
            for (var quality = 0; quality < QualitySettings.names.Length; quality++)
            {
                QualitySettings.SetQualityLevel(quality, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(previousQuality, false);

            AssetDatabase.SaveAssets();
        }

        private static void EnsureRequiredShadersIncluded()
        {
            var graphicsSettings = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var includedShaders = graphicsSettings.FindProperty("m_AlwaysIncludedShaders");
            foreach (var shaderName in RequiredShaderNames)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    throw new System.InvalidOperationException($"Required URP shader is unavailable: {shaderName}");
                }

                var isIncluded = false;
                for (var index = 0; index < includedShaders.arraySize; index++)
                {
                    if (includedShaders.GetArrayElementAtIndex(index).objectReferenceValue == shader)
                    {
                        isIncluded = true;
                        break;
                    }
                }

                if (isIncluded)
                {
                    continue;
                }

                var newIndex = includedShaders.arraySize;
                includedShaders.InsertArrayElementAtIndex(newIndex);
                includedShaders.GetArrayElementAtIndex(newIndex).objectReferenceValue = shader;
            }

            graphicsSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string parent, string name)
        {
            var path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }
    }
}
#endif
