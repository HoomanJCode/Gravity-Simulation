using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

/// <summary>The available execution strategies exposed by the demo controls.</summary>
public enum SimulationMode
{
    SingleThreaded,
    CSharpAsync,
    CSharpParallel,
    UnityModularMainThread,
    UnityJobs,
    UnityCoroutine,
    UnityJobsBurst
}

/// <summary>Constrains motion to a plane or leaves all three spatial axes active.</summary>
public enum SimulationDimension
{
    TwoD,
    ThreeD
}

/// <summary>Compact, blittable state shared by the managed and Unity Jobs implementations.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct BodyState
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Mass;

    public BodyState(Vector3 position, Vector3 velocity, float mass)
    {
        Position = position;
        Velocity = velocity;
        Mass = mass;
    }
}

/// <summary>Common interface lets the presentation compare execution strategies consistently.</summary>
public interface IGravityStepper : IDisposable
{
    BodyState[] States { get; }
    double LastStepMilliseconds { get; }
    void Step(float deltaTime, float gravity, float softening, SimulationDimension dimension);
}

internal static class GravityStateFactory
{
    /// <summary>Creates a deterministic expanding orbit cloud for repeatable comparisons.</summary>
    public static BodyState[] Create(int count, SimulationDimension dimension)
    {
        var states = new BodyState[count];
        uint random = 0xC0FFEEu;
        for (int i = 0; i < count; i++)
        {
            // Fibonacci sphere sampling keeps the initial cloud evenly spread.
            float y = 1f - (i / (float)Mathf.Max(1, count - 1)) * 2f;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float angle = i * 2.39996323f;
            Vector3 direction = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
            if (dimension == SimulationDimension.TwoD)
            {
                direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            }

            float distance = 2.0f + NextFloat(ref random) * 4.5f;
            Vector3 position = direction * distance;
            Vector3 tangent = dimension == SimulationDimension.TwoD
                ? new Vector3(-direction.y, direction.x, 0f)
                : Vector3.Cross(direction, Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.01f)
                tangent = Vector3.right;

            // Mild tangential velocity makes the gravitational collapse visibly dynamic.
            Vector3 velocity = tangent.normalized * (0.12f + NextFloat(ref random) * 0.22f);
            states[i] = new BodyState(position, velocity, 0.7f + NextFloat(ref random) * 0.8f);
        }

        return states;
    }

    private static float NextFloat(ref uint state)
    {
        state = state * 1664525u + 1013904223u;
        return (state & 0x00FFFFFFu) / 16777216f;
    }
}

internal static class GravityMath
{
    public static Vector3 Acceleration(BodyState[] states, int index, float gravity, float softening, bool is2D)
    {
        Vector3 acceleration = Vector3.zero;
        Vector3 position = states[index].Position;
        float softeningSquared = softening * softening;
        for (int other = 0; other < states.Length; other++)
        {
            if (other == index)
                continue;

            Vector3 offset = states[other].Position - position;
            if (is2D)
                offset.z = 0f;
            float distanceSquared = offset.sqrMagnitude + softeningSquared;
            float inverseDistance = 1f / Mathf.Sqrt(distanceSquared);
            float inverseDistanceCubed = inverseDistance * inverseDistance * inverseDistance;
            acceleration += offset * (gravity * states[other].Mass * inverseDistanceCubed);
        }

        return acceleration;
    }

    public static BodyState[] Advance(BodyState[] source, float deltaTime, float gravity, float softening, SimulationDimension dimension)
    {
        var accelerations = new Vector3[source.Length];
        bool is2D = dimension == SimulationDimension.TwoD;
        for (int i = 0; i < source.Length; i++)
            accelerations[i] = Acceleration(source, i, gravity, softening, is2D);
        return Integrate(source, accelerations, deltaTime, is2D);
    }

    public static BodyState[] Integrate(BodyState[] source, Vector3[] accelerations, float deltaTime, bool is2D)
    {
        var result = new BodyState[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            BodyState body = source[i];
            body.Velocity += accelerations[i] * deltaTime;
            body.Position += body.Velocity * deltaTime;
            if (is2D)
            {
                body.Velocity.z = 0f;
                body.Position.z = 0f;
            }
            result[i] = body;
        }
        return result;
    }

    public static BodyState[] Integrate(BodyState[] source, NativeArray<Vector3> accelerations, float deltaTime, bool is2D)
    {
        var result = new BodyState[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            BodyState body = source[i];
            body.Velocity += accelerations[i] * deltaTime;
            body.Position += body.Velocity * deltaTime;
            if (is2D)
            {
                body.Velocity.z = 0f;
                body.Position.z = 0f;
            }
            result[i] = body;
        }
        return result;
    }
}

internal static class GravityStepperFactory
{
    public static IGravityStepper Create(SimulationMode mode, MonoBehaviour owner, BodyState[] initialStates)
    {
        switch (mode)
        {
            case SimulationMode.CSharpAsync: return new AsyncGravityStepper(initialStates);
            case SimulationMode.CSharpParallel: return new ParallelGravityStepper(initialStates);
            case SimulationMode.UnityModularMainThread: return new ModularMainThreadStepper(initialStates, owner);
            case SimulationMode.UnityJobs: return new UnityJobsGravityStepper(initialStates);
            case SimulationMode.UnityCoroutine: return new CoroutineGravityStepper(initialStates, owner);
            case SimulationMode.UnityJobsBurst: return new UnityJobsBurstGravityStepper(initialStates);
            default: return new SingleThreadGravityStepper(initialStates);
        }
    }
}

/// <summary>Reference implementation: serial force evaluation and integration on Unity's main thread.</summary>
internal sealed class SingleThreadGravityStepper : IGravityStepper
{
    public BodyState[] States { get; private set; }
    public double LastStepMilliseconds { get; private set; }
    public SingleThreadGravityStepper(BodyState[] initialStates) { States = (BodyState[])initialStates.Clone(); }
    public void Step(float deltaTime, float gravity, float softening, SimulationDimension dimension)
    {
        var timer = Stopwatch.StartNew();
        States = GravityMath.Advance(States, deltaTime, gravity, softening, dimension);
        LastStepMilliseconds = timer.Elapsed.TotalMilliseconds;
    }
    public void Dispose() { }
}

/// <summary>Moves a pure managed snapshot to a worker task; Unity objects stay on the main thread.</summary>
internal sealed class AsyncGravityStepper : IGravityStepper
{
    private static readonly ProfilerMarker WorkerMarker = new ProfilerMarker("GravitySimulation.CSharpAsync.Worker");
    private Task<StepResult> _pending;
    public BodyState[] States { get; private set; }
    public double LastStepMilliseconds { get; private set; }
    public AsyncGravityStepper(BodyState[] initialStates) { States = (BodyState[])initialStates.Clone(); }

    public void Step(float deltaTime, float gravity, float softening, SimulationDimension dimension)
    {
        if (_pending != null && _pending.IsCompleted)
        {
            StepResult completed = _pending.GetAwaiter().GetResult();
            States = completed.States;
            LastStepMilliseconds = completed.ElapsedMilliseconds;
            _pending = null;
        }

        if (_pending == null)
        {
            BodyState[] snapshot = (BodyState[])States.Clone();
            _pending = Task.Run(() =>
            {
                var timer = Stopwatch.StartNew();
                BodyState[] next;
                using (WorkerMarker.Auto())
                    next = GravityMath.Advance(snapshot, deltaTime, gravity, softening, dimension);
                timer.Stop();
                return new StepResult(next, timer.Elapsed.TotalMilliseconds);
            });
        }
    }

    public void Dispose()
    {
        if (_pending != null)
            _pending.GetAwaiter().GetResult();
        _pending = null;
    }

    private struct StepResult
    {
        public readonly BodyState[] States;
        public readonly double ElapsedMilliseconds;
        public StepResult(BodyState[] states, double elapsedMilliseconds)
        {
            States = states;
            ElapsedMilliseconds = elapsedMilliseconds;
        }
    }
}

/// <summary>Uses the .NET thread pool to evaluate each body's independent force calculation.</summary>
internal sealed class ParallelGravityStepper : IGravityStepper
{
    public BodyState[] States { get; private set; }
    public double LastStepMilliseconds { get; private set; }
    public ParallelGravityStepper(BodyState[] initialStates) { States = (BodyState[])initialStates.Clone(); }

    public void Step(float deltaTime, float gravity, float softening, SimulationDimension dimension)
    {
        var timer = Stopwatch.StartNew();
        var accelerations = new Vector3[States.Length];
        bool is2D = dimension == SimulationDimension.TwoD;
        System.Threading.Tasks.Parallel.For(0, States.Length,
            i => accelerations[i] = GravityMath.Acceleration(States, i, gravity, softening, is2D));
        States = GravityMath.Integrate(States, accelerations, deltaTime, is2D);
        LastStepMilliseconds = timer.Elapsed.TotalMilliseconds;
    }
    public void Dispose() { }
}

/// <summary>Schedules independent body calculations without Burst and completes before presentation.</summary>
internal sealed class UnityJobsGravityStepper : IGravityStepper
{
    private NativeArray<BodyState> _nativeStates;
    private NativeArray<Vector3> _nativeAccelerations;
    public BodyState[] States { get; private set; }
    public double LastStepMilliseconds { get; private set; }

    public UnityJobsGravityStepper(BodyState[] initialStates)
    {
        States = (BodyState[])initialStates.Clone();
        _nativeStates = new NativeArray<BodyState>(States.Length, Allocator.Persistent);
        _nativeAccelerations = new NativeArray<Vector3>(States.Length, Allocator.Persistent);
    }

    public void Step(float deltaTime, float gravity, float softening, SimulationDimension dimension)
    {
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < States.Length; i++)
            _nativeStates[i] = States[i];

        var job = new GravityAccelerationJob
        {
            States = _nativeStates,
            Accelerations = _nativeAccelerations,
            Gravity = gravity,
            SofteningSquared = softening * softening,
            Is2D = dimension == SimulationDimension.TwoD
        };
        JobHandle handle = job.Schedule(States.Length, 32);
        handle.Complete();
        States = GravityMath.Integrate(States, _nativeAccelerations, deltaTime, dimension == SimulationDimension.TwoD);
        LastStepMilliseconds = timer.Elapsed.TotalMilliseconds;
    }

    public void Dispose()
    {
        if (_nativeStates.IsCreated) _nativeStates.Dispose();
        if (_nativeAccelerations.IsCreated) _nativeAccelerations.Dispose();
    }

    private struct GravityAccelerationJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<BodyState> States;
        [WriteOnly] public NativeArray<Vector3> Accelerations;
        public float Gravity;
        public float SofteningSquared;
        public bool Is2D;

        public void Execute(int index)
        {
            Accelerations[index] = GravityJobMath.CalculateAcceleration(
                States, index, Gravity, SofteningSquared, Is2D);
        }
    }
}

/// <summary>Schedules the same independent force calculations through Burst-compiled jobs.</summary>
internal sealed class UnityJobsBurstGravityStepper : IGravityStepper
{
    private NativeArray<BodyState> _nativeStates;
    private NativeArray<Vector3> _nativeAccelerations;
    public BodyState[] States { get; private set; }
    public double LastStepMilliseconds { get; private set; }

    public UnityJobsBurstGravityStepper(BodyState[] initialStates)
    {
        States = (BodyState[])initialStates.Clone();
        _nativeStates = new NativeArray<BodyState>(States.Length, Allocator.Persistent);
        _nativeAccelerations = new NativeArray<Vector3>(States.Length, Allocator.Persistent);
    }

    public void Step(float deltaTime, float gravity, float softening, SimulationDimension dimension)
    {
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < States.Length; i++)
            _nativeStates[i] = States[i];

        var job = new BurstGravityAccelerationJob
        {
            States = _nativeStates,
            Accelerations = _nativeAccelerations,
            Gravity = gravity,
            SofteningSquared = softening * softening,
            Is2D = dimension == SimulationDimension.TwoD
        };
        JobHandle handle = job.Schedule(States.Length, 32);
        handle.Complete();
        States = GravityMath.Integrate(States, _nativeAccelerations, deltaTime, dimension == SimulationDimension.TwoD);
        LastStepMilliseconds = timer.Elapsed.TotalMilliseconds;
    }

    public void Dispose()
    {
        if (_nativeStates.IsCreated) _nativeStates.Dispose();
        if (_nativeAccelerations.IsCreated) _nativeAccelerations.Dispose();
    }

    [BurstCompile]
    private struct BurstGravityAccelerationJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<BodyState> States;
        [WriteOnly] public NativeArray<Vector3> Accelerations;
        public float Gravity;
        public float SofteningSquared;
        public bool Is2D;

        public void Execute(int index)
        {
            Accelerations[index] = GravityJobMath.CalculateAcceleration(
                States, index, Gravity, SofteningSquared, Is2D);
        }
    }
}

/// <summary>Blittable math shared by the managed and Burst-compiled Unity job variants.</summary>
internal static class GravityJobMath
{
    public static Vector3 CalculateAcceleration(
        NativeArray<BodyState> states, int index, float gravity, float softeningSquared, bool is2D)
    {
        BodyState body = states[index];
        float x = 0f;
        float y = 0f;
        float z = 0f;

        for (int other = 0; other < states.Length; other++)
        {
            if (other == index)
                continue;

            BodyState source = states[other];
            float dx = source.Position.x - body.Position.x;
            float dy = source.Position.y - body.Position.y;
            float dz = is2D ? 0f : source.Position.z - body.Position.z;
            float distanceSquared = dx * dx + dy * dy + dz * dz + softeningSquared;
            float inverseDistance = 1f / math.sqrt(distanceSquared);
            float scale = gravity * source.Mass * inverseDistance * inverseDistance * inverseDistance;
            x += dx * scale;
            y += dy * scale;
            z += dz * scale;
        }

        return new Vector3(x, y, z);
    }
}

/// <summary>
/// Keeps the main-thread update modular by asking each body view to evaluate its own force
/// contribution, then applies the resulting state in a separate integration pass.
/// </summary>
internal sealed class ModularMainThreadStepper : IGravityStepper
{
    private readonly GravityBodyView[] _bodyModules;
    public BodyState[] States { get; private set; }
    public double LastStepMilliseconds { get; private set; }

    public ModularMainThreadStepper(BodyState[] initialStates, MonoBehaviour owner)
    {
        States = (BodyState[])initialStates.Clone();
        var app = owner as GravitySimulationApp;
        _bodyModules = app == null ? new GravityBodyView[0] : app.GetBodyViews();
    }

    public void Step(float deltaTime, float gravity, float softening, SimulationDimension dimension)
    {
        var timer = Stopwatch.StartNew();
        var accelerations = new Vector3[States.Length];
        bool is2D = dimension == SimulationDimension.TwoD;
        for (int i = 0; i < States.Length; i++)
            accelerations[i] = _bodyModules[i].CalculateAcceleration(States, gravity, softening, is2D);
        States = GravityMath.Integrate(States, accelerations, deltaTime, is2D);
        LastStepMilliseconds = timer.Elapsed.TotalMilliseconds;
    }
    public void Dispose() { }
}

/// <summary>Splits the serial force pass across frames using Unity's coroutine scheduler.</summary>
internal sealed class CoroutineGravityStepper : IGravityStepper
{
    private const int BodiesPerFrame = 16;
    private static readonly ProfilerMarker ChunkMarker = new ProfilerMarker("GravitySimulation.UnityCoroutine.Chunk");
    private readonly MonoBehaviour _owner;
    private Vector3[] _accelerations;
    private Coroutine _routine;
    public bool Paused { get; set; }
    public BodyState[] States { get; private set; }
    public double LastStepMilliseconds { get; private set; }

    public CoroutineGravityStepper(BodyState[] initialStates, MonoBehaviour owner)
    {
        States = (BodyState[])initialStates.Clone();
        _accelerations = new Vector3[States.Length];
        _owner = owner;
    }

    public void Step(float deltaTime, float gravity, float softening, SimulationDimension dimension)
    {
        if (_routine == null)
            _routine = _owner.StartCoroutine(AdvanceOverFrames(deltaTime, gravity, softening, dimension));
    }

    private System.Collections.IEnumerator AdvanceOverFrames(float deltaTime, float gravity, float softening, SimulationDimension dimension)
    {
        // Yield once so StartCoroutine has a valid handle before work can finish.
        yield return null;
        bool is2D = dimension == SimulationDimension.TwoD;
        double computeMilliseconds = 0d;
        for (int start = 0; start < States.Length; start += BodiesPerFrame)
        {
            while (Paused)
                yield return null;

            var timer = Stopwatch.StartNew();
            int end = Math.Min(start + BodiesPerFrame, States.Length);
            using (ChunkMarker.Auto())
            {
                for (int i = start; i < end; i++)
                    _accelerations[i] = GravityMath.Acceleration(States, i, gravity, softening, is2D);
            }
            timer.Stop();
            computeMilliseconds += timer.Elapsed.TotalMilliseconds;
            if (end < States.Length)
                yield return null;
        }

        States = GravityMath.Integrate(States, _accelerations, deltaTime, is2D);
        LastStepMilliseconds = computeMilliseconds;
        _routine = null;
    }

    public void Dispose()
    {
        if (_routine != null && _owner != null)
            _owner.StopCoroutine(_routine);
        _routine = null;
    }
}
