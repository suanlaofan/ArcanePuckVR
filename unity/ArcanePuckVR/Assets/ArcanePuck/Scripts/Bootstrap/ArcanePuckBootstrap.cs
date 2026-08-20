using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace ArcanePuck
{
    public sealed class ArcanePuckBootstrap : MonoBehaviour
    {
        public static ArcanePuckBootstrap Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (FindAnyObjectByType<ArcanePuckBootstrap>() == null)
            {
                new GameObject("Arcane Puck Bootstrap").AddComponent<ArcanePuckBootstrap>();
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            BuildRuntime();
        }

        private void BuildRuntime()
        {
            var config = TableConfig.CreateRuntimeDefault();
            var theme = Resources.Load<ThemeConfig>("ArcanePuckTheme") ?? ThemeConfig.CreateRuntimeDefault();
            ApplyStarfieldSkybox();
            var sceneModels = FindAnyObjectByType<SceneModelLayout>();
            var sceneEnvironment = sceneModels?.EnableRuntimeEnvironment();
            var usePicoXR = PicoXRRuntime.IsRunning;
            Debug.Log($"Arcane Puck theme: table={theme.TABLE_MODEL != null}, playerMallet={theme.PLAYER_MALLET_MODEL != null}, aiMallet={theme.AI_MALLET_MODEL != null}, puck={theme.PUCK_MODEL != null}");
            Time.fixedDeltaTime = config.FixedDeltaTime;
            Physics.defaultSolverIterations = config.solverIterations;
            Physics.defaultSolverVelocityIterations = 4;
            Physics.bounceThreshold = config.railBounceThreshold;
            Physics.gravity = Vector3.zero;

            var root = new GameObject("Arcane Puck Runtime");
            if (sceneEnvironment == null)
            {
                var arenaRoot = new GameObject("Arena Visuals").transform;
                arenaRoot.SetParent(root.transform, false);
                ProceduralTheme.BuildArena(arenaRoot, config, theme);
                ProceduralTheme.BuildWizard(arenaRoot, theme);
            }

            var tableRoot = new GameObject("Table Anchor").transform;
            tableRoot.SetParent(root.transform, false);
            tableRoot.position = new Vector3(0f, config.height, 0f);
            ProceduralTheme.BuildTableVisual(tableRoot, config, theme, sceneModels?.TakeTableModel());
            BuildRails(tableRoot, config);

            var puck = BuildPuck(tableRoot, config, theme, sceneModels?.TakePuckModel(), out var trail);
            var player = BuildPlayerMallet(tableRoot, config, theme, puck, sceneModels?.TakePlayerMalletModel(), usePicoXR, out var malletInput);
            var ai = BuildAIMallet(tableRoot, config, theme, puck, sceneModels?.TakeAiMalletModel());

            var camera = usePicoXR
                ? PicoXRRuntime.CreateCamera(config)
                : sceneModels?.EnableRuntimeCamera() ?? BuildCamera(config, tableRoot);
            if (!usePicoXR)
            {
                var cameraController = camera.GetComponent<DesktopCameraController>() ??
                                       camera.gameObject.AddComponent<DesktopCameraController>();
                cameraController.Initialize();
            }
            malletInput.Initialize(config, camera, tableRoot);
            player.Initialize(config, malletInput);

            var matchObject = new GameObject("Match Manager");
            matchObject.transform.SetParent(root.transform, false);
            var match = matchObject.AddComponent<MatchManager>();
            var hud = matchObject.AddComponent<GameHUD>();
            var audio = matchObject.AddComponent<GameAudioController>();
            var playerGoal = BuildGoalTrigger(tableRoot, config, false, match, puck);
            var aiGoal = BuildGoalTrigger(tableRoot, config, true, match, puck);
            match.Initialize(config, malletInput, player, ai, puck, playerGoal, aiGoal, camera, tableRoot, hud);
            audio.Initialize(theme, match, puck);
            hud.Initialize(match, theme, audio);

            var effects = new GameObject("Hit Effects").AddComponent<HitEffectController>();
            effects.transform.SetParent(tableRoot, false);
            effects.Initialize(theme, puck, trail);

            if (sceneEnvironment == null) BuildLighting(theme);
        }

        private static Camera BuildCamera(TableConfig config, Transform tableRoot)
        {
            var existing = Camera.main;
            if (existing != null) Destroy(existing.gameObject);
            var cameraObject = new GameObject("Desktop Observation Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.fieldOfView = 50f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 70f;
            ResetCamera(camera, config, tableRoot);
            return camera;
        }

        public static void ResetCamera(Camera camera, TableConfig config, Transform tableRoot = null)
        {
            var cameraLocalPosition = new Vector3(0f, 3.05f - config.height, -4.65f);
            var targetLocalPosition = new Vector3(0f, -0.04f, 0.22f);
            if (tableRoot == null)
            {
                camera.transform.position = cameraLocalPosition + Vector3.up * config.height;
                camera.transform.LookAt(targetLocalPosition + Vector3.up * config.height);
                return;
            }

            camera.transform.position = config.ToWorldPosition(tableRoot, cameraLocalPosition);
            camera.transform.LookAt(config.ToWorldPosition(tableRoot, targetLocalPosition), tableRoot.up);
        }

        private static PuckController BuildPuck(Transform parent, TableConfig config, ThemeConfig theme, Transform sceneVisual, out TrailRenderer trail)
        {
            var puckObject = new GameObject("Puck Physics", typeof(Rigidbody), typeof(MeshCollider));
            puckObject.transform.SetParent(parent, false);
            puckObject.transform.localPosition = new Vector3(0f, config.puckHeight * 0.5f, 0f);
            trail = ProceduralTheme.BuildPuckVisual(puckObject.transform, config, theme, sceneVisual);
            var puck = puckObject.AddComponent<PuckController>();
            puck.Initialize(config);
            return puck;
        }

        private static PlayerMalletController BuildPlayerMallet(
            Transform parent,
            TableConfig config,
            ThemeConfig theme,
            PuckController puck,
            Transform sceneVisual,
            bool usePicoXR,
            out IMalletInput input)
        {
            var mallet = BuildMalletBody("Player Mallet Physics", parent, config, theme, true, sceneVisual);
            input = usePicoXR
                ? mallet.AddComponent<PicoMalletInput>()
                : mallet.AddComponent<DesktopMalletInput>();
            var controller = mallet.AddComponent<PlayerMalletController>();
            mallet.AddComponent<MalletHitReporter>().Initialize(config, puck, HitSide.Player, controller);
            return controller;
        }

        private static AIMalletController BuildAIMallet(Transform parent, TableConfig config, ThemeConfig theme, PuckController puck, Transform sceneVisual)
        {
            var mallet = BuildMalletBody("AI Mallet Physics", parent, config, theme, false, sceneVisual);
            var controller = mallet.AddComponent<AIMalletController>();
            controller.Initialize(config, puck);
            mallet.AddComponent<MalletHitReporter>().Initialize(config, puck, HitSide.AI);
            return controller;
        }

        private static GameObject BuildMalletBody(string name, Transform parent, TableConfig config, ThemeConfig theme, bool player, Transform sceneVisual)
        {
            var mallet = new GameObject(name, typeof(Rigidbody), typeof(MeshCollider));
            mallet.transform.SetParent(parent, false);
            mallet.transform.localPosition = new Vector3(0f, 0f, (player ? -1f : 1f) * config.length * 0.28f);
            var body = mallet.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var material = PhysicsMaterials.Create(
                "Mallet Physics", config.malletFriction, config.malletRestitution, PhysicsMaterialCombine.Average);
            PuckController.ConfigureDiscColliders(
                mallet,
                config.malletRadius,
                config.malletHeight,
                new Vector3(0f, config.malletHeight * 0.5f, 0f),
                material);
            ProceduralTheme.BuildMalletVisual(mallet.transform, config, theme, player, sceneVisual);
            return mallet;
        }

        private static GoalTrigger BuildGoalTrigger(Transform parent, TableConfig config, bool playerScores, MatchManager match, PuckController puck)
        {
            var goal = new GameObject(playerScores ? "AI Goal Trigger" : "Player Goal Trigger", typeof(BoxCollider));
            goal.transform.SetParent(parent, false);
            var collider = goal.GetComponent<BoxCollider>();
            var trigger = goal.AddComponent<GoalTrigger>();
            trigger.Initialize(config, puck, match, playerScores ? MatchSide.AI : MatchSide.Player, parent);
            return trigger;
        }

        private static void BuildRails(Transform parent, TableConfig config)
        {
            var material = PhysicsMaterials.Create(
                "Rail Physics",
                config.railFriction,
                config.railRestitution,
                PhysicsMaterialCombine.Minimum,
                PhysicsMaterialCombine.Maximum);
            CreateRail(parent, "Left Rail Collider", new Vector3(-config.HalfWidth - config.wallThickness * 0.5f, config.wallHeight * 0.5f, 0f),
                new Vector3(config.wallThickness, config.wallHeight, config.length + config.wallThickness * 2f), material);
            CreateRail(parent, "Right Rail Collider", new Vector3(config.HalfWidth + config.wallThickness * 0.5f, config.wallHeight * 0.5f, 0f),
                new Vector3(config.wallThickness, config.wallHeight, config.length + config.wallThickness * 2f), material);

            var segment = (config.width - config.goalWidth) * 0.5f;
            var x = config.goalWidth * 0.5f + segment * 0.5f;
            foreach (var z in new[] { -config.HalfLength - config.wallThickness * 0.5f, config.HalfLength + config.wallThickness * 0.5f })
            {
                CreateRail(parent, "End Rail Collider", new Vector3(-x, config.wallHeight * 0.5f, z),
                    new Vector3(segment, config.wallHeight, config.wallThickness), material);
                CreateRail(parent, "End Rail Collider", new Vector3(x, config.wallHeight * 0.5f, z),
                    new Vector3(segment, config.wallHeight, config.wallThickness), material);
            }
        }

        private static void CreateRail(Transform parent, string name, Vector3 localPosition, Vector3 size, PhysicsMaterial material)
        {
            var rail = new GameObject(name, typeof(BoxCollider));
            rail.transform.SetParent(parent, false);
            rail.transform.localPosition = localPosition;
            var collider = rail.GetComponent<BoxCollider>();
            collider.size = size;
            collider.material = material;
            rail.AddComponent<RailSurface>();
        }

        private static void BuildLighting(ThemeConfig theme)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.2f, 0.22f, 0.35f);
            RenderSettings.ambientEquatorColor = new Color(0.08f, 0.06f, 0.15f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.02f, 0.05f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.03f, 0.045f, 0.1f);
            RenderSettings.fogDensity = 0.018f;

            var sun = new GameObject("Warm Academy Light", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.86f, 0.61f);
            sun.intensity = 1.65f;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.shadows = LightShadows.Soft;
            CreatePointLight("Cyan Arcane Light", new Vector3(-2.4f, 2.4f, -1.8f), theme.cyan, 5.4f, 7f);
            CreatePointLight("Violet Arcane Light", new Vector3(2.4f, 2.6f, 1.8f), theme.violet, 4.8f, 7f);
        }

        private static void ApplyStarfieldSkybox()
        {
            var skybox = Resources.Load<Material>("StarfieldSkybox");
            if (skybox == null) return;
            RenderSettings.skybox = skybox;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            DynamicGI.UpdateEnvironment();
        }

        private static void CreatePointLight(string name, Vector3 position, Color color, float intensity, float range)
        {
            var light = new GameObject(name, typeof(Light)).GetComponent<Light>();
            light.type = LightType.Point;
            light.transform.position = position;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
        }
    }
}
