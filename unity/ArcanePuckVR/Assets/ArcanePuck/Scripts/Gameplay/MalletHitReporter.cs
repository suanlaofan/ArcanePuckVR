using UnityEngine;

namespace ArcanePuck
{
    public sealed class MalletHitReporter : MonoBehaviour
    {
        private TableConfig config;
        private PuckController puck;
        private PlayerMalletController player;
        private HitSide side;
        private float cooldown;

        public void Initialize(TableConfig tableConfig, PuckController puckController, HitSide hitSide, PlayerMalletController playerController = null)
        {
            config = tableConfig;
            puck = puckController;
            side = hitSide;
            player = playerController;
        }

        private void FixedUpdate()
        {
            cooldown = Mathf.Max(0f, cooldown - Time.fixedDeltaTime);
        }

        private void OnCollisionEnter(Collision collision)
        {
            Report(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            Report(collision);
        }

        private void Report(Collision collision)
        {
            if (cooldown > 0f || puck == null || collision.rigidbody != puck.Body || collision.contactCount == 0)
            {
                return;
            }

            cooldown = config.hitCooldownSeconds;
            var contact = collision.GetContact(0);
            var direction = puck.transform.position - transform.position;
            direction = config.ProjectWorldDirectionToTable(transform.parent, direction);
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = config.ToWorldDirection(
                    transform.parent,
                    side == HitSide.Player ? Vector3.forward : Vector3.back);
            }

            var charged = false;
            var charge01 = 0f;
            if (player != null && player.ConsumePendingCharge(out charge01))
            {
                charged = charge01 > 0.01f;
                puck.ApplyChargeImpulse(direction, charge01);
            }

            var impulse = collision.impulse.magnitude + charge01 * config.chargeImpulse;
            var strength = charged || impulse >= 0.45f
                ? HitStrength.Power
                : impulse >= 0.18f
                    ? HitStrength.Normal
                    : HitStrength.Light;
            HitEvents.Raise(new HitEvent(
                contact.point,
                contact.normal,
                direction.normalized,
                impulse,
                side,
                charged,
                strength));
        }
    }
}
