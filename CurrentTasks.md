# ArmokRestored maintenance plan

Updated: 2026-09-14

Status: Phases 0-2 complete and verified. Phase 3 is underway, starting with cursor inspection and retiring the legacy command UI. Phases 4-6 remain pending.

## Objective

Make the restored project easier to understand, test, and extend while preserving its working Dwarf Fortress connection, rendering, modern controls, and faster content loading. Work in small, independently verifiable changes, with a working project at the end of each phase.

The first implementation milestone is a versioned baseline, maintained regression tests, and predictable startup/shutdown. Broader class and folder reorganization follows that milestone.

## Working baseline to preserve

- Unity version: `6000.6.0f1`, as recorded in `ProjectSettings/ProjectVersion.txt`.
- Input: Input System Package (New), using `Assets/InputSystem_Actions.inputactions` and the shared `ArmokInput` wrapper.
- Rendering: Built-in Render Pipeline, with no URP override in Graphics settings or any Quality preset. The existing terrain/material shaders depend on this configuration.
- Connection previously verified with DF 53.16, DFHack 53.16-r1.1, and RemoteFortressReader 0.21.0. Record the actual versions again when validating future work.
- Generated material arrays and definitions are rebuilt by the existing material collection tooling when missing before Play Mode.
- Content-loading measurements improved from about 167 seconds to about 10 seconds warm / 12 seconds with an empty sprite cache on the measured setup. These are observations, not universal timing targets.
- The content timer excludes Unity imports, compilation, and the initial DF connection. Coroutine yields and editor scheduling affect the measurement.
- All 4,271 creature sprites across 39 pages retained identical color/normal pixels at every mip level in the previous validation runs.
- Enter Play Mode currently disables both domain reload and scene reload. Repeated Play sessions must remain supported.

Existing local evidence: `Logs/LoadingPerformance-20260913-173013/verification.txt`, `final-checks.json`, and the related validation scripts/results. These are ignored local artifacts and must not become required files for a fresh checkout.

## Implementation rules

- Preserve existing scene and prefab references. Keep `.meta` files and GUIDs intact when moving assets; inspect serialized references after each extraction or move.
- Keep modern Input System handling and the current rendering configuration throughout this work.
- Preserve the generated sprite cache's invalidation rules, corruption recovery, and pixel output. Live DF state must continue to come from the connection.
- Keep Unity object creation, destruction, and rendering operations on the main thread. Document ownership of data passed to workers.
- Change behavior and file locations in separate steps where practical so failures are easy to trace.
- Keep generated protocol code reproducible and preserve third-party licenses and provenance.
- Add tests for failure modes and externally observable behavior. Avoid tests that merely duplicate implementation details.
- Update this file after each completed slice with affected files, validation evidence, and remaining issues.

## Phase 0 — Preserve a reproducible starting point

Priority: First. Dependencies: None.

At review time, Git history contains only the `.gitignore` commit. The restored source, assets, packages, and settings are untracked.

- [x] Audit the pending files and update ignore rules for machine-specific/generated content, including `UserSettings`. Inspect the root `lfs/` directory before deciding what belongs in version control.
- [x] Verify Git LFS configuration and that required binary assets contain their actual payloads. Avoid committing duplicate staging copies of LFS objects.
- [x] Resolve the `Assets/RemoteClientDF-Net` checkout representation: `.gitmodules` declares a submodule, but the restored files must have a reproducible relationship to a pinned revision. Preserve any local changes before choosing a proper submodule checkout or documented vendored copy.
- [x] Save the working restoration as a local baseline commit, including required `.meta` files, packages/lock file, project settings, and this plan. Record the revision below. Publishing a remote is separate from this task.
- [x] Document the fresh-checkout steps, generated material prerequisites, and the distinction between required assets and disposable caches.
- [x] Establish a startup/Play-stop/build smoke-check procedure and record the actual scene, DF fixture, quality level, and cache state used.

Acceptance:

- The current working project is recoverable from a known revision.
- An isolated checkout can restore its dependencies and compile without relying on files in `Logs/` or the sibling reference clone.
- Missing generated material resources can be rebuilt using tracked tooling.

## Phase 1 — Establish maintained regression tests

Priority: First implementation milestone. Dependencies: Phase 0.

- [x] Review the existing validation helpers under `Logs/LoadingPerformance-20260913-173013/` and the prior input/rendering validation artifacts. Extract reusable checks and small fixtures; leave machine-specific automation and large reports outside the test suite.
- [x] Map the minimum compilation dependencies needed for those tests. `TextureScale` and `ArmokInput` currently participate in Unity's first-pass compilation through special folders.
- [x] Establish the smallest necessary production/test assembly boundary for the targeted helpers. Custom test assemblies cannot directly reference types left in Unity's predefined assemblies; avoid adding an assembly definition over all of `Assets/Scripts` as a shortcut.
- [x] Add Edit Mode tests for texture resizing: fixed expected samples/output, up/down scaling, same-size behavior, one-pixel dimensions, invalid dimensions, and concurrent independent calls where supported.
- [x] Add graphics-capable tests for cache round trips at every mip level, source/layout invalidation, corrupt/truncated entries, unavailable storage, and replacement of invalid entries. Use a unique test-owned directory and clean up created textures/files.
- [x] Maintain modern input checks for action availability, representative keyboard/mouse bindings, button edges, and device removal using simulated input devices.
- [x] Add an automated editor integration check that starts/stops Play at least three times with the current reload settings and detects duplicate initialization, stale callbacks, unexpected exceptions, and surviving workers. Keep the test's orchestration alive across Play transitions and restore editor settings afterward.
- [x] Separate tests that need no DF process from live DF smoke checks. Controlled connection substitutes are part of Phase 2 below.
- [x] Document how to run the tests from the editor and command line, including graphics/device requirements. Tests requiring a graphics device should report an explicit limitation on unsuitable runners.

Acceptance:

- Tests and their fixtures run from a fresh checkout without the old `Logs/` artifacts.
- Representative pixel/cache/input failures are caught by the maintained suite.
- Offline checks do not require the user's personal fortress save or a running DF process.
- The repeated-Play check has a recorded result before lifecycle changes begin.

## Phase 2 — Make session startup and shutdown explicit

Priority: First implementation milestone. Dependencies: Phase 1; add lifecycle tests alongside the implementation.

Primary files: `DFConnection.cs`, `ContentLoader.cs`, `GameMap.cs`, and `Meshing/BlockMesher.cs` under `Assets/Scripts/MapGen/`.

- [x] Document the actual initialization dependencies, especially which content managers require fetched DF definitions and when rendering may begin.
- [x] Introduce a small application/session coordinator with explicit stages such as `Disconnected`, `Connecting`, `LoadingContent`, `Ready`, `Stopping`, and `Failed`. Keep DF suspension and application loading states distinct.
- [x] Give each stage a clear completion/failure result. Drive loading status through a progress interface/event so the loader does not need to manipulate help panels or unrelated scene UI.
- [x] Move session-scoped callbacks and state under the coordinator's ownership. Make registration/removal and repeated initialization explicit; clear state consistently with reload enabled or disabled.
- [x] Begin separating the RPC/polling client from the `DFConnection` scene component. Expose the small connection operations needed by startup and tests without creating a broad abstraction framework. Add a controlled connection substitute for offline lifecycle/failure tests.
- [x] Replace `Thread.Abort()` in network and meshing shutdown with cooperative cancellation. Wake sleeping workers and provide bounded socket waits/interruption for blocked network calls; cancellation alone cannot unblock a synchronous read.
- [x] Pair DF suspend/resume operations with exception-safe cleanup. Define behavior when a disconnect prevents a resume acknowledgement and test it using a controlled peer.
- [x] Ensure workers finish or acknowledge cancellation before their data and Unity-facing owners are disposed. An old session must never clear or dispose a newer session's connection/state.
- [x] Keep stop operations bounded and observable, including multiple meshing workers. Avoid accumulating a separate long main-thread wait for every worker.
- [x] Define failure handling for absent DF, incompatible/failed RPC setup, interrupted content loading, disconnect during polling, and stop during a load. Restore the UI to a useful state and allow a deliberate retry without retaining callbacks from the failed attempt.

Acceptance:

- Repeated Play sessions initialize each stage once and stop without lingering workers or callback growth.
- Controlled failures during connection, loading, polling, and shutdown produce a clear result and release owned resources.
- Both existing polling modes, if retained, and the configured meshing modes have defined stop behavior.
- No network or meshing shutdown path relies on `Thread.Abort()`.
- A live DF smoke run still loads and renders correctly, with preserved input behavior and no material startup errors.

Completed 2026-09-13:

- Added the independent `Armok.Lifecycle` assembly and scene composition in `Assets/Armok/Runtime/Integration/SessionRuntime.cs`. Startup dependencies, cancellation, socket ownership, shutdown deadlines, and retry behavior are documented in `Docs/SessionLifecycle.md`.
- `DFConnection` now reports startup/polling failures through the coordinator, separates suspended RPC batches from result publication, and starts polling only after content and presentation initialization. The network worker owns its transport; all shutdown paths use cooperative cancellation.
- `ContentLoader` owns nested enumerators and reports failures/cancellation. Scene managers own disposable registrations. Explicit participant registration fixes the three `ExecuteInEditMode` managers that skipped runtime loading with reloads disabled: both reload modes now initialize five content callbacks.
- Meshing supports zero, one, and multiple background workers, queue wakeups, cancellation within jobs, and one shared stop deadline. The live regression exercises zero/two/four meshing threads and both polling modes, requires active generated map geometry, checks shaders, and verifies worker completion.
- All **50 maintained tests passed**: 23 offline lifecycle/transport/mesher checks, 21 texture/cache checks, 4 modern input checks, and 2 live integration tests. The live tests cover three Play sessions, interrupted loading and deliberate retry, then a real socket interruption during polling. All 39 sprite pages still match the original color/normal mip hashes.
- The isolated checkout passed the 23 lifecycle tests with Unity exit code 0. The separate `FullReloadLive` batch check passed with normal domain and scene reloads enabled, five callbacks, and all workers stopped before domain unload. Its report records an unrelated Unity batch search-index exception separately; application errors remain fatal to validation.
- New-only input and Built-in rendering settings are unchanged. Logs/reports are under `Logs/Phase2-20260913/` and `TestResults/` and are not checkout dependencies. Third-party transport modifications are recorded in `Docs/ThirdParty.md`.
- This phase does not claim a player-build verification or finish the editor preview/content-catalog split; those remain in later phases.

## Phase 3 — Extract focused responsibilities

Priority: After the first milestone. Dependencies: Phase 2.

- [x] Extract screenshots and data export from `GameMap`. Preserve file formats, naming behavior, and existing UI/shortcut entry points; release temporary screenshot textures after use. Its mesh-export method was an unused commented prototype; separate Collada/OBJ tools remain unchanged.
- [x] Extract cursor inspection and its UI formatting from map chunk management.
- [ ] Move map shortcuts/UI commands into a focused input/controller component using the existing action asset. Keep a clear handoff for commands that affect map rendering or DF.
- [x] Separate coordinate conversion and shared world constants from `GameMap` where they can become independent helpers. Add representative round-trip/boundary tests.
- [ ] Reduce `GameMap` to map presentation, visibility, and coordination with its renderer/mesher, extracting those further only where ownership becomes clearer.
- [ ] Split `ContentLoader` into the loading process and the resulting content catalog used for material/sprite lookups. Publish an explicit ready result; document any data that must remain mutable after loading.
- [ ] Retain forwarding methods or serialized compatibility fields during migration where existing prefabs/scenes depend on them. Remove them only after checking every caller and serialized reference.

Acceptance:

- Input/UI or export changes can be understood without following the map meshing loop.
- Content consumers have a defined ready contract instead of inferring readiness from whichever singleton happens to exist.
- Representative exports, camera navigation, and rendering retain their existing behavior. Cursor inspection remains available through a replacement HUD; the original command panels are retired at the user's request.
- There are no missing scripts, broken UnityEvent bindings, or lost prefab overrides caused by extraction.

Cursor/HUD slice (2026-09-13):

- User direction: retire the original command UI and retain cursor inspection. This replaces the earlier requirement to preserve those command panels. Camera navigation, settings, connection dialogs, and loading progress remain available.
- Moved inspection out of `GameMap` into `Assets/Armok/Runtime/UI/CursorInspection.cs` and `TileInspection.cs`. Hover shows a compact description; clicking pins/releases a tile. The passive HUD does not block pointer input, and inspection no longer requires the debug-info option.
- Disabled the dwarf/adventure command panels and their command components, clock, old cursor text, log panel/toggle, and Discord link in Map Mode. Disabled `UiSwitcher` so it cannot reactivate the retired panels. Serialized references are retained for later cleanup.
- `MapSelection` retains camera panning. `MapInputUI` prevents a previously selected button from blocking camera navigation; active text editing still receives keyboard input. Unassigned minimap references are guarded.
- Expanded the existing three-session live regression to exercise inspection, pin/release, UI blocking, button focus, and inactive legacy panels with debug info disabled. It waits for selectable terrain after the first generated mesh appears. Controls and ownership are documented in `Docs/MapInspection.md`.
- The disconnect regression exposed duplicate error logging when interruption occurs between RPCs. A closed connection still throws, and its session owner handles reporting or deliberate shutdown. The vendor change is recorded in `Docs/ThirdParty.md`.
- Validation: both live tests passed, including inspection across three Play sessions, unchanged sprite mip hashes, interrupted loading/retry, and polling disconnect/shutdown. All 23 lifecycle tests and four modern-input tests passed. The game-view capture is `TestResults/inspection.png`; XML results and stages are under `TestResults/`. The 21 texture/cache tests were not rerun for this UI slice.
- Other Phase 3 extractions remain pending. The user's successful game-mode check does not yet establish a recorded standalone-player build validation.

## Phase 4 — Separate editor preparation and runtime loading

Priority: After lifecycle ownership is established. Dependencies: Phase 2; coordinate with Phase 3's content split.

Primary files: `Items/ItemManager.cs`, `Items/DecorationManager.cs`, `Images/ImageManager.cs`, and `Assets/Scripts/Editor/MaterialCollector.cs`.

- [ ] Inventory which previews actually require edit-time loading and what they depend on.
- [ ] Replace unconditional synchronous loader execution in editor `Awake` with explicit preview/build commands or narrowly scoped, invalidated preview preparation.
- [ ] Keep shared parsing/generation logic usable by both editor and runtime callers, with separate owners for their temporary resources.
- [ ] Provide a clear way to rebuild material arrays/definitions after source changes while preserving the current missing-resource generation behavior.
- [ ] Verify startup and builds when generated resources are absent. Preserve existing references when regenerating assets that are already present.
- [x] Make release packaging check `BuildReport.summary.result` and stop on a failed player build before copying extras or creating an archive.

Acceptance:

- Opening scenes or recompiling does not unexpectedly perform a full content load merely to initialize a manager.
- Required previews and generated resources remain available through documented actions.
- A clean build prepares required resources and a failed build is reported as failed without packaging an incomplete release.

## Phase 5 — Organize files and enforce dependency boundaries

Priority: After tests and ownership changes are stable. Dependencies: Phases 3–4.

Proposed destination for owned code:

```text
Assets/Armok/
  Runtime/
    App/          # Session lifecycle and startup coordination
    Connection/   # DF integration and polling adapters
    Content/      # Definitions, loading, generated graphics, caching
    World/        # Map data, coordinates, independent world rules
    Rendering/    # Meshing and scene presentation
    Input/        # Input System integration and command handling
    UI/           # Panels, progress, cursor presentation
  Editor/         # Content preparation, diagnostics, build tools
  Tests/
    EditMode/
    PlayMode/
```

This describes responsibilities, not one mandatory assembly per folder. Keep tests for editor Play transitions in an appropriate editor test assembly even when they exercise runtime behavior.

- [ ] Inventory third-party, locally modified third-party, generated protocol, and owned source files. Preserve licenses and record local vendor modifications before reorganizing.
- [ ] Move owned code a feature at a time, with `.meta` files and serialized reference checks. Consolidate the overlapping `Ui`, `UIScripts`, and scattered UI helpers as their dependencies allow.
- [ ] Clearly identify third-party and generated code. Use dedicated folders where safe; preserve the chosen submodule path and protocol-generation paths unless tooling is migrated in the same verified step.
- [ ] Preserve `Resources` lookup keys and external `StreamingAssets` content paths. Any relocation must audit string references, XML content, build scripts, and exported content contracts.
- [ ] Expand assembly definitions from the small boundaries introduced for tests. Prefer shared data/helpers at the bottom, integration/rendering above them, and application/UI composition at the top.
- [ ] Remove reverse dependencies as encountered: world data should not need `GameMap` just to obtain a block-size constant, and workers should not reach into UI singletons to report progress.
- [ ] Handle `Plugins` and `Standard Assets` deliberately, including their dependency on `ArmokInput`; verify editor and player compilation after each assembly change.
- [ ] Audit inactive URP/template assets, legacy samples, analytics, VR integrations, and optional packages for actual use. Remove only confirmed unnecessary content and verify scenes/builds after each removal.

Acceptance:

- A contributor can locate a feature's code and distinguish owned code from dependencies/generated output.
- Assemblies have explicit, non-circular dependencies and editor-only code is excluded from player assemblies.
- Existing scenes/prefabs/content lookups and protocol generation still work.
- Any claimed compile-time improvement has a comparable before/after measurement; runtime startup is measured separately.

## Phase 6 — Validate the reorganized project and document it

Dependencies: All implemented phases.

- [ ] Run the maintained test suite and fresh-checkout resource/dependency checks.
- [x] Build a Windows player through the supported build command and perform a live DF smoke check. Record other platforms as unverified until actually tested.
- [ ] Recheck modern controls, terrain/material rendering, cursor UI, exports, repeated Play, failed connection recovery, and shutdown.
- [ ] Measure cold-cache and warm-cache content loads using the same DF fixture and comparable editor conditions. Investigate material regressions instead of treating the prior timing as a hardware-independent test threshold.
- [ ] Compare sprite color/normal mip hashes against the applicable original fixture. Keep timing observations separate from deterministic pixel correctness checks.
- [ ] Add a short architecture guide covering startup order, ownership, thread boundaries, content generation, input/rendering requirements, and how to run tests/builds.
- [ ] Ensure this task list reflects completed work and clearly identifies any deferred scope.

## Suggested implementation slices

1. Repository baseline and reproducible setup.
2. Minimal test assembly boundaries and texture/cache/input regression tests.
3. Session lifecycle characterization, worker cancellation, and shutdown tests.
4. Startup coordinator, owned callbacks, progress/failure handling, and retry tests.
5. Individual `GameMap` extractions and content catalog separation.
6. Editor preparation and build-result handling.
7. Feature folder moves and further assembly boundaries, one group at a time.
8. Dependency cleanup, final player verification, and architecture documentation.

Do not combine all slices into one change. Preserve a verified checkpoint before proceeding to the next slice. During shutdown work, characterize the current path before replacing it; complete the full coordinator migration afterward.

## Deferred unless a measured need or product decision justifies them

- Porting custom shaders to URP or changing the rendering appearance.
- Introducing Addressables, DOTS/ECS, or replacing the entire meshing implementation.
- A comprehensive settings/UI redesign or mandatory keybinding redesign.
- Rewriting stable third-party libraries or hand-editing generated protocol classes.
- Widespread renaming and formatting unrelated to the extracted responsibilities.

## Progress record

- 2026-09-14: Resolved the Windows build's warning about 73 meshes lacking explicit collision baking. Matched the build's prefab references to 32 model importers and enabled only the required convex or triangle bake flag in each `.meta` file. All 128 meshes in those source models retain identical vertex/index data, bounds, submesh counts, and asset IDs; original importer fields and model/prefab sources are unchanged. The user's Windows build profile rebuilt successfully with **0 warnings and 0 errors**. Their existing profile and project-setting changes remain untouched. Evidence: `Logs/CollisionBake-20260914/`, including the build report and a separate verification player. This verifies the profile build; release-script failure handling and a live check of that player remain pending.
- 2026-09-14: Upgraded both `Nagidal24x24BeardsArray` assets through Unity from Texture2DArray version 1 to 3, preserving their raw pixel bytes, every loaded slice/mip, texture settings, and GUIDs. Fixed the discarded `TrimEnd` result in engraving-art lookup, added aliases for bundled bird/marmot/falcon filenames, and enabled CPU reading on the four generic creature illustrations now reached by that lookup. The live fixture resolves 155 formerly missing matches; 236 creatures still lack optional illustrations and retain DF glyphs, reported in one informational summary per load. All 39 offline content tests and both live DF integration tests passed, including three Play sessions, retry, rendering, inspection, and worker shutdown; all 39 creature sprite pages match across sessions. Checkout preflight passed for 11,853 files, and project settings remain byte-identical. Evidence: `Logs/ArtLoading-20260914/` and `TestResults/`. Windows player behavior was not tested in this slice.
- 2026-09-14: Replaced the deprecated build-group define APIs in `CrossPlatformInputInitialize` with their `NamedBuildTarget` equivalents. Retired the editor mesh export's BinaryFormatter `.avmesh` output in favor of JSON, and repaired XML serialization for readonly `CPUMesh` data using data contracts. Both formats close their output streams. Eight export/read-back checks passed, covering every mesh field, optional/null/empty data, Unicode names, and a 65,538-vertex mesh. Unity compilation no longer reports the three supplied warnings; project settings remain byte-identical. Format compatibility is documented in `Docs/Development.md`; local evidence is under `Logs/EditorWarnings-20260914/`. This does not complete the broader Phase 3 export extraction or Phase 4 build tooling work.
- 2026-09-14: Resaved `LavaMaterial`, `Cloudy Sky`, `ParticleFlames`, and `ParticleFirecloud` through Unity 6000.6's asset pipeline, upgrading Material serialization versions 3/5 to 8. All four reimported successfully with supported shaders and identical loaded property snapshots. All 74 original saved properties, shader references, and `.meta` files were preserved. Evidence: `Logs/MaterialUpgrade-20260914/`. This was a targeted material-format repair; no rendering pipeline, runtime code, or package changes were required.
- 2026-09-13: Reviewed the core architecture and documented this phased plan. No runtime, asset, or project-setting changes were made as part of the planning task.
- Baseline revision: `a78ae7f` (baseline `a4568e1` plus the fresh-checkout OBJ-source correction). Isolated import/compile and material generation passed with Unity exit code 0.
- First implementation milestone: Pending Phases 0–2.

References: [Unity assembly organization](https://docs.unity3d.com/6000.0/Documentation/Manual/assembly-definitions-intro.html), [Unity assembly references](https://docs.unity3d.com/6000.0/Documentation/Manual/assembly-definitions-referencing.html), and [Microsoft thread cancellation guidance](https://learn.microsoft.com/en-us/dotnet/standard/threading/destroying-threads).

### Phase 0 implementation evidence

- Restored 19 remaining LFS payloads (77.35 MiB) under `Content/` and `ProtoGen/`, verifying each SHA-256 and size against its pointer. Existing runtime assets already contained their payloads.
- Excluded local editor preferences, recovery staging, and temporary art files from the baseline.
- Vendored DF client matches all 56 pinned upstream files after text encoding/line-ending normalization; removed the stale submodule declaration and documented provenance in `Docs/ThirdParty.md`.
- Added `Tools/VerifyCheckout.ps1`, `Docs/Development.md`, and the offline Unity content-preparation entry point `Armok.Editor.ProjectValidation.PrepareContent`. Checkout preflight passed over 11,773 files.
- Local live smoke context: Map Mode scene, PC quality preset, Unity 6000.6.0f1, current local DF fortress fixture. Prior DF/plugin versions and warm/cold timing limitations are recorded above; new live verification is still pending.
- Fresh-checkout validation caught six exporter C# files (and their metadata) excluded by the legacy `Obj/` rule. Narrowed it to the root build-output folder and extended preflight to detect ignored C# source.

### Phase 1 implementation evidence

- Introduced only `Armok.Textures` and `Armok.Input` production assemblies; kept all three moved helper metadata files/GUIDs unchanged. Cache coordinates now use `Vector2Int` with an adapter in `TilePage`; persisted fingerprint values and cache version are unchanged.
- Added maintained content/cache, Play Mode input, and separate live DF characterization tests, plus menu/command-line runners and `Docs/Testing.md`.
- Final editor validation: content/cache suite 21 passed, input suite 4 passed, live smoke test 1 passed (three Play sessions), no skipped tests. Test cleanup handles the Test Runner's unnamed starting scene and restores editor state.
- Final live sessions measured 9.4, 9.6, and 8.0 seconds for content loading, reused all 39 cache pages, kept two unique callbacks, and stopped all captured workers. Every color/normal mip hash matched the original 4,271-sprite fixture.
- Settings and the Input Actions asset remain unchanged. All moved helper `.meta` files match the baseline exactly.
- `Tools/VerifyCheckout.ps1` passes for the expanded 11,818-file working project. Isolated-checkout command-line runs passed: 21 content/cache tests and 4 input tests, zero skips, Unity exit code 0 for both.
- Fresh baseline validation: standalone Windows editor target; 30 shape slices, 57 pattern slices, and 668 material definitions rebuilt without DF or historical log helpers. Report: `Logs/Maintenance-20260913/fresh-checkout-corrected/process-result.json`.

### Completed checkpoint

- Source/dependency baseline: `a78ae7f` (including the OBJ-source ignore-rule correction).
- Regression infrastructure: `a400d33`; batch-mode portability and input-settings cleanup: `b7f648a`, `c3f5b66`.
- Final validation: all 26 editor tests passed, including the live test covering three Play sessions. Offline command-line suites passed independently in the isolated checkout. Input, rendering, package, and reload settings in the working project are unchanged.
- Final local evidence: `Logs/Maintenance-20260913/phase1-final-results.json`; repeatable instructions and test scope: `Docs/Testing.md`.
- Next task: characterize session failure/shutdown paths, then implement cooperative worker cancellation and explicit startup ownership under Phase 2. Windows player builds and broader class/folder extraction remain pending as listed above.

- 2026-09-14: Release packaging now requires BuildReport success before copying extras or creating an archive, and excludes Unity's debugging backup. A deliberately rejected build previously continued into CopyExtras; it now throws BuildFailedException with no extras/archive. Fixed material preparation destroying the existing resource during replacement and synchronously imported replacements before Resources lookup. Repeated preparation preserved serialized data and GUIDs; 40 content tests passed. Windows release built successfully (first compile: 23 existing API/serialization warnings; incremental rebuild: 0 warnings/errors). The real executable connected to DF 53.16 / DFHack 53.16-r1.1 / RFR 0.21.0, rendered terrain/entities, supported movement/zoom/orbit, inspection pin/release and settings, and closed in 2.3 seconds without exceptions. A small magenta entity was visible and remains a separate rendering issue. Archive has 520 entries with executable/extras and no debugging backup. Evidence: Logs/ReleaseAndPhase3-20260914. Other platforms remain unverified.

- 2026-09-14: Extracted MapCoordinates and shared world constants from GameMap, migrating all source callers while preserving DFCoord and scene GUIDs. Twelve characterization cases passed before and after extraction (52 content tests total); both live tests passed with three Play sessions, unchanged sprite mip hashes, working inspection, retry and stopped workers. Horizontal midpoint ties, vertical floor behavior, half-tile floating origins and synchronized map elevation are unchanged. Evidence: Logs/ReleaseAndPhase3-20260914/coordinates-*.xml.

- 2026-09-14: Extracted MapScreenshot and MapDataExport. GameMap supplies the existing debug flags, definition collections and Documents paths; CSV/JSON formatting and screenshot naming are preserved. Minimap capture restores the previous active render target and destroys temporary textures on success or failure, and reports an unavailable minimap instead of dereferencing it. Removed only the uncalled, entirely commented GameMap mesh-export prototype; retained its serialized testMesh field. All 57 content tests passed, including five export/capture checks. Live shortcut verification follows with the catalog slice.
