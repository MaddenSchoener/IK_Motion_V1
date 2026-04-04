using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ChestCollisionGuard : MonoBehaviour
{
    private Renderer _renderer;
    private Color _originalColor;
    private int _overlapCount = 0;

    void Start()
    {
        GetComponent<Collider>().isTrigger = true;

        _renderer = GetComponent<Renderer>();
        if (_renderer != null)
            _originalColor = _renderer.material.color;
    }

    void OnTriggerEnter(Collider other)
    {
        _overlapCount++;
        if (_overlapCount == 1 && _renderer != null)
            _renderer.material.color = Color.red;
    }

    void OnTriggerExit(Collider other)
    {
        _overlapCount = Mathf.Max(0, _overlapCount - 1);
        if (_overlapCount == 0 && _renderer != null)
            _renderer.material.color = _originalColor;
    }
}