using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FishingSim.EditorTools
{
    /// <summary>
    /// Builds the game from the command line (GameCI's buildMethod): makes the bootstrap scene (one empty object: the
    /// game builds itself from code, see GameRoot), sets the player settings (name, version, linear colour, input) and
    /// builds the player to -customBuildPath for -buildTarget.
    /// </summary>
    public static class BuildGame
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";

        private static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        public static void PrepareProject()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Boot");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            string version = File.Exists("../tools/release/VERSION") ? File.ReadAllText("../tools/release/VERSION").Trim() : Arg("-buildVersion", "0.9.2");
            string fromArg = Arg("-buildVersion", "");
            if (fromArg.Length > 0 && fromArg != "none")
                version = fromArg.TrimStart('v');
            PlayerSettings.productName = "Fishing Sim";
            PlayerSettings.companyName = "Krystian Naskreski";
            PlayerSettings.bundleVersion = version;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SetApiCompatibilityLevel(UnityEditor.Build.NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            // Windows: Direct3D 11 first (no Vulkan: the Godot build's crashes came from Vulkan shader compiling)
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11, UnityEngine.Rendering.GraphicsDeviceType.Direct3D12 });
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Icon/icon.png");
            if (icon != null)
                PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            IncludeGltfShaders();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The models are read at run time (glTFast from StreamingAssets), so nothing in the scenes references glTFast's
        /// shaders: they and its shader variant collections are added to the graphics settings, or the build strips them.
        /// </summary>
        private static void IncludeGltfShaders()
        {
            var gs = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            if (gs == null)
            {
                Debug.LogWarning("GraphicsSettings not found");
                return;
            }
            var so = new SerializedObject(gs);
            var always = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in new[] { "glTF/PbrMetallicRoughness", "glTF/PbrSpecularGlossiness", "glTF/Unlit" })
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogWarning("glTFast shader missing: " + name);
                    continue;
                }
                bool has = false;
                for (int i = 0; i < always.arraySize; i++)
                    has |= always.GetArrayElementAtIndex(i).objectReferenceValue == shader;
                if (!has)
                {
                    always.arraySize++;
                    always.GetArrayElementAtIndex(always.arraySize - 1).objectReferenceValue = shader;
                    Debug.Log("Always included: " + name);
                }
            }
            var preloaded = so.FindProperty("m_PreloadedShaders");
            foreach (var guid in AssetDatabase.FindAssets("t:ShaderVariantCollection", new[] { "Packages/com.unity.cloud.gltfast" }))
            {
                var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(AssetDatabase.GUIDToAssetPath(guid));
                if (svc == null)
                    continue;
                preloaded.arraySize++;
                preloaded.GetArrayElementAtIndex(preloaded.arraySize - 1).objectReferenceValue = svc;
                Debug.Log("Preloaded shader variants: " + AssetDatabase.GUIDToAssetPath(guid));
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void Build()
        {
            PrepareProject();
            string targetName = Arg("-buildTarget", "StandaloneWindows64");
            var target = (BuildTarget)Enum.Parse(typeof(BuildTarget), targetName);
            string path = Arg("-customBuildPath", $"build/{targetName}/FishingSim");
            if (target == BuildTarget.StandaloneWindows64 && !path.EndsWith(".exe"))
                path += ".exe";
            if (target == BuildTarget.StandaloneLinux64 && !path.EndsWith(".x86_64"))
                path += ".x86_64";
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                locationPathName = path,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"Build {report.summary.result}: {report.summary.totalSize / 1048576} MB, {report.summary.totalErrors} errors -> {path}");
            if (report.summary.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                    foreach (var m in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                        Debug.LogError(m.content);
                EditorApplication.Exit(1);
            }
        }
    }
}
