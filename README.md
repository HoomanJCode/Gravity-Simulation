# Unity Batch Processing and Gravity Demos

This Unity project contains two presentation examples. The batch example compares seven ways to process the same list of numbers. The gravity example shows spheres attracting each other in either a 2D plane or 3D space, using the same seven execution strategies.

## Requirements

- Unity **6000.5.1f1** (see `ProjectSettings/ProjectVersion.txt`)
- Packages listed in `Packages/manifest.json`. Unity Package Manager installs them when the project opens.

## Start with the simple batch example

1. Open the project in Unity Hub.
2. Open [`Assets/Examples/BatchProcessing/Scenes/BatchProcessing.unity`](Assets/Examples/BatchProcessing/Scenes/BatchProcessing.unity).
3. Press **Play**, choose a processing mode, and press **Run batch**.
4. Keep the item count and operations per item fixed while comparing modes. Each run starts with the same deterministic values and displays a checksum so you can check that modes produce equivalent results.

The batch scene has sliders for workload size, a progress bar, compute and wall-clock timings, a checksum, and a Unity Profiler marker. Find the matching `BatchProcessing.*` marker in **Window → Analysis → Profiler**. Async work is marked `BatchProcessing.CSharpAsync.Worker`; coroutine chunks use the selected mode marker across frames. The Burst mode also displays whether Burst compilation is enabled or using the managed fallback.

The sample calculation repeats a small multiply/add operation for each number. It is intentionally simple so it is easy to explain: the calculation stays the same while the scheduling changes.

| Mode | Simple explanation |
| --- | --- |
| Single threaded | One loop processes every number on Unity's main thread. |
| C# async | A `Task` processes the numbers on a worker thread; Unity reads the result after it finishes. |
| C# parallel | `Parallel.For` splits numbers across .NET thread-pool workers. |
| Unity modular main thread | A `ProcessingModule` component runs the same loop on the main thread. It demonstrates code organization, not parallel work. |
| Unity Jobs (no Burst) | `IJobParallelFor` processes native data in parallel without Burst compilation. |
| Unity coroutine | A loop processes a small chunk, yields, then continues on a later frame. |
| Unity Jobs (Burst) | The same job is marked with `[BurstCompile]` and compiled by Burst when available. |

Choose **Tools → Batch Processing → Create Presentation Scene** to regenerate the batch scene and its Build Settings entry.

## Open the gravity example

The gravity project is in [`Assets/Examples/GravitySimulation`](Assets/Examples/GravitySimulation). Open a `Gravity_*.unity` scene and press **Play**. Each scene selects a strategy when it starts. Use the runtime controls to change between 2D and 3D, pause, reset, and adjust body count and simulation speed.

| Scene | Starts with |
| --- | --- |
| [`Gravity_SingleThreaded.unity`](Assets/Examples/GravitySimulation/Scenes/Gravity_SingleThreaded.unity) | Single threaded |
| [`Gravity_CSharpAsync.unity`](Assets/Examples/GravitySimulation/Scenes/Gravity_CSharpAsync.unity) | C# async |
| [`Gravity_CSharpParallel.unity`](Assets/Examples/GravitySimulation/Scenes/Gravity_CSharpParallel.unity) | C# parallel |
| [`Gravity_UnityModularMainThread.unity`](Assets/Examples/GravitySimulation/Scenes/Gravity_UnityModularMainThread.unity) | Unity modular main thread |
| [`Gravity_UnityJobs.unity`](Assets/Examples/GravitySimulation/Scenes/Gravity_UnityJobs.unity) | Unity Jobs without Burst |
| [`Gravity_UnityCoroutine.unity`](Assets/Examples/GravitySimulation/Scenes/Gravity_UnityCoroutine.unity) | Unity coroutine |
| [`Gravity_UnityJobsBurst.unity`](Assets/Examples/GravitySimulation/Scenes/Gravity_UnityJobsBurst.unity) | Unity Jobs with Burst |

All seven gravity scenes and the batch scene are in **Build Settings**. The gravity starter scene is `Assets/Examples/GravitySimulation/Scenes/SampleScene.unity`. Regenerate the gravity scenes with **Tools → Gravity Simulation → Generate Mode Scenes**.

The gravity demo uses deterministic initial positions and velocities, softened inverse-square attraction, and a simple semi-implicit Euler integration. It is a visual workload example, not a high-precision orbital solver. Sphere collisions are not simulated. At `N` bodies, each step performs roughly `N × (N - 1)` pair evaluations, so the workload grows quadratically.

## Presenting and profiling

1. Open **Window → Analysis → Profiler** before running the example.
2. Use the same workload settings for each mode. For gravity, reset the scene between captures; the batch scene recreates its same starting values for each run.
3. Warm up each mode first. Discard the first Burst measurement in Editor Play mode because it may include JIT compilation.
4. Compare frame time and main-thread cost as well as the example's compute time. C# async and coroutines can keep the main thread responsive by moving or spreading work, even when total elapsed time is not lower.
5. Small workloads may be slower with parallel strategies because scheduling has a cost. Results depend on hardware, Editor load, Unity version, and Burst configuration.

The batch display's **compute time** measures work within that mode (including job setup and copying for Unity Jobs); **wall time** measures how long the run takes from start to completion. For async, the wall time includes the wait until the worker finishes. For coroutines, wall time includes the frames spent yielding while compute time sums only the chunk work.

## Project layout

```text
Assets/Examples/
├── BatchProcessing/
│   ├── Editor/       Scene creation command
│   ├── Scenes/       Standalone comparison scene
│   └── Scripts/      Shared calculation and seven processing modes
└── GravitySimulation/
    ├── Editor/       Gravity mode scene generator
    ├── Scenes/       Starter scene and seven preset scenes
    └── Scripts/      Gravity simulation, sphere views, and execution modes
```

Useful batch files:

- [`BatchProcessingApp.cs`](Assets/Examples/BatchProcessing/Scripts/BatchProcessingApp.cs) — the controls, mode selection, execution, and timings.
- [`BatchProcessingMath.cs`](Assets/Examples/BatchProcessing/Scripts/BatchProcessingMath.cs) — the shared calculation and Unity job definitions.
- [`ProcessingModule.cs`](Assets/Examples/BatchProcessing/Scripts/ProcessingModule.cs) — the component-based main-thread example.

Burst is a package dependency but is not enabled project-wide. The plain Jobs mode remains a baseline; the separate Burst job opts into compilation with `[BurstCompile]`. The overlay reports Burst availability in the gravity demo.

## Optional Unity CLI integration

The project includes Unity's `com.unity.pipeline` package for the Unity CLI editor connection. To install Unity's official skills and register the local MCP server in Codex on another machine:

```powershell
codex plugin marketplace add Unity-Technologies/unity-agent-plugin
codex plugin add unity@unity-agent-plugin
unity skill install codex
unity pipeline install
unity mcp configure codex --project-path .
```

Restart Codex after installing the plugin. The MCP connection is available while the project is open in Unity Editor with the Pipeline package resolved.
