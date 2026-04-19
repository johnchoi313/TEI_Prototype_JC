using System;
using System.Collections;
using System.Threading;
using Mediapipe.Tasks.Vision.HandLandmarker;
using UnityEngine;
using UnityEngine.Rendering;


namespace Mediapipe.Unity.Sample.HandLandmarkDetection
{
    public class HandLandmarkerHubRunner : VisionTaskApiRunner<HandLandmarker>
    {
        private HandLandmarkerResult _latestFromWorker;
        private int _hasNewResult;

        private Experimental.TextureFramePool _textureFramePool;

        [Header("Config Overrides")]
        [Range(1, 2)] public int NumHands = 2;
        [Range(0f, 1f)] public float MinHandDetectionConfidence = 0.6f;
        [Range(0f, 1f)] public float MinHandPresenceConfidence = 0.6f;
        [Range(0f, 1f)] public float MinTrackingConfidence = 0.6f;

        [Header("Preview")]
        public bool initializePreviewScreen = true; // keep this ON while debugging

        public readonly HandLandmarkDetectionConfig config = new HandLandmarkDetectionConfig();

        public HandLandmarkerResult LatestResult { get; private set; }
        public event Action<HandLandmarkerResult> OnResult;

        void Update()
        {
            if (Interlocked.Exchange(ref _hasNewResult, 0) == 1)
            {
                LatestResult = _latestFromWorker;
                OnResult?.Invoke(LatestResult); // MAIN THREAD safe now
            }
        }


        public override void Stop()
        {
            base.Stop();
            _textureFramePool?.Dispose();
            _textureFramePool = null;
        }

        protected override IEnumerator Run()
        {
            // Apply overrides
            config.NumHands = NumHands;
            config.MinHandDetectionConfidence = MinHandDetectionConfidence;
            config.MinHandPresenceConfidence = MinHandPresenceConfidence;
            config.MinTrackingConfidence = MinTrackingConfidence;

            // Hub wants LIVE_STREAM
            config.RunningMode = Tasks.Vision.Core.RunningMode.LIVE_STREAM;

            yield return AssetLoader.PrepareAssetAsync(config.ModelPath);

            var options = config.GetHandLandmarkerOptions(OnHandLandmarkDetectionOutput);
            taskApi = HandLandmarker.CreateFromOptions(options, GpuManager.GpuResources);

            var imageSource = ImageSourceProvider.ImageSource;
            yield return imageSource.Play();

            if (!imageSource.isPrepared)
            {
                Debug.LogError("[HandHub] Failed to start ImageSource, exiting...");
                yield break;
            }

            // THIS is what makes the preview show up on AnnotatableScreen
            if (initializePreviewScreen)
            {
                screen.Initialize(imageSource);
            }

            _textureFramePool = new Experimental.TextureFramePool(
              imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10
            );

            var transformationOptions = imageSource.GetTransformationOptions();
            var flipHorizontally = transformationOptions.flipHorizontally;
            var flipVertically = transformationOptions.flipVertically;
            var imageProcessingOptions = new Tasks.Vision.Core.ImageProcessingOptions(
              rotationDegrees: (int)transformationOptions.rotationAngle
            );

            AsyncGPUReadbackRequest req = default;
            var waitUntilReqDone = new WaitUntil(() => req.done);
            var waitForEndOfFrame = new WaitForEndOfFrame();

            var canUseGpuImage = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 && GpuManager.GpuResources != null;
            using var glContext = canUseGpuImage ? GpuManager.GetGlContext() : null;

            while (true)
            {
                if (isPaused) yield return new WaitWhile(() => isPaused);

                if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
                {
                    yield return new WaitForEndOfFrame();
                    continue;
                }

                Image image;
                switch (config.ImageReadMode)
                {
                    case ImageReadMode.GPU:
                        if (!canUseGpuImage) throw new Exception("ImageReadMode.GPU is not supported");
                        textureFrame.ReadTextureOnGPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
                        image = textureFrame.BuildGPUImage(glContext);
                        yield return waitForEndOfFrame;
                        break;

                    case ImageReadMode.CPU:
                        yield return waitForEndOfFrame;
                        textureFrame.ReadTextureOnCPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
                        image = textureFrame.BuildCPUImage();
                        textureFrame.Release();
                        break;

                    case ImageReadMode.CPUAsync:
                    default:
                        req = textureFrame.ReadTextureAsync(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
                        yield return waitUntilReqDone;

                        if (req.hasError)
                        {
                            Debug.LogWarning("[HandHub] Failed to read texture from the image source");
                            continue;
                        }

                        image = textureFrame.BuildCPUImage();
                        textureFrame.Release();
                        break;
                }

                // LIVE_STREAM results fire through OnHandLandmarkDetectionOutput
                taskApi.DetectAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
            }
        }

        private void OnHandLandmarkDetectionOutput(HandLandmarkerResult result, Image image, long timestamp)
        {
            // worker thread: store only
            _latestFromWorker = result;
            Interlocked.Exchange(ref _hasNewResult, 1);
        }

    }
}
