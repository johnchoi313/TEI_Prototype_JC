using System.Collections;
using Mediapipe;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Unity.Sample;
using Mediapipe.Unity.Sample.HandLandmarkDetection;
using UnityEngine;
using UnityEngine.Rendering;

public class TEIHandTrackingRunner : VisionTaskApiRunner<HandLandmarker>
{
    [Header("Tracking")]
    [SerializeField] private bool trackLeftHand = true;
    [SerializeField] private bool trackRightHand = true;
    [SerializeField, Range(1, 2)] private int maxHands = 2;

    [Tooltip("Minimum improvement in total spatial distance (image UV units) required before " +
             "accepting a label-swap from the continuity check. Prevents oscillation when both " +
             "assignments are equidistant. Raise if false-swaps occur; lower if real flips slip through.")]
    [SerializeField] private float spatialContinuityThreshold = 0.05f;

    private Mediapipe.Unity.Experimental.TextureFramePool _textureFramePool;
    private readonly HandLandmarkDetectionConfig _config = new HandLandmarkDetectionConfig();

    // Whether each hand has been seen at least once — guards the continuity
    // check so we don't compare against the default (0,0) initial positions.
    private bool _hasSeenLeft;
    private bool _hasSeenRight;

    public bool HasLeftHand { get; private set; }
    public bool HasRightHand { get; private set; }

    // Normalized 0-1 viewport coords.
    // NOTE: intentionally NOT cleared in ClearHands() — retains last-known
    // position between frames for the spatial continuity check.
    public Vector2 LeftHandCenter { get; private set; }
    public Vector2 RightHandCenter { get; private set; }

    // Apparent hand size in image space.
    // Larger = hand appears bigger in camera = likely closer to camera.
    public float LeftHandScale { get; private set; }
    public float RightHandScale { get; private set; }

    // Copied landmark data for downstream gesture interpretation.
    // x,y are normalized image coordinates. z is normalized landmark depth.
    public Vector3[] LeftHandLandmarks { get; } = new Vector3[21];
    public Vector3[] RightHandLandmarks { get; } = new Vector3[21];

    public override void Stop()
    {
        base.Stop();
        _textureFramePool?.Dispose();
        _textureFramePool = null;

        ClearHands();
    }

    protected override IEnumerator Run()
    {
        _config.NumHands = Mathf.Clamp(maxHands, 1, 2);

        yield return AssetLoader.PrepareAssetAsync(_config.ModelPath);

        var options = _config.GetHandLandmarkerOptions(
            _config.RunningMode == Mediapipe.Tasks.Vision.Core.RunningMode.LIVE_STREAM
                ? OnLiveStreamResult
                : null
        );

        taskApi = HandLandmarker.CreateFromOptions(options, Mediapipe.Unity.GpuManager.GpuResources);

        var imageSource = ImageSourceProvider.ImageSource;
        yield return imageSource.Play();

        if (!imageSource.isPrepared)
        {
            Debug.LogError("Failed to start ImageSource.");
            yield break;
        }

        _textureFramePool = new Mediapipe.Unity.Experimental.TextureFramePool(
            imageSource.textureWidth,
            imageSource.textureHeight,
            TextureFormat.RGBA32,
            10
        );

        screen.Initialize(imageSource);

        var transformationOptions = imageSource.GetTransformationOptions();
        var flipHorizontally = transformationOptions.flipHorizontally;
        var flipVertically = transformationOptions.flipVertically;

        var imageProcessingOptions =
            new Mediapipe.Tasks.Vision.Core.ImageProcessingOptions(
                rotationDegrees: (int)transformationOptions.rotationAngle
            );

        AsyncGPUReadbackRequest req = default;
        var waitUntilReqDone = new WaitUntil(() => req.done);
        var waitForEndOfFrame = new WaitForEndOfFrame();
        var result = HandLandmarkerResult.Alloc(options.numHands);

        var canUseGpuImage =
            SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 &&
            Mediapipe.Unity.GpuManager.GpuResources != null;

        using var glContext = canUseGpuImage ? Mediapipe.Unity.GpuManager.GetGlContext() : null;

        while (true)
        {
            if (isPaused)
            {
                yield return new WaitWhile(() => isPaused);
            }

            if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
            {
                yield return waitForEndOfFrame;
                continue;
            }

            Image image;
            switch (_config.ImageReadMode)
            {
                case Mediapipe.Unity.ImageReadMode.GPU:
                    if (!canUseGpuImage)
                    {
                        throw new System.Exception("ImageReadMode.GPU is not supported");
                    }

                    textureFrame.ReadTextureOnGPU(
                        imageSource.GetCurrentTexture(),
                        flipHorizontally,
                        flipVertically
                    );
                    image = textureFrame.BuildGPUImage(glContext);
                    yield return waitForEndOfFrame;
                    break;

                case Mediapipe.Unity.ImageReadMode.CPU:
                    yield return waitForEndOfFrame;
                    textureFrame.ReadTextureOnCPU(
                        imageSource.GetCurrentTexture(),
                        flipHorizontally,
                        flipVertically
                    );
                    image = textureFrame.BuildCPUImage();
                    textureFrame.Release();
                    break;

                case Mediapipe.Unity.ImageReadMode.CPUAsync:
                default:
                    req = textureFrame.ReadTextureAsync(
                        imageSource.GetCurrentTexture(),
                        flipHorizontally,
                        flipVertically
                    );
                    yield return waitUntilReqDone;

                    if (req.hasError)
                    {
                        Debug.LogWarning("Failed to read texture from image source");
                        continue;
                    }

                    image = textureFrame.BuildCPUImage();
                    textureFrame.Release();
                    break;
            }

            switch (taskApi.runningMode)
            {
                case Mediapipe.Tasks.Vision.Core.RunningMode.IMAGE:
                    if (taskApi.TryDetect(image, imageProcessingOptions, ref result))
                    {
                        ProcessResult(result);
                    }
                    else
                    {
                        ClearHands();
                    }
                    break;

                case Mediapipe.Tasks.Vision.Core.RunningMode.VIDEO:
                    if (taskApi.TryDetectForVideo(
                        image,
                        GetCurrentTimestampMillisec(),
                        imageProcessingOptions,
                        ref result))
                    {
                        ProcessResult(result);
                    }
                    else
                    {
                        ClearHands();
                    }
                    break;

                case Mediapipe.Tasks.Vision.Core.RunningMode.LIVE_STREAM:
                    taskApi.DetectAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
                    break;
            }
        }
    }

    private void OnLiveStreamResult(HandLandmarkerResult result, Image image, long timestamp)
    {
        ProcessResult(result);
    }

    private void ProcessResult(HandLandmarkerResult result)
    {
        ClearHands();

        if (result.handLandmarks == null || result.handedness == null)
            return;

        // ── Pass 1: collect candidates per MediaPipe label ────────────────────
        // We store index into result.handLandmarks so we can copy landmarks later
        // without needing a temporary array.

        bool    gotLeft  = false, gotRight  = false;
        Vector2 leftPos  = Vector2.zero, rightPos  = Vector2.zero;
        float   leftScl  = 0f,           rightScl  = 0f;
        int     leftIdx  = -1,           rightIdx  = -1;

        for (int i = 0; i < result.handLandmarks.Count; i++)
        {
            if (i >= result.handedness.Count ||
                result.handedness[i].categories == null ||
                result.handedness[i].categories.Count == 0)
                continue;

            var landmarks  = result.handLandmarks[i];
            var handedness = result.handedness[i].categories[0].categoryName;

            float x = (landmarks.landmarks[0].x + landmarks.landmarks[5].x +
                       landmarks.landmarks[9].x  + landmarks.landmarks[13].x +
                       landmarks.landmarks[17].x) / 5f;
            float y = (landmarks.landmarks[0].y + landmarks.landmarks[5].y +
                       landmarks.landmarks[9].y  + landmarks.landmarks[13].y +
                       landmarks.landmarks[17].y) / 5f;

            Vector2 center = new Vector2(x, y);
            float   scale  = ComputePalmScale(landmarks);

            if (handedness == "Left" && trackLeftHand && !gotLeft)
            {
                gotLeft  = true;
                leftPos  = center;
                leftScl  = scale;
                leftIdx  = i;
            }
            else if (handedness == "Right" && trackRightHand && !gotRight)
            {
                gotRight = true;
                rightPos = center;
                rightScl = scale;
                rightIdx = i;
            }
        }

        // ── Pass 2: spatial continuity check ─────────────────────────────────
        // MediaPipe labels each detected blob "Left" or "Right" using visual
        // anatomy (thumb direction, finger geometry). At distance / low resolution
        // these features become ambiguous and the label can flip — even when the
        // same physical hand is still in frame. We correct this by comparing the
        // proposed assignment to last-frame known positions and swapping labels
        // back when the spatial evidence is stronger for the swap.
        //
        // Three cases are handled:
        //   A) Both hands detected with swapped labels.
        //   B) Only a "Left"-labeled hand detected, but it's closer to last Right.
        //      (Most common flip: physical right hand → mislabeled "Left")
        //   C) Only a "Right"-labeled hand detected, but it's closer to last Left.
        //      (Most common flip: physical left hand → mislabeled "Right")
        //
        // Guard on _hasSeenLeft && _hasSeenRight so we never compare against the
        // default Vector2.zero positions on the very first frames.

        if (_hasSeenLeft && _hasSeenRight)
        {
            if (gotLeft && gotRight)
            {
                // Case A: both present — check if label swap is spatially tighter.
                float sameAssign = Vector2.Distance(leftPos,  LeftHandCenter)
                                 + Vector2.Distance(rightPos, RightHandCenter);
                float swapAssign = Vector2.Distance(leftPos,  RightHandCenter)
                                 + Vector2.Distance(rightPos, LeftHandCenter);

                if (swapAssign < sameAssign - spatialContinuityThreshold)
                {
                    (leftPos,  rightPos) = (rightPos, leftPos);
                    (leftScl,  rightScl) = (rightScl, leftScl);
                    (leftIdx,  rightIdx) = (rightIdx, leftIdx);
                }
            }
            else if (gotLeft && !gotRight)
            {
                // Case B: only a "Left"-labeled detection — is it the right hand?
                float distToLeft  = Vector2.Distance(leftPos, LeftHandCenter);
                float distToRight = Vector2.Distance(leftPos, RightHandCenter);

                if (distToRight < distToLeft - spatialContinuityThreshold)
                {
                    // Spatially closer to last Right — relabel as Right.
                    gotRight = true;  rightPos = leftPos; rightScl = leftScl; rightIdx = leftIdx;
                    gotLeft  = false;
                }
            }
            else if (!gotLeft && gotRight)
            {
                // Case C: only a "Right"-labeled detection — is it the left hand?
                float distToRight = Vector2.Distance(rightPos, RightHandCenter);
                float distToLeft  = Vector2.Distance(rightPos, LeftHandCenter);

                if (distToLeft < distToRight - spatialContinuityThreshold)
                {
                    // Spatially closer to last Left — relabel as Left.
                    gotLeft  = true;  leftPos = rightPos; leftScl = rightScl; leftIdx = rightIdx;
                    gotRight = false;
                }
            }
        }

        // ── Pass 3: commit ────────────────────────────────────────────────────

        if (gotLeft)
        {
            HasLeftHand    = true;
            LeftHandCenter = leftPos;
            LeftHandScale  = leftScl;
            if (leftIdx >= 0) CopyLandmarks(result.handLandmarks[leftIdx], LeftHandLandmarks);
            _hasSeenLeft   = true;
        }

        if (gotRight)
        {
            HasRightHand    = true;
            RightHandCenter = rightPos;
            RightHandScale  = rightScl;
            if (rightIdx >= 0) CopyLandmarks(result.handLandmarks[rightIdx], RightHandLandmarks);
            _hasSeenRight   = true;
        }
    }

    /// <summary>
    /// Computes an apparent hand-size proxy from stable palm landmarks.
    /// Larger result = hand appears larger in image = likely closer to camera.
    /// </summary>
    private float ComputePalmScale(NormalizedLandmarks landmarks)
    {
        Vector2 p0 = ToV2(landmarks.landmarks[0]);   // wrist
        Vector2 p5 = ToV2(landmarks.landmarks[5]);   // index MCP
        Vector2 p9 = ToV2(landmarks.landmarks[9]);   // middle MCP
        Vector2 p13 = ToV2(landmarks.landmarks[13]); // ring MCP
        Vector2 p17 = ToV2(landmarks.landmarks[17]); // pinky MCP

        float wristToMiddle = Vector2.Distance(p0, p9);
        float palmWidthOuter = Vector2.Distance(p5, p17);
        float palmWidthInner = Vector2.Distance(p5, p13);

        return (wristToMiddle + palmWidthOuter + palmWidthInner) / 3f;
    }

    private void CopyLandmarks(NormalizedLandmarks source, Vector3[] target)
    {
        int count = Mathf.Min(source.landmarks.Count, target.Length);
        for (int i = 0; i < count; i++)
        {
            var lm = source.landmarks[i];
            target[i] = new Vector3(lm.x, lm.y, lm.z);
        }
    }

    private Vector2 ToV2(Mediapipe.Tasks.Components.Containers.NormalizedLandmark lm)
    {
        return new Vector2(lm.x, lm.y);
    }

    private void ClearHands()
    {
        HasLeftHand = false;
        HasRightHand = false;

        LeftHandScale = 0f;
        RightHandScale = 0f;

        ClearLandmarkArray(LeftHandLandmarks);
        ClearLandmarkArray(RightHandLandmarks);
    }

    private void ClearLandmarkArray(Vector3[] arr)
    {
        for (int i = 0; i < arr.Length; i++)
        {
            arr[i] = Vector3.zero;
        }
    }
}