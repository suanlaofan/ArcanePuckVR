using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcanePuck
{
    public sealed class MatchManager : MonoBehaviour
    {
        [SerializeField] private TableConfig config;
        [SerializeField] private PuckController puck;
        [SerializeField] private PlayerMalletController playerMallet;
        [SerializeField] private DesktopMalletInput desktopInput;
        [SerializeField] private AIMalletController aiMallet;
        [SerializeField] private DifficultyId difficulty = DifficultyId.Mage;

        private readonly List<GoalTrigger> goalTriggers = new();
        private GameRules rules;
        private IMalletInput malletInput;
        private bool initialized;
        private bool ownsRuntimeConfig;
        private bool pausedPuckWasDynamic;
        private Vector3 pausedPuckVelocity;
        private Vector3 pausedPuckAngularVelocity;
        private Camera observationCamera;
        private Transform tableRoot;
        private GameHUD hud;
        private string statusText = "选择 AI 难度开始对局";
        private int lastDisplayedChargePercent = -1;

        public event Action<MatchSnapshot> StateChanged;
        public event Action<MatchSide, bool> ScoreAwarded;
        public event Action<MatchSide> ServeStarted;
        public event Action<MatchSide> MatchCompleted;
        public event Action ViewResetRequested;

        public MatchSnapshot Snapshot => rules != null ? rules.Snapshot : default;
        public DifficultyId Difficulty => difficulty;
        public TableConfig Config => config;
        public int PlayerScore => Snapshot.PlayerScore;
        public int AIScore => Snapshot.AIScore;
        public bool GoldenGoal => Snapshot.GoldenGoal;
        public bool WinnerIsPlayer => Snapshot.Winner == MatchSide.Player;
        public string DifficultyLabel => config == null ? difficulty.ToString() : config.GetAI(difficulty).label;
        public float PlayerCharge01 => malletInput == null ? 0f : malletInput.Charge01;
        public string StatusText => statusText;

        private void Awake()
        {
            if (puck != null && playerMallet != null && desktopInput != null && aiMallet != null)
            {
                Initialize(config, puck, playerMallet, desktopInput, aiMallet);
            }
        }

        public void Initialize(
            TableConfig tableConfig,
            PuckController puckController,
            PlayerMalletController playerController,
            IMalletInput input,
            AIMalletController aiController)
        {
            if (initialized && puck != null)
            {
                puck.ResetRequired -= HandlePuckResetRequired;
            }

            config = tableConfig;
            if (config == null)
            {
                config = TableConfig.CreateRuntimeDefault();
                ownsRuntimeConfig = true;
            }

            puck = puckController;
            playerMallet = playerController;
            malletInput = input;
            desktopInput = input as DesktopMalletInput;
            aiMallet = aiController;
            rules = new GameRules(config);
            Time.fixedDeltaTime = config.FixedDeltaTime;
            Time.maximumDeltaTime = config.FixedDeltaTime * 5f;
            Physics.defaultSolverIterations = Mathf.Max(1, config.solverIterations);

            if (puck != null)
            {
                puck.ResetRequired += HandlePuckResetRequired;
                puck.SetSimulating(false);
            }

            aiMallet?.SetDifficulty(difficulty);
            SetControllersActive(false);
            initialized = true;
            PublishState();
        }

        public void Initialize(
            TableConfig tableConfig,
            IMalletInput input,
            PlayerMalletController playerController,
            AIMalletController aiController,
            PuckController puckController,
            GoalTrigger playerGoal,
            GoalTrigger aiGoal,
            Camera camera,
            Transform playSpace,
            GameHUD gameHud)
        {
            observationCamera = camera;
            tableRoot = playSpace;
            hud = gameHud;
            Initialize(tableConfig, puckController, playerController, input, aiController);
            playerGoal?.Initialize(config, puck, this, MatchSide.Player, tableRoot);
            aiGoal?.Initialize(config, puck, this, MatchSide.AI, tableRoot);
            statusText = "选择 AI 难度开始对局";
            AlignBodiesToTable();
        }

        public void OpenDifficultySelection()
        {
            hud?.ShowDifficulty();
        }

        private void OnDestroy()
        {
            if (puck != null)
            {
                puck.ResetRequired -= HandlePuckResetRequired;
            }

            if (ownsRuntimeConfig && config != null)
            {
                Destroy(config);
            }
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            HandleKeyboardShortcuts();
            HandlePicoShortcuts();
            if (rules.Tick(Time.unscaledDeltaTime, out var toward))
            {
                ReleaseServe(toward);
                PublishState();
            }

            var chargePercent = Mathf.RoundToInt(PlayerCharge01 * 100f);
            if (chargePercent != lastDisplayedChargePercent)
            {
                lastDisplayedChargePercent = chargePercent;
                hud?.Refresh();
            }
        }

        public void StartMatch(DifficultyId selectedDifficulty)
        {
            if (!initialized)
            {
                return;
            }

            difficulty = selectedDifficulty;
            aiMallet?.SetDifficulty(difficulty);
            aiMallet?.ResetMallet();
            rules.StartMatch();
            ResetRoundForServe();
            statusText = $"{DifficultyLabel} AI 已就位，准备发球";
            hud?.ShowPlaying();
            PublishState();
        }

        public void RestartMatch()
        {
            if (!initialized)
            {
                return;
            }

            aiMallet?.SetDifficulty(difficulty);
            aiMallet?.ResetMallet();
            rules.RestartMatch();
            ResetRoundForServe();
            statusText = "新的决斗开始";
            hud?.ShowPlaying();
            PublishState();
        }

        public void PlayAgain()
        {
            RestartMatch();
        }

        public void ReturnToMenu()
        {
            if (!initialized)
            {
                return;
            }

            rules.ReturnToIdle();
            ResetRoundForServe();
            statusText = "选择 AI 难度开始对局";
            hud?.ShowMenu();
            PublishState();
        }

        public void TogglePause()
        {
            if (!initialized)
            {
                return;
            }

            if (rules.Snapshot.Phase == MatchPhase.Paused)
            {
                ResumeMatch();
            }
            else
            {
                PauseMatch();
            }
        }

        public void PauseMatch()
        {
            if (!rules.Pause())
            {
                return;
            }

            PausePhysicalState();
            SetControllersActive(false);
            statusText = "决斗已暂停";
            hud?.ShowPaused();
            PublishState();
        }

        public void ResumeMatch()
        {
            if (!rules.Resume())
            {
                return;
            }

            if (rules.Snapshot.Phase == MatchPhase.Playing)
            {
                RestorePhysicalState();
                SetControllersActive(true);
            }
            else
            {
                puck?.SetSimulating(false);
                SetControllersActive(false);
            }

            statusText = rules.Snapshot.Phase == MatchPhase.Serving
                ? "准备发球"
                : PicoXRRuntime.IsRunning
                    ? "移动主手球槌，按住扳机蓄力"
                    : "鼠标移动击槌，按住左键蓄力";
            hud?.ShowPlaying();
            PublishState();
        }

        public void ResetPuckToCenter()
        {
            if (!initialized || !rules.RequestNeutralReset())
            {
                return;
            }

            ResetRoundForServe();
            statusText = "圆盘已回到中线";
            PublishState();
        }

        public void ResetObservationView()
        {
            if (observationCamera == null || config == null || PicoXRRuntime.IsRunning)
            {
                return;
            }

            ArcanePuckBootstrap.ResetCamera(observationCamera, config, tableRoot);
            observationCamera.GetComponent<DesktopCameraController>()?.Initialize();
        }

        public void ReportGoal(MatchSide scoringSide)
        {
            if (!initialized)
            {
                return;
            }

            var result = rules.TryScore(scoringSide);
            if (!result.Accepted)
            {
                return;
            }

            statusText = result.Scorer == MatchSide.Player ? "你得分了" : "AI 巫师得分";
            ScoreAwarded?.Invoke(result.Scorer, result.WasGoldenGoal);
            ResetRoundForServe();
            PublishState();
            if (result.MatchWon && result.Winner.HasValue)
            {
                hud?.ShowFinished();
                MatchCompleted?.Invoke(result.Winner.Value);
            }
            else
            {
                hud?.ShowPlaying();
            }
        }

        public void RegisterGoalTrigger(GoalTrigger goalTrigger)
        {
            if (goalTrigger != null && !goalTriggers.Contains(goalTrigger))
            {
                goalTriggers.Add(goalTrigger);
            }
        }

        public void UnregisterGoalTrigger(GoalTrigger goalTrigger)
        {
            goalTriggers.Remove(goalTrigger);
        }

        public void SetScoreForTesting(int playerScore, int aiScore)
        {
            if (rules == null)
            {
                return;
            }

            rules.SetScoreForTesting(playerScore, aiScore);
            PublishState();
        }

        private void HandleKeyboardShortcuts()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (rules.Snapshot.Phase == MatchPhase.Finished)
                {
                    ReturnToMenu();
                }
                else
                {
                    TogglePause();
                }
            }

            if (keyboard.rKey.wasPressedThisFrame)
            {
                ResetPuckToCenter();
            }

            if (keyboard.cKey.wasPressedThisFrame)
            {
                ResetObservationView();
                ViewResetRequested?.Invoke();
            }
        }

        private void HandlePicoShortcuts()
        {
            if (malletInput is not PicoMalletInput picoInput || rules == null) return;

            if (picoInput.ConsumeStartRequest() && rules.Snapshot.Phase == MatchPhase.Idle)
            {
                StartMatch(DifficultyId.Mage);
            }

            if (picoInput.ConsumePauseRequest() &&
                rules.Snapshot.Phase is (MatchPhase.Playing or MatchPhase.Serving or MatchPhase.Paused))
            {
                TogglePause();
            }
        }

        private void HandlePuckResetRequired()
        {
            ResetPuckToCenter();
        }

        private void ResetRoundForServe()
        {
            SetControllersActive(false);
            puck?.SetSimulating(false);
            puck?.ResetPuck(rules.Snapshot.ServeToward == MatchSide.AI, 0f);
            playerMallet?.ResetMallet();
            aiMallet?.ResetMallet();
            malletInput?.ResetInput();
            pausedPuckWasDynamic = false;
            pausedPuckVelocity = Vector3.zero;
            pausedPuckAngularVelocity = Vector3.zero;
            foreach (var goalTrigger in goalTriggers)
            {
                goalTrigger?.ResetLatch();
            }

            AlignBodiesToTable();
        }

        private void AlignBodiesToTable()
        {
            if (tableRoot == null || config == null)
            {
                return;
            }

            if (puck != null && puck.Body != null)
            {
                var puckPosition = config.ToWorldPosition(tableRoot, new Vector3(0f, config.puckHeight * 0.5f, 0f));
                puck.Body.position = puckPosition;
            }

            if (playerMallet != null && playerMallet.TryGetComponent<Rigidbody>(out var playerBody))
            {
                playerBody.position = config.ToWorldPosition(tableRoot, new Vector3(0f, 0f, -config.length * 0.28f));
            }

            if (aiMallet != null && aiMallet.Body != null)
            {
                aiMallet.Body.position = config.ToWorldPosition(tableRoot, new Vector3(0f, 0f, config.length * 0.28f));
            }
        }

        private void ReleaseServe(MatchSide toward)
        {
            if (puck != null)
            {
                puck.SetSimulating(true);
                puck.ResetPuck(toward == MatchSide.AI, config.serveSpeed);
            }

            SetControllersActive(true);
            statusText = toward == MatchSide.AI
                ? "圆盘向 AI 半场发出"
                : "防守：圆盘向你的半场发出";
            ServeStarted?.Invoke(toward);
        }

        private void PausePhysicalState()
        {
            var body = puck != null ? puck.Body : null;
            pausedPuckWasDynamic = body != null && !body.isKinematic;
            if (pausedPuckWasDynamic)
            {
                pausedPuckVelocity = body.linearVelocity;
                pausedPuckAngularVelocity = body.angularVelocity;
                body.isKinematic = true;
            }
        }

        private void RestorePhysicalState()
        {
            var body = puck != null ? puck.Body : null;
            if (!pausedPuckWasDynamic || body == null)
            {
                return;
            }

            body.isKinematic = false;
            body.linearVelocity = pausedPuckVelocity;
            body.angularVelocity = pausedPuckAngularVelocity;
            pausedPuckWasDynamic = false;
        }

        private void SetControllersActive(bool active)
        {
            malletInput?.SetEnabled(active);
            if (playerMallet != null)
            {
                playerMallet.enabled = active;
            }

            aiMallet?.SetSimulationEnabled(active);
        }

        private void PublishState()
        {
            StateChanged?.Invoke(rules.Snapshot);
            hud?.Refresh();
        }
    }
}
