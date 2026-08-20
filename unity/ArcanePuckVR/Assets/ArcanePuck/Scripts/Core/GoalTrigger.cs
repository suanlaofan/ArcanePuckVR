using System;
using UnityEngine;

namespace ArcanePuck
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class GoalTrigger : MonoBehaviour
    {
        private TableConfig config;
        private PuckController puck;
        private MatchManager matchManager;
        private Transform tableSpace;
        private MatchSide defendedSide;
        private bool latched;

        public event Action<MatchSide> GoalCrossed;

        public MatchSide DefendedSide => defendedSide;
        public MatchSide AwardedSide => defendedSide == MatchSide.Player ? MatchSide.AI : MatchSide.Player;

        public void Initialize(bool playerScores)
        {
            defendedSide = playerScores ? MatchSide.AI : MatchSide.Player;
            latched = false;
            var trigger = GetComponent<BoxCollider>();
            trigger.isTrigger = true;
        }

        public void Initialize(
            TableConfig tableConfig,
            PuckController puckController,
            MatchManager manager,
            MatchSide sideDefended,
            Transform playSpace = null)
        {
            config = tableConfig;
            puck = puckController;
            matchManager = manager;
            defendedSide = sideDefended;
            tableSpace = playSpace != null ? playSpace : transform.parent;
            latched = false;

            var trigger = GetComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = Vector3.zero;
            trigger.size = new Vector3(
                config.goalWidth,
                Mathf.Max(config.goalHeight, config.puckHeight * 2f),
                config.goalDepth);

            if (tableSpace != null && transform.parent == tableSpace)
            {
                var direction = defendedSide == MatchSide.AI ? 1f : -1f;
                transform.localPosition = new Vector3(
                    0f,
                    config.goalHeight * 0.5f,
                    direction * (config.HalfLength + config.goalDepth * 0.5f));
                transform.localRotation = Quaternion.identity;
                transform.localScale = Vector3.one;
            }

            matchManager?.RegisterGoalTrigger(this);
        }

        public void ResetLatch()
        {
            latched = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            TryAwardGoal(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryAwardGoal(other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (puck != null && other.attachedRigidbody == puck.Body)
            {
                latched = false;
            }
        }

        private void OnDestroy()
        {
            matchManager?.UnregisterGoalTrigger(this);
        }

        private void TryAwardGoal(Collider other)
        {
            if (latched || config == null || puck == null || other.attachedRigidbody != puck.Body)
            {
                return;
            }

            var puckPosition = tableSpace != null
                ? tableSpace.InverseTransformPoint(puck.transform.position)
                : puck.transform.position;
            var fullyWithinMouth = Mathf.Abs(puckPosition.x) + config.puckRadius <= config.goalWidth * 0.5f;
            var fullyCrossed = defendedSide == MatchSide.AI
                ? puckPosition.z - config.puckRadius > config.HalfLength
                : puckPosition.z + config.puckRadius < -config.HalfLength;
            if (!fullyWithinMouth || !fullyCrossed)
            {
                return;
            }

            latched = true;
            var awardedSide = AwardedSide;
            GoalCrossed?.Invoke(awardedSide);
            matchManager?.ReportGoal(awardedSide);
        }
    }
}
