// One-time project setup when the project opens: linear colour, a URP
// pipeline asset (with screen-space ambient occlusion) assigned to Graphics
// and Quality settings if URP is installed and none is set, and the game
// scene in the build settings, and reference materials so player builds
// keep the shader variants the game uses. Also available as Halcyon > Set Up
// Project.
// URP types are reached by reflection so this compiles without URP.
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Halcyon.EditorTools
{
    [InitializeOnLoad]
    public static class HalcyonProjectSetup
    {
        const string ScenePath = "Assets/Scenes/Halcyon.unity";
        const string SettingsDir = "Assets/Settings";

        static HalcyonProjectSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool("Halcyon.SetupDone", false)) return;
                SessionState.SetBool("Halcyon.SetupDone", true);
                Run(false);
            };
        }

        [MenuItem("Halcyon/Set Up Project")]
        public static void SetUpFromMenu() => Run(true);

        public static void Run(bool verbose)
        {
            try
            {
                if (PlayerSettings.colorSpace != ColorSpace.Linear)
                {
                    PlayerSettings.colorSpace = ColorSpace.Linear;
                    Debug.Log("Halcyon: switched the project to linear colour space.");
                }
                EnsureUrp(verbose);
                EnsureScene();
                EnsureShaderRefs();
            }
            catch (Exception e)
            {
                Debug.LogWarning("Halcyon: project setup failed: " + e);
            }
        }

        static Type T(string name) => Type.GetType(name);

        static void EnsureUrp(bool verbose)
        {
            if (GraphicsSettings.defaultRenderPipeline != null)
            {
                if (verbose) Debug.Log("Halcyon: a render pipeline asset is already assigned (" + GraphicsSettings.defaultRenderPipeline.name + ").");
                return;
            }
            var urpT = T("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime");
            var dataT = T("UnityEngine.Rendering.Universal.UniversalRendererData, Unity.RenderPipelines.Universal.Runtime");
            var baseDataT = T("UnityEngine.Rendering.Universal.ScriptableRendererData, Unity.RenderPipelines.Universal.Runtime");
            if (urpT == null || dataT == null || baseDataT == null)
            {
                Debug.Log("Halcyon: URP is not installed; using the built-in render pipeline. Add com.unity.render-pipelines.universal for post-processing.");
                return;
            }
            Directory.CreateDirectory(SettingsDir);
            var data = ScriptableObject.CreateInstance(dataT);
            data.name = "Halcyon_Renderer";
            AssetDatabase.CreateAsset(data, SettingsDir + "/Halcyon_Renderer.asset");
            // Screen-space ambient occlusion (the feature class may be internal).
            var ssaoT = T("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion, Unity.RenderPipelines.Universal.Runtime");
            if (ssaoT != null)
            {
                var ssao = ScriptableObject.CreateInstance(ssaoT);
                ssao.name = "SSAO";
                AssetDatabase.AddObjectToAsset(ssao, data);
                var list = Get(data, "rendererFeatures") as IList;
                list?.Add(ssao);
                var mapField = baseDataT.GetField("m_RendererFeatureMap", BindingFlags.NonPublic | BindingFlags.Instance);
                if (mapField?.GetValue(data) is IList map && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out string _, out long localId)) map.Add(localId);
            }
            var create = urpT.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null, new[] { baseDataT }, null);
            var asset = (RenderPipelineAsset)create.Invoke(null, new object[] { data });
            asset.name = "Halcyon_URP";
            AssetDatabase.CreateAsset(asset, SettingsDir + "/Halcyon_URP.asset");
            Set(asset, "supportsHDR", true);
            Set(asset, "shadowDistance", 450f);
            Set(asset, "shadowCascadeCount", 4);
            Set(asset, "msaaSampleCount", 4);
            Set(asset, "supportsSoftShadows", true);
            Set(asset, "mainLightShadowmapResolution", 4096);
            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
            AssetDatabase.SaveAssets();
            Debug.Log("Halcyon: created and assigned " + SettingsDir + "/Halcyon_URP.asset (HDR, 4 shadow cascades over 450 m, SSAO).");
        }

        static object Get(object o, string name)
        {
            var t = o.GetType();
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null) return p.GetValue(o);
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f?.GetValue(o);
        }

        static void Set(object o, string name, object value)
        {
            var t = o.GetType();
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            try
            {
                if (p != null && p.CanWrite)
                {
                    if (p.PropertyType.IsEnum) value = Enum.ToObject(p.PropertyType, value);
                    p.SetValue(o, value);
                }
            }
            catch (Exception e)
            {
                Debug.Log($"Halcyon: could not set {name}: {e.Message}");
            }
        }

        /// <summary>
        /// The game makes its materials in code (Shader.Find, keywords switched at
        /// runtime), but a player build keeps only the shader variants that assets
        /// use. A material asset per shader and keyword set the game uses, in a
        /// Resources folder, pulls those variants into builds.
        /// </summary>
        static void EnsureShaderRefs()
        {
            const string dir = "Assets/Halcyon/Art/Resources/ShaderRefs";
            var none = new string[0];
            var refs = new (string shader, string[] keywords, bool transparent)[]
            {
                ("Universal Render Pipeline/Lit", none, false),
                ("Universal Render Pipeline/Lit", new[] { "_NORMALMAP" }, false),
                ("Universal Render Pipeline/Lit", new[] { "_EMISSION" }, false),
                ("Universal Render Pipeline/Lit", new[] { "_NORMALMAP", "_EMISSION" }, false),
                ("Universal Render Pipeline/Lit", none, true),
                ("Universal Render Pipeline/Complex Lit", new[] { "_CLEARCOAT" }, false),
                ("Universal Render Pipeline/Unlit", none, true),
                ("Universal Render Pipeline/Particles/Unlit", none, true),
                ("Universal Render Pipeline/Terrain/Lit", none, false),
                ("Skybox/Procedural", none, false),
                ("Standard", none, false),
                ("Legacy Shaders/Particles/Alpha Blended", none, true),
            };
            int made = 0;
            for (int i = 0; i < refs.Length; i++)
            {
                var sh = Shader.Find(refs[i].shader);
                if (sh == null) continue;
                string path = $"{dir}/{refs[i].shader.Replace('/', '_').Replace(' ', '_')}_{string.Join("_", refs[i].keywords)}{(refs[i].transparent ? "_Transparent" : "")}.mat";
                if (File.Exists(path)) continue;
                Directory.CreateDirectory(dir);
                var m = new Material(sh);
                foreach (var k in refs[i].keywords) m.EnableKeyword(k);
                if (refs[i].transparent)
                {
                    m.SetFloat("_Surface", 1f);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.renderQueue = (int)RenderQueue.Transparent;
                }
                AssetDatabase.CreateAsset(m, path);
                made++;
            }
            if (made > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"Halcyon: made {made} shader reference materials in {dir} (player builds keep the variants the game uses).");
            }
        }

        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
                Debug.Log("Halcyon: created " + ScenePath + ". Press Play: the island builds itself.");
            }
            var scenes = EditorBuildSettings.scenes;
            foreach (var s in scenes) if (s.path == ScenePath) return;
            var list = new EditorBuildSettingsScene[scenes.Length + 1];
            list[0] = new EditorBuildSettingsScene(ScenePath, true);
            Array.Copy(scenes, 0, list, 1, scenes.Length);
            EditorBuildSettings.scenes = list;
        }

        [MenuItem("Halcyon/Open Game Scene")]
        public static void OpenScene()
        {
            EnsureScene();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
