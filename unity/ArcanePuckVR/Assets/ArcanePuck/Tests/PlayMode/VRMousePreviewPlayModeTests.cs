#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ArcanePuck.Tests
{
    public sealed class VRMousePreviewPlayModeTests
    {
        private Mouse mouse;
        private bool savedPreview;
        private bool savedSimulation;
        private MatchManager match;
        private PicoMalletInput input;
        private PlayerMalletController player;
        private Rigidbody body;
        private PuckController puck;

        [UnitySetUp]
        public IEnumerator LoadMousePreview()
        {
            savedPreview = PicoXRRuntime.EditorPreviewEnabled;
            savedSimulation = PicoXRRuntime.EditorSimulateController;
            PicoXRRuntime.EditorPreviewEnabled = true;
            PicoXRRuntime.EditorSimulateController = false;
            mouse = InputSystem.AddDevice<Mouse>();
            mouse.MakeCurrent();
            yield return SceneManager.LoadSceneAsync("Assets/ArcanePuck/Scenes/ArcanePuck.unity");
            match = Object.FindAnyObjectByType<MatchManager>();
            input = Object.FindAnyObjectByType<PicoMalletInput>();
            player = Object.FindAnyObjectByType<PlayerMalletController>();
            puck = Object.FindAnyObjectByType<PuckController>();
            body = player.GetComponent<Rigidbody>();
            yield return WaitFor(() => PicoXRRuntime.Calibration.IsReady &&
                !PicoXRRuntime.Calibration.IsCalibrating && input.HasUsableTracking);
            Assert.That(PicoXRRuntime.IsEditorMousePreview, Is.True);
            Assert.That(input.IsUsingTrackedController, Is.False,
                "The interactive preview must not select a stationary synthetic controller.");
            Assert.That(input.IsUsingDirectPicoInput, Is.True);
        }

        [TearDown]
        public void RestoreEditorInput()
        {
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            PicoXRRuntime.EditorPreviewEnabled = savedPreview;
            PicoXRRuntime.EditorSimulateController = savedSimulation;
        }

        [UnityTest]
        public IEnumerator PointerMovementAndLeftButton_DriveMalletAndCharge()
        {
            match.StartMatch(DifficultyId.Mage);
            yield return WaitFor(() => match.Snapshot.Phase == MatchPhase.Playing);
            puck.SetSimulating(false);
            var first = new Vector3(-0.40f, 0f, -1.10f);
            var second = new Vector3(0.40f, 0f, -0.65f);
            yield return MovePointer(TableScreenPoint(first));
            yield return WaitForBody(first);
            Assert.That(input.Charge01, Is.Zero, "Moving the pointer must not require a held button.");
            yield return MovePointer(TableScreenPoint(second));
            yield return WaitForBody(second);

            yield return MovePointer(TableScreenPoint(second), true);
            yield return new WaitForSecondsRealtime(0.20f);
            Assert.That(input.IsCharging, Is.True);
            Assert.That(input.Charge01, Is.GreaterThan(0f));
            yield return MovePointer(TableScreenPoint(second));
            Assert.That(player.ConsumePendingCharge(out var charge), Is.True);
            Assert.That(charge, Is.GreaterThan(0f));

            match.PauseMatch();
            yield return MovePointer(TableScreenPoint(first));
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Paused));
            Assert.That(Vector3.Distance(LocalPosition, second), Is.LessThan(0.03f));
            match.ResumeMatch();
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Playing));
            yield return WaitForBody(first);
        }

        [UnityTest]
        public IEnumerator MouseClicks_AdjustSensitivityAndStartFromVRMenu()
        {
            Assert.That(PicoXRRuntime.Controls.GainX, Is.EqualTo(2.5f));
            yield return ClickButton("球槌灵敏度");
            Assert.That(PicoXRRuntime.Controls.GainX, Is.EqualTo(3f));
            Assert.That(match.Snapshot.Phase, Is.EqualTo(MatchPhase.Idle));
            Assert.That(FindButton("距桌边距离"), Is.Not.Null);
            Assert.That(FindButton("游玩姿势"), Is.Not.Null);
            yield return ClickButton("开始对局");
            yield return WaitFor(() => match.Snapshot.Phase == MatchPhase.Playing);
            Assert.That(input.HasUsableTracking, Is.True);
        }

        private Vector3 LocalPosition => match.Config.ToTablePosition(player.transform.parent, body.position);

        private Vector2 TableScreenPoint(Vector3 local)
        {
            return Camera.main.WorldToScreenPoint(match.Config.ToWorldPosition(player.transform.parent, local));
        }

        private IEnumerator MovePointer(Vector2 position, bool pressed = false)
        {
            InputSystem.QueueStateEvent(mouse,
                new MouseState { position = position }.WithButton(MouseButton.Left, pressed));
            yield return null;
            yield return null;
        }

        private IEnumerator ClickButton(string labelPrefix)
        {
            var button = FindButton(labelPrefix);
            Assert.That(button, Is.Not.Null, labelPrefix);
            var rect = button.GetComponent<RectTransform>();
            var point = (Vector2)Camera.main.WorldToScreenPoint(rect.TransformPoint(rect.rect.center));
            yield return MovePointer(point);
            yield return MovePointer(point, true);
            yield return MovePointer(point);
        }

        private static Button FindButton(string prefix)
        {
            foreach (var text in Object.FindObjectsByType<Text>(FindObjectsSortMode.None))
                if (text.isActiveAndEnabled && text.text.StartsWith(prefix, StringComparison.Ordinal))
                    return text.GetComponentInParent<Button>();
            return null;
        }

        private IEnumerator WaitForBody(Vector3 expected)
        {
            yield return WaitFor(() => Vector3.Distance(LocalPosition, expected) < 0.03f);
        }

        private static IEnumerator WaitFor(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + 4f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "Preview state or physical mallet did not reach the expected state.");
        }
    }
}
#endif
