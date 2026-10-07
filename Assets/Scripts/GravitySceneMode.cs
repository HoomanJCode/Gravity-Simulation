using UnityEngine;

/// <summary>Stores the processing strategy that a dedicated demo scene opens with.</summary>
public sealed class GravitySceneMode : MonoBehaviour
{
    [SerializeField] private SimulationMode _initialMode;

    public SimulationMode InitialMode { get { return _initialMode; } }
}
