// Copyright (c) 2021 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using System.Collections;
using UnityEngine;

namespace Mediapipe.Unity.Sample
{
  public class Bootstrap : MonoBehaviour
  {
    [SerializeField] private AppSettings _appSettings;

    public InferenceMode inferenceMode { get; private set; }
    public bool isFinished { get; private set; }
    // Static so it survives scene reloads even if DontDestroyOnLoad fails
    // (e.g. Bootstrap is a child GO). Glog is a process-level singleton —
    // calling Initialize twice aborts regardless of which instance does it.
    private static bool _isGlogInitialized;

    // ── Singleton ──────────────────────────────────────────────────────────────
    // Bootstrap initialises native glog and GPU resources — both can only be
    // initialised once per process. Making it DontDestroyOnLoad ensures it
    // survives scene reloads (LoadSceneMode.Single) and OnEnable never fires a
    // second time on the same instance, preventing the glog double-init abort.
    private static Bootstrap _instance;

    private void Awake()
    {
      if (_instance != null && _instance != this)
      {
        Destroy(gameObject);
        return;
      }
      _instance = this;
      DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
      var _ = StartCoroutine(Init());
    }

    private IEnumerator Init()
    {
      Debug.Log("The configuration for the sample app can be modified using AppSettings.asset.");
#if !DEBUG && !DEVELOPMENT_BUILD
      Debug.LogWarning("Logging for the MediaPipeUnityPlugin will be suppressed. To enable logging, please check the 'Development Build' option and build.");
#endif

      Logger.MinLogLevel = _appSettings.logLevel;

      Protobuf.SetLogHandler(Protobuf.DefaultLogHandler);

      if (!_isGlogInitialized)
      {
        Debug.Log("Setting global flags...");
        _appSettings.ResetGlogFlags();
        Glog.Initialize("MediaPipeUnityPlugin");
        _isGlogInitialized = true;
      }
      else
      {
        Debug.Log("Glog already initialized — skipping.");
      }

      Debug.Log("Initializing AssetLoader...");
      switch (_appSettings.assetLoaderType)
      {
        case AppSettings.AssetLoaderType.AssetBundle:
          {
            AssetLoader.Provide(new AssetBundleResourceManager("mediapipe"));
            break;
          }
        case AppSettings.AssetLoaderType.StreamingAssets:
          {
            AssetLoader.Provide(new StreamingAssetsResourceManager());
            break;
          }
        case AppSettings.AssetLoaderType.Local:
          {
#if UNITY_EDITOR
            AssetLoader.Provide(new LocalResourceManager());
            break;
#else
            Debug.LogError("LocalResourceManager is only supported on UnityEditor." +
              "To avoid this error, consider switching to the StreamingAssetsResourceManager and copying the required resources under StreamingAssets, for example.");
            yield break;
#endif
          }
        default:
          {
            Debug.LogError($"AssetLoaderType is unknown: {_appSettings.assetLoaderType}");
            yield break;
          }
      }

      DecideInferenceMode();
      if (inferenceMode == InferenceMode.GPU)
      {
        Debug.Log("Initializing GPU resources...");
        yield return GpuManager.Initialize();

        if (!GpuManager.IsInitialized)
        {
          Debug.LogWarning("If your native library is built for CPU, change 'Preferable Inference Mode' to CPU from the Inspector Window for AppSettings");
        }
      }

      Debug.Log("Requesting camera permission...");
      yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
      if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
      {
        Debug.LogError("Camera permission denied — hand tracking will not work.");
        yield break;
      }

      Debug.Log("Preparing ImageSource...");
      ImageSourceProvider.Initialize(
        _appSettings.BuildWebCamSource(), _appSettings.BuildStaticImageSource(), _appSettings.BuildVideoSource());
      ImageSourceProvider.Switch(_appSettings.defaultImageSource);

      isFinished = true;
    }

    private void DecideInferenceMode()
    {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_WIN
      if (_appSettings.preferableInferenceMode == InferenceMode.GPU) {
        Debug.LogWarning("Current platform does not support GPU inference mode, so falling back to CPU mode");
      }
      inferenceMode = InferenceMode.CPU;
#else
      inferenceMode = _appSettings.preferableInferenceMode;
#endif
    }

    private void OnApplicationQuit()
    {
      GpuManager.Shutdown();

      if (_isGlogInitialized)
      {
        Glog.Shutdown();
      }

      Protobuf.ResetLogHandler();
    }
  }
}
