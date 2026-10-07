# Gravity Simulation

A small Unity project for demonstrating how the same sphere-based N-body gravity simulation behaves under six C# and Unity execution strategies. Each strategy has a dedicated scene that opens with that mode selected. The scenes build their simulation and controls at runtime, so no prefab wiring is required.

## Requirements

- Unity **6000.5.1f1** (see `ProjectSettings/ProjectVersion.txt`)
- The packages listed in `Packages/manifest.json`; Unity Package Manager installs them when the project opens

## Run it

1. Clone or download the repository.
2. Open the project folder in Unity Hub using the version above.
3. Open one of the six mode scenes below and press **Play**.
4. Use the overlay to switch between 2D and 3D, choose a processing mode, pause/resume, change simulation speed, or change the body count and press **Reset scene**.

The project also creates the controller automatically after a scene loads. Each mode scene stores its initial execution mode in a `GravitySceneMode` component. The runtime creates the camera setup (if one is missing), grid, sphere views, and controls.

## Mode scenes

| Scene | Starts with |
| --- | --- |
| `Assets/Scenes/Gravity_SingleThreaded.unity` | Single threaded |
| `Assets/Scenes/Gravity_CSharpAsync.unity` | C# async |
| `Assets/Scenes/Gravity_CSharpParallel.unity` | C# parallel |
| `Assets/Scenes/Gravity_UnityModularMainThread.unity` | Unity modular main thread |
| `Assets/Scenes/Gravity_UnityJobs.unity` | Unity Jobs |
| `Assets/Scenes/Gravity_UnityCoroutine.unity` | Unity coroutine |

All six scenes are included in **Build Settings**. The `SampleScene` remains a general-purpose scene that starts in single-threaded mode. To regenerate the mode scenes, use **Tools → Gravity Simulation → Generate Mode Scenes**.

## Processing modes

| Mode | What runs where | What to look for |
| --- | --- | --- |
| **Single threaded** | Serial force and integration loops on Unity's main thread | The reference implementation. Work grows with every body pair and can hold up the frame. |
| **C# async** | A cloned, plain-data snapshot is advanced with `Task.Run` | The worker doesn't touch Unity objects. Results are displayed when the task completes, so the simulation can lag the rendered frame slightly. |
| **C# parallel** | `Parallel.For` distributes independent per-body force calculations across the .NET thread pool | Parallel overhead can outweigh the benefit at low body counts. |
| **Unity modular main thread** | Each `GravityBodyView` module calculates its body's force on the main thread; a separate pass integrates all states | Demonstrates component-oriented organization, not parallel execution. |
| **Unity Jobs** | An `IJobParallelFor` calculates each body's acceleration in native arrays | The demo completes the job before applying results, making the per-step cost easy to compare. For a production workload, jobs can be combined with Burst and dependencies to overlap useful work. |
| **Unity coroutine** | A serial force pass processes a chunk of bodies and yields between chunks | Work is spread across frames. This can improve responsiveness while increasing total simulation latency. |

All modes use the same deterministic starting distribution, softened inverse-square attraction, timestep, and body count after each reset. The simulation is **O(N²)** because every body evaluates gravity from every other body. Spheres are visual markers; collisions are not simulated.

## Profiling for a presentation

1. Enter Play mode and open **Window → Analysis → Profiler**.
2. Keep the dimension, body count, and simulation speed fixed while comparing modes. Press **Reset scene** between captures to return to the same initial state.
3. Find the `GravitySimulation.*` marker in the CPU Usage timeline or Hierarchy. Each mode has its own marker. Async worker computation appears under `GravitySimulation.CSharpAsync.Worker`; coroutine chunks appear under `GravitySimulation.UnityCoroutine.Chunk`.
4. The overlay's **Physics step** reports the latest measured force/integration work. For async it reports worker time; for coroutine it sums the CPU time spent in its chunks and excludes time spent waiting between frames. Use the Unity Profiler for frame and main-thread costs.
5. Try a small body count first, then increase it to show the cost curve. Thread-pool and job scheduling overhead mean parallel modes may not win on small workloads. Results depend on processor, editor load, Unity version, and Burst configuration.

The project intentionally does not enable Burst, so the Jobs example isolates Unity's job scheduling and native data workflow. All strategies share the same approximate semi-implicit Euler integration and are intended for a visual performance demonstration rather than a high-precision orbital solver.

## Optional Codex and Unity CLI integration

This project includes Unity's `com.unity.pipeline` package for the Unity CLI editor connection. To install the official Unity skills and register the local MCP server in Codex on another machine:

```powershell
codex plugin marketplace add Unity-Technologies/unity-agent-plugin
codex plugin add unity@unity-agent-plugin
unity skill install codex
unity pipeline install
unity mcp configure codex --project-path .
```

Restart Codex after installing its plugin. The MCP server becomes useful when this project is open in the Unity Editor with the Pipeline package resolved.

## Project map

- `Assets/Scripts/GravitySimulationApp.cs` — runtime setup, controls, profiling labels, and sphere rendering updates.
- `Assets/Scripts/GravitySimulationCore.cs` — body state, gravity math, and the six execution strategies.
- `Assets/Scripts/GravityBodyView.cs` — modular per-sphere view and main-thread calculation module.
- `Assets/Scripts/GravitySceneMode.cs` — per-scene initial mode selection.
- `Assets/Editor/GravitySceneGeneration.cs` — editor command that regenerates the six preset scenes and updates Build Settings.
- `Assets/Scenes/SampleScene.unity` — minimal starter scene.

## Controls

- **2D plane / 3D space** — constrain gravity and motion to XY or enable all three axes.
- **Processing mode buttons** — switch implementation and reset the selected backend to the common initial conditions.
- **Pause / Resume** — stop and continue advancing simulation state.
- **Reset scene** — rebuild spheres from the deterministic initial state (also applies the selected body count).
- **Bodies slider** — select between 8 and 512 spheres; press Reset scene to apply the change.
- **Simulation speed** — scale simulated delta time.
