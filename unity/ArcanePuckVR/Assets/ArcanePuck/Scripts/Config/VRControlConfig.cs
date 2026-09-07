using System;
using UnityEngine;

namespace ArcanePuck
{
    [CreateAssetMenu(menuName = "Arcane Puck/VR Control Config", fileName = "ArcanePuckVRControls")]
    public sealed class VRControlConfig : ScriptableObject
    {
        private const string PreferencePrefix = "ArcanePuck.VR.";
        public const float DefaultStartDistance = 0.45f;
        public const float MinimumStartDistance = 0.30f;
        public const float MaximumStartDistance = 0.65f;

        [SerializeField, Range(0, 2)] private int sensitivityPreset = 1;
        [SerializeField, Range(MinimumStartDistance, MaximumStartDistance)] private float startDistance = DefaultStartDistance;
        [SerializeField] private bool leftHanded;
        [SerializeField] private bool seated;
        [NonSerialized] private bool persistPreferences = true;

        public int SensitivityPreset => sensitivityPreset;
        public float GainX => 2f + sensitivityPreset * 0.5f;
        public float GainZ => GainX;
        public float StartDistance => startDistance;
        public bool LeftHanded => leftHanded;
        public bool Seated => seated;
        public event Action Changed;

        /// <summary>Clone any authored asset so changing a preference never modifies the asset.</summary>
        public static VRControlConfig LoadRuntime()
        {
            var authored = Resources.Load<VRControlConfig>("ArcanePuckVRControls");
            var runtime = authored != null ? Instantiate(authored) : CreateInstance<VRControlConfig>();
            runtime.name = "Runtime VR Control Config";
            runtime.hideFlags = HideFlags.DontSave;
#if UNITY_EDITOR
            if (PicoXRRuntime.IsEditorPreview)
            {
                // Editor acceptance uses reproducible authored/default values and
                // must never overwrite the player's saved headset preferences.
                runtime.persistPreferences = false;
                runtime.OnValidate();
                return runtime;
            }
#endif
            runtime.sensitivityPreset = Mathf.Clamp(PlayerPrefs.GetInt(PreferencePrefix + "Sensitivity", runtime.sensitivityPreset), 0, 2);
            runtime.startDistance = NormalizeStartDistance(PlayerPrefs.GetFloat(PreferencePrefix + "StartDistance", runtime.startDistance));
            runtime.leftHanded = PlayerPrefs.GetInt(PreferencePrefix + "LeftHanded", runtime.leftHanded ? 1 : 0) != 0;
            runtime.seated = PlayerPrefs.GetInt(PreferencePrefix + "Seated", runtime.seated ? 1 : 0) != 0;
            return runtime;
        }

        public void SetSensitivityPreset(int value)
        {
            value = Mathf.Clamp(value, 0, 2);
            if (value == sensitivityPreset) return;
            sensitivityPreset = value;
            Persist();
        }

        public void SetStartDistance(float value)
        {
            value = NormalizeStartDistance(value);
            if (Mathf.Approximately(value, startDistance)) return;
            startDistance = value;
            Persist();
        }

        public void SetLeftHanded(bool value)
        {
            if (value == leftHanded) return;
            leftHanded = value;
            Persist();
        }

        public void SetSeated(bool value)
        {
            if (value == seated) return;
            seated = value;
            Persist();
        }

        public static float NormalizeStartDistance(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? DefaultStartDistance
                : Mathf.Clamp(value, MinimumStartDistance, MaximumStartDistance);
        }

        private void Persist()
        {
            if (persistPreferences)
            {
                PlayerPrefs.SetInt(PreferencePrefix + "Sensitivity", sensitivityPreset);
                PlayerPrefs.SetFloat(PreferencePrefix + "StartDistance", startDistance);
                PlayerPrefs.SetInt(PreferencePrefix + "LeftHanded", leftHanded ? 1 : 0);
                PlayerPrefs.SetInt(PreferencePrefix + "Seated", seated ? 1 : 0);
                PlayerPrefs.Save();
            }
            Changed?.Invoke();
        }

        private void OnValidate()
        {
            sensitivityPreset = Mathf.Clamp(sensitivityPreset, 0, 2);
            startDistance = NormalizeStartDistance(startDistance);
        }
    }
}
