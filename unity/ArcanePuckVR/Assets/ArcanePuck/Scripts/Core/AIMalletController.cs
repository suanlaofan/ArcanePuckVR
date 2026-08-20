using UnityEngine;

namespace ArcanePuck
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class AIMalletController : MonoBehaviour
    {
        private TableConfig config;
        private PuckController puck;
        private Rigidbody body;
        private DifficultyId difficulty = DifficultyId.Mage;
        private AITuning tuning;
        private Vector3 target;
        private Vector3 currentVelocity;
        private float decisionTimer;
        private float noiseX;
        private float bankDirection = 1f;
        private bool attemptingBankShot;
        private bool simulationEnabled;

        public DifficultyId Difficulty => difficulty;
        public float Activity => tuning.aggression;
        public Rigidbody Body => body;

        public void Initialize(TableConfig tableConfig, PuckController puckController, DifficultyId initialDifficulty)
        {
            config = tableConfig;
            puck = puckController;
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            SetDifficulty(initialDifficulty);
            ResetMallet();
        }

        public void Initialize(TableConfig tableConfig, PuckController puckController)
        {
            Initialize(tableConfig, puckController, DifficultyId.Mage);
        }

        public void SetDifficulty(DifficultyId newDifficulty)
        {
            difficulty = newDifficulty;
            if (config != null)
            {
                tuning = config.GetAI(difficulty);
            }

            decisionTimer = 0f;
        }

        public void SetSimulationEnabled(bool enabled)
        {
            simulationEnabled = enabled;
            if (!enabled)
            {
                currentVelocity = Vector3.zero;
            }
        }

        public void ResetMallet()
        {
            if (config == null || body == null)
            {
                return;
            }

            var home = new Vector3(0f, 0f, config.length * 0.28f);
            body.position = config.ToWorldPosition(transform.parent, home);
            body.rotation = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
            currentVelocity = Vector3.zero;
            target = home;
            decisionTimer = 0f;
            noiseX = 0f;
            bankDirection = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            attemptingBankShot = false;
        }

        private void FixedUpdate()
        {
            if (!simulationEnabled || config == null || puck == null || body == null)
            {
                return;
            }

            decisionTimer -= Time.fixedDeltaTime;
            if (decisionTimer <= 0f)
            {
                decisionTimer = Mathf.Max(Time.fixedDeltaTime, tuning.reactionSeconds);
                noiseX = UnityEngine.Random.Range(-tuning.positionError, tuning.positionError);
                attemptingBankShot = UnityEngine.Random.value < tuning.bankShotChance;
                if (attemptingBankShot && UnityEngine.Random.value < 0.35f)
                {
                    bankDirection *= -1f;
                }

                ChooseTarget();
                target.x += noiseX;
                target = config.ClampMalletTarget(target, false);
            }

            var tableLocalPosition = config.ToTablePosition(transform.parent, body.position);
            var desiredVelocity = (target - tableLocalPosition) / Time.fixedDeltaTime;
            desiredVelocity.y = 0f;
            desiredVelocity = Vector3.ClampMagnitude(desiredVelocity, tuning.maxSpeed);
            currentVelocity = Vector3.MoveTowards(
                currentVelocity,
                desiredVelocity,
                config.maxDesktopAcceleration * Time.fixedDeltaTime);
            var next = config.ClampMalletTarget(
                tableLocalPosition + currentVelocity * Time.fixedDeltaTime,
                false);
            body.MovePosition(config.ToWorldPosition(transform.parent, next));
        }

        private void ChooseTarget()
        {
            var puckPosition = config.ToTablePosition(puck.transform.parent, puck.transform.position);
            var puckVelocity = puck.TableVelocity;
            var safeX = config.HalfWidth - config.malletRadius - 0.03f;
            var homeZ = config.HalfLength * tuning.defenseDepth;
            var approaching = puckVelocity.z > 0.08f;
            var puckOnAIHalf = puckPosition.z > -0.03f;
            var predictedX = ReflectWithinRails(
                puckPosition.x + puckVelocity.x * tuning.predictionSeconds,
                safeX);

            if (approaching)
            {
                var timeToDefense = Mathf.Max(
                    0f,
                    (homeZ - puckPosition.z) / Mathf.Max(puckVelocity.z, 0.1f));
                predictedX = ReflectWithinRails(
                    puckPosition.x + puckVelocity.x * Mathf.Min(timeToDefense, tuning.predictionSeconds),
                    safeX);
                target = new Vector3(predictedX, 0f, homeZ);
                return;
            }

            if (puckOnAIHalf && UnityEngine.Random.value < tuning.aggression)
            {
                var attackDepth = Mathf.Clamp(
                    puckPosition.z + config.malletRadius + config.puckRadius + 0.025f,
                    0.12f,
                    config.HalfLength - config.malletRadius - 0.05f);
                var bankOffset = attemptingBankShot ? bankDirection * 0.24f : 0f;
                target = new Vector3(
                    Mathf.Clamp(puckPosition.x - bankOffset, -safeX, safeX),
                    0f,
                    attackDepth);
                return;
            }

            target = new Vector3(Mathf.Clamp(predictedX * 0.72f, -safeX, safeX), 0f, homeZ);
        }

        private static float ReflectWithinRails(float value, float extent)
        {
            var reflected = value;
            for (var i = 0; i < 4 && Mathf.Abs(reflected) > extent; i++)
            {
                reflected = reflected > extent
                    ? extent * 2f - reflected
                    : -extent * 2f - reflected;
            }

            return Mathf.Clamp(reflected, -extent, extent);
        }
    }
}
