using UnityEngine;

public class TEIHandGestureInterpreter : MonoBehaviour
{
    [Header("Input Source")]
    [SerializeField] private TEIHandTrackingRunner runner;

    [Header("Fist Detection")]
    [SerializeField] private float confidenceSmoothing = 12f;
    [SerializeField] private float fistOnThreshold = 0.68f;
    [SerializeField] private float fistOffThreshold = 0.52f;

    [Header("Curl Ratios")]
    [SerializeField] private float fingerCurlThreshold = 0.92f;
    [SerializeField] private float thumbCurlThreshold = 0.95f;

    [Header("Debug")]
    [SerializeField] private bool logStateChanges = false;

    // Public read-only API for other systems
    public bool IsLeftFist => isLeftFist;
    public bool IsRightFist => isRightFist;

    public bool LeftFistDown => leftFistDown;
    public bool RightFistDown => rightFistDown;
    public bool LeftFistUp => leftFistUp;
    public bool RightFistUp => rightFistUp;

    public float LeftFistConfidence => leftFistConfidence;
    public float RightFistConfidence => rightFistConfidence;

    // Internal backing fields
    private bool isLeftFist;
    private bool isRightFist;

    private bool leftFistDown;
    private bool rightFistDown;
    private bool leftFistUp;
    private bool rightFistUp;

    private float leftFistConfidence;
    private float rightFistConfidence;

    private void Update()
    {
        // Reset one-frame events
        leftFistDown = false;
        rightFistDown = false;
        leftFistUp = false;
        rightFistUp = false;

        if (runner == null)
            return;

        UpdateHand(
            isLeft: true,
            hasHand: runner.HasLeftHand,
            landmarks: runner.LeftHandLandmarks,
            confidence: ref leftFistConfidence,
            state: ref isLeftFist,
            down: ref leftFistDown,
            up: ref leftFistUp
        );

        UpdateHand(
            isLeft: false,
            hasHand: runner.HasRightHand,
            landmarks: runner.RightHandLandmarks,
            confidence: ref rightFistConfidence,
            state: ref isRightFist,
            down: ref rightFistDown,
            up: ref rightFistUp
        );
    }

    private void UpdateHand(
        bool isLeft,
        bool hasHand,
        Vector3[] landmarks,
        ref float confidence,
        ref bool state,
        ref bool down,
        ref bool up)
    {
        float targetConfidence = 0f;

        if (hasHand && landmarks != null && landmarks.Length >= 21)
        {
            targetConfidence = ComputeFistConfidence(landmarks);
        }

        confidence = Mathf.Lerp(confidence, targetConfidence, confidenceSmoothing * Time.deltaTime);
        //if (hasHand)
        //{
        //    Debug.Log((isLeft ? "Left" : "Right") + " fist confidence: " + confidence);
        //}

        bool previousState = state;

        // Hysteresis so it doesn't chatter around the threshold
        if (!state && confidence >= fistOnThreshold)
        {
            state = true;
        }
        else if (state && confidence <= fistOffThreshold)
        {
            state = false;
        }

        if (!previousState && state)
        {
            down = true;
            if (logStateChanges)
                Debug.Log((isLeft ? "Left" : "Right") + " fist DOWN");
        }
        else if (previousState && !state)
        {
            up = true;
            if (logStateChanges)
                Debug.Log((isLeft ? "Left" : "Right") + " fist UP");
        }
    }

    /// <summary>
    /// Returns a 0..1 confidence that this hand pose is a fist.
    /// 1 = strong fist, 0 = open hand.
    /// </summary>
    private float ComputeFistConfidence(Vector3[] lm)
    {
        Vector3 wrist = lm[0];

        // MCP joints
        Vector3 indexMcp = lm[5];
        Vector3 middleMcp = lm[9];
        Vector3 ringMcp = lm[13];
        Vector3 pinkyMcp = lm[17];
        Vector3 thumbMcp = lm[2];

        // Tips
        Vector3 thumbTip = lm[4];
        Vector3 indexTip = lm[8];
        Vector3 middleTip = lm[12];
        Vector3 ringTip = lm[16];
        Vector3 pinkyTip = lm[20];

        Vector3 palmCenter = (wrist + indexMcp + middleMcp + ringMcp + pinkyMcp) / 5f;

        float indexCurl = ComputeFingerCurl(indexTip, indexMcp, wrist, fingerCurlThreshold);
        float middleCurl = ComputeFingerCurl(middleTip, middleMcp, wrist, fingerCurlThreshold);
        float ringCurl = ComputeFingerCurl(ringTip, ringMcp, wrist, fingerCurlThreshold);
        float pinkyCurl = ComputeFingerCurl(pinkyTip, pinkyMcp, wrist, fingerCurlThreshold);
        float thumbCurl = ComputeThumbCurl(thumbTip, thumbMcp, palmCenter, thumbCurlThreshold);

        float weighted =
            indexCurl * 1.0f +
            middleCurl * 1.0f +
            ringCurl * 1.0f +
            pinkyCurl * 1.0f +
            thumbCurl * 0.8f;

        return weighted / 4.8f;
    }

    /// <summary>
    /// Finger is more curled when tip gets closer to wrist than MCP is.
    /// Output: 0..1, where 1 = curled.
    /// </summary>
    private float ComputeFingerCurl(Vector3 tip, Vector3 mcp, Vector3 wrist, float threshold)
    {
        float mcpToWrist = Vector3.Distance(mcp, wrist);
        float tipToWrist = Vector3.Distance(tip, wrist);

        if (mcpToWrist <= 0.0001f)
            return 0f;

        float ratio = tipToWrist / mcpToWrist;
        return Mathf.Clamp01((threshold - ratio) / threshold);
    }

    /// <summary>
    /// Thumb is treated as curled when thumb tip is close to palm center.
    /// Output: 0..1, where 1 = curled.
    /// </summary>
    private float ComputeThumbCurl(Vector3 thumbTip, Vector3 thumbMcp, Vector3 palmCenter, float threshold)
    {
        float mcpToPalm = Vector3.Distance(thumbMcp, palmCenter);
        float tipToPalm = Vector3.Distance(thumbTip, palmCenter);

        if (mcpToPalm <= 0.0001f)
            return 0f;

        float ratio = tipToPalm / mcpToPalm;
        return Mathf.Clamp01((threshold - ratio) / threshold);
    }
}