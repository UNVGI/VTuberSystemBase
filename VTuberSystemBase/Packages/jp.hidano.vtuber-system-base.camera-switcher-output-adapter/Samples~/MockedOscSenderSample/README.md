# Mocked OSC Sender Sample

Provides a same-process `uOscClient` that emits real UCAPI Flat Records to
`127.0.0.1:9000`. Use this together with `CameraSwitcherOutputAdapterBootstrapper`
to verify the OSC receive + Camera-apply pipeline without the
`camera-switcher-tab` UI.

## Manual verification steps

1. Unity Package Manager > select **VTuberSystemBase Camera Switcher Output Adapter**
   > **Samples** > **Import** *Mocked OSC Sender*.
2. In your existing `OutputSceneBootstrapper` scene (provided by
   `output-renderer-shell`), add an empty GameObject and attach
   `CameraSwitcherOutputAdapterBootstrapper` (this package, Runtime asmdef).
   Assign a `CameraSwitcherOutputAdapterConfig` ScriptableObject (or leave empty
   to use the defaults: `127.0.0.1:9000`).
3. On a separate GameObject, attach `MockedOscSender` (this sample). Defaults:
   `host=127.0.0.1`, `port=9000`, `cameraId=cam-0001`, `60 Hz`.
4. Enter PlayMode. The sample starts emitting Flat Records immediately.
5. Inject a `camera/command add` event from a test harness or sibling tab so
   the adapter allocates `cam-0001` (the cameraId in the OSC address must
   match a registered camera; otherwise the messages are dropped).
6. Observe `CamerasRoot/Camera-cam-0001-{displayName}` orbiting the origin
   in the Hierarchy / Scene view.
7. Stop PlayMode. Both the sample's `uOscClient` GameObject and the adapter's
   `[CameraSwitcherOutputAdapter.OscReceiver]` GameObject must be gone.

## Expected results

- The `Camera-cam-0001-...` GameObject's transform updates at ~60 Hz, tracing a
  circle of radius 4 in the XZ plane.
- The `Camera` component reports `usePhysicalProperties = true` and
  `focalLength = 50`.
- After PlayMode stop, no `Camera-cam-*` GameObjects remain in the scene.
