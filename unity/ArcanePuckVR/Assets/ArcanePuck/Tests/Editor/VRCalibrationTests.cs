using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR;

namespace ArcanePuck.Tests
{
    public sealed class VRCalibrationTests
    {
        [TestCase("swan_oversea", "ranchu", "Pico/swan/pico_spaceos_oversea_emulator_arm64:16", true)]
        [TestCase("swan_oversea", "ranchu", "", true)]
        [TestCase("old_emulator_arm64", "", "", true)]
        [TestCase("swan_oversea", "qcom", "Pico/swan/pico_emulator_arm64:16", true)]
        [TestCase("swan_oversea", "qcom", "Pico/swan/device:16", false)]
        [TestCase(null, null, null, false)]
        public void EmulatorIdentity_DistinguishesCurrentImageFromHeadset(string product, string hardware,
            string fingerprint, bool expected)
        {
            Assert.That(PicoXRRuntime.IsEmulatorIdentity(product, hardware, fingerprint), Is.EqualTo(expected));
        }

        [Test]
        public void OffsetHead_IsPlacedExactlyAtNearEdgeDistance()
        {
            var head = new Pose(new Vector3(0.7f, 1.72f, -0.35f), Quaternion.Euler(25f, 37f, 6f));
            var nearEdge = new Vector3(2.4f, 0.82f, -3.1f);
            var tableForward = Quaternion.Euler(0f, 71f, 0f) * Vector3.forward;
            var origin = VRCalibrationGeometry.CalculateOriginPose(head, Vector3.zero,
                Vector3.zero, nearEdge, tableForward, 0.45f);
            var finalHead = origin.position + origin.rotation * head.position;
            var expected = nearEdge - tableForward * 0.45f;

            Assert.That(finalHead.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(finalHead.z, Is.EqualTo(expected.z).Within(0.0001f));
            Assert.That(finalHead.y, Is.EqualTo(head.position.y).Within(0.0001f));
            Assert.That(Vector3.Angle(origin.rotation * Vector3.up, Vector3.up), Is.LessThan(0.001f));
            var finalForward = Vector3.ProjectOnPlane(origin.rotation * head.rotation * Vector3.forward, Vector3.up);
            Assert.That(Vector3.Angle(finalForward, tableForward), Is.LessThan(0.01f));
        }

        [TestCase(0.30f)]
        [TestCase(0.45f)]
        [TestCase(0.65f)]
        public void RepeatedRecenter_DoesNotAccumulateOffset(float distance)
        {
            var head = new Pose(new Vector3(-0.3f, 0.1f, 0.6f), Quaternion.Euler(0f, -55f, 0f));
            var offset = Vector3.up * 1.65f;
            var nearEdge = new Vector3(0f, 0.82f, -1.8f);
            var first = VRCalibrationGeometry.CalculateOriginPose(head, offset,
                Vector3.zero, nearEdge, Vector3.forward, distance);
            var second = VRCalibrationGeometry.CalculateOriginPose(head, offset,
                first.position, nearEdge, Vector3.forward, distance);

            Assert.That(Vector3.Distance(first.position, second.position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(first.rotation, second.rotation), Is.LessThan(0.001f));
            var finalHead = second.position + second.rotation * (offset + head.position);
            Assert.That(finalHead.z, Is.EqualTo(-1.8f - distance).Within(0.0001f));
            Assert.That(finalHead.y, Is.EqualTo(1.75f).Within(0.0001f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FloorOrigin_NeverAddsEyeHeightTwice(bool seated)
        {
            Assert.That(VRCalibrationGeometry.TrackingHeightOffset(TrackingOriginModeFlags.Floor, seated), Is.Zero);
        }

        [Test]
        public void DeviceOrigin_AddsOneSharedOffset()
        {
            Assert.That(VRCalibrationGeometry.TrackingHeightOffset(TrackingOriginModeFlags.Device, false), Is.EqualTo(1.65f));
            Assert.That(VRCalibrationGeometry.TrackingHeightOffset(TrackingOriginModeFlags.Device, true), Is.EqualTo(1.20f));
        }

        [TestCase(1.20f, 0.70f)]
        [TestCase(1.60f, 0.82f)]
        [TestCase(0.70f, 0.45f)]
        public void SeatedTable_UsesReachableHeightWithoutRaisingAboveStanding(float eyeHeight, float expected)
        {
            Assert.That(VRCalibrationGeometry.SeatedTableHeight(eyeHeight, 0.82f), Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void Defaults_AreStandardGainAndNearTableDistance()
        {
            var settings = ScriptableObject.CreateInstance<VRControlConfig>();
            try
            {
                Assert.That(settings.SensitivityPreset, Is.EqualTo(1));
                Assert.That(settings.GainX, Is.EqualTo(2.5f));
                Assert.That(settings.GainZ, Is.EqualTo(2.5f));
                Assert.That(settings.StartDistance, Is.EqualTo(0.45f));
                Assert.That(settings.LeftHanded, Is.False);
                Assert.That(settings.Seated, Is.False);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [TestCase(-10f, 0.30f)]
        [TestCase(10f, 0.65f)]
        [TestCase(0.45f, 0.45f)]
        [TestCase(float.NaN, 0.45f)]
        [TestCase(float.PositiveInfinity, 0.45f)]
        public void InvalidDistance_IsContained(float value, float expected)
        {
            Assert.That(VRControlConfig.NormalizeStartDistance(value), Is.EqualTo(expected));
        }
    }
}
