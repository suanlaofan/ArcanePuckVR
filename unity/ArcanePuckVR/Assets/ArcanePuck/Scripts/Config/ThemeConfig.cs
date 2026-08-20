using UnityEngine;

namespace ArcanePuck
{
    [CreateAssetMenu(menuName = "Arcane Puck/Theme Config", fileName = "ThemeConfig")]
    public sealed class ThemeConfig : ScriptableObject
    {
        public const string TableModelSlot = "TABLE_MODEL";
        public const string MalletModelSlot = "MALLET_MODEL";
        public const string PlayerMalletModelSlot = "PLAYER_MALLET_MODEL";
        public const string AIMalletModelSlot = "AI_MALLET_MODEL";
        public const string PuckModelSlot = "PUCK_MODEL";
        public const string GoalModelSlot = "GOAL_MODEL";
        public const string ArenaModelSlot = "ARENA_MODEL";
        public const string AIOpponentModelSlot = "AI_OPPONENT_MODEL";
        public const string VfxThemeSlot = "VFX_THEME";
        public const string SfxThemeSlot = "SFX_THEME";

        [Header("Replaceable visual prefabs")]
        public GameObject TABLE_MODEL;
        public GameObject MALLET_MODEL;
        public GameObject PLAYER_MALLET_MODEL;
        public GameObject AI_MALLET_MODEL;
        public GameObject PUCK_MODEL;
        public GameObject GOAL_MODEL;
        public GameObject ARENA_MODEL;
        public GameObject AI_OPPONENT_MODEL;

        [Header("Optional theme prefabs")]
        public GameObject VFX_THEME;
        public AudioClip SFX_THEME;

        [Header("Game audio")]
        public AudioClip UI_CLICK_SFX;
        public AudioClip SERVE_SFX;
        public AudioClip HIT_LIGHT_SFX;
        public AudioClip HIT_NORMAL_SFX;
        public AudioClip HIT_POWER_SFX;
        public AudioClip RAIL_BOUNCE_SFX;
        public AudioClip PLAYER_SCORE_SFX;
        public AudioClip AI_SCORE_SFX;
        public AudioClip VICTORY_SFX;
        public AudioClip DEFEAT_SFX;

        [Header("Palette")]
        public Color wood = new(0.208f, 0.114f, 0.086f);
        public Color woodWarm = new(0.439f, 0.255f, 0.149f);
        public Color woodLight = new(0.651f, 0.416f, 0.212f);
        public Color metal = new(0.2f, 0.251f, 0.302f);
        public Color gold = new(0.902f, 0.741f, 0.384f);
        public Color cyan = new(0.275f, 0.91f, 1f);
        public Color violet = new(0.647f, 0.475f, 1f);
        public Color ember = new(1f, 0.678f, 0.353f);
        public Color enemyRed = new(0.65f, 0.08f, 0.12f);
        public Color hallStone = new(0.09f, 0.13f, 0.19f);
        public Color floorStone = new(0.063f, 0.082f, 0.125f);

        public static ThemeConfig CreateRuntimeDefault()
        {
            var theme = CreateInstance<ThemeConfig>();
            theme.name = "Runtime Theme Config";
            return theme;
        }
    }

    public sealed class VisualSlot : MonoBehaviour
    {
        [SerializeField] private string slot;

        public string Slot => slot;

        public void Initialize(string slotId)
        {
            slot = slotId;
            name = $"Visual_{slotId}";
        }
    }
}
