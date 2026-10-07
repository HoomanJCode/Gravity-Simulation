using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates the small standalone scene used by the batch-processing presentation.</summary>
public static class BatchProcessingSceneGeneration
{
    private const string ScenePath = "Assets/Examples/BatchProcessing/Scenes/BatchProcessing.unity";

    [MenuItem("Tools/Batch Processing/Create Presentation Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "BatchProcessing";

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.055f, 0.09f);
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        cameraObject.AddComponent<AudioListener>();

        var lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var appObject = new GameObject("Batch Processing App");
        appObject.AddComponent<BatchProcessingApp>();
        var moduleObject = new GameObject("Processing Module");
        moduleObject.transform.SetParent(appObject.transform);
        moduleObject.AddComponent<ProcessingModule>();

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new System.Exception("Could not save batch-processing scene: " + ScenePath);

        var buildScenes = EditorBuildSettings.scenes.ToList();
        if (!buildScenes.Any(item => item.path == ScenePath))
            buildScenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = buildScenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("BATCH_PROCESSING_SCENE_GENERATED path=" + ScenePath);
    }
}
