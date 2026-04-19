using UnityEngine;
using System;


// =============================================================================
//  Brekel_Body_v3_HumanoidMapper
//
//  Identical logic to Brekel_Body_v3_DefaultMapper but auto-finds joints by
//  name — no manual Transform assignment needed.
//
//  Uses the same offset * rotation approach as the DefaultMapper:
//    localRotation = Quaternion.Inverse(bindPoseRot) * incomingRot
//
//  Assign the character root to `character` and click "Auto-Find Joints",
//  or it runs automatically at Start() for any unassigned fields.
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
    [Tooltip("Root GameObject of the character skeleton — used for auto-find")]
    public GameObject character;

    [Header("Mapping Settings")]
    [Tooltip("Which body ID from the stream drives this character")]
    public int  bodyID              = 0;
    [Tooltip("Apply positions to all joints. When OFF only the root moves (reduces foot sliding)")]
    public bool applyPositionsToAll = true;
    [Tooltip("Lock the root (hips) localPosition to (0,0,0)")]
    public bool lockRootPosition    = false;

    [Header("Body Joints (auto-filled)")]
    public Transform hips, spine, chest, neck, head;
    public Transform upperLeg_L, lowerLeg_L, foot_L;
    public Transform upperLeg_R, lowerLeg_R, foot_R;
    public Transform collar_L, upperArm_L, foreArm_L, hand_L;
    public Transform collar_R, upperArm_R, foreArm_R, hand_R;

    [Header("Face")]
    [Tooltip("Face SkinnedMeshRenderer with blendshapes")]
    public SkinnedMeshRenderer faceMesh;

    // -------------------------------------------------------------------------
    //  Name candidate lists (Body1 / Mixamo / generic — first match wins)
    // -------------------------------------------------------------------------
    private static readonly string[] N_hips    = { "Body1:Hips",          "Hips",          "mixamorig:Hips"          };
    private static readonly string[] N_spine   = { "Body1:Spine",         "Spine",         "mixamorig:Spine"         };
    private static readonly string[] N_chest   = { "Body1:Spine1",        "Spine1",        "mixamorig:Spine1",        "Chest", "Body1:Chest" };
    private static readonly string[] N_neck    = { "Body1:Neck",          "Neck",          "mixamorig:Neck"          };
    private static readonly string[] N_head    = { "Body1:Head",          "Head",          "mixamorig:Head"          };
    private static readonly string[] N_upLL    = { "Body1:LeftUpLeg",     "LeftUpLeg",     "mixamorig:LeftUpLeg",     "LeftUpperLeg"  };
    private static readonly string[] N_loLL    = { "Body1:LeftLeg",       "LeftLeg",       "mixamorig:LeftLeg",       "LeftLowerLeg"  };
    private static readonly string[] N_footL   = { "Body1:LeftFoot",      "LeftFoot",      "mixamorig:LeftFoot"      };
    private static readonly string[] N_upRL    = { "Body1:RightUpLeg",    "RightUpLeg",    "mixamorig:RightUpLeg",    "RightUpperLeg" };
    private static readonly string[] N_loRL    = { "Body1:RightLeg",      "RightLeg",      "mixamorig:RightLeg",      "RightLowerLeg" };
    private static readonly string[] N_footR   = { "Body1:RightFoot",     "RightFoot",     "mixamorig:RightFoot"     };
    private static readonly string[] N_colL    = { "Body1:LeftShoulder",  "LeftShoulder",  "mixamorig:LeftShoulder"  };
    private static readonly string[] N_upAL    = { "Body1:LeftArm",       "LeftArm",       "mixamorig:LeftArm",       "LeftUpperArm"  };
    private static readonly string[] N_foAL    = { "Body1:LeftForeArm",   "LeftForeArm",   "mixamorig:LeftForeArm",   "LeftLowerArm"  };
    private static readonly string[] N_handL   = { "Body1:LeftHand",      "LeftHand",      "mixamorig:LeftHand"      };
    private static readonly string[] N_colR    = { "Body1:RightShoulder", "RightShoulder", "mixamorig:RightShoulder" };
    private static readonly string[] N_upAR    = { "Body1:RightArm",      "RightArm",      "mixamorig:RightArm",      "RightUpperArm" };
    private static readonly string[] N_foAR    = { "Body1:RightForeArm",  "RightForeArm",  "mixamorig:RightForeArm",  "RightLowerArm" };
    private static readonly string[] N_handR   = { "Body1:RightHand",     "RightHand",     "mixamorig:RightHand"     };
    private static readonly string[] N_face    = { "Body1:Face_Mesh",     "Face_Mesh",     "FaceMesh",                "Face"          };

    // -------------------------------------------------------------------------
    //  Internal
    // -------------------------------------------------------------------------
    private Quaternion[] _offsets = new Quaternion[(int)Brekel_joint_name_v3.numJoints];
    private const int    NumBlendshapes = (int)Brekel_blendshape_name.numBlendshapes;


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

        AutoFindJoints(overwrite: false);
        StoreOffsets();
    }

    void Update()
    {
        if (receiver == null || !receiver.IsConnected)
            return;

        bodyID = Mathf.Clamp(bodyID, 0, Brekel_Body_v3_Receiver.MaxBodies - 1);

        BrekelBodyFrame body = receiver.GetBody(bodyID);
        if (body == null) return;

        bool applyPos = applyPositionsToAll;

        ApplyJoint(hips,       Brekel_joint_name_v3.waist,      true,     body);
        ApplyJoint(spine,      Brekel_joint_name_v3.spine,      applyPos, body);
        ApplyJoint(chest,      Brekel_joint_name_v3.chest,      applyPos, body);
        ApplyJoint(neck,       Brekel_joint_name_v3.neck,       applyPos, body);
        ApplyJoint(head,       Brekel_joint_name_v3.head,       applyPos, body);
        ApplyJoint(upperLeg_L, Brekel_joint_name_v3.upperLeg_L, applyPos, body);
        ApplyJoint(lowerLeg_L, Brekel_joint_name_v3.lowerLeg_L, applyPos, body);
        ApplyJoint(foot_L,     Brekel_joint_name_v3.foot_L,     applyPos, body);
        ApplyJoint(upperLeg_R, Brekel_joint_name_v3.upperLeg_R, applyPos, body);
        ApplyJoint(lowerLeg_R, Brekel_joint_name_v3.lowerLeg_R, applyPos, body);
        ApplyJoint(foot_R,     Brekel_joint_name_v3.foot_R,     applyPos, body);
        ApplyJoint(collar_L,   Brekel_joint_name_v3.collar_L,   applyPos, body);
        ApplyJoint(upperArm_L, Brekel_joint_name_v3.upperArm_L, applyPos, body);
        ApplyJoint(foreArm_L,  Brekel_joint_name_v3.foreArm_L,  applyPos, body);
        ApplyJoint(hand_L,     Brekel_joint_name_v3.hand_L,     applyPos, body);
        ApplyJoint(collar_R,   Brekel_joint_name_v3.collar_R,   applyPos, body);
        ApplyJoint(upperArm_R, Brekel_joint_name_v3.upperArm_R, applyPos, body);
        ApplyJoint(foreArm_R,  Brekel_joint_name_v3.foreArm_R,  applyPos, body);
        ApplyJoint(hand_R,     Brekel_joint_name_v3.hand_R,     applyPos, body);

        // Root position lock override
        if (hips != null && lockRootPosition)
            hips.localPosition = Vector3.zero;

        ApplyBlendshapes(body);
    }


    // =========================================================================
    //  Apply
    // =========================================================================
    private void ApplyJoint(Transform t, Brekel_joint_name_v3 joint, bool applyPos, BrekelBodyFrame body)
    {
        if (t == null) return;
        int idx = (int)joint;
        if (applyPos)
            t.localPosition = body.joints[idx].position;
        t.localRotation = _offsets[idx] * body.joints[idx].rotation;
    }

    private void ApplyBlendshapes(BrekelBodyFrame body)
    {
        if (faceMesh == null) return;
        int count = Mathf.Min(faceMesh.sharedMesh.blendShapeCount, NumBlendshapes);
        for (int i = 0; i < count; i++)
            faceMesh.SetBlendShapeWeight(i, body.blendshapes[i] * 100f);
    }


    // =========================================================================
    //  Rest-pose offset baking
    // =========================================================================
    private void StoreOffsets()
    {
        Bake(hips,       Brekel_joint_name_v3.waist);
        Bake(spine,      Brekel_joint_name_v3.spine);
        Bake(chest,      Brekel_joint_name_v3.chest);
        Bake(neck,       Brekel_joint_name_v3.neck);
        Bake(upperLeg_L, Brekel_joint_name_v3.upperLeg_L);
        Bake(lowerLeg_L, Brekel_joint_name_v3.lowerLeg_L);
        Bake(foot_L,     Brekel_joint_name_v3.foot_L);
        Bake(upperLeg_R, Brekel_joint_name_v3.upperLeg_R);
        Bake(lowerLeg_R, Brekel_joint_name_v3.lowerLeg_R);
        Bake(foot_R,     Brekel_joint_name_v3.foot_R);
        Bake(collar_L,   Brekel_joint_name_v3.collar_L);
        Bake(upperArm_L, Brekel_joint_name_v3.upperArm_L);
        Bake(foreArm_L,  Brekel_joint_name_v3.foreArm_L);
        Bake(hand_L,     Brekel_joint_name_v3.hand_L);
        Bake(collar_R,   Brekel_joint_name_v3.collar_R);
        Bake(upperArm_R, Brekel_joint_name_v3.upperArm_R);
        Bake(foreArm_R,  Brekel_joint_name_v3.foreArm_R);
        Bake(hand_R,     Brekel_joint_name_v3.hand_R);
    }

    private void Bake(Transform t, Brekel_joint_name_v3 joint)
    {
        if (t != null)
            _offsets[(int)joint] = Quaternion.Inverse(t.localRotation);
    }


    // =========================================================================
    //  Auto-find joints by name
    // =========================================================================
    [ContextMenu("Auto-Find Joints")]
    public void AutoFindJoints() => AutoFindJoints(overwrite: true);

    private void AutoFindJoints(bool overwrite)
    {
        GameObject root = character != null ? character : gameObject;
        int found = 0, total = 19;

        hips       = Resolve(hips,       N_hips,  root, overwrite, ref found);
        spine      = Resolve(spine,      N_spine, root, overwrite, ref found);
        chest      = Resolve(chest,      N_chest, root, overwrite, ref found);
        neck       = Resolve(neck,       N_neck,  root, overwrite, ref found);
        head       = Resolve(head,       N_head,  root, overwrite, ref found);
        upperLeg_L = Resolve(upperLeg_L, N_upLL,  root, overwrite, ref found);
        lowerLeg_L = Resolve(lowerLeg_L, N_loLL,  root, overwrite, ref found);
        foot_L     = Resolve(foot_L,     N_footL, root, overwrite, ref found);
        upperLeg_R = Resolve(upperLeg_R, N_upRL,  root, overwrite, ref found);
        lowerLeg_R = Resolve(lowerLeg_R, N_loRL,  root, overwrite, ref found);
        foot_R     = Resolve(foot_R,     N_footR, root, overwrite, ref found);
        collar_L   = Resolve(collar_L,   N_colL,  root, overwrite, ref found);
        upperArm_L = Resolve(upperArm_L, N_upAL,  root, overwrite, ref found);
        foreArm_L  = Resolve(foreArm_L,  N_foAL,  root, overwrite, ref found);
        hand_L     = Resolve(hand_L,     N_handL, root, overwrite, ref found);
        collar_R   = Resolve(collar_R,   N_colR,  root, overwrite, ref found);
        upperArm_R = Resolve(upperArm_R, N_upAR,  root, overwrite, ref found);
        foreArm_R  = Resolve(foreArm_R,  N_foAR,  root, overwrite, ref found);
        hand_R     = Resolve(hand_R,     N_handR, root, overwrite, ref found);

        if (overwrite || faceMesh == null)
        {
            Transform ft = FindInHierarchy(N_face, root.transform);
            if (ft != null)
            {
                faceMesh = ft.GetComponent<SkinnedMeshRenderer>();
                if (faceMesh != null) { found++; total++; }
            }
        }

        Debug.Log($"[HumanoidMapper] Auto-Find: {found}/{total} joints on '{root.name}'.");
    }

    private static Transform Resolve(Transform cur, string[] names, GameObject root,
                                     bool overwrite, ref int found)
    {
        if (!overwrite && cur != null) return cur;
        Transform t = FindInHierarchy(names, root.transform);
        if (t != null) { found++; return t; }
        return cur;
    }

    private static Transform FindInHierarchy(string[] names, Transform root)
    {
        foreach (string n in names)
        {
            Transform t = FindInHierarchy(n, root);
            if (t != null) return t;
        }
        return null;
    }

    private static Transform FindInHierarchy(string name, Transform root)
    {
        if (root.name == name) return root;
        foreach (Transform c in root.GetComponentsInChildren<Transform>(true))
            if (c.name == name) return c;
        return null;
    }
}
