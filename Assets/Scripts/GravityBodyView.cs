using UnityEngine;

/// <summary>
/// The view-side module for one simulated body. It owns rendering and exposes its
/// force calculation for the Unity modular-main-thread comparison mode.
/// </summary>
public sealed class GravityBodyView : MonoBehaviour
{
    private static Material _sharedMaterial;
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int StandardColorId = Shader.PropertyToID("_Color");
    private MaterialPropertyBlock _propertyBlock;
    private Renderer _renderer;
    private int _stateIndex;

    public void Initialize(BodyState initialState, Color color)
    {
        _renderer = GetComponent<Renderer>();
        if (_sharedMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader != null)
                _sharedMaterial = new Material(shader) { name = "Gravity Body Shared Material", hideFlags = HideFlags.HideAndDontSave };
        }
        if (_renderer != null && _sharedMaterial != null)
            _renderer.sharedMaterial = _sharedMaterial;

        _propertyBlock = new MaterialPropertyBlock();
        if (_renderer != null)
        {
            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(ColorId, color);
            _propertyBlock.SetColor(StandardColorId, color);
            _renderer.SetPropertyBlock(_propertyBlock);
        }

        transform.position = initialState.Position;
    }

    internal void SetStateIndex(int index)
    {
        _stateIndex = index;
    }

    /// <summary>Called only by the modular mode; ordinary modes use shared math loops.</summary>
    public Vector3 CalculateAcceleration(BodyState[] states, float gravity, float softening, bool is2D)
    {
        return GravityMath.Acceleration(states, _stateIndex, gravity, softening, is2D);
    }

    public void ApplyState(BodyState state, SimulationDimension dimension)
    {
        Vector3 position = state.Position;
        if (dimension == SimulationDimension.TwoD)
            position.z = 0f;
        transform.position = position;
    }
}
