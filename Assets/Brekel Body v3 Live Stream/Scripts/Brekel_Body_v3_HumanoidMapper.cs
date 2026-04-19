using UnityEngine;
using System;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;


// ---------------------------------------------------------------------------
//  Brekel joint index -> Unity HumanBodyBones mapping.
//  Each entry also carries a secondary bone tried when the primary returns
//  null (e.g. Chest vs UpperChest differs between rigs).
//  head_tip, toes_L/R, fingersTip_L/R have no HumanBodyBones equivalent
//  and are intentionally left unmapped (HumanBodyBones.LastBone).
// ---------------------------------------------------------------------------
internal static class BrekelHumanoidMap
{
    // Index order matches Brekel_joint_name_v3 enum (0..23)
    public static readonly HumanBodyBones[] Bones = new HumanBodyBones[]
    {
        HumanBodyBones.Hips,            // 0  waist
        HumanBodyBones.Spine,           // 1  spine
        HumanBodyBones.Chest,           // 2  chest       (falls back to UpperChest)
        HumanBodyBones.Neck,            // 3  neck
        HumanBodyBones.Head,            // 4  head
        HumanBodyBones.LastBone,        // 5  head_tip      (no equivalent)

        HumanBodyBones.LeftUpperLeg,    // 6  upperLeg_L
        HumanBodyBones.LeftLowerLeg,    // 7  lowerLeg_L
        HumanBodyBones.LeftFoot,        // 8  foot_L
        HumanBodyBones.LeftToes,        // 9  toes_L
        
        HumanBodyBones.RightUpperLeg,   // 10 upperLeg_R
        HumanBodyBones.RightLowerLeg,   // 11 lowerLeg_R
        HumanBodyBones.RightFoot,       // 12 foot_R
        HumanBodyBones.RightToes,       // 13 toes_R

        HumanBodyBones.LeftShoulder,    // 14 collar_L
        HumanBodyBones.LeftUpperArm,    // 15 upperArm_L
        HumanBodyBones.LeftLowerArm,    // 16 foreArm_L
        HumanBodyBones.LeftHand,        // 17 hand_L
        HumanBodyBones.LastBone,        // 18 fingersTip_L  (no equivalent)

        HumanBodyBones.RightShoulder,   // 19 collar_R
        HumanBodyBones.RightUpperArm,   // 20 upperArm_R
        HumanBodyBones.RightLowerArm,   // 21 foreArm_R
        HumanBodyBones.RightHand,       // 22 hand_R
        HumanBodyBones.LastBone,        // 23 fingersTip_R  (no equivalent)
    };

    // Fallback bones tried when the primary lookup returns null
    public static readonly HumanBodyBones[] FallbackBones = new HumanBodyBones[]
    {
        HumanBodyBones.LastBone,        // 0  waist
        HumanBodyBones.LastBone,        // 1  spine
        HumanBodyBones.UpperChest,      // 2  chest -> UpperChest fallback
        HumanBodyBones.LastBone,        // 3  neck
        HumanBodyBones.LastBone,        // 4  head
        HumanBodyBones.LastBone,        // 5  head_tip
        HumanBodyBones.LastBone,        // 6  upperLeg_L
        HumanBodyBones.LastBone,        // 7  lowerLeg_L
        HumanBodyBones.LastBone,        // 8  foot_L
        HumanBodyBones.LastBone,        // 9  toes_L
        HumanBodyBones.LastBone,        // 10 upperLeg_R
        HumanBodyBones.LastBone,        // 11 lowerLeg_R
        HumanBodyBones.LastBone,        // 12 foot_R
        HumanBodyBones.LastBone,        // 13 toes_R
        HumanBodyBones.LastBone,        // 14 collar_L
        HumanBodyBones.LastBone,        // 15 upperArm_L
        HumanBodyBones.LastBone,        // 16 foreArm_L
        HumanBodyBones.LastBone,        // 17 hand_L
        HumanBodyBones.LastBone,        // 18 fingersTip_L
        HumanBodyBones.LastBone,        // 19 collar_R
        HumanBodyBones.LastBone,        // 20 upperArm_R
        HumanBodyBones.LastBone,        // 21 foreArm_R
        HumanBodyBones.LastBone,        // 22 hand_R
        HumanBodyBones.LastBone,        // 23 fingersTip_R
    };

    public static readonly string[] JointNames = new string[]
    {
        "waist", "spine", "chest", "neck", "head", "head_tip",
        "upperLeg_L", "lowerLeg_L", "foot_L", "toes_L",
        "upperLeg_R", "lowerLeg_R", "foot_R", "toes_R",
        "collar_L", "upperArm_L", "foreArm_L", "hand_L", "fingersTip_L",
        "collar_R", "upperArm_R", "foreArm_R", "hand_R", "fingersTip_R",
    };

    public const int NumJoints = 24;
}


// ---------------------------------------------------------------------------
//  Per-joint axis flip override, editable in the Inspector at runtime so
//  flipped joints can be corrected without recompiling.
// ---------------------------------------------------------------------------
[Serializable]
public class JointFlipOverride
{
    [HideInInspector] public string label;  // display-only, set from joint name
    public bool flipX;
    public bool flipY;
    public bool flipZ;
}


// ---------------------------------------------------------------------------
//  One entry in the inspector: a Brekel body stream ID mapped to a humanoid
//  character in the scene.
// ---------------------------------------------------------------------------
[Serializable]
public class BodyMapping
{
    [Tooltip("Which body ID from the Brekel stream drives this character (0 = first detected body)")]
    public int bodyID = 0;

    [Tooltip("Root GameObject of the character. Must have an Animator set to Humanoid avatar.")]
    public GameObject character;

    [Tooltip("(Optional) Face SkinnedMeshRenderer on the character for blendshape expressions")]
    public SkinnedMeshRenderer faceMesh;

    [Tooltip("Apply world positions to every joint. Disable to keep root-only translation (reduces foot sliding)")]
    public bool applyPositionsToAll = true;

    [Tooltip("Lock the root (hips) localPosition to (0,0,0), ignoring any translation from the stream")]
    public bool lockRootPosition = false;

    [Tooltip("Per-joint axis flip overrides. Enable X/Y/Z on a joint if it appears mirrored or inverted at runtime.")]
    public JointFlipOverride[] jointFlips = new JointFlipOverride[BrekelHumanoidMap.NumJoints];

    // Resolved at runtime
    [HideInInspector] public Animator animator;
    [HideInInspector] public Transform[] boneTransforms;   // length = NumJoints
    [HideInInspector] public Quaternion[] boneOffsets;     // rest-pose inverses
}


// ---------------------------------------------------------------------------
//  Raw per-joint data shared between the network thread and main thread.
//  Mirrors the original Brekel_skeleton_v3 / Brekel_joint_v3 structs but
//  kept internal to this script to avoid namespace collisions.
// ---------------------------------------------------------------------------
internal class BrekelBody
{
    public float      timestamp;
    public float[]    confidence  = new float[BrekelHumanoidMap.NumJoints];
    public Vector3[]  positions   = new Vector3[BrekelHumanoidMap.NumJoints];
    public Quaternion[] rotations = new Quaternion[BrekelHumanoidMap.NumJoints];
    public float[]    blendshapes = new float[52]; // matches Brekel_blendshapes.numBlendshapes
}


// ---------------------------------------------------------------------------
//  Main MonoBehaviour
// ---------------------------------------------------------------------------
public class Brekel_Body_v3_HumanoidMapper : MonoBehaviour
{
    // ------------------------------------------------------------------
    //  Inspector
    // ------------------------------------------------------------------
    [Header("Network Settings")]
    [Tooltip("Hostname or IP where Brekel Body v3 is running")]
    public string host = "localhost";
    [Tooltip("TCP port Brekel Body v3 is streaming on (default 8844)")]
    public int    port = 8844;

    [Header("Body Mappings")]
    [Tooltip("Each entry pairs one Brekel body ID with one humanoid character in the scene")]
    public List<BodyMapping> mappings = new List<BodyMapping>();

    // ------------------------------------------------------------------
    //  Internal networking state
    // ------------------------------------------------------------------
    private const int MaxBodies      = 10;
    private const int NumJoints      = BrekelHumanoidMap.NumJoints;
    private const int NumBlendshapes = 52;
    private const int ReadBufferSize = 65535;

    private TcpClient  _client;
    private byte[]     _readBuffer = new byte[ReadBufferSize];
    private bool       _isConnected;
    private bool       _networkBusy;           // true while background read is in flight

    // Double-buffered body data so the network thread never writes while
    // the main thread reads.  _backBodies is written by the network thread;
    // _frontBodies is swapped in on the main thread.
    private BrekelBody[] _frontBodies;
    private BrekelBody[] _backBodies;
    private readonly object _swapLock = new object();
    private bool _newDataReady;


    // ==================================================================
    //  Unity lifecycle
    // ==================================================================
    void Start()
    {
        _frontBodies = AllocBodies();
        _backBodies  = AllocBodies();

        ResolveMappings();
        Connect();
    }

    void OnDisable()
    {
        Disconnect();
    }

    void Update()
    {
        if (!_isConnected)
            return;

        // Swap buffers if the network thread has written a new frame
        if (_newDataReady)
        {
            lock (_swapLock)
            {
                BrekelBody[] tmp = _frontBodies;
                _frontBodies     = _backBodies;
                _backBodies      = tmp;
                _newDataReady    = false;
            }
        }

        ApplyAllMappings();
    }


    // ==================================================================
    //  Mapping setup
    // ==================================================================

    /// <summary>
    /// Walk every BodyMapping, pull bone Transforms out of the Animator,
    /// and bake rest-pose offsets.
    /// </summary>
    private void ResolveMappings()
    {
        foreach (BodyMapping m in mappings)
        {
            if (m.character == null)
            {
                Debug.LogWarning("[BrekelMapper] A BodyMapping has no character assigned — skipping.");
                continue;
            }

            m.animator = m.character.GetComponent<Animator>();
            if (m.animator == null || !m.animator.isHuman)
            {
                Debug.LogWarning($"[BrekelMapper] '{m.character.name}' has no Animator or is not set to Humanoid avatar — skipping.");
                continue;
            }

            m.boneTransforms = new Transform[NumJoints];
            m.boneOffsets    = new Quaternion[NumJoints];

            // Ensure jointFlips array is always the right length (safe across reloads)
            if (m.jointFlips == null || m.jointFlips.Length != NumJoints)
                m.jointFlips = new JointFlipOverride[NumJoints];

            // Disable the Animator so its runtime pose doesn't pollute the
            // bind-pose localRotations we're about to bake as offsets.
            bool wasEnabled = m.animator.enabled;
            m.animator.enabled = false;

            for (int i = 0; i < NumJoints; i++)
            {
                // Initialise flip entry with readable label
                if (m.jointFlips[i] == null)
                    m.jointFlips[i] = new JointFlipOverride();
                m.jointFlips[i].label = BrekelHumanoidMap.JointNames[i];

                HumanBodyBones hbb = BrekelHumanoidMap.Bones[i];
                if (hbb == HumanBodyBones.LastBone)
                    continue;

                Transform t = m.animator.GetBoneTransform(hbb);

                // Try fallback bone if primary returned null (e.g. Chest vs UpperChest)
                if (t == null)
                {
                    HumanBodyBones fallback = BrekelHumanoidMap.FallbackBones[i];
                    if (fallback != HumanBodyBones.LastBone)
                        t = m.animator.GetBoneTransform(fallback);
                }

                if (t == null)
                    continue;

                m.boneTransforms[i] = t;
                m.boneOffsets[i]    = Quaternion.Inverse(t.localRotation);
            }

            // Restore Animator state
            m.animator.enabled = wasEnabled;

            // Auto-find face mesh if not set
            if (m.faceMesh == null)
                m.faceMesh = m.character.GetComponentInChildren<SkinnedMeshRenderer>();
        }
    }


    // ==================================================================
    //  Per-frame application
    // ==================================================================
    private void ApplyAllMappings()
    {
        foreach (BodyMapping m in mappings)
        {
            if (m.boneTransforms == null)
                continue;

            int id = Mathf.Clamp(m.bodyID, 0, MaxBodies - 1);
            BrekelBody body = _frontBodies[id];

            for (int i = 0; i < NumJoints; i++)
            {
                Transform t = m.boneTransforms[i];
                if (t == null)
                    continue;

                bool isRoot   = (i == 0);
                bool applyPos = isRoot || m.applyPositionsToAll;
                if (applyPos)
                    t.localPosition = (isRoot && m.lockRootPosition) ? Vector3.zero : body.positions[i];

                Quaternion incoming = body.rotations[i];
                JointFlipOverride flip = m.jointFlips[i];
                if (flip != null)
                    incoming = ApplyFlips(incoming, flip);

                t.localRotation = m.boneOffsets[i] * incoming;
            }

            ApplyBlendshapes(m.faceMesh, body);
        }
    }

    /// <summary>
    /// Flips the selected axes of a quaternion by negating the corresponding
    /// imaginary components. Each flip is equivalent to a 180-degree reflection
    /// around that axis, letting you correct a mirrored joint without changing
    /// the coordinate conversion math.
    /// </summary>
    private static Quaternion ApplyFlips(Quaternion q, JointFlipOverride flip)
    {
        if (flip.flipX) { q.x = -q.x; q.w = -q.w; }
        if (flip.flipY) { q.y = -q.y; q.w = -q.w; }
        if (flip.flipZ) { q.z = -q.z; q.w = -q.w; }
        return q;
    }

    private static void ApplyBlendshapes(SkinnedMeshRenderer smr, BrekelBody body)
    {
        if (smr == null) return;
        int count = Mathf.Min(smr.sharedMesh.blendShapeCount, NumBlendshapes);
        for (int i = 0; i < count; i++)
            smr.SetBlendShapeWeight(i, body.blendshapes[i] * 100f);
    }


    // ==================================================================
    //  Networking
    // ==================================================================
    private bool Connect()
    {
        try
        {
            _client = new TcpClient(host, port);
            _client.GetStream().BeginRead(_readBuffer, 0, ReadBufferSize, OnFrameReceived, null);
            _isConnected = true;
            Debug.Log("[BrekelMapper] Connected to Brekel Body v3");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BrekelMapper] Could not connect to Brekel Body v3: " + ex.Message);
            return false;
        }
    }

    private void Disconnect()
    {
        _isConnected = false;
        _client?.Close();
        Debug.Log("[BrekelMapper] Disconnected from Brekel Body v3");
    }

    /// <summary>
    /// Async callback — runs on a background thread. Parses one TCP packet
    /// into _backBodies, then signals the main thread to swap.
    /// </summary>
    private void OnFrameReceived(IAsyncResult ar)
    {
        try
        {
            int bytesRead = _client.GetStream().EndRead(ar);

            if (bytesRead < 1)
            {
                Debug.Log("[BrekelMapper] Brekel stream closed.");
                _isConnected = false;
                return;
            }

            ParsePacket(bytesRead);
        }
        catch
        {
            // Silently drop broken packets — same behaviour as original script
        }

        // Queue next read only if still connected
        if (_isConnected)
            _client.GetStream().BeginRead(_readBuffer, 0, ReadBufferSize, OnFrameReceived, null);
    }

    private void ParsePacket(int bytesRead)
    {
        // Validate packet framing
        string header = Encoding.UTF8.GetString(_readBuffer, 0,           6);
        string footer = Encoding.UTF8.GetString(_readBuffer, bytesRead - 6, 6);
        if (header != "BRKL_S" || footer != "BRKL_E")
        {
            Debug.LogWarning("[BrekelMapper] Invalid packet framing — discarding.");
            return;
        }

        int index = 8; // skip 6-byte header + 2-byte encoded size
        int numJoints          = BitConverter.ToInt32(_readBuffer, index); index += 4;
        int numBodies          = BitConverter.ToInt32(_readBuffer, index); index += 4;
        int numFaceExpressions = BitConverter.ToInt32(_readBuffer, index); index += 4;

        if (numJoints != NumJoints)
        {
            Debug.LogWarning($"[BrekelMapper] Expected {NumJoints} joints but received {numJoints} — discarding packet.");
            return;
        }

        numBodies = Mathf.Min(numBodies, MaxBodies);

        for (int b = 0; b < numBodies; b++)
        {
            BrekelBody body = _backBodies[b];
            body.timestamp = BitConverter.ToSingle(_readBuffer, index); index += 4;

            for (int j = 0; j < numJoints; j++)
            {
                body.confidence[j] = BitConverter.ToSingle(_readBuffer, index); index += 4;

                float px = BitConverter.ToSingle(_readBuffer, index); index += 4;
                float py = BitConverter.ToSingle(_readBuffer, index); index += 4;
                float pz = BitConverter.ToSingle(_readBuffer, index); index += 4;
                float rx = BitConverter.ToSingle(_readBuffer, index); index += 4;
                float ry = BitConverter.ToSingle(_readBuffer, index); index += 4;
                float rz = BitConverter.ToSingle(_readBuffer, index); index += 4;

                body.positions[j]  = ConvertPosition(new Vector3(px, py, pz));
                body.rotations[j]  = ConvertRotation(new Vector3(rx, ry, rz));
            }

            int actualExprCount = BitConverter.ToInt32(_readBuffer, index); index += 4;
            if (actualExprCount > 0)
            {
                index += 4; // skip quality float
                for (int e = 0; e < actualExprCount; e++)
                {
                    float w = BitConverter.ToSingle(_readBuffer, index); index += 4;
                    if (e < NumBlendshapes)
                        body.blendshapes[e] = w;
                }
            }
        }

        // Signal swap on next Update()
        lock (_swapLock)
            _newDataReady = true;
    }


    // ==================================================================
    //  Coordinate system conversion.
    //
    //  Brekel Body v3 uses right-handed, Y-up (OpenGL convention).
    //  Unity uses left-handed, Y-up.
    //
    //  To go from Brekel → Unity:
    //    Position : negate X only (mirrors the left↔right axis)
    //    Rotation : negate Y and Z contributions to match handedness flip
    //               (X axis is shared and unchanged)
    // ==================================================================
    private static Vector3 ConvertPosition(Vector3 p)
    {
        p.x *= -1f;
        return p;
    }

    private static Quaternion ConvertRotation(Vector3 euler)
    {
        Quaternion qx = Quaternion.AngleAxis(euler.x, Vector3.right);
        Quaternion qy = Quaternion.AngleAxis(euler.y, Vector3.down);
        Quaternion qz = Quaternion.AngleAxis(euler.z, Vector3.back);
        return qz * qy * qx;
    }


    // ==================================================================
    //  Helpers
    // ==================================================================
    private static BrekelBody[] AllocBodies()
    {
        var arr = new BrekelBody[MaxBodies];
        for (int i = 0; i < MaxBodies; i++)
            arr[i] = new BrekelBody();
        return arr;
    }
}
