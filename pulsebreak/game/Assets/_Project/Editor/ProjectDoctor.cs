using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Pulsebreak.Client.FiringRange;
using Pulsebreak.Client.UI;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.EditorTools
{
    /// <summary>
    /// Checks that the project is set up the way the game expects and quietly fixes what it
    /// can: render pipeline assigned, linear colour space, layer names, the firing range
    /// scene present and wired. It runs once each time the Editor opens the project, and on
    /// demand from the Pulsebreak menu.
    ///
    /// It only ever fills in things that are missing. It never overwrites a choice you made.
    /// </summary>
    public static class ProjectDoctor
    {
        const string ScenePath = "Assets/_Project/Scenes/FiringRange.unity";
        const string PipelinePath = "Assets/_Project/Rendering/PB_URP_Asset.asset";
        const string MovementPath = "Assets/_Project/Content/Movement/DefaultMovement.asset";
        const string CatalogPath = "Assets/_Project/Content/Loadouts/LoadoutCatalog.asset";
        const string HudPanelPath = "Assets/_Project/Resources/" + HudController.PanelResource + ".asset";
        const string HudThemePath = "Assets/_Project/Resources/" + HudController.ThemeResource + ".tss";
        const string SessionKey = "Pulsebreak.ProjectDoctor.Ran";

        [InitializeOnLoadMethod]
        static void OnEditorLoad()
        {
            if (SessionState.GetBool(SessionKey, false)) return;

            // Wait until the Editor is idle: on a first open, assets are still importing here.
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool(SessionKey, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
                SessionState.SetBool(SessionKey, true);
                Run(firstOpen: true);
            };
        }

        [MenuItem("Pulsebreak/Run Project Doctor")]
        static void RunFromMenu()
        {
            List<string> report = Run(firstOpen: false);
            if (report.Count == 0) Debug.Log("Pulsebreak Project Doctor: everything is set up correctly.");
        }

        [MenuItem("Pulsebreak/Validate Content")]
        static void ValidateContentFromMenu() => ValidateContent(showAllClear: true);

        /// <summary>Checks every weapon, attachment, utility item, perk set and preset, and logs anything that looks wrong.</summary>
        static void ValidateContent(bool showAllClear)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LoadoutCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogWarning("Pulsebreak: no loadout catalog at " + CatalogPath + ". The firing range will use stand-in equipment.");
                return;
            }

            List<string> problems = LoadoutValidator.Validate(catalog);
            foreach (string problem in problems) Debug.LogWarning("Pulsebreak content: " + problem, catalog);
            if (problems.Count == 0 && showAllClear)
            {
                Debug.Log("Pulsebreak: all content is valid: " + catalog.weapons.weapons.Length + " weapons, " +
                          catalog.weapons.attachments.Length + " attachments, " + catalog.utilities.Length + " utility items, " +
                          catalog.perkSets.Length + " perk sets, " + catalog.presets.Length + " presets.");
            }
        }

        [MenuItem("Pulsebreak/Open Saved Loadouts Folder")]
        static void OpenSaveFolder() => EditorUtility.RevealInFinder(Application.persistentDataPath);

        [MenuItem("Pulsebreak/Open Firing Range")]
        static void OpenFiringRange()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath)) CreateFiringRangeScene();
            else EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Pulsebreak/Rebuild Firing Range Scene")]
        static void RebuildFiringRange()
        {
            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Rebuild firing range scene",
                    "This replaces " + ScenePath + " with a fresh one. Anything you added to that scene is lost.",
                    "Rebuild", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateFiringRangeScene();
        }

        static List<string> Run(bool firstOpen)
        {
            var report = new List<string>();

            EnsureRenderPipeline(report);
            EnsureColorSpace(report);
            EnsureLayerNames(report);
            EnsureHudPanel(report);
            EnsureScene(report, firstOpen);
            EnsureSceneInBuild(report);
            ValidateContent(showAllClear: false);

            foreach (string line in report) Debug.Log("Pulsebreak Project Doctor: " + line);
            return report;
        }

        static void EnsureRenderPipeline(List<string> report)
        {
            if (GraphicsSettings.defaultRenderPipeline != null) return;

            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                Debug.LogWarning("Pulsebreak Project Doctor: no render pipeline is assigned and " + PipelinePath +
                                 " could not be loaded. The game still runs (with the Built-in pipeline). To use URP, " +
                                 "create one with Assets > Create > Rendering > URP Asset (with Universal Renderer) and " +
                                 "assign it under Project Settings > Graphics.");
                return;
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            report.Add("assigned the URP asset as the default render pipeline.");
        }

        static void EnsureColorSpace(List<string> report)
        {
            if (PlayerSettings.colorSpace == ColorSpace.Linear) return;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            report.Add("switched the colour space to Linear.");
        }

        static void EnsureLayerNames(List<string> report)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;

            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null || !layers.isArray) return;

            bool changed = NameLayer(layers, GameLayers.Character, "Character");
            changed |= NameLayer(layers, GameLayers.Hitbox, "Hitbox");
            if (!changed) return;

            tagManager.ApplyModifiedPropertiesWithoutUndo();
            report.Add("named physics layers 8 (Character) and 9 (Hitbox).");
        }

        /// <summary>
        /// Saves the HUD's panel settings as an asset. The game can build one in memory, but
        /// only a saved asset carries the shader references a built player needs to draw UI.
        /// </summary>
        static void EnsureHudPanel(List<string> report)
        {
            if (AssetDatabase.LoadAssetAtPath<PanelSettings>(HudPanelPath) != null) return;

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(HudThemePath);
            if (theme == null)
            {
                Debug.LogWarning("Pulsebreak Project Doctor: " + HudThemePath + " did not import, so HUD text may not draw. " +
                                 "Create a theme with Assets > Create > UI Toolkit > TSS Theme File and save it at that path.");
                return;
            }

            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            HudController.ConfigurePanel(panel, theme);
            AssetDatabase.CreateAsset(panel, HudPanelPath);
            AssetDatabase.SaveAssets();
            report.Add("created the HUD panel settings asset.");
        }

        static bool NameLayer(SerializedProperty layers, int index, string name)
        {
            if (index >= layers.arraySize) return false;
            SerializedProperty layer = layers.GetArrayElementAtIndex(index);
            if (!string.IsNullOrEmpty(layer.stringValue)) return false;
            layer.stringValue = name;
            return true;
        }

        static void EnsureScene(List<string> report, bool firstOpen)
        {
            if (!File.Exists(ScenePath))
            {
                // Only take over the Editor's scene when nothing of the user's is open.
                Scene active = SceneManager.GetActiveScene();
                if (!string.IsNullOrEmpty(active.path) || active.isDirty)
                {
                    Debug.LogWarning("Pulsebreak Project Doctor: " + ScenePath + " is missing. Use Pulsebreak > Rebuild Firing Range Scene to create it.");
                    return;
                }
                CreateFiringRangeScene();
                report.Add("created the firing range scene.");
                return;
            }

            if (firstOpen)
            {
                Scene active = SceneManager.GetActiveScene();
                if (string.IsNullOrEmpty(active.path) && !active.isDirty)
                    EditorSceneManager.OpenScene(ScenePath);
            }

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) return;

            FiringRangeBootstrap bootstrap = FindBootstrap(scene);
            if (bootstrap == null)
            {
                Debug.LogWarning("Pulsebreak Project Doctor: the firing range scene has no FiringRangeBootstrap, so pressing Play " +
                                 "will show an empty scene. Use Pulsebreak > Rebuild Firing Range Scene to fix it.");
                return;
            }

            if (AssignDefaults(bootstrap))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                report.Add("filled in the firing range's missing movement config or loadout catalog.");
            }
        }

        static FiringRangeBootstrap FindBootstrap(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var bootstrap = root.GetComponentInChildren<FiringRangeBootstrap>(true);
                if (bootstrap != null) return bootstrap;
            }
            return null;
        }

        /// <summary>Points empty asset slots at the default assets. Returns true if anything changed.</summary>
        static bool AssignDefaults(FiringRangeBootstrap bootstrap)
        {
            var serialized = new SerializedObject(bootstrap);
            bool changed = AssignIfEmpty(serialized.FindProperty("movement"), AssetDatabase.LoadAssetAtPath<MovementConfig>(MovementPath));
            changed |= AssignIfEmpty(serialized.FindProperty("catalog"), AssetDatabase.LoadAssetAtPath<LoadoutCatalog>(CatalogPath));
            if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }

        static bool AssignIfEmpty(SerializedProperty property, Object asset)
        {
            if (property == null || asset == null || property.objectReferenceValue != null) return false;
            property.objectReferenceValue = asset;
            return true;
        }

        static void CreateFiringRangeScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var bootstrap = new GameObject("FiringRange").AddComponent<FiringRangeBootstrap>();
            AssignDefaults(bootstrap);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static void EnsureSceneInBuild(List<string> report)
        {
            if (!File.Exists(ScenePath)) return;

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            foreach (EditorBuildSettingsScene entry in scenes)
            {
                if (entry.path == ScenePath) return;
            }

            var updated = new List<EditorBuildSettingsScene>(scenes) { new EditorBuildSettingsScene(ScenePath, true) };
            EditorBuildSettings.scenes = updated.ToArray();
            report.Add("added the firing range scene to the build.");
        }
    }
}
