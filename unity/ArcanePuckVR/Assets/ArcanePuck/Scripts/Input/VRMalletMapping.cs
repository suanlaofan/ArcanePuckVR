using UnityEngine;

namespace ArcanePuck
{
    /// <summary>Table-local displacement mapping with no accumulated motion beyond a rail.</summary>
    public sealed class VRMalletMapping
    {
        private Vector3 handAnchor;
        private Vector3 malletAnchor;
        public bool IsAnchored { get; private set; }

        public static Vector3 Center(TableConfig config) =>
            new(0f, 0f, -(config.MalletLimitZ + config.MiddleClearance) * 0.5f);

        public void Clear() => IsAnchored = false;

        public void Anchor(Vector3 hand, Vector3 mallet)
        {
            handAnchor = hand;
            malletAnchor = mallet;
            IsAnchored = true;
        }

        public Vector3 Map(Vector3 hand, Vector3 currentTarget, TableConfig config, float gainX, float gainZ)
        {
            if (!IsAnchored) Anchor(hand, currentTarget);
            var delta = hand - handAnchor;
            var raw = malletAnchor + new Vector3(delta.x * gainX, 0f, delta.z * gainZ);
            var clamped = config.ClampMalletTarget(raw, true);
            // Consume outward overshoot on each axis. Reversing at the rail then responds
            // immediately instead of requiring the hand to travel back through a dead zone.
            if (!Mathf.Approximately(raw.x, clamped.x))
            {
                handAnchor.x = hand.x;
                malletAnchor.x = clamped.x;
            }
            if (!Mathf.Approximately(raw.z, clamped.z))
            {
                handAnchor.z = hand.z;
                malletAnchor.z = clamped.z;
            }
            return clamped;
        }
    }
}
