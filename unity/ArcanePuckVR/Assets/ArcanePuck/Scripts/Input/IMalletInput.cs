using UnityEngine;

namespace ArcanePuck
{
    public interface IMalletInput
    {
        Vector3 TargetPosition { get; }
        float Charge01 { get; }
        bool IsCharging { get; }
        void Initialize(TableConfig tableConfig, Camera camera, Transform playSpace = null);
        bool ConsumeChargeRelease(out float charge01);
        void SetEnabled(bool enabled);
        void ResetInput();
    }
}
