#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ArcanePuck.Editor
{
    public static class ArcanePuckValidation
    {
        public static void ValidateRules()
        {
            var config = TableConfig.CreateRuntimeDefault();
            var rules = new GameRules(config);
            rules.StartMatch();
            Assert(rules.Snapshot.Phase == MatchPhase.Serving, "match starts in serving phase");
            rules.Tick(config.serveDelaySeconds + 0.01f, out _);
            Assert(rules.Snapshot.Phase == MatchPhase.Playing, "serve transitions to playing");
            rules.SetScoreForTesting(6, 6);
            var golden = rules.TryScore(MatchSide.Player);
            Assert(golden.Accepted && golden.MatchWon && golden.WasGoldenGoal, "6:6 next score is golden goal");

            foreach (var difficulty in new[] { DifficultyId.Apprentice, DifficultyId.Mage, DifficultyId.Archmage })
            {
                var tuning = config.GetAI(difficulty);
                Assert(tuning.maxSpeed > 0f && tuning.reactionSeconds > 0f, $"AI tuning is valid: {difficulty}");
            }

            Object.DestroyImmediate(config);
            Debug.Log("Arcane Puck validation passed: serving, golden goal and all AI tunings.");
        }

        public static void ValidateRuntimeAssembly()
        {
            if (Object.FindAnyObjectByType<ArcanePuckBootstrap>() == null)
            {
                var bootstrap = new GameObject("Validation Bootstrap");
                bootstrap.AddComponent<ArcanePuckBootstrap>();
            }

            EditorApplication.delayCall += () =>
            {
                Assert(Object.FindAnyObjectByType<Camera>() != null, "observation camera exists");
                Assert(Object.FindAnyObjectByType<PuckController>() != null, "puck exists");
                Assert(Object.FindAnyObjectByType<AIMalletController>() != null, "AI mallet exists");
                Assert(Object.FindAnyObjectByType<GameHUD>() != null, "HUD exists");
                Debug.Log("Arcane Puck runtime assembly passed.");
            };
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new System.Exception($"Arcane Puck validation failed: {message}");
        }
    }
}
#endif
