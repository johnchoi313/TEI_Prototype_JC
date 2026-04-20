using UnityEngine;


// =============================================================================
//  Brekel_Body_v3_HumanoidMapper
//
//  Drives any Unity Humanoid-rigged character from a Brekel Body v3 stream.
//  Bones are resolved at Start() via animator.GetBoneTransform(HumanBodyBones).
//
//  RETARGETING STRATEGY
//  --------------------
//  The DefaultMapper works by storing Inverse(bone.localRotation) at start
//  (the bind-pose inverse) and applying:
//
//    localRotation = defaultBindInverse * incomingRot
//
//  That "defaultBindInverse * incomingRot" is the DELTA — how much the joint
//  rotated away from the DefaultMapper rig's T-pose.
//
//  We retarget that same delta onto the humanoid rig by applying it on top of
//  the humanoid's own bind-pose rotation:
//
//    humanoid.localRotation = humanoidBindRot * delta
//                           = humanoidBindRot * (defaultBindInverse * incomingRot)
//
//  This requires a reference to a DefaultMapper so we can read its bind-pose
//  inverses.  Both rigs must be tracking the same body ID.
// =============================================================================
public class Brekel_Body_v3_HumanoidMapper : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector
    // -------------------------------------------------------------------------
    [Header("Receiver")]
    [Tooltip("Brekel_Body_v3_Receiver component that owns the TCP connection")]
    public Brekel_Body_v3_Receiver receiver;

    [Header("Character")]
    [Tooltip("GameObject with a Humanoid Animator/Avatar — bones resolved automatically")]
    public GameObject character;

    [Header("Retargeting Source")]
    [Tooltip("DefaultMapper whose bind-pose is used as the motion reference. " +
             "Must be tracking the same body ID.")]
    public Brekel_Body_v3_DefaultMapper defaultMapper;

    [Header("Face (optional)")]
    [Tooltip("Face SkinnedMeshRenderer with blendshapes — assign manually")]
    public SkinnedMeshRenderer faceMesh;

    // -------------------------------------------------------------------------
    //  Bone transforms — resolved at Start() via HumanBodyBones
    // -------------------------------------------------------------------------
    private Transform _hips, _spine, _chest, _neck, _head;
    private Transform _upperLeg_L, _lowerLeg_L, _foot_L;
    private Transform _upperLeg_R, _lowerLeg_R, _foot_R;
    private Transform _collar_L, _upperArm_L, _foreArm_L, _hand_L;
    private Transform _collar_R, _upperArm_R, _foreArm_R, _hand_R;

    // Humanoid rig's own bind-pose rotations (stored at Start)
    private Quaternion[] _humanoidBind = new Quaternion[(int)Brekel_joint_name_v3.numJoints];

    private const int NumBlendshapes = (int)Brekel_blendshape_name.numBlendshapes;


    // =========================================================================
    //  Unity lifecycle
    // =========================================================================
    void Start()
    {
        if (receiver == null)
        {
            Debug.LogError("[HumanoidMapper] No Receiver assigned — inactive.");
            enabled = false;
            return;
        }

        if (defaultMapper == null)
        {
            Debug.LogError("[HumanoidMapper] No DefaultMapper assigned — cannot retarget.");
            enabled = false;
            return;
        }

        if (!ResolveBones()) { enabled = false; return; }

        BakeHumanoidBind();
    }

    void Update()
    {
        if (receiver == null || !receiver.IsConnected)
            return;

        BrekelBodyFrame body = receiver.GetBody(defaultMapper.body_ID);
        if (body == null) return;

        Retarget(_hips,       Brekel_joint_name_v3.waist,      body);
        Retarget(_spine,      Brekel_joint_name_v3.spine,      body);
        Retarget(_chest,      Brekel_joint_name_v3.chest,      body);
        Retarget(_neck,       Brekel_joint_name_v3.neck,       body);
        Retarget(_head,       Brekel_joint_name_v3.head,       body);
        Retarget(_upperLeg_L, Brekel_joint_name_v3.upperLeg_L, body);
        Retarget(_lowerLeg_L, Brekel_joint_name_v3.lowerLeg_L, body);
        Retarget(_foot_L,     Brekel_joint_name_v3.foot_L,     body);
        Retarget(_upperLeg_R, Brekel_joint_name_v3.upperLeg_R, body);
        Retarget(_lowerLeg_R, Brekel_joint_name_v3.lowerLeg_R, body);
        Retarget(_foot_R,     Brekel_joint_name_v3.foot_R,     body);
        Retarget(_collar_L,   Brekel_joint_name_v3.collar_L,   body);
        Retarget(_upperArm_L, Brekel_joint_name_v3.upperArm_L, body);
        Retarget(_foreArm_L,  Brekel_joint_name_v3.foreArm_L,  body);
        Retarget(_hand_L,     Brekel_joint_name_v3.hand_L,     body);
        Retarget(_collar_R,   Brekel_joint_name_v3.collar_R,   body);
        Retarget(_upperArm_R, Brekel_joint_name_v3.upperArm_R, body);
        Retarget(_foreArm_R,  Brekel_joint_name_v3.foreArm_R,  body);
        Retarget(_hand_R,     Brekel_joint_name_v3.hand_R,     body);

        ApplyBlendshapes(body);
    }


    // =========================================================================
    //  Bone resolution via HumanBodyBones
    // =========================================================================
    private bool ResolveBones()
    {
        if (character == null)
        {
            Debug.LogError("[HumanoidMapper] No Character assigned.");
            return false;
        }

        Animator anim = character.GetComponentInChildren<Animator>();
        if (anim == null || !anim.isHuman)
        {
            Debug.LogError($"[HumanoidMapper] '{character.name}' has no Humanoid Animator/Avatar.");
            return false;
        }

        _hips       = anim.GetBoneTransform(HumanBodyBones.Hips);
        _spine      = anim.GetBoneTransform(HumanBodyBones.Spine);
        _chest      = anim.GetBoneTransform(HumanBodyBones.Chest);
        _neck       = anim.GetBoneTransform(HumanBodyBones.Neck);
        _head       = anim.GetBoneTransform(HumanBodyBones.Head);
        _upperLeg_L = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        _lowerLeg_L = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        _foot_L     = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
        _upperLeg_R = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        _lowerLeg_R = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        _foot_R     = anim.GetBoneTransform(HumanBodyBones.RightFoot);
        _collar_L   = anim.GetBoneTransform(HumanBodyBones.LeftShoulder);
        _upperArm_L = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        _foreArm_L  = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        _hand_L     = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        _collar_R   = anim.GetBoneTransform(HumanBodyBones.RightShoulder);
        _upperArm_R = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        _foreArm_R  = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        _hand_R     = anim.GetBoneTransform(HumanBodyBones.RightHand);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[HumanoidMapper] Bones resolved for '{character.name}':");
        void Log(string label, Transform t) =>
            sb.AppendLine($"  {label,-14} → {(t != null ? t.name : "(null — not in avatar)")}");
        Log("hips",       _hips);       Log("spine",      _spine);
        Log("chest",      _chest);      Log("neck",       _neck);
        Log("head",       _head);       Log("upperLeg_L", _upperLeg_L);
        Log("lowerLeg_L", _lowerLeg_L); Log("foot_L",     _foot_L);
        Log("upperLeg_R", _upperLeg_R); Log("lowerLeg_R", _lowerLeg_R);
        Log("foot_R",     _foot_R);     Log("collar_L",   _collar_L);
        Log("upperArm_L", _upperArm_L); Log("foreArm_L",  _foreArm_L);
        Log("hand_L",     _hand_L);     Log("collar_R",   _collar_R);
        Log("upperArm_R", _upperArm_R); Log("foreArm_R",  _foreArm_R);
        Log("hand_R",     _hand_R);
        Debug.Log(sb.ToString());

        return true;
    }


    // =========================================================================
    //  Bake humanoid bind-pose rotations (stored at Start, before any motion)
    // =========================================================================
    private void BakeHumanoidBind()
    {
        Bake(_hips,       Brekel_joint_name_v3.waist);
        Bake(_spine,      Brekel_joint_name_v3.spine);
        Bake(_chest,      Brekel_joint_name_v3.chest);
        Bake(_neck,       Brekel_joint_name_v3.neck);
        Bake(_head,       Brekel_joint_name_v3.head);
        Bake(_upperLeg_L, Brekel_joint_name_v3.upperLeg_L);
        Bake(_lowerLeg_L, Brekel_joint_name_v3.lowerLeg_L);
        Bake(_foot_L,     Brekel_joint_name_v3.foot_L);
        Bake(_upperLeg_R, Brekel_joint_name_v3.upperLeg_R);
        Bake(_lowerLeg_R, Brekel_joint_name_v3.lowerLeg_R);
        Bake(_foot_R,     Brekel_joint_name_v3.foot_R);
        Bake(_collar_L,   Brekel_joint_name_v3.collar_L);
        Bake(_upperArm_L, Brekel_joint_name_v3.upperArm_L);
        Bake(_foreArm_L,  Brekel_joint_name_v3.foreArm_L);
        Bake(_hand_L,     Brekel_joint_name_v3.hand_L);
        Bake(_collar_R,   Brekel_joint_name_v3.collar_R);
        Bake(_upperArm_R, Brekel_joint_name_v3.upperArm_R);
        Bake(_foreArm_R,  Brekel_joint_name_v3.foreArm_R);
        Bake(_hand_R,     Brekel_joint_name_v3.hand_R);
    }

    private void Bake(Transform t, Brekel_joint_name_v3 joint)
    {
        if (t != null)
            _humanoidBind[(int)joint] = t.localRotation;
    }


    // =========================================================================
    //  Retarget one bone
    //
    //  delta = defaultBindInverse * incomingRot
    //        = "how much this joint moved from the DefaultMapper rig's T-pose"
    //
    //  humanoid.localRotation = humanoidBindRot * delta
    //        = apply that same delta on top of humanoid's own T-pose
    // =========================================================================
    private void Retarget(Transform t, Brekel_joint_name_v3 joint, BrekelBodyFrame body)
    {
        if (t == null) return;
        int idx = (int)joint;

        Quaternion defaultBindInv = defaultMapper.GetBindPoseInverse(idx);
        Quaternion incoming       = body.joints[idx].rotation;

        Quaternion delta = defaultBindInv * incoming;
        t.localRotation  = _humanoidBind[idx] * delta;
    }


    // =========================================================================
    //  Blendshapes
    // =========================================================================
    private void ApplyBlendshapes(BrekelBodyFrame body)
    {
        if (faceMesh == null) return;
        int count = Mathf.Min(faceMesh.sharedMesh.blendShapeCount, NumBlendshapes);
        for (int i = 0; i < count; i++)
            faceMesh.SetBlendShapeWeight(i, body.blendshapes[i] * 100f);
    }
}
