using UnityEngine;
using UnityEngine.Rendering;

namespace ArcanePuck
{
    public static class ProceduralTheme
    {
        public static Material CreateLitMaterial(Color color, float metallic, float smoothness)
        {
            var shader = FindRequiredShader(
                "Universal Render Pipeline/Lit",
                "Standard");
            var material = new Material(shader)
            {
                color = color
            };
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        public static Material CreateGlowMaterial(Color color, bool transparent = false)
        {
            var material = CreateLitMaterial(color, 0.18f, 0.75f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 2.2f);
            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.One);
                material.SetFloat("_ZWrite", 0f);
                material.renderQueue = (int)RenderQueue.Transparent;
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                color.a = 0.75f;
                material.color = color;
            }
            return material;
        }

        public static Material CreateParticleMaterial()
        {
            var shader = FindRequiredShader(
                "Universal Render Pipeline/Particles/Unlit",
                "Particles/Standard Unlit");
            var material = new Material(shader);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 1f);
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        private static Shader FindRequiredShader(string preferredName, string fallbackName)
        {
            var shader = Shader.Find(preferredName) ?? Shader.Find(fallbackName);
            if (shader == null)
            {
                throw new System.InvalidOperationException(
                    $"Arcane Puck requires shader '{preferredName}' or '{fallbackName}' in the player build.");
            }

            return shader;
        }

        public static GameObject CreatePrimitiveVisual(
            string name,
            PrimitiveType primitive,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            var visual = GameObject.CreatePrimitive(primitive);
            visual.name = name;
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = localPosition;
            visual.transform.localScale = localScale;
            Object.Destroy(visual.GetComponent<Collider>());
            visual.GetComponent<Renderer>().sharedMaterial = material;
            return visual;
        }

        public static Transform CreateVisualSlot(string slot, Transform parent, GameObject replacement)
        {
            var root = new GameObject($"Visual_{slot}");
            root.transform.SetParent(parent, false);
            root.AddComponent<VisualSlot>().Initialize(slot);
            if (replacement != null)
            {
                var instance = Object.Instantiate(replacement, root.transform);
                instance.name = replacement.name;
                RemoveReplacementColliders(instance);
            }
            return root.transform;
        }

        public static Transform AttachSceneVisual(string slot, Transform parent, Transform sceneVisual)
        {
            var root = new GameObject($"Visual_{slot}");
            root.transform.SetParent(parent, false);
            root.AddComponent<VisualSlot>().Initialize(slot);
            if (sceneVisual == null) return root.transform;
            sceneVisual.SetParent(root.transform, false);
            RemoveReplacementColliders(sceneVisual.gameObject);
            return root.transform;
        }

        private static void RemoveReplacementColliders(GameObject instance)
        {
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                Object.Destroy(collider);
        }

        private static void FitReplacement(Transform slot, Vector3 targetSize, bool preserveY = false)
        {
            var renderers = slot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            var size = bounds.size;
            var source = preserveY ? new Vector3(size.x, 0f, size.z) : size;
            var target = preserveY ? new Vector3(targetSize.x, 0f, targetSize.z) : targetSize;
            var ratio = Mathf.Min(
                source.x > 0.0001f ? target.x / source.x : float.MaxValue,
                source.z > 0.0001f ? target.z / source.z : float.MaxValue,
                !preserveY && source.y > 0.0001f ? target.y / source.y : float.MaxValue);
            if (float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio <= 0f) return;
            slot.localScale *= ratio;
        }

        public static void BuildTableVisual(Transform parent, TableConfig config, ThemeConfig theme, Transform sceneVisual = null)
        {
            if (sceneVisual != null)
            {
                AttachSceneVisual(ThemeConfig.TableModelSlot, parent, sceneVisual);
                return;
            }
            var slot = CreateVisualSlot(ThemeConfig.TableModelSlot, parent, theme.TABLE_MODEL);
            if (theme.TABLE_MODEL != null)
            {
                FitReplacement(slot, new Vector3(config.width + 0.25f, 1.2f, config.length + 0.25f), true);
                return;
            }
        }

        public static Transform BuildMalletVisual(Transform parent, TableConfig config, ThemeConfig theme, bool player, Transform sceneVisual = null)
        {
            if (sceneVisual != null)
                return AttachSceneVisual(player ? ThemeConfig.PlayerMalletModelSlot : ThemeConfig.AIMalletModelSlot, parent, sceneVisual);
            var replacement = player
                ? (theme.PLAYER_MALLET_MODEL ?? theme.MALLET_MODEL)
                : (theme.AI_MALLET_MODEL ?? theme.MALLET_MODEL);
            var slot = CreateVisualSlot(player ? ThemeConfig.PlayerMalletModelSlot : ThemeConfig.AIMalletModelSlot, parent, replacement);
            if (replacement != null)
            {
                FitReplacement(slot, new Vector3(config.malletRadius * 2.8f, config.malletHeight + config.malletRadius * 1.4f, config.malletRadius * 2.8f));
                return slot;
            }
            return slot;
        }

        public static TrailRenderer BuildPuckVisual(Transform parent, TableConfig config, ThemeConfig theme, Transform sceneVisual = null)
        {
            var slot = sceneVisual != null
                ? AttachSceneVisual(ThemeConfig.PuckModelSlot, parent, sceneVisual)
                : CreateVisualSlot(ThemeConfig.PuckModelSlot, parent, theme.PUCK_MODEL);
            if (sceneVisual == null && theme.PUCK_MODEL != null)
            {
                FitReplacement(slot, new Vector3(config.puckRadius * 2f, config.puckHeight, config.puckRadius * 2f));
            }

            var trail = slot.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.3f;
            trail.startWidth = config.puckRadius * 0.9f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.018f;
            trail.alignment = LineAlignment.View;
            trail.sharedMaterial = CreateGlowMaterial(theme.cyan, true);
            trail.shadowCastingMode = ShadowCastingMode.Off;
            return trail;
        }

        public static void BuildGoalVisual(Transform parent, TableConfig config, ThemeConfig theme, bool playerGoal)
        {
            var slot = CreateVisualSlot(ThemeConfig.GoalModelSlot, parent, theme.GOAL_MODEL);
            var z = (playerGoal ? -1f : 1f) * (config.HalfLength + 0.016f);
            slot.localPosition = new Vector3(0f, 0f, z);
            if (theme.GOAL_MODEL != null) return;

            var accent = playerGoal ? theme.cyan : theme.violet;
            var metal = CreateLitMaterial(theme.metal, 0.82f, 0.76f);
            var glow = CreateGlowMaterial(accent, true);
            var postScale = new Vector3(0.07f, config.goalHeight, 0.07f);
            CreatePrimitiveVisual("Portal Left Post", PrimitiveType.Cube, slot,
                new Vector3(-config.goalWidth * 0.5f, config.goalHeight * 0.5f, 0f), postScale, metal);
            CreatePrimitiveVisual("Portal Right Post", PrimitiveType.Cube, slot,
                new Vector3(config.goalWidth * 0.5f, config.goalHeight * 0.5f, 0f), postScale, metal);
            CreatePrimitiveVisual("Portal Crown", PrimitiveType.Cube, slot,
                new Vector3(0f, config.goalHeight, 0f), new Vector3(config.goalWidth + 0.12f, 0.07f, 0.07f), metal);
            CreatePrimitiveVisual("Portal Veil", PrimitiveType.Cube, slot,
                new Vector3(0f, config.goalHeight * 0.48f, 0f),
                new Vector3(config.goalWidth * 0.92f, config.goalHeight * 0.82f, 0.012f), glow);
        }

        public static void BuildArena(Transform parent, TableConfig config, ThemeConfig theme)
        {
            var slot = CreateVisualSlot(ThemeConfig.ArenaModelSlot, parent, theme.ARENA_MODEL);
            if (theme.ARENA_MODEL != null) return;

            var stone = CreateLitMaterial(theme.floorStone, 0.08f, 0.32f);
            var wall = CreateLitMaterial(theme.hallStone, 0.14f, 0.38f);
            CreatePrimitiveVisual("Duel Hall Floor", PrimitiveType.Cube, slot,
                new Vector3(0f, -0.87f, 0f), new Vector3(10f, 0.12f, 12f), stone);
            foreach (var x in new[] { -3.7f, 3.7f })
            foreach (var z in new[] { -4.3f, -1.5f, 1.5f, 4.3f })
            {
                CreatePrimitiveVisual("Academy Pillar", PrimitiveType.Cylinder, slot,
                    new Vector3(x, 0.5f, z), new Vector3(0.48f, 1.8f, 0.48f), wall);
            }

            CreatePrimitiveVisual("Back Duel Hall Wall", PrimitiveType.Cube, slot,
                new Vector3(0f, 1.2f, 5.5f), new Vector3(9f, 4.2f, 0.35f), wall);
        }

        public static void BuildWizard(Transform parent, ThemeConfig theme)
        {
            var slot = CreateVisualSlot(ThemeConfig.AIOpponentModelSlot, parent, theme.AI_OPPONENT_MODEL);
            slot.localPosition = new Vector3(0f, -0.82f, 2.85f);
            slot.localRotation = Quaternion.Euler(0f, 180f, 0f);
            if (theme.AI_OPPONENT_MODEL != null) return;

            var glow = CreateGlowMaterial(theme.violet);
            CreatePrimitiveVisual("Wizard Orb", PrimitiveType.Sphere, slot,
                new Vector3(0.62f, 1.15f, 0f), Vector3.one * 0.17f, glow);
        }
    }
}
