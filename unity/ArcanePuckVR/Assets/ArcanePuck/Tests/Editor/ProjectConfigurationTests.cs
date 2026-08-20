using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ArcanePuck.Tests
{
    public sealed class ProjectConfigurationTests
    {
        private const string ScenePath = "Assets/ArcanePuck/Scenes/ArcanePuck.unity";
        private static readonly string[] RequiredShaderNames =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Particles/Unlit"
        };

        [Test]
        public void URP_IsActiveForEveryQualityLevel()
        {
            Assert.That(GraphicsSettings.defaultRenderPipeline, Is.TypeOf<UniversalRenderPipelineAsset>());
            var defaultPipeline = GraphicsSettings.defaultRenderPipeline;
            for (var quality = 0; quality < QualitySettings.names.Length; quality++)
            {
                var effectivePipeline = QualitySettings.GetRenderPipelineAssetAt(quality) ?? defaultPipeline;
                Assert.That(effectivePipeline, Is.TypeOf<UniversalRenderPipelineAsset>(), QualitySettings.names[quality]);
            }

            var urp = (UniversalRenderPipelineAsset)defaultPipeline;
            Assert.That(urp.rendererDataList.Length, Is.GreaterThan(0));
            Assert.That(urp.rendererDataList[0], Is.Not.Null);
        }

        [Test]
        public void RuntimeScene_IsCommittedAndEnabled()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null);
            var scene = EditorBuildSettings.scenes.SingleOrDefault(item => item.path == ScenePath);
            Assert.That(scene, Is.Not.Null);
            Assert.That(scene.enabled, Is.True);
        }

        [Test]
        public void RuntimeCreatedMaterials_HaveShadersIncludedInPlayerBuild()
        {
            var graphicsSettings = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var includedShaders = graphicsSettings.FindProperty("m_AlwaysIncludedShaders");
            foreach (var shaderName in RequiredShaderNames)
            {
                var shader = Shader.Find(shaderName);
                Assert.That(shader, Is.Not.Null, shaderName);
                Assert.That(ContainsShader(includedShaders, shader), Is.True,
                    $"{shaderName} must be kept because runtime materials locate it with Shader.Find.");
            }
        }

        private static bool ContainsShader(SerializedProperty shaders, Shader expected)
        {
            for (var index = 0; index < shaders.arraySize; index++)
            {
                if (shaders.GetArrayElementAtIndex(index).objectReferenceValue == expected)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
