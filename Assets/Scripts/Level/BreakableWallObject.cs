using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to a wall root GameObject.
/// All direct children are treated as fragments — purely visual pieces that scatter
/// on break using transform animation (NO Rigidbodies or physics forces).
///
/// Why no physics on fragments:
///   The fish uses a dynamic Rigidbody for wall blocking. Physics-driven fragments
///   can apply contact impulses to the fish even with colliders disabled (depenetration
///   from the wall root collider itself is enough). Pure transform animation gives
///   identical visuals with zero interaction with the fish's physics body.
///
/// Prefab structure:
///   BreakableWall (root) — BoxCollider + this script (NO renderer, NO Rigidbody)
///   └── Fragment child GOs — MeshRenderer only (no Rigidbody needed)
///       └── Optional nested mesh/collider children — all colliders disabled at runtime
///
/// Fragment materials must use URP Lit or Unlit with Surface Type = Transparent
/// for the alpha fade to render correctly.
/// </summary>
public class BreakableWallObject : MonoBehaviour
{
    [Header("Interaction")]
    [SerializeField] private float _interactionRadius = 3f;

    [Header("Break — Visual Scatter")]
    [Tooltip("World-units/sec initial speed of fragments flying outward.")]
    [SerializeField] private float _fragmentSpeed  = 8f;

    [Tooltip("How long fragments take to fade out and the wall GameObject is destroyed.")]
    [SerializeField] private float _fadeDuration   = 1.5f;

    [Tooltip("0 = fragments all fly directly away from player. 1 = full random scatter cone.")]
    [Range(0f, 1f)]
    [SerializeField] private float _fragmentSpread = 0.4f;

    [Tooltip("Downward acceleration applied to fragments during flight (world units/sec²). 0 = no arc.")]
    [SerializeField] private float _fragmentGravity = 4f;

    [Header("Rumble")]
    [SerializeField] private float _rumbleInterval = 2.5f;
    [SerializeField] private float _rumbleAmount   = 0.06f;
    [SerializeField] private float _rumbleDuration = 0.25f;

    private Collider     _wallCollider;
    private bool         _isBroken;
    private GameObject[] _fragments;
    private Vector3[]    _originalLocalPositions;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        TryGetComponent(out _wallCollider);

        int count = transform.childCount;
        _fragments              = new GameObject[count];
        _originalLocalPositions = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            Transform child = transform.GetChild(i);
            _fragments[i]              = child.gameObject;
            _originalLocalPositions[i] = child.localPosition;

            // Fragments are purely visual — disable every collider in their subtree.
            // The wall root BoxCollider (_wallCollider) handles fish blocking.
            // Any Rigidbody on a fragment is also forced kinematic so the physics
            // engine can never apply forces through it to the fish.
            foreach (var col in child.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            foreach (var rb in child.GetComponentsInChildren<Rigidbody>(true))
                rb.isKinematic = true;
        }
    }

    private void Start()
    {
        StartCoroutine(RumbleRoutine());
    }

    private void OnEnable()  => PowerUpManager.OnPowerUpActivated += HandlePowerUpActivated;
    private void OnDisable() => PowerUpManager.OnPowerUpActivated -= HandlePowerUpActivated;

    // -------------------------------------------------------------------------

    private void HandlePowerUpActivated(PlayerIndex playerIndex, PowerUpDefinition def)
    {
        if (_isBroken) return;
        if (def.role != PowerUpRole.Break) return;

        // Use PowerUpManager.GetFish — guaranteed correct reference even after prefab rebuilds.
        GameObject playerGO = PowerUpManager.Instance?.GetFish(playerIndex);
        if (playerGO == null) return;

        // Measure from the wall SURFACE (ClosestPoint), not the wall center (transform.position).
        // A long wall's pivot center may be several units from where the fish is pressing,
        // causing a center-distance check to fail even when the fish is flush against the surface.
        Vector2 fishPos     = new Vector2(playerGO.transform.position.x, playerGO.transform.position.y);
        Vector3 closest3    = _wallCollider != null
            ? _wallCollider.ClosestPoint(playerGO.transform.position)
            : transform.position;
        Vector2 wallSurface = new Vector2(closest3.x, closest3.y);

        if (Vector2.Distance(fishPos, wallSurface) > _interactionRadius) return;

        Break(playerGO);
    }

    private void Break(GameObject player)
    {
        _isBroken = true;

        // Disable the wall blocker — the fish can now pass through.
        if (_wallCollider != null) _wallCollider.enabled = false;

        // Direction from player toward wall center — fragments scatter away from the player.
        Vector3 blastDir = transform.position - player.transform.position;
        blastDir.z = 0f;
        if (blastDir.sqrMagnitude < 0.0001f) blastDir = Vector3.right;
        blastDir.Normalize();

        for (int i = 0; i < _fragments.Length; i++)
        {
            GameObject frag = _fragments[i];
            if (frag == null) continue;

            // Snap to original rest position before scattering.
            frag.transform.localPosition = _originalLocalPositions[i];

            // Blend blast direction with random offset for a natural cone scatter.
            // Z is zeroed so fragments stay in the XY game plane.
            Vector3 scatter = new Vector3(
                Random.Range(-1f, 1f),
                Random.Range(-1f, 1f),
                0f).normalized;
            Vector3 dir = Vector3.Lerp(blastDir, scatter, _fragmentSpread).normalized;
            dir.z = 0f;

            // Animate scatter via transform — no physics, no contact with the fish.
            StartCoroutine(ScatterFragment(frag, dir * _fragmentSpeed));
            StartCoroutine(FadeFragment(frag));
        }

        StartCoroutine(DestroyAfterDelay(_fadeDuration));
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Moves a fragment outward using direct transform animation.
    /// No Rigidbody involved — cannot interact with the fish's physics body.
    /// </summary>
    private IEnumerator ScatterFragment(GameObject frag, Vector3 initialVelocity)
    {
        Vector3 velocity = initialVelocity;
        float   elapsed  = 0f;

        while (elapsed < _fadeDuration && frag != null)
        {
            float dt = Time.deltaTime;
            elapsed += dt;

            // Simple Euler integration: gravity pulls fragments down for a natural arc.
            velocity.y -= _fragmentGravity * dt;

            frag.transform.position += velocity * dt;

            // Optional: slowly spin the fragment for visual interest.
            frag.transform.Rotate(velocity.normalized * 120f * dt, Space.World);

            yield return null;
        }
    }

    private IEnumerator RumbleRoutine()
    {
        while (!_isBroken)
        {
            yield return new WaitForSeconds(_rumbleInterval);
            if (_isBroken) yield break;

            Vector3[] offsets = new Vector3[_fragments.Length];
            for (int i = 0; i < _fragments.Length; i++)
            {
                // XY-only rumble offsets — Z shake would change draw order in 2D.
                offsets[i] = new Vector3(
                    Random.Range(-_rumbleAmount, _rumbleAmount),
                    Random.Range(-_rumbleAmount, _rumbleAmount),
                    0f);
            }

            float half = _rumbleDuration * 0.5f;

            for (float t = 0f; t < half; t += Time.deltaTime)
            {
                float pct = t / half;
                for (int i = 0; i < _fragments.Length; i++)
                    if (_fragments[i] != null)
                        _fragments[i].transform.localPosition = Vector3.Lerp(
                            _originalLocalPositions[i],
                            _originalLocalPositions[i] + offsets[i], pct);
                yield return null;
            }

            for (float t = 0f; t < half; t += Time.deltaTime)
            {
                float pct = t / half;
                for (int i = 0; i < _fragments.Length; i++)
                    if (_fragments[i] != null)
                        _fragments[i].transform.localPosition = Vector3.Lerp(
                            _originalLocalPositions[i] + offsets[i],
                            _originalLocalPositions[i], pct);
                yield return null;
            }

            for (int i = 0; i < _fragments.Length; i++)
                if (_fragments[i] != null)
                    _fragments[i].transform.localPosition = _originalLocalPositions[i];
        }
    }

    private IEnumerator DestroyAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Destroy(gameObject);
    }

    private IEnumerator FadeFragment(GameObject frag)
    {
        Renderer rend = frag != null ? frag.GetComponentInChildren<Renderer>() : null;
        if (rend == null)
        {
            yield return new WaitForSeconds(_fadeDuration);
            if (frag != null) frag.SetActive(false);
            yield break;
        }

        Color startColor = rend.sharedMaterial != null
            ? rend.sharedMaterial.GetColor("_BaseColor")
            : Color.white;

        MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        for (float elapsed = 0f; elapsed < _fadeDuration; elapsed += Time.deltaTime)
        {
            if (frag == null) yield break;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / _fadeDuration);
            mpb.SetColor("_BaseColor", new Color(startColor.r, startColor.g, startColor.b, alpha));
            rend.SetPropertyBlock(mpb);
            yield return null;
        }

        if (frag != null) frag.SetActive(false);
    }

    // -------------------------------------------------------------------------

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, _interactionRadius);
    }
}
