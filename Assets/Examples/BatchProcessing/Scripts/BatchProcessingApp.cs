using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// A small presentation demo: every button transforms the same list of numbers,
/// but each mode schedules that work differently.
/// </summary>
public sealed class BatchProcessingApp : MonoBehaviour
{
    private static readonly string[] ModeNames =
    {
        "1. Single threaded", "2. C# async", "3. C# parallel",
        "4. Unity modular main thread", "5. Unity Jobs (no Burst)",
        "6. Unity coroutine", "7. Unity Jobs (Burst)"
    };
    private static readonly ProfilerMarker AsyncWorkerMarker =
        new ProfilerMarker("BatchProcessing.CSharpAsync.Worker");

    private const int DefaultItemCount = 100000;
    private const int DefaultIterations = 100;
    private const int CoroutineChunkSize = 4096;

    [SerializeField] private ProcessingModule _module;

    private BatchMode _mode;
    private int _itemCount = DefaultItemCount;
    private int _iterations = DefaultIterations;
    private float[] _values;
    private Task<double> _worker;
    private Coroutine _coroutine;
    private ProfilerMarker _marker;
    private string _status = "Choose a mode and press Run batch.";
    private string _checksum = "—";
    private double _computeMilliseconds;
    private double _wallMilliseconds;
    private float _progress;
    private bool _running;
    private GUIStyle _panel;
    private GUIStyle _title;
    private GUIStyle _label;
    private GUIStyle _button;
    private GUIStyle _selectedButton;

    private void Awake()
    {
        if (_module == null)
            _module = GetComponentInChildren<ProcessingModule>();
        if (_module == null)
            _module = gameObject.AddComponent<ProcessingModule>();
        _marker = new ProfilerMarker("BatchProcessing." + ModeNames[(int)_mode].Replace(' ', '.'));
    }

    private void Update()
    {
        if (_mode == BatchMode.CSharpAsync && _running)
            _progress = Volatile.Read(ref _completedItems) / (float)_itemCount;

        // A Task must never touch Unity objects; the main thread collects it here.
        if (_worker != null && _worker.IsCompleted)
        {
            if (_worker.IsFaulted)
            {
                _status = "Worker failed: " + _worker.Exception.GetBaseException().Message;
                _running = false;
            }
            else
            {
                _computeMilliseconds = _worker.Result;
                FinishBatch();
            }
            _worker = null;
        }
    }

    private void StartBatch()
    {
        StopCurrentBatch();
        _values = CreateInput(_itemCount);
        _progress = 0f;
        _checksum = "—";
        _running = true;
        _status = "Processing…";
        _wallMilliseconds = 0d;
        _computeMilliseconds = 0d;
        _marker = new ProfilerMarker("BatchProcessing." + ModeNames[(int)_mode].Replace(' ', '.'));
        double startedAt = Stopwatch.GetTimestamp();
        _startedAt = startedAt;

        switch (_mode)
        {
            case BatchMode.SingleThreaded:
                RunOnMainThread(() => BatchProcessingMath.ProcessRange(_values, _iterations, 0, _values.Length));
                break;
            case BatchMode.CSharpAsync:
            {
                float[] workerValues = _values;
                int iterations = _iterations;
                int itemCount = _itemCount;
                _completedItems = 0;
                ProfilerMarker workerMarker = AsyncWorkerMarker;
                _worker = Task.Run(() =>
                {
                    var timer = Stopwatch.StartNew();
                    using (workerMarker.Auto())
                    {
                        for (int i = 0; i < itemCount; i++)
                        {
                            workerValues[i] = BatchProcessingMath.ProcessValue(workerValues[i], iterations);
                            Interlocked.Exchange(ref _completedItems, i + 1);
                        }
                    }
                    return timer.Elapsed.TotalMilliseconds;
                });
                _progress = 0f;
                break;
            }
            case BatchMode.CSharpParallel:
                RunOnMainThread(() => Parallel.For(0, _values.Length, i =>
                    _values[i] = BatchProcessingMath.ProcessValue(_values[i], _iterations)));
                break;
            case BatchMode.UnityModularMainThread:
                RunOnMainThread(() => _module.Process(_values, _iterations));
                break;
            case BatchMode.UnityJobs:
                RunJob(false);
                FinishSynchronous(startedAt);
                break;
            case BatchMode.UnityJobsBurst:
                RunJob(true);
                FinishSynchronous(startedAt);
                break;
            case BatchMode.UnityCoroutine:
                _startedAt = startedAt;
                _coroutine = StartCoroutine(ProcessInChunks());
                break;
        }

        if (_mode != BatchMode.CSharpAsync && _mode != BatchMode.UnityCoroutine &&
            _mode != BatchMode.UnityJobs && _mode != BatchMode.UnityJobsBurst)
            FinishSynchronous(startedAt);
    }

    private int _completedItems;
    private double _startedAt;

    private void RunOnMainThread(Action work)
    {
        var timer = Stopwatch.StartNew();
        using (_marker.Auto())
            work();
        _computeMilliseconds = timer.Elapsed.TotalMilliseconds;
        _progress = 1f;
    }

    private void RunJob(bool useBurst)
    {
        var timer = Stopwatch.StartNew();
        using (var nativeValues = new NativeArray<float>(_values, Allocator.TempJob))
        {
            JobHandle handle;
            if (useBurst)
            {
                var job = new SimpleBatchBurstJob { Values = nativeValues, Iterations = _iterations };
                handle = job.Schedule(nativeValues.Length, 64);
            }
            else
            {
                var job = new SimpleBatchJob { Values = nativeValues, Iterations = _iterations };
                handle = job.Schedule(nativeValues.Length, 64);
            }

            using (_marker.Auto())
                handle.Complete();
            nativeValues.CopyTo(_values);
        }
        _computeMilliseconds = timer.Elapsed.TotalMilliseconds;
        _progress = 1f;
    }

    private IEnumerator ProcessInChunks()
    {
        double totalCpuMilliseconds = 0d;
        for (int start = 0; start < _values.Length; start += CoroutineChunkSize)
        {
            int count = Mathf.Min(CoroutineChunkSize, _values.Length - start);
            var timer = Stopwatch.StartNew();
            using (_marker.Auto())
                BatchProcessingMath.ProcessRange(_values, _iterations, start, count);
            totalCpuMilliseconds += timer.Elapsed.TotalMilliseconds;
            _progress = (start + count) / (float)_values.Length;
            yield return null;
        }

        _computeMilliseconds = totalCpuMilliseconds;
        _coroutine = null;
        FinishBatch();
    }

    private void FinishSynchronous(double startedAt)
    {
        _wallMilliseconds = ElapsedMilliseconds(startedAt);
        FinishBatch();
    }

    private void FinishBatch()
    {
        _wallMilliseconds = ElapsedMilliseconds(_startedAt);
        _progress = 1f;
        _running = false;
        _status = "Finished.";
        _checksum = SumValues(_values).ToString("0.000000");
    }

    private static double ElapsedMilliseconds(double startedAt)
    {
        return (Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency;
    }

    private static float[] CreateInput(int count)
    {
        // Deterministic input makes each mode start from identical data.
        var values = new float[count];
        for (int i = 0; i < count; i++)
            values[i] = ((i * 104729) % 1000003) / 1000003f;
        return values;
    }

    private static double SumValues(float[] values)
    {
        double sum = 0d;
        for (int i = 0; i < values.Length; i++)
            sum += values[i];
        return sum;
    }

    private void StopCurrentBatch()
    {
        if (_coroutine != null)
            StopCoroutine(_coroutine);
        _coroutine = null;
        _worker = null;
        _running = false;
    }

    private void OnDestroy()
    {
        StopCurrentBatch();
    }

    private void EnsureStyles()
    {
        if (_panel != null)
            return;
        _panel = new GUIStyle(GUI.skin.box);
        _panel.normal.background = MakeTexture(new Color(0.035f, 0.065f, 0.11f, 0.94f));
        _title = new GUIStyle(GUI.skin.label) { fontSize = 23, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        _label = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = new Color(0.8f, 0.87f, 0.95f) } };
        _button = new GUIStyle(GUI.skin.button) { fontSize = 12, fixedHeight = 32, wordWrap = true };
        _selectedButton = new GUIStyle(_button);
        _selectedButton.normal.background = MakeTexture(new Color(0.12f, 0.52f, 0.88f));
        _selectedButton.normal.textColor = Color.white;
    }

    private static Texture2D MakeTexture(Color color)
    {
        var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private void OnGUI()
    {
        EnsureStyles();
        float width = Mathf.Min(560f, Screen.width - 32f);
        GUILayout.BeginArea(new Rect(16f, 16f, width, Screen.height - 32f), _panel);
        GUILayout.Space(10);
        GUILayout.Label("BATCH PROCESSING LAB", _title);
        GUILayout.Label("Same numbers · same calculation · different scheduling", _label);
        GUILayout.Space(12);
        GUILayout.Label("PROCESSING MODE", _label);
        bool wasEnabled = GUI.enabled;
        GUI.enabled = !_running;
        for (int row = 0; row < 4; row++)
        {
            GUILayout.BeginHorizontal();
            for (int column = 0; column < 2; column++)
            {
                int index = row * 2 + column;
                if (index >= ModeNames.Length)
                    break;
                if (GUILayout.Button(ModeNames[index], (int)_mode == index ? _selectedButton : _button))
                {
                    _mode = (BatchMode)index;
                    _marker = new ProfilerMarker("BatchProcessing." + ModeNames[index].Replace(' ', '.'));
                }
            }
            GUILayout.EndHorizontal();
        }
        GUI.enabled = wasEnabled;

        GUILayout.Space(10);
        GUILayout.Label("Items: " + _itemCount.ToString("N0"), _label);
        GUI.enabled = !_running;
        _itemCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(_itemCount, 1000, 500000) / 1000f) * 1000;
        GUILayout.Label("Operations per item: " + _iterations, _label);
        _iterations = Mathf.RoundToInt(GUILayout.HorizontalSlider(_iterations, 1, 500));
        GUI.enabled = wasEnabled;
        GUILayout.Space(8);
        if (GUILayout.Button(_running ? "Processing…" : "Run batch", _button) && !_running)
            StartBatch();
        GUILayout.Label(_status, _label);
        GUILayout.HorizontalSlider(_progress, 0f, 1f);
        GUILayout.Label("Progress: " + (_progress * 100f).ToString("0") + "%", _label);
        GUILayout.Space(8);
        GUILayout.Label("LIVE PROFILE", _label);
        GUILayout.Label("Mode: " + ModeNames[(int)_mode], _label);
        GUILayout.Label("Items: " + _itemCount.ToString("N0") + "   Operations: " + ((long)_itemCount * _iterations).ToString("N0"), _label);
        GUILayout.Label("Compute time: " + _computeMilliseconds.ToString("0.000") + " ms", _label);
        GUILayout.Label("Wall time: " + _wallMilliseconds.ToString("0.000") + " ms", _label);
        GUILayout.Label("Result checksum: " + _checksum, _label);
        GUILayout.Label("Profiler marker: BatchProcessing." + ModeNames[(int)_mode].Replace(' ', '.'), _label);
        if (_mode == BatchMode.UnityJobsBurst)
            GUILayout.Label("Burst compiler: " + (BurstCompiler.IsEnabled ? "enabled" : "disabled (managed fallback)"), _label);
        GUILayout.FlexibleSpace();
        GUILayout.Label("Compare the same item and operation counts in Window → Analysis → Profiler.", _label);
        GUILayout.EndArea();
    }
}
