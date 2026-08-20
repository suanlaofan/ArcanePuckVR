using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcanePuck.Editor
{
    [InitializeOnLoad]
    internal static class QuGunQiuThemeBuilder
    {
        private const string GeneratedFolder = "Assets/ArcanePuck/Generated/QuGunQiu";
        private const string ResourcesFolder = "Assets/ArcanePuck/Scripts/Config/Resources";
        private const string ThemePath = ResourcesFolder + "/ArcanePuckTheme.asset";
        private const string ScenePath = "Assets/ArcanePuck/Scenes/ArcanePuck.unity";

        private const string TableSource = "Assets/QuGunQiu/曲棍球桌/tripo_convert_0233b909-aa32-476b-b624-28438e780ee4.fbx";
        private const string PlayerMalletSource = "Assets/QuGunQiu/一号球槌/tripo_convert_8728a93b-0d41-470f-ba8a-15908db039ea.fbx";
        private const string AiMalletSource = "Assets/QuGunQiu/二号球槌/tripo_convert_13faced8-e78e-4f66-a249-e00441f1361a.fbx";
        private const string PuckSource = "Assets/QuGunQiu/曲棍球/tripo_convert_9b4687b4-e1bc-4c60-b7de-66b8dd94ba9b.fbx";

        static QuGunQiuThemeBuilder()
        {
            EditorApplication.delayCall += EnsureTheme;
            EditorApplication.delayCall += EnsureAdjustableScene;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += EnsureAdjustableScene;
        }

        [MenuItem("Arcane Puck/Rebuild QuGunQiu Theme")]
        public static void RebuildTheme()
        {
            AssetDatabase.DeleteAsset(ThemePath);
            EnsureTheme();
        }

        [MenuItem("Arcane Puck/Open Adjustable Game Scene")]
        public static void OpenAdjustableGameScene()
        {
            EnsureTheme();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var layout = EnsureSceneLayout(scene);
            Selection.activeGameObject = layout.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log("Arcane Puck: adjustable game scene opened. Edit each Preview Anchor/Model Adjustment transform.");
        }

        private static void EnsureAdjustableScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorSceneManager.GetActiveScene().path != ScenePath) return;
            EnsureSceneLayout(EditorSceneManager.GetActiveScene());
        }

        private static SceneModelLayout EnsureSceneLayout(Scene scene)
        {
            var existing = UnityEngine.Object.FindAnyObjectByType<SceneModelLayout>();
            if (existing != null)
            {
                EnsurePreviewEnvironment(existing.transform);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                return existing;
            }

            var theme = AssetDatabase.LoadAssetAtPath<ThemeConfig>(ThemePath);
            if (!IsComplete(theme)) throw new InvalidOperationException("QuGunQiu theme was not generated.");

            var root = new GameObject("Game Model Layout (Adjust Here)");
            root.transform.position = Vector3.up * 0.82f;
            var layout = root.AddComponent<SceneModelLayout>();
            layout.tableModel = CreateSceneModel(root.transform, theme.TABLE_MODEL, "Table Preview Anchor", Vector3.zero);
            layout.playerMalletModel = CreateSceneModel(root.transform, theme.PLAYER_MALLET_MODEL, "Player Mallet Preview Anchor", new Vector3(0f, 0f, -1.008f));
            layout.aiMalletModel = CreateSceneModel(root.transform, theme.AI_MALLET_MODEL, "AI Mallet Preview Anchor", new Vector3(0f, 0f, 1.008f));
            layout.puckModel = CreateSceneModel(root.transform, theme.PUCK_MODEL, "Puck Preview Anchor", new Vector3(0f, 0.026f, 0f));
            EnsurePreviewEnvironment(root.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Arcane Puck: adjustable game models added to the scene. Edit the four child transforms.");
            return layout;
        }

        private static void EnsureTheme()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ThemeConfig>(ThemePath);
            if (IsComplete(existing)) return;

            try
            {
                EnsureFolder(GeneratedFolder);
                EnsureFolder(ResourcesFolder);

                var table = CreateModelPrefab("HockeyTable", TableSource);
                var playerMallet = CreateModelPrefab("PlayerMallet", PlayerMalletSource);
                var aiMallet = CreateModelPrefab("AIMallet", AiMalletSource);
                var puck = CreateModelPrefab("HockeyPuck", PuckSource);

                if (existing != null) AssetDatabase.DeleteAsset(ThemePath);
                var theme = ScriptableObject.CreateInstance<ThemeConfig>();
                theme.name = "ArcanePuckTheme";
                theme.TABLE_MODEL = table;
                theme.PLAYER_MALLET_MODEL = playerMallet;
                theme.AI_MALLET_MODEL = aiMallet;
                theme.PUCK_MODEL = puck;
                AssetDatabase.CreateAsset(theme, ThemePath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("Arcane Puck: QuGunQiu table, two mallets and puck prefabs generated successfully.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static bool IsComplete(ThemeConfig theme)
        {
            return theme != null &&
                   theme.TABLE_MODEL != null &&
                   theme.PLAYER_MALLET_MODEL != null &&
                   theme.AI_MALLET_MODEL != null &&
                   theme.PUCK_MODEL != null;
        }

        private static GameObject CreateModelPrefab(string name, string sourcePath)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) throw new InvalidOperationException($"Cannot load model at {sourcePath}");

            var prefabPath = $"{GeneratedFolder}/{name}.prefab";
            var root = new GameObject(name);
            try
            {
                var instance = PrefabUtility.InstantiatePrefab(source, root.transform) as GameObject;
                if (instance == null) throw new InvalidOperationException($"Cannot instantiate model at {sourcePath}");
                instance.name = "Model";
                instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Transform CreateSceneModel(Transform root, GameObject prefab, string anchorName, Vector3 position)
        {
            var anchor = new GameObject(anchorName).transform;
            anchor.SetParent(root, false);
            anchor.localPosition = position;
            var instance = PrefabUtility.InstantiatePrefab(prefab, anchor) as GameObject;
            if (instance == null) throw new InvalidOperationException($"Cannot instantiate {anchorName}");
            instance.name = "Model Adjustment (Edit Transform)";
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            FitModel(instance.transform, anchorName);
            return instance.transform;
        }

        private static void FitModel(Transform model, string anchorName)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            var source = bounds.size;
            var target = anchorName.StartsWith("Table")
                ? new Vector3(2.45f, source.y, 3.85f)
                : anchorName.StartsWith("Puck")
                    ? new Vector3(0.184f, 0.052f, 0.184f)
                    : new Vector3(0.406f, 0.308f, 0.406f);
            var scale = Mathf.Min(
                source.x > 0.0001f ? target.x / source.x : float.MaxValue,
                source.z > 0.0001f ? target.z / source.z : float.MaxValue,
                anchorName.StartsWith("Table") || source.y <= 0.0001f ? float.MaxValue : target.y / source.y);
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f) return;
            model.localScale = Vector3.one * scale;
        }

        private static void EnsurePreviewEnvironment(Transform layoutRoot)
        {
            if (layoutRoot.Find("Full Game Preview (Read Only)") != null) return;

            var root = new GameObject("Full Game Preview (Read Only)").transform;
            root.SetParent(layoutRoot, false);
            root.localPosition = Vector3.down * 0.82f;
            CreatePreviewFloor(root);
            CreatePreviewRails(root);
            CreatePreviewGoals(root);
            CreatePreviewOpponent(root);
            CreatePreviewCamera(root);
            CreatePreviewLights(root);
        }

        private static void CreatePreviewFloor(Transform root)
        {
            CreatePrimitive(root, "Arena Floor", PrimitiveType.Cube, new Vector3(0f, -0.9f, 0f), new Vector3(9f, 0.12f, 11f), new Color(0.06f, 0.08f, 0.12f));
            CreatePrimitive(root, "Back Wall", PrimitiveType.Cube, new Vector3(0f, 1.2f, 5.5f), new Vector3(9f, 4.2f, 0.35f), new Color(0.09f, 0.13f, 0.19f));
            foreach (var x in new[] { -3.7f, 3.7f })
            foreach (var z in new[] { -4.3f, -1.5f, 1.5f, 4.3f })
                CreatePrimitive(root, "Arena Pillar", PrimitiveType.Cylinder, new Vector3(x, 0.5f, z), new Vector3(0.48f, 1.8f, 0.48f), new Color(0.12f, 0.16f, 0.23f));
        }

        private static void CreatePreviewRails(Transform root)
        {
            var rail = new Color(0.1f, 0.16f, 0.23f);
            CreatePrimitive(root, "Left Rail", PrimitiveType.Cube, new Vector3(-1.1425f, 0.07f, 0f), new Vector3(0.085f, 0.14f, 3.77f), rail);
            CreatePrimitive(root, "Right Rail", PrimitiveType.Cube, new Vector3(1.1425f, 0.07f, 0f), new Vector3(0.085f, 0.14f, 3.77f), rail);
            foreach (var z in new[] { -1.8425f, 1.8425f })
            {
                CreatePrimitive(root, "End Rail Left", PrimitiveType.Cube, new Vector3(-0.755f, 0.07f, z), new Vector3(0.73f, 0.14f, 0.085f), rail);
                CreatePrimitive(root, "End Rail Right", PrimitiveType.Cube, new Vector3(0.755f, 0.07f, z), new Vector3(0.73f, 0.14f, 0.085f), rail);
            }
        }

        private static void CreatePreviewGoals(Transform root)
        {
            foreach (var z in new[] { -1.816f, 1.816f })
            {
                var goal = new GameObject(z < 0f ? "Player Goal Preview" : "AI Goal Preview").transform;
                goal.SetParent(root, false);
                goal.localPosition = new Vector3(0f, 0f, z);
                CreatePrimitive(goal, "Goal Left Post", PrimitiveType.Cube, new Vector3(-0.41f, 0.22f, 0f), new Vector3(0.07f, 0.44f, 0.07f), new Color(0.2f, 0.3f, 0.4f));
                CreatePrimitive(goal, "Goal Right Post", PrimitiveType.Cube, new Vector3(0.41f, 0.22f, 0f), new Vector3(0.07f, 0.44f, 0.07f), new Color(0.2f, 0.3f, 0.4f));
                CreatePrimitive(goal, "Goal Crossbar", PrimitiveType.Cube, new Vector3(0f, 0.44f, 0f), new Vector3(0.89f, 0.07f, 0.07f), new Color(0.2f, 0.3f, 0.4f));
            }
        }

        private static void CreatePreviewOpponent(Transform root)
        {
            var opponent = new GameObject("AI Opponent Preview").transform;
            opponent.SetParent(root, false);
            opponent.localPosition = new Vector3(0f, -0.82f, 2.85f);
            CreatePrimitive(opponent, "Wizard Robe", PrimitiveType.Capsule, new Vector3(0f, 0.85f, 0f), new Vector3(0.7f, 1.1f, 0.7f), new Color(0.16f, 0.08f, 0.35f));
            CreatePrimitive(opponent, "Wizard Hood", PrimitiveType.Sphere, new Vector3(0f, 1.68f, 0f), new Vector3(0.58f, 0.52f, 0.58f), new Color(0.16f, 0.08f, 0.35f));
        }

        private static void CreatePreviewCamera(Transform root)
        {
            var cameraObject = new GameObject("Preview Camera", typeof(Camera));
            cameraObject.transform.SetParent(root, false);
            cameraObject.transform.localPosition = new Vector3(0f, 3.05f, -4.65f);
            cameraObject.transform.LookAt(new Vector3(0f, 0.78f, 0.22f));
            cameraObject.GetComponent<Camera>().enabled = false;
        }

        private static void CreatePreviewLights(Transform root)
        {
            var sun = new GameObject("Preview Directional Light", typeof(Light)).GetComponent<Light>();
            sun.transform.SetParent(root, false);
            sun.type = LightType.Directional;
            sun.intensity = 1.65f;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            CreatePreviewPointLight(root, "Preview Cyan Light", new Vector3(-2.4f, 2.4f, -1.8f), Color.cyan);
            CreatePreviewPointLight(root, "Preview Violet Light", new Vector3(2.4f, 2.6f, 1.8f), new Color(0.65f, 0.48f, 1f));
        }

        private static void CreatePreviewPointLight(Transform root, string name, Vector3 position, Color color)
        {
            var light = new GameObject(name, typeof(Light)).GetComponent<Light>();
            light.transform.SetParent(root, false);
            light.transform.localPosition = position;
            light.type = LightType.Point;
            light.color = color;
            light.intensity = 4.5f;
            light.range = 7f;
        }

        private static GameObject CreatePrimitive(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
        {
            var visual = GameObject.CreatePrimitive(type);
            visual.name = name;
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = position;
            visual.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            var renderer = visual.GetComponent<Renderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = color };
            return visual;
        }

        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
