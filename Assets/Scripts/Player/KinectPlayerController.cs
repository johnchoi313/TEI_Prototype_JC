using UnityEngine;
using TMPro;

/// <summary>
/// Converts body-tracking Transform positions into a normalised XY input
/// vector and a jump signal for PlayerLightController.
///
/// DIRECTION  — average of both hands' world-position offset from the chest.
///              X offset → horizontal input axis.
///              Y offset → vertical input axis (hands up = up, hands down = down).
///              Offsets below deadzone are ignored; remapped [deadzone…maxRange] → [0…1].
///
/// JUMP       — detected when pelvis Y rises above (rollingAvg + jumpRiseThreshold)
///              then falls back below (rollingAvg + jumpFallThreshold).
///              JumpPressed stays true for jumpHoldTime seconds after detection.
///
/// Assign the tracked bone Transforms (from your DefaultMapper or HumanoidMapper
/// character) to the inspector fields and reference this from PlayerLightController.
/// </summary>
public class KinectPlayerController : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector — tracked transforms
    // -------------------------------------------------------------------------
    [Header("Auto-Find")]
    [Tooltip("Root GameObject of the character to search when auto-finding bones")]
    public GameObject character;

    [Header("Tracked Transforms")]
    [Tooltip("World transform of the left hand bone")]
    public Transform leftHand;
    [Tooltip("World transform of the right hand bone")]
    public Transform rightHand;
    [Tooltip("World transform of the chest bone — hand offsets measured from here")]
    public Transform chest;
    [Tooltip("World transform of the pelvis/hips bone — used for jump detection")]
    public Transform pelvis;

    [Header("Hand Direction")]
    [Tooltip("Minimum hand-to-chest distance (metres) before input registers")]
    public float deadzone = 0.15f;
    [Tooltip("Hand-to-chest distance that maps to full (1.0) input")]
    public float maxRange = 0.6f;

    [Header("Jump Detection")]
    [Tooltip("How many seconds of pelvis Y history to average for the baseline")]
    public float baselineWindow    = 2f;
    [Tooltip("How far above baseline (metres) pelvis must rise to start a jump")]
    public float jumpRiseThreshold = 0.12f;
    [Tooltip("How far above baseline pelvis must fall back to to confirm jump")]
    public float jumpFallThreshold = 0.04f;
    [Tooltip("How long (seconds) the jump signal stays true after detection")]
    public float jumpHoldTime      = 0.25f;


    [Header("Debug")]
    [Tooltip("Optional TextMeshPro component to display live output values")]
    public TMP_Text debugText;

    // -------------------------------------------------------------------------
    //  Public output — read by PlayerLightController
    // -------------------------------------------------------------------------
    /// <summary>Normalised XY movement input, magnitude ≤ 1.</summary>
    public Vector2 InputAxis   { get; private set; }

    /// <summary>True for jumpHoldTime seconds after a jump is detected.</summary>
    public bool    JumpPressed { get; private set; }

    /// <summary>
    /// True when the player's character is visible (active in hierarchy).
    /// The Brekel system hides the character GameObject when no signal is received.
    /// </summary>
    public bool IsTracked => pelvis != null ? pelvis.gameObject.activeInHierarchy : true;

    /// <summary>
    /// World-space Z of this player's pelvis (falls back to the character root,
    /// then this GameObject). Used by SharedFOVBudget to compute depth differential.
    /// Always valid — even when the player is hidden the skeleton keeps its last pose.
    /// </summary>
    public float WorldZ
    {
        get
        {
            if (pelvis    != null) return pelvis.position.z;
            if (character != null) return character.transform.position.z;
            return transform.position.z;
        }
    }

    // -------------------------------------------------------------------------
    //  Internal
    // -------------------------------------------------------------------------
    private float[] _pelvisHistory;
    private int     _historyHead;
    private int     _historyCount;
    private int     _historyCapacity;
    private float   _pelvisBaseline;

    private bool  _jumpArmed;
    private float _jumpTimer;



    // =========================================================================
    //  Unity lifecycle
    // =========================================================================
    private void Start()
    {
        _historyCapacity = Mathf.Max(10, Mathf.CeilToInt(baselineWindow * 50f));
        _pelvisHistory   = new float[_historyCapacity];
    }

    private void Update()
    {
        InputAxis   = Vector2.zero;
        JumpPressed = false;

        if (chest != null && leftHand != null && rightHand != null)
            InputAxis = ComputeDirectionInput();

        if (pelvis != null)
            UpdateJump();

        if (_jumpTimer > 0f)
        {
            _jumpTimer -= Time.deltaTime;
            JumpPressed = true;
        }

        UpdateDebugText();
    }


    // =========================================================================
    //  Direction — average hand world-position offset from chest
    // =========================================================================
    private Vector2 ComputeDirectionInput()
    {
        Vector3 chestPos  = chest.position;
        Vector3 offsetL   = leftHand.position  - chestPos;
        Vector3 offsetR   = rightHand.position - chestPos;
        Vector3 avg       = (offsetL + offsetR) * 0.5f;

        // X offset = left/right (negated to match screen), Y offset = up/down
        float inputX = ApplyDeadzone(-avg.x);
        float inputY = ApplyDeadzone(avg.y);

        Vector2 result = new Vector2(inputX, inputY);
        if (result.sqrMagnitude > 1f) result.Normalize();
        return result;
    }

    private float ApplyDeadzone(float raw)
    {
        float abs = Mathf.Abs(raw);
        if (abs < deadzone) return 0f;
        float range = maxRange - deadzone;
        if (range <= 0f) return Mathf.Sign(raw);
        return Mathf.Sign(raw) * Mathf.Clamp01((abs - deadzone) / range);
    }


    // =========================================================================
    //  Jump — pelvis Y rises above rolling baseline then falls back
    // =========================================================================
    private void UpdateJump()
    {
        float pelvisY = pelvis.position.y;

        _pelvisHistory[_historyHead] = pelvisY;
        _historyHead = (_historyHead + 1) % _historyCapacity;
        if (_historyCount < _historyCapacity) _historyCount++;

        float sum = 0f;
        for (int i = 0; i < _historyCount; i++) sum += _pelvisHistory[i];
        _pelvisBaseline = sum / _historyCount;

        float aboveBaseline = pelvisY - _pelvisBaseline;

        // Fire immediately when pelvis rises above threshold — don't wait for the drop
        if (!_jumpArmed && aboveBaseline >= jumpRiseThreshold)
        {
            _jumpArmed = true;
            _jumpTimer = jumpHoldTime;
        }

        // Reset arm once pelvis returns below fall threshold, ready for next jump
        if (_jumpArmed && aboveBaseline <= jumpFallThreshold)
            _jumpArmed = false;
    }


    // =========================================================================
    //  Auto-find via Humanoid Avatar
    // =========================================================================
    [ContextMenu("Auto-Find Bones")]
    public void AutoFindBones()
    {
        if (character == null)
        {
            Debug.LogWarning("[KinectPlayerController] Auto-Find: assign a Character object first.");
            return;
        }

        leftHand = rightHand = chest = pelvis = null;

        Animator anim = character.GetComponentInChildren<Animator>();
        bool hasAvatar = anim != null && anim.isHuman;

        Transform root = character.transform;
        int found = 0;

        if (hasAvatar)
        {
            leftHand  = Bone(anim, HumanBodyBones.LeftHand,    ref found);
            rightHand = Bone(anim, HumanBodyBones.RightHand,   ref found);
            chest     = Bone(anim, HumanBodyBones.Chest,       ref found);
            pelvis    = Bone(anim, HumanBodyBones.Hips,        ref found);
        }
        else
        {
            Debug.LogWarning($"[KinectPlayerController] '{character.name}' has no Humanoid Avatar — falling back to name search.");
            leftHand  = NameSearch(new[]{ "LeftHand",  "Body1:LeftHand",  "mixamorig:LeftHand"  }, root, ref found);
            rightHand = NameSearch(new[]{ "RightHand", "Body1:RightHand", "mixamorig:RightHand" }, root, ref found);
            chest     = NameSearch(new[]{ "Spine1", "Chest", "Body1:Spine1", "mixamorig:Spine1"  }, root, ref found);
            pelvis    = NameSearch(new[]{ "Hips", "Body1:Hips", "mixamorig:Hips"                 }, root, ref found);
        }

        Debug.Log($"[KinectPlayerController] Auto-Find: {found}/4 bones resolved on '{character.name}'" +
                  (hasAvatar ? " (Humanoid Avatar)" : " (name search)") + ".");
    }

    private static Transform Bone(Animator anim, HumanBodyBones bone, ref int found)
    {
        Transform t = anim.GetBoneTransform(bone);
        if (t != null) found++;
        return t;
    }

    private static Transform NameSearch(string[] names, Transform root, ref int found)
    {
        foreach (string n in names)
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == n) { found++; return t; }
        return null;
    }


    // =========================================================================
    //  Debug display
    // =========================================================================
    private void UpdateDebugText()
    {
        if (debugText == null) return;

        float pelvisY   = pelvis  != null ? pelvis.position.y  : 0f;
        float pelvisZ   = pelvis  != null ? pelvis.position.z  : 0f;
        float aboveBase = pelvisY - _pelvisBaseline;

        Vector3 chestPos = chest != null ? chest.position : Vector3.zero;
        Vector3 avgOffset = Vector3.zero;
        if (leftHand != null && rightHand != null)
            avgOffset = ((leftHand.position - chestPos) + (rightHand.position - chestPos)) * 0.5f;

        debugText.text =
            $"── Kinect Input ──\n" +
            $"Input X     : {InputAxis.x,+6:F2}\n" +
            $"Input Y     : {InputAxis.y,+6:F2}\n" +
            $"\n── Hand Offsets (avg) ──\n" +
            $"Offset X    : {avgOffset.x,+6:F3}\n" +
            $"Offset Y    : {avgOffset.y,+6:F3}\n" +
            $"\n── Jump ──\n" +
            $"Pelvis Y    : {pelvisY:F3}\n" +
            $"Baseline    : {_pelvisBaseline:F3}\n" +
            $"Above Base  : {aboveBase,+6:F3}\n" +
            $"Armed       : {_jumpArmed}\n" +
            $"Jump        : {(JumpPressed ? "YES" : "no")}\n" +
            $"\n── Depth ──\n" +
            $"Pelvis Z    : {pelvisZ,+6:F3}\n" +
            $"Tracked     : {(IsTracked ? "YES" : "no")}";
    }
}
