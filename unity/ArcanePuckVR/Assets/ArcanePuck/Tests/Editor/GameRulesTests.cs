using NUnit.Framework;
using UnityEngine;

namespace ArcanePuck.Tests
{
    public sealed class GameRulesTests
    {
        private TableConfig config;

        [SetUp]
        public void SetUp()
        {
            config = TableConfig.CreateRuntimeDefault();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
        }

        [Test]
        public void StartMatch_ReleasesServeIntoPlaying()
        {
            var rules = new GameRules(config);
            rules.StartMatch();

            Assert.That(rules.Snapshot.Phase, Is.EqualTo(MatchPhase.Serving));
            Assert.That(rules.Tick(config.serveDelaySeconds + 0.01f, out _), Is.True);
            Assert.That(rules.Snapshot.Phase, Is.EqualTo(MatchPhase.Playing));
        }

        [Test]
        public void GoldenGoal_EndsMatchOnNextScore()
        {
            var rules = new GameRules(config);
            rules.StartMatch();
            rules.Tick(config.serveDelaySeconds + 0.01f, out _);
            rules.SetScoreForTesting(config.goldenGoalAt, config.goldenGoalAt);

            var result = rules.TryScore(MatchSide.Player);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.WasGoldenGoal, Is.True);
            Assert.That(result.MatchWon, Is.True);
            Assert.That(rules.Snapshot.Winner, Is.EqualTo(MatchSide.Player));
        }

        [TestCase(DifficultyId.Apprentice)]
        [TestCase(DifficultyId.Mage)]
        [TestCase(DifficultyId.Archmage)]
        public void AllAITunings_HaveReachableLimits(DifficultyId difficulty)
        {
            var tuning = config.GetAI(difficulty);

            Assert.That(tuning.maxSpeed, Is.GreaterThan(0f));
            Assert.That(tuning.reactionSeconds, Is.GreaterThan(0f));
            Assert.That(tuning.aggression, Is.InRange(0f, 1f));
            Assert.That(tuning.bankShotChance, Is.InRange(0f, 1f));
        }

        [Test]
        public void TableSpaceConversions_RoundTripWithOffsetAndRotation()
        {
            var root = new GameObject("Table Space Test");
            try
            {
                root.transform.SetPositionAndRotation(
                    new Vector3(2.4f, config.height, -1.7f),
                    Quaternion.Euler(0f, 31f, 0f));
                var tableLocal = new Vector3(0.45f, 0.02f, -1.1f);

                var world = config.ToWorldPosition(root.transform, tableLocal);
                var roundTrip = config.ToTablePosition(root.transform, world);

                Assert.That(Vector3.Distance(tableLocal, roundTrip), Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TablePlaneProjection_PreservesLocalXZWithTiltedAnchor()
        {
            var root = new GameObject("Tilted Table Space Test");
            try
            {
                root.transform.SetPositionAndRotation(
                    new Vector3(-1.2f, config.height + 0.35f, 2.6f),
                    Quaternion.Euler(14f, 37f, -9f));
                var tableDirection = new Vector3(1.4f, 0.7f, -2.1f);
                var worldDirection = config.ToWorldDirection(root.transform, tableDirection);

                var projectedWorld = config.ProjectWorldDirectionToTable(root.transform, worldDirection);
                var projectedTable = config.ToTableDirection(root.transform, projectedWorld);

                Assert.That(projectedTable.x, Is.EqualTo(tableDirection.x).Within(0.0001f));
                Assert.That(projectedTable.y, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(projectedTable.z, Is.EqualTo(tableDirection.z).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
