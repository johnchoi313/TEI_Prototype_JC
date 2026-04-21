# TEI Prototype

A Unity maze prototype featuring mic-driven fish movement, Kinect body tracking, and collaborative ability mechanics.

📖 **[View Documentation Site →](https://johnchoi313.github.io/TEI_Prototype_JC/)**

---

## Documentation

| Doc | Description |
|---|---|
| [Controls & Hotkeys](https://johnchoi313.github.io/TEI_Prototype_JC/Docs/controls.html) | Keyboard player controls, developer hotkeys, and Kinect gesture reference |
| [MicVolumeToFishSpeed](https://johnchoi313.github.io/TEI_Prototype_JC/Docs/mic-volume-to-fish-speed.html) | Parameter reference for the microphone-to-speed pipeline — calibration, smoothing, tuning |

---

## Project Structure

```
Assets/
├── Scripts/
│   ├── Player/
│   │   ├── PlayerFishController.cs       — Physics fish that follows a light
│   │   ├── PlayerLightController.cs      — Keyboard / Kinect light movement
│   │   ├── FishAbility.cs                — Break Wall / Collect Station ability
│   │   ├── KinectPlayerController.cs     — Hand + pelvis gesture → input axis
│   │   ├── KinectAbilitySwap.cs          — Proximity chest-touch ability swap
│   │   ├── MicVolumeToFishSpeed.cs       — Mic RMS → fish speed pipeline
│   │   └── SharedFOVBudget.cs            — Shared field-of-view zoom budget
│   ├── Level/
│   │   └── MazeGenerator.cs              — Randomized Prim's maze generator
│   └── Utilities/
│       ├── Hotkeys.cs                    — Global developer hotkey manager
│       ├── ScoreTracker.cs               — Walls broken / stations / swaps counter
│       ├── Logger.cs                     — CSV session logger
│       └── ScreenFlash.cs                — Full-screen flash effect singleton
└── ...
Docs/
├── controls.html
└── mic-volume-to-fish-speed.html
index.html                                — GitHub Pages entry point
```

---

## Quick Start

1. Open the project in **Unity 2022.3+**
2. Open the main scene and press **Play**
3. Use **WASD** (Player 1) or **Arrow Keys** (Player 2) to move lights
4. Press **E** / **Enter** to use your fish's ability
5. Press **Tab** to swap abilities between players
6. Hold **Shift** and press hotkeys to toggle debug UI panels

See the [Controls & Hotkeys doc](https://johnchoi313.github.io/TEI_Prototype_JC/Docs/controls.html) for the full reference.
