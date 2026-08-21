using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace ArcanePuck.Editor
{
    /// <summary>
    /// Applies the checked-in Arcane Puck artwork to every Android/PICO launcher-icon slot.
    /// Keeping this in the build path prevents a future Android build from falling back to
    /// Unity's default application icon.
    /// </summary>
    public static class ArcanePuckLauncherIconSetup
    {
        private const string IconPath = "Assets/ArcanePuck/Branding/ArcanePuckIcon.png";

        [MenuItem("Arcane Puck/PICO/Configure Android Launcher Icon")]
        public static void ApplyAndroidIcons()
        {
            var iconTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (iconTexture == null)
                throw new BuildFailedException(
                    "Arcane Puck Android launcher icon is missing. Expected " + IconPath + ".");

            ApplyIconKind(AndroidPlatformIconKind.Adaptive, iconTexture, includeAllLayers: true);
            ApplyIconKind(AndroidPlatformIconKind.Round, iconTexture, includeAllLayers: false);
            ApplyIconKind(AndroidPlatformIconKind.Legacy, iconTexture, includeAllLayers: false);

            AssetDatabase.SaveAssets();
            Debug.Log("ARCANE_PUCK_ANDROID_ICON_CONFIGURED: supplied artwork applied to all Android/PICO icon slots.");
        }

        private static void ApplyIconKind(PlatformIconKind kind, Texture2D iconTexture, bool includeAllLayers)
        {
            var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            foreach (var icon in icons)
            {
                if (!includeAllLayers || icon.maxLayerCount <= 1)
                {
                    icon.SetTexture(iconTexture, 0);
                    continue;
                }

                var layers = new Texture2D[icon.maxLayerCount];
                for (var layer = 0; layer < layers.Length; layer++)
                    layers[layer] = iconTexture;
                icon.SetTextures(layers);
            }

            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
        }
    }
}
