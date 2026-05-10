# VTuberSystemBase Camera Switcher Output Adapter

Main-output-side adapter that consumes the camera control traffic emitted by `camera-switcher-tab` (UI side) and applies it to the `output-renderer-shell` scene.

## Responsibilities

- Owns a `uOSC.uOscServer` GameObject and listens on `127.0.0.1:9000` (default; configurable via `CameraSwitcherOutputAdapterConfig`) for `/ucapi/camera/{cameraId}/flat` blobs.
- Decodes UCAPI Flat Records via `UCAPI4Unity.UcApi4UnityCamera.ApplyToCamera(byte[], Camera)` onto the matching `UnityEngine.Camera`.
- Registers IPC handlers on `IOutputCommandDispatcher` for `camera/command`, `camera/{id}/metadata/{key}`, `camera/{id}/volume/*`, `camera/{id}/volume/overrides/metadata`, `camera/preview/command`, and observes `camera/preset/*`.
- Allocates `cam-{NNNN}` cameraIds, manages Camera GameObjects under `CamerasRoot`, swaps `IOutputSceneRoots.DefaultCamera.enabled` between active and fallback states, and runs Local Volume `enabled` toggles in lockstep with active-set.
- Publishes `cameras/list`, `cameras/active`, `camera/created`, `camera/error`, and placeholder `camera/{id}/preview/handle`.

## Scope

This package is the *main-output-side* counterpart of `camera-switcher-tab`. The UI tab, UCAPI serialiser, OSC sender and preset persistence belong to that package; this one only receives and applies their effects.

## Default OSC port

`127.0.0.1:9000` (provisional, see `docs/integration-plan.md` §7.2).
