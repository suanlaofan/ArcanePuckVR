using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ArcanePuck
{
    public enum GameView
    {
        Menu,
        Difficulty,
        Playing,
        Paused,
        Finished
    }

    /// <summary>
    /// Runtime-only presentation for the desktop game. The match state and all button
    /// callbacks deliberately stay in MatchManager; this class only translates those
    /// states into the Arcane Disc Figma visual language.
    /// </summary>
    public sealed class GameHUD : MonoBehaviour
    {
        // Keep the PICO HUD comfortably inside the view without attaching it to
        // the game table. The Canvas remains a camera child, so the HMD's
        // UpdateAndBeforeRender tracking update carries it with the user's head.
        private static readonly Vector3 PicoHeadLockedOffset = new(0f, -0.32f, 1.85f);
        private static readonly Color FallbackGold = new(0.902f, 0.741f, 0.384f);
        private static readonly Color FallbackCyan = new(0.275f, 0.91f, 1f);
        private static readonly Color FallbackViolet = new(0.647f, 0.475f, 1f);
        private static readonly Color VioletGlass = new(0.10f, 0.055f, 0.22f, 0.94f);
        private static readonly Color Teal = new(0.21f, 0.91f, 0.77f);

        private MatchManager match;
        private ThemeConfig theme;
        private GameAudioController audioController;
        private Canvas canvas;
        private GameObject menuLayer;
        private GameObject hudLayer;
        private Font font;
        private GameView view;
        private Text scoreLabel;
        private Text statusLabel;
        private Image chargeFill;
        private Sprite cutCornerSprite;
        private Sprite solidSprite;

        public string ScoreText => scoreLabel != null ? scoreLabel.text : string.Empty;
        public string StatusLineText => statusLabel != null ? statusLabel.text : string.Empty;

        private Color Gold => theme != null ? theme.gold : FallbackGold;
        private Color Cyan => theme != null ? theme.cyan : FallbackCyan;
        private Color Violet => theme != null ? theme.violet : FallbackViolet;

        public void Initialize(MatchManager manager, ThemeConfig themeConfig)
        {
            Initialize(manager, themeConfig, null);
        }

        public void Initialize(MatchManager manager, ThemeConfig themeConfig, GameAudioController gameAudioController)
        {
            match = manager;
            theme = themeConfig;
            audioController = gameAudioController;
            font = ResolveFont();
            EnsureEventSystem();
            BuildCanvas();
            ShowMenu();
        }

        public void ShowMenu()
        {
            view = GameView.Menu;
            Rebuild();
        }

        public void ShowDifficulty()
        {
            view = GameView.Difficulty;
            Rebuild();
        }

        public void ShowPlaying()
        {
            view = GameView.Playing;
            Rebuild();
        }

        public void ShowPaused()
        {
            view = GameView.Paused;
            Rebuild();
        }

        public void ShowFinished()
        {
            view = GameView.Finished;
            Rebuild();
        }

        public void Refresh()
        {
            if (view is GameView.Playing or GameView.Paused or GameView.Finished)
            {
                UpdateHUDText();
            }
        }

        private void BuildCanvas()
        {
            if (canvas != null)
            {
                return;
            }

            var canvasObject = new GameObject("Arcane Puck UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            var picoXR = PicoXRRuntime.IsRunning;
            canvas.renderMode = picoXR ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (picoXR && Camera.main != null)
            {
                var rect = canvas.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(1000f, 620f);
                // The PICO compositor displays this canvas from its back side.
                // The previous world-space pose was LookRotation(back) followed
                // by a local Y-axis 180 degree correction, whose resulting
                // orientation matches the HMD. Parent the canvas directly to the
                // tracked camera to preserve that correction while making the UI
                // genuinely head-locked (including before-render pose updates).
                rect.SetParent(Camera.main.transform, false);
                rect.localScale = Vector3.one * 0.0024f;
                rect.localPosition = PicoHeadLockedOffset;
                rect.localRotation = Quaternion.identity;
                canvas.worldCamera = Camera.main;
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            }
        }

        private void Rebuild()
        {
            if (canvas == null)
            {
                return;
            }

            if (menuLayer != null) Destroy(menuLayer);
            if (hudLayer != null) Destroy(hudLayer);

            scoreLabel = null;
            statusLabel = null;
            chargeFill = null;

            menuLayer = new GameObject("Menu Layer", typeof(RectTransform));
            menuLayer.transform.SetParent(canvas.transform, false);
            Stretch(menuLayer.GetComponent<RectTransform>());

            hudLayer = new GameObject("HUD Layer", typeof(RectTransform));
            hudLayer.transform.SetParent(canvas.transform, false);
            Stretch(hudLayer.GetComponent<RectTransform>());
            menuLayer.transform.SetAsLastSibling();

            if (view is GameView.Playing or GameView.Paused or GameView.Finished)
            {
                BuildGameplayHUD();
            }

            switch (view)
            {
                case GameView.Menu:
                    BuildMenu();
                    break;
                case GameView.Difficulty:
                    BuildDifficulty();
                    break;
                case GameView.Paused:
                    BuildPauseOverlay();
                    break;
                case GameView.Finished:
                    BuildFinishedOverlay();
                    break;
            }
        }

        private void BuildMenu()
        {
            AddFullscreenTint(menuLayer.transform, new Color(0.012f, 0.004f, 0.045f, 0.20f));
            AddSideVignettes(menuLayer.transform, 0.12f);
            AddBrand(menuLayer.transform, 258f);

            AddButton(menuLayer.transform, "开始游戏", new Vector2(0f, 12f), match.OpenDifficultySelection,
                Gold, new Vector2(340f, 58f), 24);
            AddButton(menuLayer.transform, "退出游戏", new Vector2(0f, -70f), QuitApplication,
                new Color(0.52f, 0.42f, 0.72f), new Vector2(340f, 50f), 20);

            AddDivider(menuLayer.transform, -144f, 280f, new Color(Gold.r, Gold.g, Gold.b, 0.58f));
            AddLabel(menuLayer.transform, PicoXRRuntime.IsRunning
                    ? "主按键开始对局  ·  副按键暂停  ·  移动主手球槌  ·  扳机蓄力"
                    : "右键调整视角  ·  WASD 移动  ·  鼠标移动球槌  ·  按住左键蓄力",
                16, new Color(0.78f, 0.80f, 0.96f, 0.84f), 0f, -188f, new Vector2(900f, 38f));
        }

        private void BuildDifficulty()
        {
            AddFullscreenTint(menuLayer.transform, new Color(0.008f, 0.004f, 0.035f, 0.50f));
            var panel = CreateFramedPanel(menuLayer.transform, "Difficulty Panel", new Vector2(600f, 652f),
                Violet, VioletGlass, 2f);
            panel.anchoredPosition = new Vector2(0f, -16f);

            AddLabel(panel, "选择 AI 对手", 45, Color.white, 0f, 237f, new Vector2(520f, 64f), FontStyle.Bold);
            AddLabel(panel, "SELECT YOUR ARCANE RIVAL", 15, Gold, 0f, 190f, new Vector2(520f, 30f));
            AddDivider(panel, 160f, 330f, new Color(Gold.r, Gold.g, Gold.b, 0.7f));

            AddDifficulty(panel, "学徒", "反应慢 · 速度低 · 失误更多", DifficultyId.Apprentice, 82f, Cyan);
            AddDifficulty(panel, "法师", "攻守均衡 · 基础预判与射门", DifficultyId.Mage, -26f, Gold);
            AddDifficulty(panel, "大法师", "反应迅捷 · 尝试借边进攻", DifficultyId.Archmage, -134f, Violet);
            AddButton(panel, "返回", new Vector2(0f, -243f), match.ReturnToMenu,
                new Color(0.53f, 0.47f, 0.73f), new Vector2(300f, 48f), 19);
        }

        private void BuildPauseOverlay()
        {
            AddFullscreenTint(menuLayer.transform, new Color(0.006f, 0.003f, 0.025f, 0.62f));
            var panel = CreateFramedPanel(menuLayer.transform, "Pause Panel", new Vector2(500f, 492f),
                Violet, VioletGlass, 2f);

            AddLabel(panel, "时间凝滞", 46, Color.white, 0f, 140f, new Vector2(440f, 62f), FontStyle.Bold);
            AddLabel(panel, "ARCANE DUEL PAUSED", 15, Gold, 0f, 92f, new Vector2(440f, 28f));
            AddDivider(panel, 58f, 276f, new Color(Gold.r, Gold.g, Gold.b, 0.68f));
            AddButton(panel, "继续对局", new Vector2(0f, -10f), match.ResumeMatch,
                Cyan, new Vector2(330f, 54f), 22);
            AddButton(panel, "重新开始", new Vector2(0f, -84f), match.RestartMatch,
                Gold, new Vector2(330f, 50f), 20);
            AddButton(panel, "返回主页", new Vector2(0f, -157f), match.ReturnToMenu,
                new Color(0.53f, 0.47f, 0.73f), new Vector2(330f, 48f), 19);
        }

        private void BuildFinishedOverlay()
        {
            AddFullscreenTint(menuLayer.transform, new Color(0.004f, 0.002f, 0.02f, 0.68f));
            var playerWon = match != null && match.WinnerIsPlayer;
            var accent = playerWon ? Gold : Teal;
            var fill = playerWon
                ? new Color(0.145f, 0.075f, 0.025f, 0.96f)
                : new Color(0.015f, 0.13f, 0.125f, 0.96f);
            var panel = CreateFramedPanel(menuLayer.transform, "Duel Result", new Vector2(520f, 570f), accent, fill, 2f);
            panel.anchoredPosition = new Vector2(0f, -10f);

            AddLabel(panel, "决斗结果", 17, accent, 0f, 208f, new Vector2(440f, 34f));
            AddLabel(panel, playerWon ? "你获得了胜利！" : "AI巫师获胜", 44, Color.white, 0f, 145f,
                new Vector2(480f, 68f), FontStyle.Bold);
            AddLabel(panel,
                playerWon ? "魔法飞盘竞技场为你见证荣耀" : "下一次飞盘会为你带来胜利",
                17, new Color(0.87f, 0.87f, 0.98f, 0.9f), 0f, 95f, new Vector2(450f, 34f));
            AddDivider(panel, 58f, 292f, new Color(accent.r, accent.g, accent.b, 0.72f));

            var score = $"<color=#{ToHex(Cyan)}>{match?.PlayerScore ?? 0}</color>  <color=#EEE7FF>:</color>  <color=#{ToHex(Gold)}>{match?.AIScore ?? 0}</color>";
            AddRichLabel(panel, score, 58, Color.white, 0f, -2f, new Vector2(410f, 80f), FontStyle.Bold);
            AddLabel(panel, match != null && match.GoldenGoal ? "金球加时 · 一击定胜负" : "本轮决斗已结束", 15,
                new Color(0.84f, 0.83f, 0.96f, 0.9f), 0f, -56f, new Vector2(420f, 28f));
            AddButton(panel, "再来一局", new Vector2(0f, -126f), match.RestartMatch,
                accent, new Vector2(330f, 52f), 21);
            AddButton(panel, "返回主页", new Vector2(0f, -199f), match.ReturnToMenu,
                new Color(0.53f, 0.47f, 0.73f), new Vector2(330f, 48f), 19);
        }

        private void BuildGameplayHUD()
        {
            var scoreFrame = CreateFramedPanel(hudLayer.transform, "Arcane Score Bar", new Vector2(500f, 68f),
                new Color(Gold.r, Gold.g, Gold.b, 0.92f), new Color(0.12f, 0.055f, 0.27f, 0.94f), 2f);
            PinToTop(scoreFrame, new Vector2(0f, -38f));
            scoreLabel = AddRichLabel(scoreFrame, string.Empty, 25, Color.white, 0f, 0f,
                new Vector2(450f, 62f), FontStyle.Bold);

            AddTopDiamond(hudLayer.transform, new Vector2(-264f, -68f), 10f, Gold);
            AddTopDiamond(hudLayer.transform, new Vector2(264f, -68f), 10f, Gold);

            var statusFrame = CreateFramedPanel(hudLayer.transform, "Arcane Status", new Vector2(400f, 34f),
                new Color(Violet.r, Violet.g, Violet.b, 0.92f), new Color(0.04f, 0.022f, 0.12f, 0.90f), 1.5f);
            PinToTop(statusFrame, new Vector2(0f, -113f));
            statusLabel = AddRichLabel(statusFrame, string.Empty, 15, new Color(0.88f, 0.90f, 1f), 0f, 0f,
                new Vector2(372f, 28f));

            var bar = new GameObject("Charge Meter", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(hudLayer.transform, false);
            var barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.anchoredPosition = new Vector2(0f, -139f);
            barRect.sizeDelta = new Vector2(254f, 5f);
            var barImage = bar.GetComponent<Image>();
            barImage.sprite = SolidSprite;
            barImage.color = new Color(0.31f, 0.23f, 0.52f, 0.62f);
            barImage.raycastTarget = false;

            var fill = new GameObject("Charge Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(bar.transform, false);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            chargeFill = fill.GetComponent<Image>();
            chargeFill.sprite = SolidSprite;
            chargeFill.color = Cyan;
            chargeFill.raycastTarget = false;

            UpdateHUDText();
        }

        private void UpdateHUDText()
        {
            if (match == null || scoreLabel == null || statusLabel == null)
            {
                return;
            }

            var overtime = match.GoldenGoal
                ? $"   <size=13><color=#{ToHex(Gold)}>· 金球加时</color></size>"
                : string.Empty;
            scoreLabel.text =
                $"<color=#{ToHex(new Color(0.83f, 0.69f, 1f))}>你</color>  <size=31><color=#FFFFFF>{match.PlayerScore}</color></size>" +
                $"   <color=#{ToHex(Gold)}>✦</color>   <size=17><color=#{ToHex(new Color(0.92f, 0.86f, 1f))}>{match.DifficultyLabel}</color></size>" +
                $"   <color=#{ToHex(Gold)}>✦</color>   <color=#{ToHex(Cyan)}>AI</color>  <size=31><color=#{ToHex(new Color(0.75f, 0.94f, 1f))}>{match.AIScore}</color></size>{overtime}";

            var charge = Mathf.RoundToInt(match.PlayerCharge01 * 100f);
            statusLabel.text = $"{match.StatusText}   <color=#{ToHex(Cyan)}>蓄力 {charge}%</color>";
            if (chargeFill != null)
            {
                chargeFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(match.PlayerCharge01), 1f);
            }
        }

        private void AddBrand(Transform parent, float y)
        {
            AddLabel(parent, "奥术飞盘", 72, new Color(0.12f, 0.065f, 0.03f, 0.92f), 3f, y - 4f,
                new Vector2(760f, 100f), FontStyle.Bold);
            var title = AddLabel(parent, "奥术飞盘", 72, Gold, 0f, y, new Vector2(760f, 100f), FontStyle.Bold);
            var outline = title.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.08f, 0.025f, 0.12f, 0.9f);
            outline.effectDistance = new Vector2(1.2f, -1.2f);
            AddLabel(parent, "A  R  C  A  N  E     D  I  S  C", 16, new Color(Gold.r, Gold.g, Gold.b, 0.92f),
                0f, y - 62f, new Vector2(680f, 30f));
            AddDivider(parent, y - 94f, 320f, new Color(Gold.r, Gold.g, Gold.b, 0.72f));
        }

        private void AddDifficulty(Transform parent, string title, string detail, DifficultyId difficulty, float y, Color accent)
        {
            var button = CreateButton(parent, new Vector2(0f, y), () => match.StartMatch(difficulty), accent,
                new Vector2(470f, 82f));
            AddLabel(button.transform, title, 25, Color.white, 0f, 14f, new Vector2(400f, 34f), FontStyle.Bold);
            AddLabel(button.transform, detail, 15, new Color(0.90f, 0.90f, 1f, 0.9f), 0f, -17f, new Vector2(420f, 26f));
        }

        private Button AddButton(Transform parent, string text, Vector2 position, UnityAction action, Color accent,
            Vector2 size, int fontSize)
        {
            var button = CreateButton(parent, position, action, accent, size);
            AddLabel(button.transform, text, fontSize, Color.white, 0f, 0f,
                new Vector2(size.x - 36f, size.y - 10f), FontStyle.Bold);
            return button;
        }

        private Button CreateButton(Transform parent, Vector2 position, UnityAction action, Color accent, Vector2 size)
        {
            var frame = CreateFramedPanel(parent, "Arcane Button", size, accent,
                new Color(accent.r * 0.18f, accent.g * 0.12f, accent.b * 0.30f, 0.97f), 2f);
            frame.anchoredPosition = position;
            var image = frame.GetComponent<Image>();
            image.raycastTarget = true;

            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (audioController != null)
            {
                button.onClick.AddListener(audioController.PlayUiClick);
            }
            button.onClick.AddListener(action);
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.Lerp(Color.white, accent, 0.22f);
            colors.pressedColor = Color.Lerp(Color.white, accent, 0.48f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.55f, 0.55f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            return button;
        }

        private RectTransform CreateFramedPanel(Transform parent, string objectName, Vector2 size, Color outline,
            Color fill, float border)
        {
            var outer = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            outer.transform.SetParent(parent, false);
            var outerRect = outer.GetComponent<RectTransform>();
            outerRect.anchorMin = outerRect.anchorMax = new Vector2(0.5f, 0.5f);
            outerRect.sizeDelta = size;
            var outerImage = outer.GetComponent<Image>();
            ConfigureCutCornerImage(outerImage, outline);

            var inner = new GameObject("Glass Fill", typeof(RectTransform), typeof(Image));
            inner.transform.SetParent(outer.transform, false);
            var innerRect = inner.GetComponent<RectTransform>();
            Stretch(innerRect);
            innerRect.offsetMin = Vector2.one * border;
            innerRect.offsetMax = -Vector2.one * border;
            var innerImage = inner.GetComponent<Image>();
            ConfigureCutCornerImage(innerImage, fill);
            innerImage.raycastTarget = false;
            return outerRect;
        }

        private void AddFullscreenTint(Transform parent, Color color)
        {
            var overlay = new GameObject("Arcane Backdrop", typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(parent, false);
            Stretch(overlay.GetComponent<RectTransform>());
            var image = overlay.GetComponent<Image>();
            image.sprite = SolidSprite;
            image.color = color;
            image.raycastTarget = false;
        }

        private void AddSideVignettes(Transform parent, float alpha)
        {
            AddFlatRect(parent, "Left Vignette", new Vector2(0f, 0.5f), Vector2.zero,
                new Vector2(245f, 1080f), new Color(0.005f, 0.002f, 0.018f, alpha));
            AddFlatRect(parent, "Right Vignette", new Vector2(1f, 0.5f), Vector2.zero,
                new Vector2(245f, 1080f), new Color(0.005f, 0.002f, 0.018f, alpha));
        }

        private void AddDivider(Transform parent, float y, float width, Color color)
        {
            var halfWidth = width * 0.5f;
            AddFlatRect(parent, "Divider Left", new Vector2(0.5f, 0.5f), new Vector2(-halfWidth * 0.5f - 12f, y),
                new Vector2(halfWidth - 20f, 1.5f), color);
            AddFlatRect(parent, "Divider Right", new Vector2(0.5f, 0.5f), new Vector2(halfWidth * 0.5f + 12f, y),
                new Vector2(halfWidth - 20f, 1.5f), color);
            AddDiamond(parent, new Vector2(0f, y), 9f, color);
        }

        private void AddDiamond(Transform parent, Vector2 position, float size, Color color)
        {
            var diamond = AddFlatRect(parent, "Arcane Diamond", new Vector2(0.5f, 0.5f), position,
                new Vector2(size, size), color);
            diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }

        private void AddTopDiamond(Transform parent, Vector2 position, float size, Color color)
        {
            var diamond = AddFlatRect(parent, "Arcane Top Diamond", new Vector2(0.5f, 1f), position,
                new Vector2(size, size), color);
            diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }

        private RectTransform AddFlatRect(Transform parent, string objectName, Vector2 anchor, Vector2 position,
            Vector2 size, Color color)
        {
            var rectObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            rectObject.transform.SetParent(parent, false);
            var rect = rectObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = rectObject.GetComponent<Image>();
            image.sprite = SolidSprite;
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private Text AddLabel(Transform parent, string text, int size, Color color, float x, float y, Vector2 area,
            FontStyle style = FontStyle.Normal)
        {
            return AddText(parent, text, size, color, x, y, area, style, false);
        }

        private Text AddRichLabel(Transform parent, string text, int size, Color color, float x, float y, Vector2 area,
            FontStyle style = FontStyle.Normal)
        {
            return AddText(parent, text, size, color, x, y, area, style, true);
        }

        private Text AddText(Transform parent, string text, int size, Color color, float x, float y, Vector2 area,
            FontStyle style, bool richText)
        {
            var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(parent, false);
            var rect = label.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = area;

            var component = label.GetComponent<Text>();
            component.font = font;
            component.text = text;
            component.fontSize = size;
            component.fontStyle = style;
            component.alignment = TextAnchor.MiddleCenter;
            component.color = color;
            component.horizontalOverflow = HorizontalWrapMode.Overflow;
            component.verticalOverflow = VerticalWrapMode.Overflow;
            component.supportRichText = richText;
            component.raycastTarget = false;
            return component;
        }

        private void ConfigureCutCornerImage(Image image, Color color)
        {
            image.sprite = CutCornerSprite;
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.color = color;
            image.raycastTarget = false;
        }

        private void PinToTop(RectTransform rect, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
        }

        private Sprite CutCornerSprite
        {
            get
            {
                if (cutCornerSprite != null)
                {
                    return cutCornerSprite;
                }

                const int textureSize = 96;
                const int chamfer = 25;
                var pixels = new Color32[textureSize * textureSize];
                for (var y = 0; y < textureSize; y++)
                {
                    for (var x = 0; x < textureSize; x++)
                    {
                        var alpha = CutCornerAlpha(x, y, textureSize, chamfer);
                        pixels[y * textureSize + x] = new Color32(255, 255, 255, alpha);
                    }
                }

                var texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
                {
                    name = "ArcaneCutCorner",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave
                };
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                cutCornerSprite = Sprite.Create(texture, new Rect(0f, 0f, textureSize, textureSize), new Vector2(0.5f, 0.5f),
                    100f, 0, SpriteMeshType.FullRect, new Vector4(28f, 28f, 28f, 28f));
                cutCornerSprite.name = "Arcane Cut Corner UI";
                cutCornerSprite.hideFlags = HideFlags.DontSave;
                return cutCornerSprite;
            }
        }

        private Sprite SolidSprite
        {
            get
            {
                if (solidSprite != null)
                {
                    return solidSprite;
                }

                solidSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
                solidSprite.name = "Arcane UI Solid";
                solidSprite.hideFlags = HideFlags.DontSave;
                return solidSprite;
            }
        }

        private static byte CutCornerAlpha(int x, int y, int textureSize, int chamfer)
        {
            var distance = float.MaxValue;
            if (x < chamfer && y < chamfer) distance = x + y - chamfer + 1.5f;
            if (x < chamfer && y >= textureSize - chamfer) distance = x + (textureSize - 1 - y) - chamfer + 1.5f;
            if (x >= textureSize - chamfer && y < chamfer) distance = (textureSize - 1 - x) + y - chamfer + 1.5f;
            if (x >= textureSize - chamfer && y >= textureSize - chamfer)
                distance = (textureSize - 1 - x) + (textureSize - 1 - y) - chamfer + 1.5f;
            return distance == float.MaxValue ? byte.MaxValue : (byte)Mathf.RoundToInt(Mathf.Clamp01(distance) * byte.MaxValue);
        }

        private static string ToHex(Color color)
        {
            return ColorUtility.ToHtmlStringRGB(color);
        }

        private static Font ResolveFont()
        {
            var builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            try
            {
                var cjk = Font.CreateDynamicFontFromOSFont(
                    new[] { "Hiragino Sans GB", "STHeiti", "Arial Unicode MS", "Arial" }, 64);
                if (cjk != null)
                {
                    return cjk;
                }
            }
            catch (System.Exception)
            {
                // Player builds without a matching OS font use Unity's built-in fallback.
            }

            return builtin;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void EnsureEventSystem()
        {
            var existing = FindAnyObjectByType<EventSystem>();
            if (existing != null)
            {
                var existingModule = existing.GetComponent<BaseInputModule>();
                if (existingModule != null && existingModule.enabled)
                {
                    return;
                }

                if (existingModule != null)
                {
                    existingModule.enabled = true;
                }
                else
                {
                    var existingModuleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                    if (existingModuleType != null) existing.gameObject.AddComponent(existingModuleType);
                    else existing.gameObject.AddComponent<StandaloneInputModule>();
                }
                return;
            }

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            var moduleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (moduleType != null) eventSystem.AddComponent(moduleType);
            else eventSystem.AddComponent<StandaloneInputModule>();
        }

        private static void QuitApplication()
        {
            Application.Quit();
        }

        private void OnDestroy()
        {
            if (cutCornerSprite != null)
            {
                var texture = cutCornerSprite.texture;
                Destroy(cutCornerSprite);
                if (texture != null)
                {
                    Destroy(texture);
                }
            }

            if (solidSprite != null)
            {
                Destroy(solidSprite);
            }

        }
    }
}
