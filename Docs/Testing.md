# Regression tests

## Running the suite

In the editor, use these menu commands outside Play Mode:

- `Mytools > Tests > Run Content Tests`: offline Edit Mode texture, cache, and creature-art lookup checks.
- `Mytools > Tests > Run Input Tests`: Play Mode checks using synthetic input devices and a clone of the project's action map, plus block visibility, mesh allocation and mesh-arrival checks.
- `Mytools > Tests > Run Live DF Smoke Test`: three Map Mode Play sessions against DFHack on `127.0.0.1:5000`. Save open scenes first and keep the same fortress loaded throughout the run.

The menu runner writes NUnit XML and a short result summary under `TestResults/`. Unity's Test Runner window can also run the assemblies directly and export results. The live test additionally writes stage logs and per-session sprite hashes. Results are local, ignored files.

For a separate checkout, or with its editor closed, use PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/RunUnityValidation.ps1 -Mode EditMode
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/RunUnityValidation.ps1 -Mode PlayMode
```

The helper explicitly selects `Armok.Content.Tests` for Edit Mode and `Armok.Input.Tests` for Play Mode, with test filter `Armok.Tests`. This keeps live tests out of offline runs even when matching parent suites. Add `-Mode EditMode -TestAssembly Armok.Tests.Editor -TestFilter Armok.LiveTests` to run the live smoke test. `-ProjectPath`, `-UnityPath`, and `-ReportDirectory` override their defaults. The helper records the actual Unity process exit code in `process-result.json` and writes test results to `results.xml`.

The equivalent Unity test arguments are `-batchmode -runTests -testPlatform EditMode -assemblyNames Armok.Content.Tests -testFilter Armok.Tests -testResults <xml-path> -projectPath <checkout> -logFile <log-path>`. Use `PlayMode` and assembly `Armok.Input.Tests` for input tests. Do not add `-quit` to a test run; let the Test Framework finish and exit. Content preparation uses the separate `-Mode PrepareContent` path.

## Release player shutdown

For rendering changes, run the normal Windows release player, wait for map loading, exercise navigation and inspection, then close its window. Retain the launched `System.Diagnostics.Process`, call `WaitForExit`, and check that `ExitCode` is **0**. Repeat loaded-map starts and exits. A disappearing window is not sufficient: the September 2026 post-processing failure occurred after Unity's last cleanup log. Editor tests, development players and batch mode did not reproduce it.

The in-game Quit button is on connection/failure dialogs, not the loaded-map settings panel. Verify that path separately using a temporary unavailable localhost endpoint, then restore the client's configuration. It does not require stopping the user's DF game and does not replace the loaded-map exit check.

Both Map Mode Post Process Layers must keep `finalBlitToCameraTarget` off. The regression case is a normal threaded-rendering release with that option on; it crashed even in a camera-and-cube scene without DF. Check that the layers remain enabled and that the scene renders correctly with the option off. See `Docs/Development.md` and `CurrentTasks.md` for the compatibility setting and verification record.

## What is covered

| Suite | Behavior checked | Requirements |
| --- | --- | --- |
| `TextureScaleTests` | Known point/bilinear samples, same-size legacy sampling, channel interpolation, large parallel work, concurrent calls, degenerate dimensions, invalid input, source immutability | No DF; CPU pixel tests |
| `SpriteTextureCacheTests` | Exact color/normal mip round trips, source/layout invalidation, damaged/truncated files, replacement, unavailable storage, temporary-file cleanup | Graphics device supporting texture arrays; no DF |
| `CreatureArtTests` | Exact bundled artwork, numbered generic creatures, legacy filename aliases, readable source pixels, and missing optional illustrations | Project Resources assets; reflection into legacy `ImageManager`; no DF |
| `BuildingColorInstancingTests` | Shared-material property-block colors match separate-material rendering for base color, all three pattern channels, material defaults and per-renderer pattern textures | Graphics device supporting instancing and texture arrays; isolated preview scene; no DF |
| `EditorContentPreparationTests` | Opening item, decoration, and image managers does not eagerly prepare content or replace global image arrays | Project Resources prefabs; no DF |
| `ImagePreparationTests` | Repeat preparation, disable, and destruction release owned arrays, other owners' global textures remain intact, imported artwork stays unchanged, and CPU resizing preserves stored color/normal pixels | Graphics device; temporary definitions restored after each test; no DF |
| `MaterialCollectionTests` | Creating a replacement collection preserves the loaded resource and its file | Run Prepare Content first; no DF |
| `MapCoordinatesTests` | Tile-anchor round trips, negative coordinates, horizontal midpoint rounding, vertical floor boundaries, directions, origins and block corners | No DF; restores the shared vertical offset |
| `MapExportTests` | Material CSV/JSON data, building rows, screenshot naming, PNG pixels and render-target/texture cleanup on successful and failed writes | Temporary test-owned output; minimap capture cases require graphics |
| `ModernInputTests` | Project movement/elevation bindings, button edges, pointer/look/zoom sensitivity, and removal of held devices | Play Mode; project Input Actions asset; no DF |
| `LiveDFSmokeTests` | Three completed loads with reload disabled, stable callback counts and sprite hashes, supported visible shaders, connection teardown, and captured worker completion | Running DF/DFHack with a suitable fortress; graphics device |
| `BlockVisibilityTests` | Initial and repeated visibility, every visibility transition, collision layers, and newly arriving meshes receiving unchanged shadow settings | Play Mode in `Armok.Input.Tests`; reflection into legacy block presentation; no DF |
| `BlockMeshAllocationTests` | Empty results allocate no presentation, populated/empty/refilled meshes preserve geometry and shadow behavior, terrain normals survive refill, clearing hides old geometry, and the block prefab retains its collider without unused renderers | Play Mode in `Armok.Input.Tests`; reflection into legacy block presentation; no DF |

Cache tests use unique test-owned directories and destroy all textures they create. Unsupported graphics environments report those tests as skipped. A missing DF endpoint or unsaved scene skips the live test. A skipped test is not a successful integration check; inspect the pass/fail/skip counts.

The input tests restrict their cloned action map to synthetic devices and restore the previously current keyboard/mouse. They temporarily use a cloned input-settings object that processes events without editor focus, allowing the same checks in batch mode, then restore the original settings and background-update behavior. They do not change the project's action asset. The live test restores open scenes, reload settings, and the temporary background-update setting after completion or failure.

## Current assembly boundaries

| Assembly | Purpose | Production dependencies |
| --- | --- | --- |
| `Armok.Textures` | `TextureScale` and `SpriteTextureCache` | Unity engine types; no DF types |
| `Armok.Input` | Shared `ArmokInput` entry point | `Unity.InputSystem` |

Both are auto-referenced so the existing default and first-pass assemblies can continue to use them. Existing GUIDs were preserved when moving the three helpers. The graphics cache takes ordered `Vector2Int` coordinates; `TilePage` translates its DF coordinates before fingerprinting. The serialized fingerprint values and cache version remain unchanged.

The cache is internal with explicit friend access for the existing `Assembly-CSharp` content caller and its tests. Other application code remains in the existing assemblies until later refactor phases. Test assemblies are marked with `UNITY_INCLUDE_TESTS` and are not automatically referenced by production code.

The live scene test currently uses a small reflection bridge to inspect the legacy scene's callbacks, worker handles, and sprite arrays. This is characterization coverage while those components remain in `Assembly-CSharp`; migrate the bridge to the session API during Phase 2. The three-session check verifies the workers it captures, not all native or Unity-owned threads in the editor.

## Interpreting results

Use the same DF fixture for comparisons of live sprite hashes. The live test compares all mip pixels across its own three runs; the historical restoration hash report is additional local evidence, not a required test fixture. Shader support/error checks are supplemented by a visual smoke check when changing rendering code.

The color tests check rendered RGB output, including non-primary colors, with a two-byte channel tolerance. They do not assert that Unity batches a particular editor draw. Measure batching and performance separately in a player with ordinary/instanced draw counters and unchanged geometry. Per-renderer pattern textures and additional Forward lighting passes can still prevent batching.

Loading timings are observations and include yielded frames. Imports, compilation, first connection, and the cost of the test's hash/shader checks are separate. The suite has no universal timing threshold and does not establish Windows player or other-platform correctness by itself.
## Session lifecycle checks

`Mytools/Tests/Run Lifecycle Tests` runs `Armok.Lifecycle.Tests` offline. It exercises stage ordering, callback removal, nested coroutine failure/cancellation, cooperative polling shutdown, the shared worker deadline, and the real DFHack transport against a controlled loopback peer. No running game is required. Reports are written to `TestResults/lifecycle.xml` and `.txt`.

For a closed project or an isolated checkout:

```powershell
Tools/RunUnityValidation.ps1 -Mode EditMode -TestAssembly Armok.Lifecycle.Tests -TestFilter Armok.Tests
```

The live fixture also covers interrupted content loading followed by scene retry, a real socket interruption during polling, and repeated sessions using both polling modes and zero/two/four meshing threads. It checks that loading/cancellation publish no catalog, Ready exposes the loader's completed catalog, workers retain that catalog, and stopping unpublishes it before a new session creates another. Temporary meshing settings are restored before scene destruction so they are not saved to the user's configuration.

The repeat-session check also requires prepared image arrays at Ready and verifies their destruction after stopping. For editor-preparation changes, additionally check `Mytools > Prepare Content Previews`, missing material-resource generation before Play, and a Windows release build starting without generated material resources. Back up existing generated assets and their metadata before this manual recovery check.

`Tools/RunUnityValidation.ps1 -Mode FullReloadLive` checks the same startup with both normal domain and scene reloads enabled in a closed batch checkout. It requires live DF, restores editor options, and writes `full-reload-validation.json` plus the process exit result. It checks readiness and worker shutdown before unloading the domain. Unity 6000.6's observed batch search-index `ArgumentOutOfRangeException` is recorded separately as `editorDiagnostic`; this exact editor-only stack is not treated as an application failure. All application errors still fail the check.
