using System;
using UnityEngine;

namespace ArcanePuck
{
    public enum DifficultyId
    {
        Apprentice,
        Mage,
        Archmage
    }

    [Serializable]
    public struct AITuning
    {
        public string label;
        public float reactionSeconds;
        public float maxSpeed;
        public float positionError;
        public float predictionSeconds;
        public float defenseDepth;
        public float aggression;
        public float bankShotChance;

        public AITuning(
            string label,
            float reactionSeconds,
            float maxSpeed,
            float positionError,
            float predictionSeconds,
            float defenseDepth,
            float aggression,
            float bankShotChance)
        {
            this.label = label;
            this.reactionSeconds = reactionSeconds;
            this.maxSpeed = maxSpeed;
            this.positionError = positionError;
            this.predictionSeconds = predictionSeconds;
            this.defenseDepth = defenseDepth;
            this.aggression = aggression;
            this.bankShotChance = bankShotChance;
        }
    }

    [CreateAssetMenu(menuName = "Arcane Puck/Table Config", fileName = "TableConfig")]
    public sealed class TableConfig : ScriptableObject
    {
        [Header("Simulation")]
        [Min(30)] public int fixedStepsPerSecond = 90;
        [Min(1)] public int solverIterations = 8;

        [Header("Table (metres)")]
        public float width = 2.2f;
        public float length = 3.6f;
        public float height = 0.82f;
        public float wallThickness = 0.085f;
        public float wallHeight = 0.14f;
        public float goalWidth = 0.82f;
        public float goalHeight = 0.44f;
        public float goalDepth = 0.42f;

        [Header("Puck")]
        public float puckRadius = 0.092f;
        public float puckHeight = 0.052f;
        public float puckMass = 0.17f;
        public float puckLinearDamping = 0.16f;
        public float puckAngularDamping = 0.9f;
        public float puckRestitution = 0.93f;
        public float puckFriction = 0.02f;
        public float puckMaxSpeed = 5.6f;
        public float puckResetSpeedThreshold = 0.075f;
        public float stationaryResetSeconds = 4f;
        public float outOfBoundsMargin = 0.55f;
        public float serveSpeed = 1.05f;

        [Header("Rail response")]
        [Range(0f, 1f)] public float railFriction = 0f;
        [Range(0f, 1f)] public float railRestitution = 1f;
        [Min(0f)] public float railBounceThreshold = 0.05f;
        [Min(0f)] public float railMinimumReboundSpeed = 0.42f;
        [Min(0f)] public float railRecoveryCooldownSeconds = 0.06f;

        [Header("Mallet")]
        public float malletRadius = 0.145f;
        public float malletHeight = 0.105f;
        public float malletRestitution = 0.72f;
        public float malletFriction = 0.04f;
        public float maxDesktopSpeed = 3.6f;
        public float maxDesktopAcceleration = 28f;
        public float maxVrSpeed = 4.5f;
        public float maxVrAcceleration = 36f;
        public float chargeSeconds = 1.15f;
        public float chargeImpulse = 0.62f;
        public float hitCooldownSeconds = 0.12f;

        [Header("Match")]
        public int winScore = 7;
        public int goldenGoalAt = 6;
        public float serveDelaySeconds = 1.05f;

        [Header("AI")]
        public AITuning apprentice = new(
            "学徒", 0.28f, 1.45f, 0.2f, 0.14f, 0.67f, 0.42f, 0.04f);
        public AITuning mage = new(
            "法师", 0.12f, 2.05f, 0.075f, 0.48f, 0.61f, 0.67f, 0.16f);
        public AITuning archmage = new(
            "大法师", 0.055f, 2.7f, 0.025f, 0.92f, 0.57f, 0.88f, 0.48f);

        public float FixedDeltaTime => 1f / fixedStepsPerSecond;
        public float HalfWidth => width * 0.5f;
        public float HalfLength => length * 0.5f;
        public float MalletLimitX => HalfWidth - malletRadius - 0.025f;
        public float MalletLimitZ => HalfLength - malletRadius - 0.04f;
        public float MiddleClearance => malletRadius + 0.018f;
        public float GoalCenterLimitX => goalWidth * 0.5f - puckRadius;

        public AITuning GetAI(DifficultyId difficulty)
        {
            return difficulty switch
            {
                DifficultyId.Apprentice => apprentice,
                DifficultyId.Archmage => archmage,
                _ => mage
            };
        }

        public static TableConfig CreateRuntimeDefault()
        {
            var config = CreateInstance<TableConfig>();
            config.name = "Runtime Table Config";
            return config;
        }

        public Vector3 ClampMalletTarget(Vector3 target, bool playerSide)
        {
            target.x = Mathf.Clamp(target.x, -MalletLimitX, MalletLimitX);
            target.y = 0f;
            target.z = playerSide
                ? Mathf.Clamp(target.z, -MalletLimitZ, -MiddleClearance)
                : Mathf.Clamp(target.z, MiddleClearance, MalletLimitZ);
            return target;
        }

        public Vector3 ToWorldPosition(Transform tableSpace, Vector3 tableLocalPosition)
        {
            return tableSpace != null
                ? tableSpace.TransformPoint(tableLocalPosition)
                : tableLocalPosition;
        }

        public Vector3 ToTablePosition(Transform tableSpace, Vector3 worldPosition)
        {
            return tableSpace != null
                ? tableSpace.InverseTransformPoint(worldPosition)
                : worldPosition;
        }

        public Vector3 ToWorldDirection(Transform tableSpace, Vector3 tableLocalDirection)
        {
            return tableSpace != null
                ? tableSpace.TransformDirection(tableLocalDirection)
                : tableLocalDirection;
        }

        public Vector3 ToTableDirection(Transform tableSpace, Vector3 worldDirection)
        {
            return tableSpace != null
                ? tableSpace.InverseTransformDirection(worldDirection)
                : worldDirection;
        }

        public Vector3 ProjectWorldDirectionToTable(Transform tableSpace, Vector3 worldDirection)
        {
            var tableLocalDirection = ToTableDirection(tableSpace, worldDirection);
            tableLocalDirection.y = 0f;
            return ToWorldDirection(tableSpace, tableLocalDirection);
        }
    }
}
