using System;
using UnityEngine;

namespace ArcanePuck
{
    public enum HitSide
    {
        Player,
        AI
    }

    public enum HitStrength
    {
        Light,
        Normal,
        Power
    }

    public readonly struct HitEvent
    {
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        public readonly Vector3 Direction;
        public readonly float Impulse;
        public readonly HitSide Side;
        public readonly bool Charged;
        public readonly HitStrength Strength;

        public HitEvent(
            Vector3 point,
            Vector3 normal,
            Vector3 direction,
            float impulse,
            HitSide side,
            bool charged,
            HitStrength strength)
        {
            Point = point;
            Normal = normal;
            Direction = direction;
            Impulse = impulse;
            Side = side;
            Charged = charged;
            Strength = strength;
        }
    }

    public static class HitEvents
    {
        public static event Action<HitEvent> Hit;

        public static void Raise(in HitEvent hitEvent)
        {
            Hit?.Invoke(hitEvent);
        }

        public static void ClearForTests()
        {
            Hit = null;
        }
    }
}
