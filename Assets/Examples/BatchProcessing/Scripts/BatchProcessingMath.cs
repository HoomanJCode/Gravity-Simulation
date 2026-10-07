using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

/// <summary>Seven ways to run the same small data-processing batch.</summary>
public enum BatchMode
{
    SingleThreaded,
    CSharpAsync,
    CSharpParallel,
    UnityModularMainThread,
    UnityJobs,
    UnityCoroutine,
    UnityJobsBurst
}

/// <summary>Shared math keeps the comparison focused on how the work is scheduled.</summary>
public static class BatchProcessingMath
{
    public static float ProcessValue(float value, int iterations)
    {
        // Repeated arithmetic gives the Profiler enough work to measure.
        for (int i = 0; i < iterations; i++)
        {
            value = value * 1.00001f + 0.00002f;
            if (value >= 1f)
                value -= 1f;
        }

        return value;
    }

    public static void ProcessRange(float[] values, int iterations, int start, int count)
    {
        int end = start + count;
        for (int i = start; i < end; i++)
            values[i] = ProcessValue(values[i], iterations);
    }
}

/// <summary>Jobs version without Burst; it uses the same arithmetic as the managed modes.</summary>
public struct SimpleBatchJob : IJobParallelFor
{
    public NativeArray<float> Values;
    public int Iterations;

    public void Execute(int index)
    {
        float value = Values[index];
        for (int i = 0; i < Iterations; i++)
        {
            value = value * 1.00001f + 0.00002f;
            if (value >= 1f)
                value -= 1f;
        }
        Values[index] = value;
    }
}

/// <summary>The identical job marked for Burst compilation.</summary>
[BurstCompile]
public struct SimpleBatchBurstJob : IJobParallelFor
{
    public NativeArray<float> Values;
    public int Iterations;

    public void Execute(int index)
    {
        float value = Values[index];
        for (int i = 0; i < Iterations; i++)
        {
            value = value * 1.00001f + 0.00002f;
            if (value >= 1f)
                value -= 1f;
        }
        Values[index] = value;
    }
}
