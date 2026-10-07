using UnityEngine;

/// <summary>A component-style worker that deliberately processes on Unity's main thread.</summary>
public sealed class ProcessingModule : MonoBehaviour
{
    public void Process(float[] values, int iterations)
    {
        BatchProcessingMath.ProcessRange(values, iterations, 0, values.Length);
    }
}
