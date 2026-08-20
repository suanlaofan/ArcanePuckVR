using UnityEngine;

namespace ArcanePuck
{
    [DisallowMultipleComponent]
    public sealed class GameAudioController : MonoBehaviour
    {
        private const float RailSoundCooldown = 0.055f;

        private ThemeConfig theme;
        private MatchManager match;
        private PuckController puck;
        private AudioSource gameSource;
        private AudioSource uiSource;
        private float nextRailSoundTime;

        public AudioSource GameSource => gameSource;
        public AudioSource UISource => uiSource;
        public bool HasRequiredClips => theme != null &&
                                        theme.UI_CLICK_SFX != null &&
                                        theme.SERVE_SFX != null &&
                                        theme.HIT_LIGHT_SFX != null &&
                                        theme.HIT_NORMAL_SFX != null &&
                                        theme.HIT_POWER_SFX != null &&
                                        theme.RAIL_BOUNCE_SFX != null &&
                                        theme.PLAYER_SCORE_SFX != null &&
                                        theme.AI_SCORE_SFX != null &&
                                        theme.VICTORY_SFX != null &&
                                        theme.DEFEAT_SFX != null;

        public void Initialize(ThemeConfig themeConfig, MatchManager matchManager, PuckController puckController)
        {
            Unsubscribe();

            theme = themeConfig;
            match = matchManager;
            puck = puckController;

            var sources = GetComponents<AudioSource>();
            gameSource = sources.Length > 0 ? sources[0] : gameObject.AddComponent<AudioSource>();
            uiSource = sources.Length > 1 ? sources[1] : gameObject.AddComponent<AudioSource>();
            ConfigureSource(gameSource, 64);
            ConfigureSource(uiSource, 32);

            HitEvents.Hit += HandleHit;
            if (match != null)
            {
                match.ServeStarted += HandleServeStarted;
                match.ScoreAwarded += HandleScoreAwarded;
                match.MatchCompleted += HandleMatchCompleted;
            }

            if (puck != null)
            {
                puck.RailContact += HandleRailContact;
            }
        }

        public void PlayUiClick()
        {
            Play(uiSource, theme != null ? theme.UI_CLICK_SFX : null, 0.3f, 0.98f, 1.02f);
        }

        private static void ConfigureSource(AudioSource source, int priority)
        {
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.priority = priority;
            source.ignoreListenerPause = true;
        }

        private void HandleHit(HitEvent hit)
        {
            if (theme == null)
            {
                return;
            }

            switch (hit.Strength)
            {
                case HitStrength.Light:
                    Play(gameSource, theme.HIT_LIGHT_SFX, 0.3f, 1.02f, 1.09f);
                    break;
                case HitStrength.Power:
                    Play(gameSource, theme.HIT_POWER_SFX, 0.66f, 0.9f, 0.97f);
                    break;
                default:
                    Play(gameSource, theme.HIT_NORMAL_SFX, 0.46f, 0.97f, 1.04f);
                    break;
            }
        }

        private void HandleRailContact(float speed)
        {
            if (theme == null || Time.unscaledTime < nextRailSoundTime)
            {
                return;
            }

            nextRailSoundTime = Time.unscaledTime + RailSoundCooldown;
            var speed01 = Mathf.InverseLerp(0.15f, 2.4f, speed);
            Play(gameSource, theme.RAIL_BOUNCE_SFX, Mathf.Lerp(0.2f, 0.4f, speed01), 0.96f, 1.06f);
        }

        private void HandleServeStarted(MatchSide toward)
        {
            if (theme == null)
            {
                return;
            }

            var pitch = toward == MatchSide.AI ? 1.02f : 0.98f;
            Play(gameSource, theme.SERVE_SFX, 0.4f, pitch, pitch);
        }

        private void HandleScoreAwarded(MatchSide scorer, bool goldenGoal)
        {
            if (theme == null || match == null || match.Snapshot.Phase == MatchPhase.Finished)
            {
                return;
            }

            var clip = scorer == MatchSide.Player ? theme.PLAYER_SCORE_SFX : theme.AI_SCORE_SFX;
            var volume = scorer == MatchSide.Player ? 0.62f : 0.52f;
            Play(gameSource, clip, goldenGoal ? volume + 0.08f : volume, 1f, 1f);
        }

        private void HandleMatchCompleted(MatchSide winner)
        {
            if (theme == null)
            {
                return;
            }

            Play(gameSource, winner == MatchSide.Player ? theme.VICTORY_SFX : theme.DEFEAT_SFX,
                0.76f, 1f, 1f);
        }

        private static void Play(AudioSource source, AudioClip clip, float volume, float minPitch, float maxPitch)
        {
            if (source == null || clip == null)
            {
                return;
            }

            source.pitch = Mathf.Approximately(minPitch, maxPitch)
                ? minPitch
                : Random.Range(minPitch, maxPitch);
            source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            HitEvents.Hit -= HandleHit;
            if (match != null)
            {
                match.ServeStarted -= HandleServeStarted;
                match.ScoreAwarded -= HandleScoreAwarded;
                match.MatchCompleted -= HandleMatchCompleted;
            }

            if (puck != null)
            {
                puck.RailContact -= HandleRailContact;
            }
        }
    }
}
