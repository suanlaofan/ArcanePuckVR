#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ArcanePuck.Tests
{
    /// <summary>
    /// Exercises the real VR bootstrap, match states, input mapping and rigidbody
    /// using deterministic Editor tracking. This verifies Unity integration; it
    /// does not measure a PICO headset's tracking latency or physical comfort.
    /// </summary>
    public sealed class VRControlPlayModeTests
    {
        private const string ScenePath = "Assets/ArcanePuck/Scenes/ArcanePuck.unity";
        private static readonly Vector3 NeutralHand = new(0.25f, 1.20f, 0.35f);
        private static readonly string[] PreferenceKeys =
        {
            "ArcanePuck.VR.Sensitivity", "ArcanePuck.VR.StartDistance",
            "ArcanePuck.VR.LeftHanded", "ArcanePuck.VR.Seated"
        };

        private readonly bool[] savedKeyExists = new bool[4];
        private readonly int[] savedIntValues = new int[4];
        private float savedDistance;
        private bool capturedPreferences;
        private bool savedEditorPreview;
        private bool savedControllerSimulation;
        private MatchManager match;
        private PicoMalletInput input;
        private PlayerMalletController player;
        private Rigidbody playerBody;
        private PuckController puck;
        private GameHUD hud;

        [UnitySetUp]
        public IEnumerator LoadVRPreview()
        {
            for (var i = 0; i < PreferenceKeys.Length; i++)
            {
                savedKeyExists[i] = PlayerPrefs.HasKey(PreferenceKeys[i]);
                if (i == 1) savedDistance = PlayerPrefs.GetFloat(PreferenceKeys[i]);
                else savedIntValues[i] = PlayerPrefs.GetInt(PreferenceKeys[i]);
            }
            capturedPreferences = true;
            savedEditorPreview = PicoXRRuntime.EditorPreviewEnabled;
            savedControllerSimulation = PicoXRRuntime.EditorSimulateController;
            PicoXRRuntime.EditorPreviewEnabled = true;
            PicoXRRuntime.EditorSimulateController = true;
            PicoXRRuntime.EditorControllerPosition = NeutralHand;
            PicoXRRuntime.EditorControllerTracked = true;
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            match = Object.FindAnyObjectByType<MatchManager>();
            input = Object.FindAnyObjectByType<PicoMalletInput>();
            player = Object.FindAnyObjectByType<PlayerMalletController>();
            puck = Object.FindAnyObjectByType<PuckController>();
            hud = Object.FindAnyObjectByType<GameHUD>();
            Assert.That(match, Is.Not.Null);
            Assert.That(input, Is.Not.Null);
            Assert.That(player, Is.Not.Null);
            Assert.That(puck, Is.Not.Null);
            Assert.That(hud, Is.Not.Null);
            playerBody = player.GetComponent<Rigidbody>();
            Assert.That(PicoXRRuntime.IsEditorPreview, Is.True);
            Assert.That(PicoXRRuntime.Controls, Is.Not.Null);
            Assert.That(PicoXRRuntime.Calibration, Is.Not.Null);
            yield return WaitForReady();

            // The Editor preview config is transient and never writes PlayerPrefs.
            PicoXRRuntime.Controls.SetSensitivityPreset(1);
            Assert.That(PicoXRRuntime.Controls.GainX, Is.EqualTo(2.5f));
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Idle));
        }

        [TearDown]
        public void LeavePreviewAndRestorePreferences()
        {
            // NUnit invokes TearDown after assertion failures as well. Never leave
            // synthetic VR input enabled for another fixture or the user's Editor.
            PicoXRRuntime.EditorPreviewEnabled = savedEditorPreview;
            PicoXRRuntime.EditorSimulateController = savedControllerSimulation;
            PicoXRRuntime.EditorControllerTracked = true;
            PicoXRRuntime.EditorControllerPosition = NeutralHand;
            if (!capturedPreferences) return;
            var changed = false;
            for (var i = 0; i < PreferenceKeys.Length; i++)
            {
                var key = PreferenceKeys[i];
                if (!savedKeyExists[i])
                {
                    if (PlayerPrefs.HasKey(key))
                    {
                        PlayerPrefs.DeleteKey(key);
                        changed = true;
                    }
                }
                else if (i == 1)
                {
                    if (!PlayerPrefs.HasKey(key) || PlayerPrefs.GetFloat(key) != savedDistance)
                    {
                        PlayerPrefs.SetFloat(key, savedDistance);
                        changed = true;
                    }
                }
                else if (!PlayerPrefs.HasKey(key) || PlayerPrefs.GetInt(key) != savedIntValues[i])
                {
                    PlayerPrefs.SetInt(key, savedIntValues[i]);
                    changed = true;
                }
            }
            if (changed) PlayerPrefs.Save();
            capturedPreferences = false;
        }

        [UnityTest]
        public IEnumerator StandardGain_MovesPhysicalMalletAndRoundResetKeepsNeutralPosition()
        {
            AssertHeadDistance(0.45f);
            match.StartMatch(DifficultyId.Mage);
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Serving));
            var neutralMallet = VRMalletMapping.Center(match.Config);
            AssertLocalPosition(neutralMallet);
            Assert.That(Vector3.Distance(input.TargetPosition, neutralMallet), Is.LessThan(0.001f));
            yield return WaitForPhase(MatchPhase.Playing);
            // The puck is not under test here; freezing it isolates the measured
            // controller-to-rigidbody displacement from unrelated goals.
            puck.SetSimulating(false);
            yield return WaitForBody(neutralMallet);

            PicoXRRuntime.EditorControllerPosition = NeutralHand + Vector3.right * 0.10f;
            yield return WaitForBody(neutralMallet + Vector3.right * 0.25f);
            PicoXRRuntime.EditorControllerPosition = NeutralHand - Vector3.right * 0.10f;
            yield return WaitForBody(neutralMallet - Vector3.right * 0.25f);
            PicoXRRuntime.EditorControllerPosition = NeutralHand + Vector3.forward * 0.10f;
            yield return WaitForBody(neutralMallet + Vector3.forward * 0.25f);
            PicoXRRuntime.EditorControllerPosition = NeutralHand - Vector3.forward * 0.10f;
            yield return WaitForBody(neutralMallet - Vector3.forward * 0.25f);

            match.ReportGoal(MatchSide.Player);
            Assert.That(match.PlayerScore, Is.EqualTo(1));
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Serving));
            AssertLocalPosition(neutralMallet);
            Assert.That(Vector3.Distance(input.TargetPosition, neutralMallet), Is.LessThan(0.001f));
            yield return WaitForPhase(MatchPhase.Playing);
            yield return WaitForBody(neutralMallet);
        }

        [UnityTest]
        public IEnumerator FastControllerSweep_HitsPuckWithoutPassingThrough()
        {
            match.StartMatch(DifficultyId.Mage);
            yield return WaitForPhase(MatchPhase.Playing);
            puck.SetSimulating(false);
            var center = VRMalletMapping.Center(match.Config);
            PicoXRRuntime.EditorControllerPosition = NeutralHand - Vector3.forward * 0.12f;
            yield return WaitForBody(center - Vector3.forward * 0.30f);
            puck.Body.position = match.Config.ToWorldPosition(player.transform.parent,
                new Vector3(center.x, match.Config.puckHeight * 0.5f, center.z));
            puck.SetSimulating(true);
            puck.Body.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();

            // A 24 cm hand sweep becomes a 60 cm physics-proxy sweep, crossing
            // the entire puck between poses and exercising continuous collision.
            PicoXRRuntime.EditorControllerPosition = NeutralHand + Vector3.forward * 0.12f;
            var hit = false;
            for (var i = 0; i < 12; i++)
            {
                yield return new WaitForFixedUpdate();
                var velocity = match.Config.ToTableDirection(player.transform.parent, puck.Body.linearVelocity);
                if (velocity.z > 0.1f) { hit = true; break; }
            }
            Assert.That(hit, Is.True, "Fast tracked movement must contact the puck along its swept path.");
        }

        [UnityTest]
        public IEnumerator TrackingLoss_PausesAndRequiresManualResumeWithoutMalletJump()
        {
            match.StartMatch(DifficultyId.Mage);
            yield return WaitForPhase(MatchPhase.Playing);
            puck.ResetPuck(true, 0.15f);
            PicoXRRuntime.EditorControllerPosition = NeutralHand + Vector3.right * 0.10f;
            yield return WaitForBody(VRMalletMapping.Center(match.Config) + Vector3.right * 0.25f);
            var heldPosition = LocalPosition;
            PicoXRRuntime.EditorControllerTracked = false;
            yield return WaitForPhase(MatchPhase.Paused);
            Assert.That(input.HasUsableTracking, Is.False);
            Assert.That(puck.Body.isKinematic, Is.True);
            AssertLocalPosition(heldPosition);

            match.ResumeMatch();
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Paused), "Missing tracking must block resume.");
            var recoveredHand = NeutralHand + new Vector3(-0.50f, 0f, 0.55f);
            PicoXRRuntime.EditorControllerPosition = recoveredHand;
            PicoXRRuntime.EditorControllerTracked = true;
            yield return WaitForReady();
            yield return new WaitForSecondsRealtime(0.20f);
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Paused), "Tracking recovery must not resume automatically.");
            AssertLocalPosition(heldPosition);

            hud.HandlePicoMenuNavigation(Vector2.zero, true);
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Playing));
            Assert.That(puck.Body.isKinematic, Is.False);
            yield return WaitForBody(heldPosition);
            PicoXRRuntime.EditorControllerPosition = recoveredHand + Vector3.right * 0.10f;
            yield return WaitForBody(heldPosition + Vector3.right * 0.25f);
        }

        [UnityTest]
        public IEnumerator TrackingLossDuringServe_HoldsCountdownUntilManualResume()
        {
            match.StartMatch(DifficultyId.Mage);
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Serving));
            PicoXRRuntime.EditorControllerTracked = false;
            yield return WaitForPhase(MatchPhase.Paused);
            yield return new WaitForSecondsRealtime(match.Config.serveDelaySeconds + 0.1f);
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Paused));
            Assert.That(puck.Body.isKinematic, Is.True);
            PicoXRRuntime.EditorControllerTracked = true;
            yield return WaitForReady();
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Paused));
            match.ResumeMatch();
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Serving));
            yield return WaitForPhase(MatchPhase.Playing);
        }

        [UnityTest]
        public IEnumerator MenuNavigation_AdjustsSettingsAndRepositionsTrackedHead()
        {
            // Menu order: start, difficulty, sensitivity, distance, hand, posture, recenter.
            hud.HandlePicoMenuNavigation(Vector2.down, false);
            hud.HandlePicoMenuNavigation(Vector2.down, false);
            hud.HandlePicoMenuNavigation(Vector2.right, false);
            Assert.That(PicoXRRuntime.Controls.GainX, Is.EqualTo(3f));
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Idle));
            hud.HandlePicoMenuNavigation(Vector2.left, false);
            Assert.That(PicoXRRuntime.Controls.GainX, Is.EqualTo(2.5f));

            hud.HandlePicoMenuNavigation(Vector2.down, false);
            hud.HandlePicoMenuNavigation(Vector2.right, false);
            Assert.That(PicoXRRuntime.Controls.StartDistance, Is.EqualTo(0.65f).Within(0.001f));
            Assert.That(PicoXRRuntime.Calibration.IsCalibrating, Is.True);
            yield return WaitForReady();
            AssertHeadDistance(0.65f);

            hud.HandlePicoMenuNavigation(Vector2.down, false);
            hud.HandlePicoMenuNavigation(Vector2.zero, true);
            Assert.That(PicoXRRuntime.Controls.LeftHanded, Is.True);
            Camera.main.transform.localPosition = new Vector3(0f, 1.10f, 0f);
            hud.HandlePicoMenuNavigation(Vector2.down, false);
            hud.HandlePicoMenuNavigation(Vector2.right, false);
            Assert.That(PicoXRRuntime.Controls.Seated, Is.True);
            yield return WaitForReady();
            Assert.That(player.transform.parent.position.y, Is.EqualTo(0.60f).Within(0.01f));

            var head = Camera.main.transform;
            head.localPosition += new Vector3(0.30f, 0f, 0.25f);
            hud.HandlePicoMenuNavigation(Vector2.down, false);
            hud.HandlePicoMenuNavigation(Vector2.zero, true);
            yield return WaitForReady();
            AssertHeadDistance(0.65f);
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Idle));
            AssertPreferencesUnchanged();

            for (var i = 0; i < 6; i++) hud.HandlePicoMenuNavigation(Vector2.up, false);
            hud.HandlePicoMenuNavigation(Vector2.zero, true);
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Serving));
        }

        private Vector3 LocalPosition => match.Config.ToTablePosition(player.transform.parent, playerBody.position);

        private IEnumerator WaitForReady()
        {
            yield return WaitUntil(() => PicoXRRuntime.Calibration != null &&
                                        PicoXRRuntime.Calibration.IsReady && !PicoXRRuntime.Calibration.IsCalibrating &&
                                        input != null && input.HasUsableTracking, 4f, "VR calibration and tracked input did not become ready.");
        }

        private IEnumerator WaitForPhase(MatchPhase phase)
        {
            yield return WaitUntil(() => match.Snapshot.Phase == phase,
                match.Config.serveDelaySeconds + 3f, $"Match did not enter {phase}; actual {match.Snapshot.Phase}.");
        }

        private IEnumerator WaitForBody(Vector3 expected)
        {
            yield return WaitUntil(() => Vector3.Distance(LocalPosition, expected) < 0.008f,
                1.5f, $"VR mallet did not follow the tracked target {expected}.");
            AssertLocalPosition(expected);
            Assert.That(Vector3.Distance(input.TargetPosition, expected), Is.LessThan(0.008f));
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string failure)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, failure);
        }

        private void AssertLocalPosition(Vector3 expected)
        {
            Assert.That(Vector3.Distance(LocalPosition, expected), Is.LessThan(0.008f),
                $"Expected table-local mallet {expected}; actual {LocalPosition}.");
        }

        private void AssertHeadDistance(float expected)
        {
            var table = player.transform.parent;
            var nearEdge = table.TransformPoint(new Vector3(0f, 0f, -match.Config.HalfLength));
            var expectedHead = nearEdge - Vector3.ProjectOnPlane(table.forward, Vector3.up).normalized * expected;
            var head = Camera.main.transform.position;
            Assert.That(Vector2.Distance(new Vector2(head.x, head.z), new Vector2(expectedHead.x, expectedHead.z)),
                Is.LessThan(0.01f), "The actual HMD must be centred at the selected distance from the near edge.");
        }

        private void AssertPreferencesUnchanged()
        {
            for (var i = 0; i < PreferenceKeys.Length; i++)
            {
                Assert.That(PlayerPrefs.HasKey(PreferenceKeys[i]), Is.EqualTo(savedKeyExists[i]));
                if (!savedKeyExists[i]) continue;
                if (i == 1) Assert.That(PlayerPrefs.GetFloat(PreferenceKeys[i]), Is.EqualTo(savedDistance));
                else Assert.That(PlayerPrefs.GetInt(PreferenceKeys[i]), Is.EqualTo(savedIntValues[i]));
            }
        }
    }
}
#endif
