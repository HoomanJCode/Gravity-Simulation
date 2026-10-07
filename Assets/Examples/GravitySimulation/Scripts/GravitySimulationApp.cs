using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// Boots and presents the gravity benchmark. The starter scene stays deliberately small;
/// this component builds its demo objects at runtime so a fresh clone is immediately usable.
/// </summary>
public sealed class GravitySimulationApp : MonoBehaviour
{
    private const int MinimumBodyCount = 8;
    private const int MaximumBodyCount = 512;
    private const float DefaultGravity = 1.15f;
    private const float DefaultSoftening = 0.2f;
    private static Material _gridMaterial;

    private static readonly string[] ModeNames =
    {
        "Single threaded", "C# async", "C# parallel",
        "Unity modular main thread", "Unity Jobs (no Burst)", "Unity coroutine",
        "Unity Jobs (Burst)"
    };

    private readonly List<GravityBodyView> _views = new List<GravityBodyView>();
    private GameObject _gridRoot;
    private BodyState[] _initialStates;
    private IGravityStepper _stepper;
    private ProfilerMarker _stepMarker;
    private SimulationMode _mode = SimulationMode.SingleThreaded;
    private SimulationDimension _dimension = SimulationDimension.ThreeD;
    private int _bodyCount = 128;
    private bool _isRunning = true;
    private float _timeScale = 1f;
    private float _gravity = DefaultGravity;
    private float _softening = DefaultSoftening;
    private float _smoothedFps;
    private GUIStyle _panelStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _selectedButtonStyle;

    public SimulationMode Mode { get { return _mode; } }
    public SimulationDimension Dimension { get { return _dimension; } }
    internal GravityBodyView[] GetBodyViews() { return _views.ToArray(); }

    /// <summary>Applies a scene's preset before the first simulation update.</summary>
    public void SetModePreset(SimulationMode mode)
    {
        SelectMode(mode);
    }

    /// <summary>Creates the controller when the project is launched from its starter scene.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureDemoIsRunning()
    {
        if (FindAnyObjectByType<GravitySimulationApp>() != null)
            return;

        GravitySceneMode sceneMode = FindAnyObjectByType<GravitySceneMode>();
        // The comparison example has its own controller and should not boot gravity too.
        if (sceneMode == null)
            return;

        var appObject = new GameObject("Gravity Simulation App");
        var app = appObject.AddComponent<GravitySimulationApp>();
        app.SetModePreset(sceneMode.InitialMode);
    }

    private void Awake()
    {
        ConfigureCamera();
        CreateInitialStates();
        RebuildWorld();
        SelectMode(_mode);
    }

    private void Update()
    {
        if (_stepper is CoroutineGravityStepper)
            ((CoroutineGravityStepper)_stepper).Paused = !_isRunning;

        if (_isRunning && _stepper != null)
        {
            using (_stepMarker.Auto())
                _stepper.Step(Time.deltaTime * _timeScale, _gravity, _softening, _dimension);

            ApplyBodyViews();
        }

        float fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        _smoothedFps = Mathf.Lerp(_smoothedFps, fps, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 5f));
    }

    private void OnDestroy()
    {
        if (_stepper != null)
            _stepper.Dispose();
    }

    private void ConfigureCamera()
    {
        Camera sceneCamera = Camera.main;
        if (sceneCamera == null)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            sceneCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        sceneCamera.clearFlags = CameraClearFlags.SolidColor;
        sceneCamera.backgroundColor = new Color(0.035f, 0.055f, 0.09f);
        sceneCamera.transform.position = new Vector3(0f, 5.5f, -16f);
        sceneCamera.transform.rotation = Quaternion.Euler(16f, 0f, 0f);
        sceneCamera.fieldOfView = 55f;
    }

    private void CreateInitialStates()
    {
        _initialStates = GravityStateFactory.Create(_bodyCount, _dimension);
    }

    private void RebuildWorld()
    {
        DisposeStepper();
        for (int i = 0; i < _views.Count; i++)
            if (_views[i] != null)
                Destroy(_views[i].gameObject);
        _views.Clear();

        _initialStates = GravityStateFactory.Create(_bodyCount, _dimension);
        for (int i = 0; i < _initialStates.Length; i++)
        {
            var bodyObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bodyObject.name = "Gravity Body " + i.ToString("D3");
            Collider collider = bodyObject.GetComponent<Collider>();
            Destroy(collider);

            float scale = Mathf.Clamp(0.28f * Mathf.Pow(128f / _bodyCount, 0.22f), 0.11f, 0.32f);
            bodyObject.transform.localScale = Vector3.one * scale;
            var view = bodyObject.AddComponent<GravityBodyView>();
            view.Initialize(_initialStates[i], ColorForIndex(i));
            view.SetStateIndex(i);
            _views.Add(view);
        }

        CreateGrid();
        SelectMode(_mode);
    }

    private void CreateGrid()
    {
        if (_gridRoot != null)
            Destroy(_gridRoot);
        _gridRoot = new GameObject("Simulation Grid");
        float span = _dimension == SimulationDimension.TwoD ? 11f : 12f;
        int lines = 20;
        for (int i = 0; i <= lines; i++)
        {
            float offset = Mathf.Lerp(-span * 0.5f, span * 0.5f, i / (float)lines);
            CreateGridLine(_gridRoot.transform, new Vector3(-span * 0.5f, -span * 0.5f, 0f),
                new Vector3(span * 0.5f, -span * 0.5f, 0f), offset, true);
            CreateGridLine(_gridRoot.transform, new Vector3(-span * 0.5f, -span * 0.5f, 0f),
                new Vector3(-span * 0.5f, span * 0.5f, 0f), offset, false);
        }

        if (_dimension == SimulationDimension.ThreeD)
            _gridRoot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private static void CreateGridLine(Transform parent, Vector3 start, Vector3 end, float offset, bool horizontal)
    {
        var lineObject = new GameObject("Grid line");
        lineObject.transform.SetParent(parent, false);
        var line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = 2;
        line.SetPosition(0, horizontal ? new Vector3(start.x, offset, 0f) : new Vector3(offset, start.y, 0f));
        line.SetPosition(1, horizontal ? new Vector3(end.x, offset, 0f) : new Vector3(offset, end.y, 0f));
        line.startWidth = line.endWidth = 0.012f;
        if (_gridMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
                _gridMaterial = new Material(shader) { name = "Gravity Grid Shared Material", hideFlags = HideFlags.HideAndDontSave };
        }
        line.sharedMaterial = _gridMaterial;
        line.startColor = line.endColor = new Color(0.16f, 0.28f, 0.4f, 0.4f);
    }

    private static Color ColorForIndex(int index)
    {
        return Color.HSVToRGB((index * 0.6180339f) % 1f, 0.68f, 1f);
    }

    private void ApplyBodyViews()
    {
        BodyState[] states = _stepper.States;
        int count = Mathf.Min(states.Length, _views.Count);
        for (int i = 0; i < count; i++)
            _views[i].ApplyState(states[i], _dimension);
    }

    private void SelectMode(SimulationMode mode)
    {
        DisposeStepper();
        _mode = mode;
        _stepMarker = new ProfilerMarker("GravitySimulation." + ModeNames[(int)mode].Replace(' ', '.'));
        _stepper = GravityStepperFactory.Create(mode, this, _initialStates);
    }

    private void DisposeStepper()
    {
        if (_stepper == null)
            return;
        _stepper.Dispose();
        _stepper = null;
    }

    private void ResetSimulation()
    {
        RebuildWorld();
    }

    private void ChangeDimension(SimulationDimension dimension)
    {
        _dimension = dimension;
        ResetSimulation();
        ConfigureCamera();
    }

    private void EnsureStyles()
    {
        if (_panelStyle != null)
            return;
        _panelStyle = new GUIStyle(GUI.skin.box);
        _panelStyle.normal.background = MakeTexture(new Color(0.035f, 0.065f, 0.11f, 0.94f));
        _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = new Color(0.77f, 0.84f, 0.92f) } };
        _buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, fixedHeight = 32 };
        _selectedButtonStyle = new GUIStyle(_buttonStyle);
        _selectedButtonStyle.normal.background = MakeTexture(new Color(0.12f, 0.52f, 0.88f));
        _selectedButtonStyle.hover.background = _selectedButtonStyle.normal.background;
        _selectedButtonStyle.normal.textColor = Color.white;
    }

    private static Texture2D MakeTexture(Color color)
    {
        var texture = new Texture2D(1, 1);
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private void OnGUI()
    {
        EnsureStyles();
        float width = Mathf.Min(480f, Screen.width - 32f);
        GUILayout.BeginArea(new Rect(16f, 16f, width, Screen.height - 32f), _panelStyle);
        GUILayout.Space(10);
        GUILayout.Label("GRAVITY LAB", _titleStyle);
        GUILayout.Label("N-body simulation · execution strategy profiler", _labelStyle);
        GUILayout.Space(10);

        GUILayout.Label("DIMENSION", _labelStyle);
        GUILayout.BeginHorizontal();
        DrawChoice("2D plane", _dimension == SimulationDimension.TwoD, () => ChangeDimension(SimulationDimension.TwoD));
        DrawChoice("3D space", _dimension == SimulationDimension.ThreeD, () => ChangeDimension(SimulationDimension.ThreeD));
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.Label("PROCESSING MODE", _labelStyle);
        for (int row = 0; row < 4; row++)
        {
            GUILayout.BeginHorizontal();
            for (int column = 0; column < 2; column++)
            {
                int index = row * 2 + column;
                if (index >= ModeNames.Length)
                    break;
                DrawChoice(ModeNames[index], (int)_mode == index, () => SelectMode((SimulationMode)index));
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(_isRunning ? "Pause" : "Resume", _buttonStyle))
            _isRunning = !_isRunning;
        if (GUILayout.Button("Reset scene", _buttonStyle))
            ResetSimulation();
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.Label("Bodies: " + _bodyCount + "   (drag to change, then Reset)", _labelStyle);
        int requestedCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(_bodyCount, MinimumBodyCount, MaximumBodyCount));
        requestedCount = Mathf.Clamp(((requestedCount + 7) / 8) * 8, MinimumBodyCount, MaximumBodyCount);
        if (requestedCount != _bodyCount)
            _bodyCount = requestedCount;
        GUILayout.Label("Simulation speed: " + _timeScale.ToString("0.0") + "×", _labelStyle);
        _timeScale = GUILayout.HorizontalSlider(_timeScale, 0.1f, 2.5f);

        GUILayout.Space(10);
        GUILayout.Label("LIVE PROFILE", _labelStyle);
        GUILayout.Label("Mode: " + ModeNames[(int)_mode], _labelStyle);
        GUILayout.Label("Dimension: " + (_dimension == SimulationDimension.TwoD ? "2D plane" : "3D space"), _labelStyle);
        GUILayout.Label("Bodies: " + _bodyCount + "   Pair evaluations: " + (long)_bodyCount * (_bodyCount - 1), _labelStyle);
        GUILayout.Label("Physics step: " + (_stepper == null ? "—" : _stepper.LastStepMilliseconds.ToString("0.000") + " ms"), _labelStyle);
        GUILayout.Label("Frame time (avg): " + (1000f / Mathf.Max(_smoothedFps, 0.01f)).ToString("0.00") + " ms   FPS: " + _smoothedFps.ToString("0"), _labelStyle);
        GUILayout.Label("Profiler marker: GravitySimulation." + ModeNames[(int)_mode].Replace(' ', '.'), _labelStyle);
        if (_mode == SimulationMode.UnityJobsBurst)
            GUILayout.Label("Burst compiler: " + (BurstCompiler.IsEnabled ? "enabled" : "disabled (managed fallback)"), _labelStyle);
        GUILayout.Space(5);
        GUILayout.Label("Tip: open Window → Analysis → Profiler and compare modes at the same body count.", _labelStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label("Newtonian attraction · softened inverse-square force · no collisions", _labelStyle);
        GUILayout.EndArea();
    }

    private void DrawChoice(string text, bool selected, Action onClick)
    {
        if (GUILayout.Button(text, selected ? _selectedButtonStyle : _buttonStyle))
            onClick();
    }
}
