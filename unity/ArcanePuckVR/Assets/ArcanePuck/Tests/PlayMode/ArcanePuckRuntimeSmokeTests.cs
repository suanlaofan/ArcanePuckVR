using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ArcanePuck.Tests
{
    public sealed class ArcanePuckRuntimeSmokeTests
    {
        private const string ScenePath = "Assets/ArcanePuck/Scenes/ArcanePuck.unity";

#if UNITY_EDITOR
        private bool savedVRPreview;

        [SetUp]
        public void UseDesktopControls()
        {
            savedVRPreview = PicoXRRuntime.EditorPreviewEnabled;
            PicoXRRuntime.EditorPreviewEnabled = false;
        }

        [TearDown]
        public void RestorePreviewPreference()
        {
            PicoXRRuntime.EditorPreviewEnabled = savedVRPreview;
        }
#endif

        [UnityTest]
        public IEnumerator RuntimeScene_BuildsPlayableMatchAndCompletesGoldenGoal()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var match = Object.FindAnyObjectByType<MatchManager>();
            var puck = Object.FindAnyObjectByType<PuckController>();
            var player = Object.FindAnyObjectByType<PlayerMalletController>();
            var ai = Object.FindAnyObjectByType<AIMalletController>();
            var hud = Object.FindAnyObjectByType<GameHUD>();
            var audio = Object.FindAnyObjectByType<GameAudioController>();
            Assert.That(match, Is.Not.Null);
            Assert.That(puck, Is.Not.Null);
            Assert.That(player, Is.Not.Null);
            Assert.That(ai, Is.Not.Null);
            Assert.That(hud, Is.Not.Null);
            Assert.That(audio, Is.Not.Null);
            Assert.That(audio.HasRequiredClips, Is.True);
            Assert.That(audio.GameSource, Is.Not.Null);
            Assert.That(audio.UISource, Is.Not.Null);
            Assert.That(audio.GameSource.playOnAwake, Is.False);
            Assert.That(audio.UISource.playOnAwake, Is.False);
            Assert.That(audio.GameSource.spatialBlend, Is.Zero);
            Assert.That(audio.UISource.spatialBlend, Is.Zero);
            Assert.That(Object.FindAnyObjectByType<Camera>(), Is.Not.Null);
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Idle));
            Assert.That(puck.Body.isKinematic, Is.True);

            match.StartMatch(DifficultyId.Archmage);
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Serving));

            var serveTimeout = Time.realtimeSinceStartup + match.Config.serveDelaySeconds + 2f;
            while (match.Snapshot.Phase != MatchPhase.Playing && Time.realtimeSinceStartup < serveTimeout)
            {
                yield return null;
            }

            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Playing));
            Assert.That(puck.Body.isKinematic, Is.False);
            AssertDiscCollider(puck.gameObject, match.Config.puckRadius, match.Config.puckHeight, Vector3.zero);
            AssertDiscCollider(
                player.gameObject,
                match.Config.malletRadius,
                match.Config.malletHeight,
                new Vector3(0f, match.Config.malletHeight * 0.5f, 0f));
            match.SetScoreForTesting(match.Config.goldenGoalAt, match.Config.goldenGoalAt);
            Assert.That(match.GoldenGoal, Is.True);
            Assert.That(hud.ScoreText, Does.Contain("6"));
            Assert.That(hud.ScoreText, Does.Contain("Golden Goal"));

            var positionAtScoreEvent = Vector3.zero;
            match.ScoreAwarded += (_, _) => positionAtScoreEvent = puck.Body.position;
            var positionBeforeGoal = match.Config.ToWorldPosition(
                puck.transform.parent,
                new Vector3(0.25f, match.Config.puckHeight * 0.5f, 0.3f));
            puck.Body.position = positionBeforeGoal;
            Physics.SyncTransforms();

            match.ReportGoal(MatchSide.Player);

            Assert.That(Vector3.Distance(positionAtScoreEvent, positionBeforeGoal), Is.LessThan(0.001f));
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Finished));
            Assert.That(match.WinnerIsPlayer, Is.True);
            Assert.That(match.PlayerScore, Is.EqualTo(match.Config.goldenGoalAt + 1));
            Assert.That(puck.Body.isKinematic, Is.True);
            var resetLocalPosition = match.Config.ToTablePosition(puck.transform.parent, puck.Body.position);
            Assert.That(Vector3.Distance(
                resetLocalPosition,
                new Vector3(0f, match.Config.puckHeight * 0.5f, 0f)), Is.LessThan(0.001f));
            Assert.That(hud.ScoreText, Does.Contain("7"));
        }

        [UnityTest]
        public IEnumerator Mallets_StayWithinTheirOwnHalvesDuringPlay()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var match = Object.FindAnyObjectByType<MatchManager>();
            var player = Object.FindAnyObjectByType<PlayerMalletController>();
            var ai = Object.FindAnyObjectByType<AIMalletController>();
            Assert.That(match, Is.Not.Null);
            Assert.That(player, Is.Not.Null);
            Assert.That(ai, Is.Not.Null);

            var tableSpace = player.transform.parent;
            tableSpace.SetPositionAndRotation(
                new Vector3(1.35f, match.Config.height + 0.2f, -0.8f),
                Quaternion.Euler(9f, 31f, -6f));
            match.StartMatch(DifficultyId.Apprentice);
            var timeout = Time.realtimeSinceStartup + match.Config.serveDelaySeconds + 2f;
            while (match.Snapshot.Phase != MatchPhase.Playing && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            yield return new WaitForFixedUpdate();
            var playerLocal = match.Config.ToTablePosition(tableSpace, player.transform.position);
            var aiLocal = match.Config.ToTablePosition(tableSpace, ai.transform.position);
            Assert.That(Mathf.Abs(playerLocal.y), Is.LessThan(0.001f));
            Assert.That(Mathf.Abs(aiLocal.y), Is.LessThan(0.001f));
            Assert.That(playerLocal.z, Is.LessThanOrEqualTo(-match.Config.MiddleClearance + 0.001f));
            Assert.That(aiLocal.z, Is.GreaterThanOrEqualTo(match.Config.MiddleClearance - 0.001f));
        }

        [UnityTest]
        public IEnumerator TiltedTable_PreservesServePlaneAndDetectsLocalOutOfBounds()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var match = Object.FindAnyObjectByType<MatchManager>();
            var puck = Object.FindAnyObjectByType<PuckController>();
            Assert.That(match, Is.Not.Null);
            Assert.That(puck, Is.Not.Null);

            var tableSpace = puck.transform.parent;
            tableSpace.SetPositionAndRotation(
                new Vector3(-1.4f, match.Config.height + 0.45f, 1.8f),
                Quaternion.Euler(12f, 43f, -8f));
            puck.SetSimulating(true);
            puck.ResetPuck(true, match.Config.serveSpeed);

            yield return new WaitForFixedUpdate();

            var puckLocal = match.Config.ToTablePosition(tableSpace, puck.Body.position);
            var velocityLocal = match.Config.ToTableDirection(tableSpace, puck.Body.linearVelocity);
            Assert.That(puckLocal.y, Is.EqualTo(match.Config.puckHeight * 0.5f).Within(0.001f));
            Assert.That(velocityLocal.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(velocityLocal.z, Is.GreaterThan(0f));
            Assert.That(Quaternion.Angle(puck.Body.rotation, tableSpace.rotation), Is.LessThan(0.1f));

            var resetRequested = false;
            puck.ResetRequired += () => resetRequested = true;
            puck.Body.position = match.Config.ToWorldPosition(
                tableSpace,
                new Vector3(
                    match.Config.HalfWidth + match.Config.outOfBoundsMargin + 0.1f,
                    match.Config.puckHeight * 0.5f,
                    0f));

            yield return new WaitForFixedUpdate();

            Assert.That(resetRequested, Is.True);
        }

        [UnityTest]
        public IEnumerator TiltedTable_GoalTriggerAwardsPlayer()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var match = Object.FindAnyObjectByType<MatchManager>();
            var puck = Object.FindAnyObjectByType<PuckController>();
            Assert.That(match, Is.Not.Null);
            Assert.That(puck, Is.Not.Null);

            var tableSpace = puck.transform.parent;
            tableSpace.SetPositionAndRotation(
                new Vector3(0.7f, match.Config.height + 0.3f, -1.1f),
                Quaternion.Euler(-7f, 28f, 10f));
            match.StartMatch(DifficultyId.Mage);
            var timeout = Time.realtimeSinceStartup + match.Config.serveDelaySeconds + 2f;
            while (match.Snapshot.Phase != MatchPhase.Playing && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            var playerScore = match.PlayerScore;
            puck.Body.position = match.Config.ToWorldPosition(
                tableSpace,
                new Vector3(
                    0f,
                    match.Config.puckHeight * 0.5f,
                    match.Config.HalfLength + match.Config.puckRadius + 0.03f));
            puck.Body.linearVelocity = match.Config.ToWorldDirection(tableSpace, Vector3.forward * 0.2f);
            Physics.SyncTransforms();

            for (var i = 0; i < 3 && match.PlayerScore == playerScore; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(match.PlayerScore, Is.EqualTo(playerScore + 1));
        }

        [UnityTest]
        public IEnumerator ObservationReset_FollowsTiltedTableAnchor()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var match = Object.FindAnyObjectByType<MatchManager>();
            var puck = Object.FindAnyObjectByType<PuckController>();
            var camera = Camera.main;
            Assert.That(match, Is.Not.Null);
            Assert.That(puck, Is.Not.Null);
            Assert.That(camera, Is.Not.Null);

            var tableSpace = puck.transform.parent;
            tableSpace.SetPositionAndRotation(
                new Vector3(2.1f, match.Config.height + 0.15f, 0.9f),
                Quaternion.Euler(8f, -36f, 5f));
            match.ResetObservationView();
            yield return null;

            var expectedPosition = tableSpace.TransformPoint(
                new Vector3(0f, 3.05f - match.Config.height, -4.65f));
            var expectedTarget = tableSpace.TransformPoint(new Vector3(0f, -0.04f, 0.22f));
            Assert.That(Vector3.Distance(camera.transform.position, expectedPosition), Is.LessThan(0.001f));
            Assert.That(Vector3.Dot(camera.transform.forward, (expectedTarget - expectedPosition).normalized),
                Is.GreaterThan(0.9999f));
        }

        [UnityTest]
        public IEnumerator Rails_BounceOnEverySideAndRecoverFromTheCorner()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var puck = Object.FindAnyObjectByType<PuckController>();
            var match = Object.FindAnyObjectByType<MatchManager>();
            Assert.That(puck, Is.Not.Null);
            Assert.That(match, Is.Not.Null);
            puck.SetSimulating(true);
            var tableSpace = puck.transform.parent;
            var config = match.Config;
            var railContactCount = 0;
            puck.RailContact += _ => railContactCount++;

            yield return AssertRailBounce(puck, config, tableSpace,
                new Vector3(config.HalfWidth - config.puckRadius - 0.01f, config.puckHeight * 0.5f, 0f),
                Vector3.right, Vector3.left);
            yield return AssertRailBounce(puck, config, tableSpace,
                new Vector3(-config.HalfWidth + config.puckRadius + 0.01f, config.puckHeight * 0.5f, 0f),
                Vector3.left, Vector3.right);
            yield return AssertRailBounce(puck, config, tableSpace,
                new Vector3(config.HalfWidth * 0.78f, config.puckHeight * 0.5f, config.HalfLength - config.puckRadius - 0.01f),
                Vector3.forward, Vector3.back);
            yield return AssertRailBounce(puck, config, tableSpace,
                new Vector3(-config.HalfWidth * 0.78f, config.puckHeight * 0.5f, -config.HalfLength + config.puckRadius + 0.01f),
                Vector3.back, Vector3.forward);

            puck.Body.position = config.ToWorldPosition(tableSpace,
                new Vector3(config.HalfWidth - config.puckRadius - 0.012f, config.puckHeight * 0.5f,
                    config.HalfLength - config.puckRadius - 0.012f));
            puck.Body.linearVelocity = config.ToWorldDirection(tableSpace, new Vector3(0.5f, 0f, 0.5f));
            Physics.SyncTransforms();
            for (var step = 0; step < 12; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            var cornerVelocity = config.ToTableDirection(tableSpace, puck.Body.linearVelocity);
            Assert.That(railContactCount, Is.GreaterThan(0));
            Assert.That(cornerVelocity.x, Is.LessThan(-config.railMinimumReboundSpeed * 0.5f));
            Assert.That(cornerVelocity.z, Is.LessThan(-config.railMinimumReboundSpeed * 0.5f));
        }

        [UnityTest]
        public IEnumerator HitEffect_RingIsHollowAndFacesAboveTheTablePlane()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var puck = Object.FindAnyObjectByType<PuckController>();
            Assert.That(puck, Is.Not.Null);
            var tableSpace = puck.transform.parent;
            var point = tableSpace.TransformPoint(new Vector3(0.1f, 0.03f, -0.2f));
            var direction = tableSpace.TransformDirection(new Vector3(0.7f, 0f, 0.7f).normalized);

            HitEvents.Raise(new HitEvent(
                point,
                tableSpace.up,
                direction,
                0.8f,
                HitSide.Player,
                true,
                HitStrength.Power));
            yield return null;

            GameObject activeRing = null;
            foreach (var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (renderer.gameObject.name == "Rune Shock Ring" && renderer.gameObject.activeInHierarchy)
                {
                    activeRing = renderer.gameObject;
                    break;
                }
            }

            Assert.That(activeRing, Is.Not.Null);
            var ringMesh = activeRing.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(ringMesh, Is.Not.Null);
            Assert.That(ringMesh.name, Is.EqualTo("Runtime Rune Shock Ring"));
            Assert.That(ringMesh.bounds.size.y, Is.LessThan(0.001f));
            Assert.That(ringMesh.vertexCount, Is.EqualTo(64));
            Assert.That(ringMesh.triangles.Length, Is.EqualTo(192));

            var vertices = ringMesh.vertices;
            var minimumRadius = float.MaxValue;
            var maximumRadius = 0f;
            foreach (var vertex in vertices)
            {
                var radius = new Vector2(vertex.x, vertex.z).magnitude;
                minimumRadius = Mathf.Min(minimumRadius, radius);
                maximumRadius = Mathf.Max(maximumRadius, radius);
            }

            Assert.That(minimumRadius, Is.EqualTo(0.36f).Within(0.001f));
            Assert.That(maximumRadius, Is.EqualTo(0.5f).Within(0.001f));

            foreach (var normal in ringMesh.normals)
            {
                Assert.That(normal, Is.EqualTo(Vector3.up));
            }

            var triangles = ringMesh.triangles;
            for (var index = 0; index < triangles.Length; index += 3)
            {
                var edgeA = vertices[triangles[index + 1]] - vertices[triangles[index]];
                var edgeB = vertices[triangles[index + 2]] - vertices[triangles[index]];
                Assert.That(Vector3.Dot(Vector3.Cross(edgeA, edgeB), Vector3.up), Is.GreaterThan(0f));
            }

            Assert.That(activeRing.transform.localScale.y, Is.LessThan(0.01f));
            Assert.That(activeRing.transform.localScale.x, Is.GreaterThan(activeRing.transform.localScale.y * 10f));
            Assert.That(Vector3.Dot(activeRing.transform.up, tableSpace.up), Is.GreaterThan(0.999f));
            yield return AssertRingRendersAsHollow(activeRing);
        }

        private static IEnumerator AssertRingRendersAsHollow(GameObject ring)
        {
            const int textureSize = 128;
            const int effectLayer = 30;
            var verificationCameraObject = new GameObject("Rune Ring Verification Camera");
            var verificationCamera = verificationCameraObject.AddComponent<Camera>();
            var renderTexture = new RenderTexture(textureSize, textureSize, 24, RenderTextureFormat.ARGB32);
            var capture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);

            ring.layer = effectLayer;
            ring.transform.localScale = Vector3.one;
            verificationCamera.clearFlags = CameraClearFlags.SolidColor;
            verificationCamera.backgroundColor = Color.black;
            verificationCamera.cullingMask = 1 << effectLayer;
            verificationCamera.orthographic = true;
            verificationCamera.orthographicSize = 0.6f;
            verificationCamera.nearClipPlane = 0.01f;
            verificationCamera.farClipPlane = 2f;
            verificationCamera.targetTexture = renderTexture;
            verificationCamera.transform.position = ring.transform.position + ring.transform.up;
            verificationCamera.transform.rotation = Quaternion.LookRotation(
                -ring.transform.up,
                ring.transform.forward);

            verificationCamera.Render();
            var previousActive = RenderTexture.active;
            RenderTexture.active = renderTexture;
            capture.ReadPixels(new Rect(0f, 0f, textureSize, textureSize), 0, 0);
            capture.Apply();
            RenderTexture.active = previousActive;

            var centerMaximum = 0f;
            var brightRingPixels = 0;
            var center = new Vector2(textureSize * 0.5f, textureSize * 0.5f);
            for (var y = 0; y < textureSize; y++)
            {
                for (var x = 0; x < textureSize; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    var pixel = capture.GetPixel(x, y);
                    var brightness = Mathf.Max(pixel.r, pixel.g, pixel.b);
                    if (distance < 28f)
                    {
                        centerMaximum = Mathf.Max(centerMaximum, brightness);
                    }
                    else if (distance > 39f && distance < 55f && brightness > 0.08f)
                    {
                        brightRingPixels++;
                    }
                }
            }

            Assert.That(centerMaximum, Is.LessThan(0.03f), "The rune ring center must remain transparent.");
            Assert.That(brightRingPixels, Is.GreaterThan(250), "The upward-facing rune band must render on Metal.");

            verificationCamera.targetTexture = null;
            renderTexture.Release();
            Object.Destroy(capture);
            Object.Destroy(renderTexture);
            Object.Destroy(verificationCameraObject);
            yield return null;
        }

        private static IEnumerator AssertRailBounce(
            PuckController puck,
            TableConfig config,
            Transform tableSpace,
            Vector3 localPosition,
            Vector3 incomingDirection,
            Vector3 expectedDirection)
        {
            puck.Body.position = config.ToWorldPosition(tableSpace, localPosition);
            puck.Body.linearVelocity = config.ToWorldDirection(tableSpace, incomingDirection * 0.65f);
            Physics.SyncTransforms();
            for (var step = 0; step < 8; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            var localVelocity = config.ToTableDirection(tableSpace, puck.Body.linearVelocity);
            Assert.That(Vector3.Dot(localVelocity, expectedDirection),
                Is.GreaterThan(config.railMinimumReboundSpeed * 0.5f));
        }

        private static void AssertDiscCollider(GameObject owner, float radius, float height, Vector3 expectedCenter)
        {
            Assert.That(owner.GetComponent<CapsuleCollider>(), Is.Null);
            Assert.That(owner.GetComponent<BoxCollider>(), Is.Null);
            var collider = owner.GetComponent<MeshCollider>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.convex, Is.True);
            Assert.That(collider.sharedMesh, Is.Not.Null);
            var bounds = collider.sharedMesh.bounds;
            Assert.That(Vector3.Distance(bounds.center, expectedCenter), Is.LessThan(0.0001f));
            Assert.That(bounds.size.x, Is.EqualTo(radius * 2f).Within(0.0001f));
            Assert.That(bounds.size.y, Is.EqualTo(height).Within(0.0001f));
            Assert.That(bounds.size.z, Is.EqualTo(radius * 2f).Within(0.0001f));
        }
    }
}
