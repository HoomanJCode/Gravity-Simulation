using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Generates one preset scene for each execution strategy.</summary>
public static class GravitySceneGeneration
{
    private const string TemplateScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Tools/Gravity Simulation/Generate Mode Scenes")]
    public static void Build()
    {
        var generatedScenes = new List<EditorBuildSettingsScene>();
        foreach (SimulationMode mode in System.Enum.GetValues(typeof(SimulationMode)))
        {
            EditorSceneManager.OpenScene(TemplateScenePath, OpenSceneMode.Single);
            var presetObject = new GameObject("Gravity Scene Mode");
            GravitySceneMode preset = presetObject.AddComponent<GravitySceneMode>();
            var serializedPreset = new SerializedObject(preset);
            serializedPreset.FindProperty("_initialMode").enumValueIndex = (int)mode;
            serializedPreset.ApplyModifiedPropertiesWithoutUndo();

            string scenePath = "Assets/Scenes/Gravity_" + SceneName(mode) + ".unity";
            if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), scenePath))
                throw new System.Exception("Could not save generated scene: " + scenePath);
            generatedScenes.Add(new EditorBuildSettingsScene(scenePath, true));
            Debug.Log("Generated preset scene for " + mode + ": " + scenePath);
        }

        var buildScenes = EditorBuildSettings.scenes.ToList();
        foreach (EditorBuildSettingsScene generated in generatedScenes)
        {
            if (!buildScenes.Any(scene => scene.path == generated.path))
                buildScenes.Add(generated);
        }
        EditorBuildSettings.scenes = buildScenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("GRAVITY_SCENES_GENERATED count=" + generatedScenes.Count);
    }

    private static string SceneName(SimulationMode mode)
    {
        switch (mode)
        {
            case SimulationMode.SingleThreaded: return "SingleThreaded";
            case SimulationMode.CSharpAsync: return "CSharpAsync";
            case SimulationMode.CSharpParallel: return "CSharpParallel";
            case SimulationMode.UnityModularMainThread: return "UnityModularMainThread";
            case SimulationMode.UnityJobs: return "UnityJobs";
            case SimulationMode.UnityCoroutine: return "UnityCoroutine";
            default: return mode.ToString();
        }
    }
}
