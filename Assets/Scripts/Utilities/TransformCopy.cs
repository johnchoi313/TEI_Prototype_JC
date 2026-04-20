using UnityEngine;

/// <summary>
/// Every LateUpdate, force-sets this object's position, rotation, and scale
/// to match a target Transform. Toggle each axis independently in the Inspector.
/// </summary>
public class TransformCopy : MonoBehaviour
{
    [SerializeField] private Transform _target;

    [Header("Copy")]
    [SerializeField] private bool _position = true;
    [SerializeField] private bool _rotation = true;
    [SerializeField] private bool _scale    = true;

    private void LateUpdate()
    {
        if (_target == null) return;

        if (_position) transform.position   = _target.position;
        if (_rotation) transform.rotation   = _target.rotation;
        if (_scale)    transform.localScale  = _target.localScale;
    }
}
