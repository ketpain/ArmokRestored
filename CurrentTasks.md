# ArmokRestored maintenance plan

Updated: 2026-09-13

Status: Implementation started. Phase 0 baseline preparation is in progress; later phases remain pending.

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

- [ ] Review the existing validation helpers under `Logs/LoadingPerformance-20260913-173013/` and the prior input/rendering validation artifacts. Extract reusable checks and small fixtures; leave machine-specific automation and large reports outside the test suite.
- [ ] Map the minimum compilation dependencies needed for those tests. `TextureScale` and `ArmokInput` currently participate in Unity's first-pass compilation through special folders.
- [ ] Establish the smallest necessary production/test assembly boundary for the targeted helpers. Custom test assemblies cannot directly reference types left in Unity's predefined assemblies; avoid adding an assembly definition over all of `Assets/Scripts` as a shortcut.
- [ ] Add Edit Mode tests for texture resizing: fixed expected samples/output, up/down scaling, same-size behavior, one-pixel dimensions, invalid dimensions, and concurrent independent calls where supported.
- [ ] Add graphics-capable tests for cache round trips at every mip level, source/layout invalidation, corrupt/truncated entries, unavailable storage, and replacement of invalid entries. Use a unique test-owned directory and clean up created textures/files.
- [ ] Maintain modern input checks for action availability, representative keyboard/mouse bindings, button edges, and device removal using simulated input devices.
- [ ] Add an automated editor integration check that starts/stops Play at least three times with the current reload settings and detects duplicate initialization, stale callbacks, unexpected exceptions, and surviving workers. Keep the test's orchestration alive across Play transitions and restore editor settings afterward.
- [ ] Separate tests that need no DF process from live DF smoke checks. Add a controlled connection substitute as the connection boundary is extracted in Phase 2.
- [ ] Document how to run the tests from the editor and command line, including graphics/device requirements. Tests requiring a graphics device should report an explicit limitation on unsuitable runners.

Acceptance:

- Tests and their fixtures run from a fresh checkout without the old `Logs/` artifacts.
- Representative pixel/cache/input failures are caught by the maintained suite.
- Offline checks do not require the user's personal fortress save or a running DF process.
- The repeated-Play check has a recorded result before lifecycle changes begin.

## Phase 2 — Make session startup and shutdown explicit

Priority: First implementation milestone. Dependencies: Phase 1; add lifecycle tests alongside the implementation.

Primary files: `DFConnection.cs`, `ContentLoader.cs`, `GameMap.cs`, and `Meshing/BlockMesher.cs` under `Assets/Scripts/MapGen/`.

- [ ] Document the actual initialization dependencies, especially which content managers require fetched DF definitions and when rendering may begin.
- [ ] Introduce a small application/session coordinator with explicit stages such as `Disconnected`, `Connecting`, `LoadingContent`, `Ready`, `Stopping`, and `Failed`. Keep DF suspension and application loading states distinct.
- [ ] Give each stage a clear completion/failure result. Drive loading status through a progress interface/event so the loader does not need to manipulate help panels or unrelated scene UI.
- [ ] Move session-scoped callbacks and state under the coordinator's ownership. Make registration/removal and repeated initialization explicit; clear state consistently with reload enabled or disabled.
- [ ] Begin separating the RPC/polling client from the `DFConnection` scene component. Expose the small connection operations needed by startup and tests without creating a broad abstraction framework.
- [ ] Replace `Thread.Abort()` in network and meshing shutdown with cooperative cancellation. Wake sleeping workers and provide bounded socket waits/interruption for blocked network calls; cancellation alone cannot unblock a synchronous read.
- [ ] Pair DF suspend/resume operations with exception-safe cleanup. Define behavior when a disconnect prevents a resume acknowledgement and test it using a controlled peer.
- [ ] Ensure workers finish or acknowledge cancellation before their data and Unity-facing owners are disposed. An old session must never clear or dispose a newer session's connection/state.
- [ ] Keep stop operations bounded and observable, including multiple meshing workers. Avoid accumulating a separate long main-thread wait for every worker.
- [ ] Define failure handling for absent DF, incompatible/failed RPC setup, interrupted content loading, disconnect during polling, and stop during a load. Restore the UI to a useful state and allow a deliberate retry without retaining callbacks from the failed attempt.

Acceptance:

- Repeated Play sessions initialize each stage once and stop without lingering workers or callback growth.
- Controlled failures during connection, loading, polling, and shutdown produce a clear result and release owned resources.
- Both existing polling modes, if retained, and the configured meshing modes have defined stop behavior.
- No network or meshing shutdown path relies on `Thread.Abort()`.
- A live DF smoke run still loads and renders correctly, with preserved input behavior and no material startup errors.

## Phase 3 — Extract focused responsibilities

Priority: After the first milestone. Dependencies: Phase 2.

- [ ] Extract screenshots and mesh/data export from `GameMap`. Preserve file formats, naming behavior, and existing UI/shortcut entry points; release temporary screenshot textures after use.
- [ ] Extract cursor inspection and its UI formatting from map chunk management.
- [ ] Move map shortcuts/UI commands into a focused input/controller component using the existing action asset. Keep a clear handoff for commands that affect map rendering or DF.
- [ ] Separate coordinate conversion and shared world constants from `GameMap` where they can become independent helpers. Add representative round-trip/boundary tests.
- [ ] Reduce `GameMap` to map presentation, visibility, and coordination with its renderer/mesher, extracting those further only where ownership becomes clearer.
- [ ] Split `ContentLoader` into the loading process and the resulting content catalog used for material/sprite lookups. Publish an explicit ready result; document any data that must remain mutable after loading.
- [ ] Retain forwarding methods or serialized compatibility fields during migration where existing prefabs/scenes depend on them. Remove them only after checking every caller and serialized reference.

Acceptance:

- Input/UI or export changes can be understood without following the map meshing loop.
- Content consumers have a defined ready contract instead of inferring readiness from whichever singleton happens to exist.
- Representative exports, cursor inspection, camera/map controls, and rendering retain their existing behavior.
- There are no missing scripts, broken UnityEvent bindings, or lost prefab overrides caused by extraction.

## Phase 4 — Separate editor preparation and runtime loading

Priority: After lifecycle ownership is established. Dependencies: Phase 2; coordinate with Phase 3's content split.

Primary files: `Items/ItemManager.cs`, `Items/DecorationManager.cs`, `Images/ImageManager.cs`, and `Assets/Scripts/Editor/MaterialCollector.cs`.

- [ ] Inventory which previews actually require edit-time loading and what they depend on.
- [ ] Replace unconditional synchronous loader execution in editor `Awake` with explicit preview/build commands or narrowly scoped, invalidated preview preparation.
- [ ] Keep shared parsing/generation logic usable by both editor and runtime callers, with separate owners for their temporary resources.
- [ ] Provide a clear way to rebuild material arrays/definitions after source changes while preserving the current missing-resource generation behavior.
- [ ] Verify startup and builds when generated resources are absent. Preserve existing references when regenerating assets that are already present.
- [ ] Make release packaging check `BuildReport.summary.result` and stop on a failed player build before copying extras or creating an archive. The current build script logs the result and continues.

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
- [ ] Build a Windows player through the supported build command and perform a live DF smoke check. Record other platforms as unverified until actually tested.
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

- 2026-09-13: Reviewed the core architecture and documented this phased plan. No runtime, asset, or project-setting changes were made as part of the planning task.
- Baseline revision: `a4568e1` saved; isolated checkout found an overbroad `Obj/` ignore rule excluding six OBJ exporter source files. A follow-up correction and repeat validation are in progress.
- First implementation milestone: Pending Phases 0–2.

References: [Unity assembly organization](https://docs.unity3d.com/6000.0/Documentation/Manual/assembly-definitions-intro.html), [Unity assembly references](https://docs.unity3d.com/6000.0/Documentation/Manual/assembly-definitions-referencing.html), and [Microsoft thread cancellation guidance](https://learn.microsoft.com/en-us/dotnet/standard/threading/destroying-threads).

### Phase 0 implementation evidence

- Restored 19 remaining LFS payloads (77.35 MiB) under `Content/` and `ProtoGen/`, verifying each SHA-256 and size against its pointer. Existing runtime assets already contained their payloads.
- Excluded local editor preferences, recovery staging, and temporary art files from the baseline.
- Vendored DF client matches all 56 pinned upstream files after text encoding/line-ending normalization; removed the stale submodule declaration and documented provenance in `Docs/ThirdParty.md`.
- Added `Tools/VerifyCheckout.ps1`, `Docs/Development.md`, and the offline Unity content-preparation entry point `Armok.Editor.ProjectValidation.PrepareContent`. Checkout preflight passed over 11,773 files.
- Local live smoke context: Map Mode scene, PC quality preset, Unity 6000.6.0f1, current local DF fortress fixture. Prior DF/plugin versions and warm/cold timing limitations are recorded above; new live verification is still pending.
- Fresh-checkout validation caught six exporter C# files (and their metadata) excluded by the legacy `Obj/` rule. Narrowed it to the root build-output folder and extended preflight to detect ignored C# source.
