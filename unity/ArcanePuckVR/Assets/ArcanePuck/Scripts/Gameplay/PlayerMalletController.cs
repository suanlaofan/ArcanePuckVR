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
        private bool wasUsingDirectPicoTracking;

        public void Initialize(TableConfig tableConfig, IMalletInput malletInput)
        {
            config = tableConfig;
            input = malletInput;
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            // PICO tracking already supplies a filtered pose. The input can fall
            // back to a mouse in the emulator, so interpolation is adjusted when
            // the active source changes rather than being fixed by input class.
            wasUsingDirectPicoTracking = malletInput is PicoMalletInput picoInput &&
                                           picoInput.IsUsingDirectPicoInput;
            body.interpolation = wasUsingDirectPicoTracking
                ? RigidbodyInterpolation.None
                : RigidbodyInterpolation.Interpolate;
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
            var useDirectPicoTracking = input is PicoMalletInput picoInput &&
                                        picoInput.IsUsingDirectPicoInput;
            if (useDirectPicoTracking != wasUsingDirectPicoTracking)
            {
                // A controller may connect or lose tracking while playing. Do not
                // carry the direct-tracking velocity into the smoothed mouse path.
                currentVelocity = Vector3.zero;
                wasUsingDirectPicoTracking = useDirectPicoTracking;
                body.interpolation = useDirectPicoTracking
                    ? RigidbodyInterpolation.None
                    : RigidbodyInterpolation.Interpolate;
            }

            if (useDirectPicoTracking)
            {
                // Move the kinematic physics proxy to the tracked controller's
                // current table position in one physics step. MovePosition keeps
                // reliable puck collisions while deliberately avoiding the
                // desktop speed/acceleration smoothing that made the mallet lag.
                currentVelocity = (tableLocalTarget - tableLocalPosition) / Time.fixedDeltaTime;
                body.MovePosition(config.ToWorldPosition(transform.parent, tableLocalTarget));
                return;
            }

            var desiredVelocityLocal = (tableLocalTarget - tableLocalPosition) / Time.fixedDeltaTime;
            var maxSpeed = config.maxDesktopSpeed;
            var maxAcceleration = config.maxDesktopAcceleration;
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

        public void ClearPendingMotion()
        {
            currentVelocity = Vector3.zero;
            pendingCharge = 0f;
            hasPendingCharge = false;
        }

        public void ResetMallet()
        {
            ClearPendingMotion();
            body.position = config.ToWorldPosition(
                transform.parent,
                input is PicoMalletInput ? VRMalletMapping.Center(config)
                    : new Vector3(0f, 0f, -config.length * 0.28f));
            body.rotation = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
            input?.ResetInput();
        }
    }
}
