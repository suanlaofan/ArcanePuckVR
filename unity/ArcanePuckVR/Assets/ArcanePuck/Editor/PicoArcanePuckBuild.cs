using System;
using System.IO;
using System.Linq;
using ByteDance.PICO.XR;
using ByteDance.PICO.XR.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

namespace ArcanePuck.Editor
{
    /// <summary>Repeatable Android/PICO configuration and APK build entry point.</summary>
    public static class PicoArcanePuckBuild
    {
        private const string ScenePath = "Assets/ArcanePuck/Scenes/ArcanePuck.unity";
        private const string SettingsKey = "ByteDance.PICO.XR.Settings";
        private const string XrSettingsPath = "Assets/ArcanePuck/XR/PICO_XRGeneralSettings.asset";
        private const string ApkPath = "Builds/PICO/ArcanePuckVR-PICO.apk";

        [MenuItem("Arcane Puck/PICO/Configure Android XR")]
        public static void Configure()
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Unable to switch Arcane Puck to Android.");

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.arcanepuck.vr");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });

            ArcanePuckLauncherIconSetup.ApplyAndroidIcons();
            ConfigurePicoLoader();
            ConfigurePicoSettings();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
            Debug.Log("ARCANE_PUCK_PICO_CONFIGURED: Android ARM64/IL2CPP, Linear, Vulkan and PICO XR loader are enabled.");
        }

        [MenuItem("Arcane Puck/PICO/Build Development APK")]
        public static void BuildDevelopmentApk()
        {
            Configure();
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath)!);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"PICO APK build failed: {report.summary.result}");
            Debug.Log($"ARCANE_PUCK_PICO_APK: {Path.GetFullPath(ApkPath)} ({report.summary.totalSize} bytes)");
        }

        private static void ConfigurePicoLoader()
        {
            var perTarget = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>)
                .FirstOrDefault(asset => asset != null);
            if (perTarget == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(XrSettingsPath)!);
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, XrSettingsPath);
            }

            var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (general == null)
            {
                general = ScriptableObject.CreateInstance<XRGeneralSettings>();
                AssetDatabase.AddObjectToAsset(general, perTarget);
                perTarget.SetSettingsForBuildTarget(BuildTargetGroup.Android, general);
            }
            if (general.Manager == null)
            {
                general.Manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                AssetDatabase.AddObjectToAsset(general.Manager, perTarget);
            }

            // The runtime selects its HMD camera and controller path from
            // XRSettings.enabled. Keep XR Management responsible for loading and
            // starting the PICO loader at application startup; otherwise Android
            // would silently fall back to the desktop runtime path.
            general.Manager.automaticLoading = true;
            general.Manager.automaticRunning = true;

            while (general.Manager.activeLoaders.Count > 0)
                XRPackageMetadataStore.RemoveLoader(general.Manager, general.Manager.activeLoaders[0].GetType().FullName, BuildTargetGroup.Android);
            if (!XRPackageMetadataStore.AssignLoader(general.Manager, typeof(PXR_Loader).FullName, BuildTargetGroup.Android))
                throw new InvalidOperationException("Could not enable the PICO XR loader.");
            EditorUtility.SetDirty(perTarget);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(general.Manager);
            PXR_Utils.UpdateSDKSymbols();
        }

        private static void ConfigurePicoSettings()
        {
            var settings = PXR_Settings.GetSettings() ?? AssetDatabase.FindAssets("t:PXR_Settings")
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<PXR_Settings>).FirstOrDefault();
            if (settings == null)
            {
                const string path = "Assets/ArcanePuck/XR/PXR_Settings.asset";
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                settings = ScriptableObject.CreateInstance<PXR_Settings>();
                AssetDatabase.CreateAsset(settings, path);
            }
            if (PXR_Settings.GetSettings() == null)
                EditorBuildSettings.AddConfigObject(SettingsKey, settings, true);
            settings.appMode = PXR_Settings.AppMode.XR;
            settings.stereoRenderingModeAndroid = PXR_Settings.StereoRenderingModeAndroid.Multiview;
            settings.optimizeBufferDiscards = true;
            EditorUtility.SetDirty(settings);
            var projectSettings = PXR_ProjectSetting.GetProjectConfig();
            projectSettings.isSpatialAdapter = false;
            projectSettings.portalInited = true;
            projectSettings.portalFirstSelected = (int)PXR_PortalEditor.ResponseFirst.XR;
            PXR_ProjectSetting.SaveAssets();
        }

    }
}
