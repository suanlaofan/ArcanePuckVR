using NUnit.Framework;
using UnityEngine;

namespace ArcanePuck.Tests
{
    public sealed class VRMalletMappingTests
    {
        private TableConfig config;
        [SetUp] public void SetUp() => config = TableConfig.CreateRuntimeDefault();
        [TearDown] public void TearDown() => Object.DestroyImmediate(config);

        [TestCase(2f, .2f)]
        [TestCase(2.5f, .25f)]
        [TestCase(3f, .3f)]
        public void TenCentimetreMotion_IsRelativeToNaturalHandPosition(float gain, float expected)
        {
            var mapping = new VRMalletMapping();
            var neutral = new Vector3(7.2f, 1.3f, -4.1f);
            var center = VRMalletMapping.Center(config);
            Assert.That(mapping.Map(neutral, center, config, gain, gain), Is.EqualTo(center));
            var moved = mapping.Map(neutral + new Vector3(.1f, 1f, .1f), center, config, gain, gain);
            Assert.That(moved.x, Is.EqualTo(expected).Within(.00001f));
            Assert.That(moved.z - center.z, Is.EqualTo(expected).Within(.00001f));
            Assert.That(moved.y, Is.Zero);
        }

        [TestCase(1, 0)] [TestCase(-1, 0)] [TestCase(0, 1)] [TestCase(0, -1)]
        public void ReversingAfterLargeRailOvershoot_HasNoDeadTravel(int x, int z)
        {
            var mapping = new VRMalletMapping();
            var center = VRMalletMapping.Center(config);
            mapping.Anchor(Vector3.zero, center);
            var outside = new Vector3(x * 10f, 0f, z * 10f);
            var rail = mapping.Map(outside, center, config, 2.5f, 2.5f);
            var inward = mapping.Map(outside - new Vector3(x * .01f, 0f, z * .01f), rail, config, 2.5f, 2.5f);
            Assert.That((inward - rail).magnitude, Is.EqualTo(.025f).Within(.00001f));
            Assert.That(config.ClampMalletTarget(inward, true), Is.EqualTo(inward));
        }

        [Test]
        public void ReanchorAfterHandSwitchOrRecenter_PreservesCurrentMallet()
        {
            var mapping = new VRMalletMapping();
            var current = new Vector3(.3f, 0f, -.5f);
            mapping.Anchor(Vector3.one, current);
            mapping.Clear();
            var newHand = new Vector3(-10f, 3f, 20f);
            Assert.That(mapping.Map(newHand, current, config, 3f, 3f), Is.EqualTo(current));
            var moved = mapping.Map(newHand + Vector3.right * .1f, current, config, 3f, 3f);
            Assert.That(moved.x, Is.EqualTo(.6f).Within(.00001f));
        }

        [Test]
        public void StandardGain_CoversBothHalfTableAxesWithPlannedHandTravel()
        {
            var mapping = new VRMalletMapping();
            var center = VRMalletMapping.Center(config);
            mapping.Anchor(Vector3.zero, center);
            var farRight = mapping.Map(new Vector3(.372f, 0f, .2904f), center, config, 2.5f, 2.5f);
            Assert.That(farRight.x, Is.EqualTo(config.MalletLimitX).Within(.0001f));
            Assert.That(farRight.z, Is.EqualTo(-config.MiddleClearance).Within(.0001f));
            var nearLeft = mapping.Map(new Vector3(-.372f, 0f, -.2904f), center, config, 2.5f, 2.5f);
            Assert.That(nearLeft.x, Is.EqualTo(-config.MalletLimitX).Within(.0001f));
            Assert.That(nearLeft.z, Is.EqualTo(-config.MalletLimitZ).Within(.0001f));
        }
    }
}
