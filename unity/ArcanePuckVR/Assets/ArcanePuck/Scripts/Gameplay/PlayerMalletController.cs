using UnityEngine;

namespace ArcanePuck
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PlayerMalletController : MonoBehaviour
    {
        private TableConfig config;
        private Rigidbody body;
        private IMalletInput input;
        private Vector3 currentVelocity;
        private float pendingCharge;
        private bool hasPendingCharge;

        public void Initialize(TableConfig tableConfig, IMalletInput malletInput)
        {
            config = tableConfig;
            input = malletInput;
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.FreezeRotation;
        }

        private void Update()
        {
            if (input != null && input.ConsumeChargeRelease(out var charge01))
            {
                pendingCharge = charge01;
                hasPendingCharge = charge01 > 0f;
            }
        }

        private void FixedUpdate()
        {
            if (config == null || input == null)
            {
                return;
            }

            var tableLocalTarget = config.ClampMalletTarget(input.TargetPosition, true);
            var tableLocalPosition = config.ToTablePosition(transform.parent, body.position);
            var desiredVelocityLocal = (tableLocalTarget - tableLocalPosition) / Time.fixedDeltaTime;
            var maxSpeed = input is PicoMalletInput ? config.maxVrSpeed : config.maxDesktopSpeed;
            var maxAcceleration = input is PicoMalletInput ? config.maxVrAcceleration : config.maxDesktopAcceleration;
            desiredVelocityLocal = Vector3.ClampMagnitude(desiredVelocityLocal, maxSpeed);
            currentVelocity = Vector3.MoveTowards(
                currentVelocity,
                desiredVelocityLocal,
                maxAcceleration * Time.fixedDeltaTime);
            var nextLocal = config.ClampMalletTarget(
                tableLocalPosition + currentVelocity * Time.fixedDeltaTime,
                true);
            body.MovePosition(config.ToWorldPosition(transform.parent, nextLocal));
        }

        public bool ConsumePendingCharge(out float charge01)
        {
            charge01 = pendingCharge;
            if (!hasPendingCharge)
            {
                return false;
            }

            hasPendingCharge = false;
            pendingCharge = 0f;
            return true;
        }

        public void ResetMallet()
        {
            currentVelocity = Vector3.zero;
            pendingCharge = 0f;
            hasPendingCharge = false;
            body.position = config.ToWorldPosition(
                transform.parent,
                new Vector3(0f, 0f, -config.length * 0.28f));
            body.rotation = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
            input?.ResetInput();
        }
    }
}
