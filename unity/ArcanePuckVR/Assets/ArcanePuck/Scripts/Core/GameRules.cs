using System;
using UnityEngine;

namespace ArcanePuck
{
    public enum MatchSide
    {
        Player,
        AI
    }

    public enum MatchPhase
    {
        Idle,
        Serving,
        Playing,
        Paused,
        Finished
    }

    public readonly struct MatchSnapshot
    {
        public int PlayerScore { get; }
        public int AIScore { get; }
        public MatchPhase Phase { get; }
        public bool GoldenGoal { get; }
        public MatchSide? Winner { get; }
        public MatchSide? LastScorer { get; }
        public float ServeSecondsRemaining { get; }
        public MatchSide ServeToward { get; }

        public MatchSnapshot(
            int playerScore,
            int aiScore,
            MatchPhase phase,
            bool goldenGoal,
            MatchSide? winner,
            MatchSide? lastScorer,
            float serveSecondsRemaining,
            MatchSide serveToward)
        {
            PlayerScore = playerScore;
            AIScore = aiScore;
            Phase = phase;
            GoldenGoal = goldenGoal;
            Winner = winner;
            LastScorer = lastScorer;
            ServeSecondsRemaining = serveSecondsRemaining;
            ServeToward = serveToward;
        }
    }

    public readonly struct ScoreResult
    {
        public bool Accepted { get; }
        public MatchSide Scorer { get; }
        public bool WasGoldenGoal { get; }
        public bool MatchWon { get; }
        public MatchSide? Winner { get; }

        public ScoreResult(
            bool accepted,
            MatchSide scorer,
            bool wasGoldenGoal,
            bool matchWon,
            MatchSide? winner)
        {
            Accepted = accepted;
            Scorer = scorer;
            WasGoldenGoal = wasGoldenGoal;
            MatchWon = matchWon;
            Winner = winner;
        }
    }

    public sealed class GameRules
    {
        private readonly TableConfig config;
        private int playerScore;
        private int aiScore;
        private MatchPhase phase;
        private MatchPhase phaseBeforePause;
        private bool goldenGoal;
        private MatchSide? winner;
        private MatchSide? lastScorer;
        private float serveSecondsRemaining;
        private MatchSide serveToward;

        public GameRules(TableConfig tableConfig)
        {
            config = tableConfig != null
                ? tableConfig
                : throw new ArgumentNullException(nameof(tableConfig));
            ReturnToIdle();
        }

        public MatchSnapshot Snapshot => new(
            playerScore,
            aiScore,
            phase,
            goldenGoal,
            winner,
            lastScorer,
            serveSecondsRemaining,
            serveToward);

        public void StartMatch()
        {
            playerScore = 0;
            aiScore = 0;
            phase = MatchPhase.Serving;
            phaseBeforePause = MatchPhase.Serving;
            goldenGoal = false;
            winner = null;
            lastScorer = null;
            serveToward = UnityEngine.Random.value < 0.5f ? MatchSide.Player : MatchSide.AI;
            serveSecondsRemaining = config.serveDelaySeconds;
        }

        public void RestartMatch()
        {
            StartMatch();
        }

        public void ReturnToIdle()
        {
            playerScore = 0;
            aiScore = 0;
            phase = MatchPhase.Idle;
            phaseBeforePause = MatchPhase.Idle;
            goldenGoal = false;
            winner = null;
            lastScorer = null;
            serveSecondsRemaining = 0f;
            serveToward = UnityEngine.Random.value < 0.5f ? MatchSide.Player : MatchSide.AI;
        }

        public bool Pause()
        {
            if (phase != MatchPhase.Playing && phase != MatchPhase.Serving)
            {
                return false;
            }

            phaseBeforePause = phase;
            phase = MatchPhase.Paused;
            return true;
        }

        public bool Resume()
        {
            if (phase != MatchPhase.Paused)
            {
                return false;
            }

            phase = phaseBeforePause;
            return true;
        }

        public bool Tick(float deltaSeconds, out MatchSide releasedToward)
        {
            releasedToward = serveToward;
            if (phase != MatchPhase.Serving)
            {
                return false;
            }

            serveSecondsRemaining = Mathf.Max(0f, serveSecondsRemaining - Mathf.Max(0f, deltaSeconds));
            if (serveSecondsRemaining > 0f)
            {
                return false;
            }

            phase = MatchPhase.Playing;
            return true;
        }

        public ScoreResult TryScore(MatchSide side)
        {
            if (phase != MatchPhase.Playing)
            {
                return new ScoreResult(false, side, false, false, winner);
            }

            var wasGoldenGoal = goldenGoal;
            if (side == MatchSide.Player)
            {
                playerScore++;
            }
            else
            {
                aiScore++;
            }

            lastScorer = side;
            var hasWinner = wasGoldenGoal ||
                            playerScore >= config.winScore ||
                            aiScore >= config.winScore;
            if (hasWinner)
            {
                winner = side;
                phase = MatchPhase.Finished;
                return new ScoreResult(true, side, wasGoldenGoal, true, side);
            }

            goldenGoal = playerScore == config.goldenGoalAt && aiScore == config.goldenGoalAt;
            serveToward = side == MatchSide.Player ? MatchSide.AI : MatchSide.Player;
            serveSecondsRemaining = config.serveDelaySeconds;
            phase = MatchPhase.Serving;
            return new ScoreResult(true, side, wasGoldenGoal, false, null);
        }

        public bool RequestNeutralReset()
        {
            var livePhase = phase == MatchPhase.Paused ? phaseBeforePause : phase;
            if (livePhase != MatchPhase.Playing && livePhase != MatchPhase.Serving)
            {
                return false;
            }

            serveToward = lastScorer == MatchSide.Player ? MatchSide.AI : MatchSide.Player;
            serveSecondsRemaining = config.serveDelaySeconds * 0.75f;
            if (phase == MatchPhase.Paused)
            {
                phaseBeforePause = MatchPhase.Serving;
            }
            else
            {
                phase = MatchPhase.Serving;
            }

            return true;
        }

        public void SetScoreForTesting(int newPlayerScore, int newAIScore)
        {
            playerScore = Mathf.Max(0, newPlayerScore);
            aiScore = Mathf.Max(0, newAIScore);
            goldenGoal = playerScore == config.goldenGoalAt && aiScore == config.goldenGoalAt;
            winner = null;
        }
    }
}
