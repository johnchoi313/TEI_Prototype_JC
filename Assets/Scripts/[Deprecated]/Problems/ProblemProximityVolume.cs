using UnityEngine;

/// <summary>
/// Thin forwarding component that lives on the ProximityVolume child of a Problem prefab.
///
/// The child has a SphereCollider with IsTrigger = true (larger than the physics collider
/// on the root). When players enter/exit this trigger, calls are forwarded to the
/// parent ProblemObject so it can track which players are nearby.
///
/// PREFAB SETUP
///   Parent: Problem root (ProblemObject.cs + physics Collider)
///   Child:  ProximityVolume (this script + SphereCollider, IsTrigger = true)
/// </summary>
[RequireComponent(typeof(Collider))]
public class ProblemProximityVolume : MonoBehaviour
{
    private ProblemObject _parent;

    private void Awake()
    {
        _parent = GetComponentInParent<ProblemObject>();
        if (_parent == null)
            Debug.LogError("[ProblemProximityVolume] No ProblemObject found in parent hierarchy.", this);
    }

    // Proximity detection is now handled via direct distance check in ProblemObject.
    // Trigger callbacks intentionally removed — this component is inert.
}
