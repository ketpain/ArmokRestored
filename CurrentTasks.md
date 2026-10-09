# ArmokRestored maintenance plan

Updated: 2026-09-14

Status: Phases 0-2 and 4 complete and verified. Phase 3 includes inspection, coordinates, screenshots/data exports and the loaded content catalog. The remaining magenta player entity and native release exit crash are fixed on the tested setup. Profiling now has valid draw counts and render/GPU timings. Skipping empty map presentation removes thousands of unused renderers and measurably reduces frame time and memory, following the earlier visibility-update optimization. Creature color instancing now also reduces draw submissions and frame time in controlled player comparisons. Further map input/presentation work and Phases 5-6 remain in the backlog.

## Objective

Make the restored project easier to understand, test, and extend while preserving its working Dwarf Fortress connection, rendering, modern controls, and faster content loading. Work in small, independently verifiable changes, with a working project at the end of each phase.

The baseline, regression tests, and session lifecycle are established. Two measured reductions in map presentation work, creature color instancing and a verified release shutdown compatibility fix are implemented. Opaque geometry submission remains the largest rendering cost. Broader class and folder reorganization remains deferred until needed by that work or a later feature.

## Current priority — Profile and optimize the existing project

User direction (2026-09-14): improve the experience with the existing assets. Windows player captures now establish repeated map visibility updates as the largest script cost and opaque geometry rendering as the remaining frame-time limit. The first focused change is measured below; no visual-quality settings or assets were changed.

### Establish the baseline

- [x] Record the tested source revision, Unity/DF/DFHack/plugin versions, hardware, resolution, graphics quality, view distance, frame cap/VSync, and creature-cache state. Keep the same fortress, DF pause/running state, camera positions, and navigation route for comparisons; preserve the user's settings.
- [ ] Capture representative player sessions: an idle loaded view, camera movement/orbit/zoom, elevation changes, and a populated area. Record initial map settling separately from steady navigation. Repeat the same scenarios to distinguish consistent costs from isolated spikes.
- [x] Use Unity's existing profiling tools to measure main-thread, render-thread, worker, and GPU time where available; managed allocations/GC pauses; draw calls; and memory use. Record unavailable counters and profiling overhead. Confirm user-visible results in a normal release player with comparable settings.
- [ ] Summarize median and 95th-percentile frame times, notable stalls, and memory before/after repeated navigation. Measure cold- and warm-cache startup separately, distinguishing connection, content preparation, and time until usable map geometry. Store captures locally under `Logs/` and record the scenario, findings, and report paths here.

### Make the first measured improvement

- [x] Rank the observed costs and select one concrete bottleneck. Investigate map/entity update work and allocations, dirty-block meshing and Unity mesh uploads, rendering of distant/off-screen objects, or duplicate resources/rebuilding only where the captures point to them. Distinguish client work from waiting on DF, frame limits, or presentation.
- [x] Define the expected benefit and a repeatable verification case before changing code. Implement the smallest complete fix while preserving visible content, inspection, modern controls, and session ownership. Record any proposed visual-quality tradeoff explicitly.
- [x] Repeat the baseline scenarios with the same fixture and settings. Report the measured change and run-to-run variation, including memory or loading regressions. Keep a performance change only when its benefit is supported by the comparison.
- [x] Run regression checks relevant to the changed subsystem, then build and smoke-test the Windows player. Check rendering, camera/elevation controls, cursor inspection, and shutdown; include repeated Play, retry, and normal reload checks when resource or worker lifetime changes.

Acceptance:

- The chosen bottleneck is supported by a player capture and a reproducible scenario.
- Before/after results show a repeatable improvement without an unexplained loss of detail, increased memory growth, or broken behavior. There is no assumed universal FPS or loading-time target.
- Evidence and remaining costs are recorded before selecting the next optimization. A profiling finding alone is not a completed optimization.

### First performance result — block visibility, 2026-09-14

`BlockMeshSet.UpdateVisibility` now skips an already-applied visibility mode. Creating a renderer invalidates that state, so newly arriving meshes still receive their shadow settings. This nine-line runtime change preserves block activation, top-surface shadows and collision layers without changing content or draw distances.

The CPU profiles measured `GameMap.Update` at **3.70 ms before / 0.61 ms after** per frame during navigation (about 84% less). `UpdateBlocks` stayed at 0.047 ms. The larger render-thread `RenderForward.RenderLoopJob` cost stayed at approximately 6.2 ms. The benefit is CPU headroom; an FPS increase is not established.

Release-player results below are medians of three repetitions, in milliseconds. A second run of the original assembly followed the optimized run to check repeatability. Full per-repetition ranges and p95 values are in the evidence report.

| Scenario | Main thread: original / optimized / original repeat | Wall frame: original / optimized / original repeat |
| --- | --- | --- |
| Idle | 5.54 / 3.05 / 5.75 | 8.23 / 8.40 / 8.32 |
| Pan, orbit and zoom | 5.52 / 3.04 / 5.69 | 7.58 / 7.76 / 7.72 |
| Elevation | 4.75 / 2.49 / 5.19 | 4.88 / 4.85 / 5.25 |
| Close view | 5.36 / 3.11 / 5.59 | 7.19 / 7.43 / 7.28 |

Fixture: user-loaded paused 192×192×186 map, current embark area; Unity 6000.6.0f1 / DF 53.16 / DFHack 53.16-r1.1 / RFR 0.21.0; Ryzen 9 7950X / RX 7900 XTX / D3D11; PC quality, 1280×720, VSync off and uncapped. Existing view distances and unit detail were preserved. Each run waits 30 seconds after Ready, then repeats four 10-second scenarios three times, discarding the first 2 seconds of each transition.

End-of-route memory was similar: 2157 MiB original, 2155 MiB optimized, 2143 MiB original repeat. Each release capture advanced the generation-0 collection counter by 19. This includes loaded map data and uncollected allocations, not a retained-memory test. Cold-path and warm-path Ready times were 12.0 and 11.3–11.5 seconds; no loading improvement is attributed to this change.

Verification: **82 tests passed** (67 content, 13 Play Mode including nine new block-visibility checks, two live tests covering three sessions). The new checks cover initial/repeated requests, every visibility transition, collision layers and mesh arrival without a camera change. Windows release builds succeeded; manual player checks verified rendering, movement, elevation, orbit, zoom and pinned inspection. Checkout validation passed for 11,880 files.

Release testing initially exposed a native `0xC0000005` exit after the window closed and engine cleanup finished. Both optimized and clean original-code releases reproduced it without the capture harness; development captures exited 0. The compatibility fix and completed release verification are recorded below.

Measurement limits: release timing runs used temporary counters; detailed development profiles add overhead. Draw-call/batch counters returned unusable zeros, and custom worker/present-wait counters were unavailable. The current embark/close view is not a busy mature-fortress stress test. Exact time until usable geometry and retained-memory growth remain unmeasured, so the broader baseline checklist stays open. Temporary capture scripts were archived outside `Assets`; the production build contains no capture harness.

Evidence: `Logs/PlayerPerformance-20260914/report.md`, `comparison.txt`, per-run `frames.csv` / `summary.json`, development `cpu.raw` / `samples.csv`, test XML, player logs and screenshots. The report records the source revision plus the pre-existing working diff, exact configuration, route, cache state, and limitations.

### Release shutdown compatibility fix, 2026-09-14

Both Post Process Layers in `Assets/Scenes/Map Mode.unity` now set `finalBlitToCameraTarget: 0` (**Directly to Camera Target** off). These are the only production asset changes in this fix. Effects and threaded rendering remain enabled. Session code, graphics API, quality, input and user configuration are preserved.

Controlled nondevelopment builds isolated the trigger to this direct-output option: a camera-and-cube scene without DF crashed with it enabled and exited 0 with it disabled. Full-map tests reproduced the same enabled/disabled/enabled/disabled result. Stopping the session early with zero workers did not prevent the original crash. This establishes a compatibility setting for Unity 6000.6.0f1 / Post Processing 3.5.4 on the tested Windows/Radeon setup; the underlying native defect remains unidentified.

Verification: the normal Windows release build succeeded with all diagnostic scripts removed. **Three loaded-map window-close runs and one connection-failure dialog Quit run exited 0**, recorded from their process handles. The failure check used an unused localhost port and restored the configuration afterward; it did not stop DF. Loaded runs had no managed exceptions. Rendering, movement, elevation, orbit, zoom, pinned inspection and settings interaction were checked. **Two live tests passed**, covering three sessions, retry, inspection and worker cleanup. Checkout validation passed for 11,880 files. Documentation now records the release exit-code check and camera setting.

This restores the final image copy skipped by the fast path. The comparison below uses the corrected camera configuration throughout; it does not isolate the cost of that copy. Evidence: `Logs/ExitCrash-20260914/report.md`, per-run logs/exit codes, screenshots, `live.xml`, `live-stages.txt` and `final-build.log`.

### Second performance result — empty map presentation, 2026-09-14

The Frame Debugger found **4,340 draw calls**, with 4,256 events in opaque rendering. Tile geometry accounts for 2,211 events, distant terrain for 217, and many remaining draws are separate creature body parts. In that editor snapshot, **16,493 of 19,992 MeshRenderers had zero vertices**. Eight unused renderer/filter templates were instantiated with every map block, and runtime results also created renderers for empty meshes.

`BlockMeshSet` now allocates presentation only when geometry arrives. An empty update clears and disables an existing renderer, retaining its mesh for reuse. Terrain normals/tangents are recalculated only after a nonempty copy. `MapBlock.prefab` retains its root, block component, collider and GUID, and drops the unused renderer children and obsolete serialized references. Visible geometry, material selection, draw distances, shadow modes and input are preserved.

Four alternating release captures used the same paused embark, configuration, resolution and route described above, with both camera compatibility flags off. Each value below is the range of the two runs' medians of three repetitions, in milliseconds:

| Scenario | Original frame time | Optimized frame time | Original CPU main | Optimized CPU main |
| --- | --- | --- | --- | --- |
| Idle | 8.33–8.61 | 7.51–7.58 | 3.10–3.58 | 2.42–2.48 |
| Pan, orbit and zoom | 7.68–7.90 | 7.00–7.09 | 3.06–3.50 | 2.42–2.44 |
| Elevation | 4.75–4.99 | 4.21–4.30 | 2.50–3.01 | 1.96–2.00 |
| Close view | 7.38–7.65 | 6.55–6.59 | 2.95–3.37 | 2.32–2.34 |

Averaging those two run medians gives approximately **10–13% less frame time** across the four scenarios. The idle render thread changes from 6.56–6.82 to 6.32–6.41 ms; GPU time changes from 4.32–4.50 to 4.05–4.09 ms. These are observations on this machine, not universal targets. Active enabled MeshRenderers at the same settled starting view fall from **18,955 to 3,355** in both comparisons. End-of-route Unity used memory falls from 2,195–2,197 to 2,132–2,135 MiB (about 63 MiB); this counter includes uncollected allocations.

Fixed-view median triangle counts match: 2,862,693 idle, 1,870,682 elevation and 2,522,127 close. Draw calls remain approximately 4,410 at idle; removing empty renderers primarily reduces scene/rendering bookkeeping, not visible draw submissions. Screenshots preserve visible geometry. The optimized runs each advance the generation-0 collection counter by 21–22, as do the originals. One original run contains a 359 ms stall; its repeat does not. Optimized worst steady frames are 33–35 ms. This does not establish a fix for occasional stalls.

`Standard Draw Calls Count`, `Standard Instanced Draw Calls Count`, SetPass and triangle counters are valid in these captures; the old aggregate Draw Calls/Batches counters remain unusable. FrameTimingManager supplies render-thread and GPU times. All four measured players exited 0. Detailed p95 values, per-segment ranges, exact settings, screenshots and logs are under `Logs/RenderingPerformance-20260914/`, with methodology in `report.md`. Temporary profiling code is archived outside `Assets`.

The six-minute memory run repeated the route nine times, forcing managed collection only between cycles, outside the timing comparisons. After the second cycle, counts stayed at **2,135 blocks, 6,901 MeshRenderers, 4,947 meshes, 194 materials and 2,180 textures** through the ninth. Retained managed memory decreased from 665 to 552 MiB over those cycles. Unity's allocated-memory total increased modestly from 1,364.6 to 1,369.9 MiB; this is not proof of a leak-free session, and a longer populated/running fixture remains useful. Initial growth came with newly visited map blocks. Raw snapshots are in `candidate-memory/retained-memory.csv`; the memory player exited 0.

Sixteen new allocation/empty/refill/terrain/prefab cases fail against the original implementation and pass after the change; **all 29 Play Mode input/presentation tests and both live DF tests pass**. The final test run also verifies exact recalculated terrain normals and tangents. Live checks cover three loads, hover/pinned inspection, retry and worker shutdown. The normal Windows release built successfully with profiling code removed; rendering, movement, elevation, orbit, zoom, pinned inspection and settings were checked. **Two loaded-map window-close runs exited 0**, in 0.98 and 0.81 seconds. Checkout validation passed for 11,882 files. User configuration and project settings match the pre-task backups; existing unrelated edits remain intact. Other platforms and a busy running fortress remain unverified.

### Embark rendering investigation, 2026-09-14

User direction: investigate the currently loaded earlier embark; do not wait for a mature fortress. DF was actually running during this investigation. The temporary player stopped its own polling worker after loading and settling, then held map/creature presentation and displayed time fixed. It sent no pause commands to DF. This isolates rendering costs on one snapshot; it does not measure normal live-update performance or compare directly with the earlier paused-save captures.

The nondevelopment Windows player used the existing quality/settings at 1280×720, uncapped with VSync off. Both cameras remained Forward, with direct post-process output disabled. Two fixed views (initial and half camera-rig scale) each alternated baseline and hidden-category segments, repeating each category twice. Segments lasted six seconds, with the first two discarded; **18,223 frames** were retained. Four zero/unavailable GPU samples were excluded from GPU statistics only. All render suppression was temporary and restored before exit.

The table gives the range of the two repetitions' reductions relative to the adjacent baseline medians, in milliseconds. These are diagnostic removals of entire rendering groups, **not achievable optimization gains**, and must not be added together.

| Temporarily hidden group | Initial view: frame / render thread / GPU reduction | Closer view: frame / render thread / GPU reduction |
| --- | --- | --- |
| Map blocks, including local terrain and liquids | 3.31–3.34 / 2.98–2.99 / 3.08–3.13 | 2.56–2.61 / 2.28–2.31 / 2.05–2.10 |
| Creatures | 2.63–2.68 / 2.40–2.46 / 1.72–1.78 | 2.48–2.54 / 2.22–2.25 / 1.35–1.41 |
| Distant terrain, excluding clouds | 0.03–0.10 / 0.08–0.12 / 0.04–0.07 | 0.04–0.11 / 0.01–0.08 / 0.13–0.17 |

Baseline frame medians stayed within **7.39–7.47 ms initially / 6.38–6.45 ms closer**; baseline p95 values were 7.90–8.01 / 6.87–6.97 ms. Standard draw counters stayed at 4,014 / 3,113, with a separate 123 / 128 instanced draws, and triangle counts stayed at 2,599,294 / 2,166,636 within each view. These are separate draw categories, not an aggregate draw counter or an instance count. Hiding creatures removed 1,121 standard plus 112 instanced draws initially; hiding map blocks removed 2,244 standard draws. Distant terrain removed only 28 standard draws in that view.

The player inventory contains **872 enabled creature renderer/material entries across 20 displayed creatures**, using 51 meshes and 11 materials: 856 MeshRenderers and 16 SkinnedMeshRenderers. Shared examples include 200 cubes, 84 spheres, 70 toes and 70 toenails. The map has 2,456 enabled nonempty renderer entries, each with its own mesh, using nine materials. Inventory counts include objects outside the camera frustum; they are not draw counts.

The next focused candidate is **creature color instancing**. `BodyLayer` sets `_Color1`, `_Color2` and `_Color3` in property blocks, but `buildingInputs.cginc` declares those as ordinary uniforms; `_MatColor`, `_MatIndex` and `_ShapeIndex` are already instanced. The current Skin material enables instancing and `_PATTERN_MASK`; 454 Skin entries have property blocks. [Unity's instancing documentation](https://docs.unity3d.com/6000.6/Documentation/Manual/gpu-instancing-per-instance-properties.html) explains that non-instanced property overrides prevent instancing. This is a source-supported batching obstacle, not a measured fix. Some creature parts already instance: a separate editor frame contained 1,305 ordinary and 89 instanced creature draw events. Per-event native pass/batch-reason retrieval was unavailable, so no specific Frame Debugger batch-break diagnosis is claimed.

Prototype the three color properties first, preserving their values and the current shader output. Retain existing handling of pattern textures, eyes, clothing, skinned parts, clipping and shadows; do not assume that every creature draw can be instanced. Forward lighting passes and per-renderer pattern textures remain constraints. Verify appearance and geometry against fixed references, then compare draw counts and frame/render/GPU timings before keeping the change. Larger map-mesh grouping would also have to preserve per-block textures, dirty updates, elevation visibility and inspection, making it a broader follow-up. Distant terrain is not the priority for these views.

Verification: the diagnostic build succeeded with **zero errors and warnings**, the completed player run logged no managed exceptions/session failures and exited **0**. An earlier consumer-only freeze filled the incoming queue and was discarded; the corrected capture stopped polling before freezing consumers. Temporary scripts were archived outside `Assets` and removed from the project. Production rendering code/assets and user settings are unchanged by this investigation. Final cleanup/checkout verification is recorded in `Logs/DrawInvestigation-20260914/report.md`; existing regression tests were not rerun because this slice ships documentation only. Detailed timings, inventories, screenshots, methodology, limitations and the rejected-run evidence are under that same directory.

### Third performance result — creature color instancing, 2026-09-14

`buildingInputs.cginc` now declares `_Color1`, `_Color2` and `_Color3` as instanced half4 properties; `BuildingSurf.cginc` reads them through the existing instancing buffer. The change preserves per-creature colors, material defaults, pattern-texture overrides, meshes, lighting, clipping and shadows. No rendering settings, material assets or runtime C# paths changed.

Two nondevelopment Windows player captures alternate original and optimized shaders on a held client snapshot of the user’s running embark. Each uses the existing settings at 1280×720, two fixed camera views, eight-second segments with two seconds discarded, and original segments bracketing each candidate. DF itself keeps running. The snapshot changes between launches as the game progresses, so comparisons are made within each capture.

| Capture / view | Original frame medians | Optimized frame medians | Paired frame reduction | Paired render-thread reduction |
| --- | --- | --- | --- | --- |
| First / initial | 8.536–8.764 ms | 7.870–7.930 ms | 0.720–0.787 ms | 0.681–0.736 ms |
| First / closer | 7.322–7.346 ms | 6.656–6.739 ms | 0.605–0.674 ms | 0.559–0.612 ms |
| Repeat / initial | 9.443–9.567 ms | 8.595–8.687 ms | 0.818–0.933 ms | 0.796–0.898 ms |
| Repeat / closer | 8.429–8.581 ms | 7.792–8.023 ms | 0.481–0.738 ms | 0.491–0.710 ms |

This is approximately **6–10% less frame time** on the tested RX 7900 XTX setup. GPU reductions range from 0.16 to 0.70 ms; main-thread timing changes little. The first capture has three comparisons per view. The repeat excludes its initially hidden-window phase and the adjacent candidate, leaving two valid initial-view comparisons and three closer comparisons. Raw rejected data is retained. These are rendering measurements, not live-update, startup, navigation or mature-fortress results.

In the first capture, ordinary draws fall **4,689 → 4,472** initial and **3,516 → 3,389** closer, while instanced draws rise **158 → 194** and **187 → 200**. Combining those separate categories gives **181 / 114 fewer draw submissions**. SetPass falls **3,795 → 3,599 / 3,052 → 2,918**. Triangle counts remain exactly **2,986,275 / 2,435,456** in every segment. The repeat removes 168 / 141 combined draws with unchanged geometry. Unique meshes, skinned parts, per-renderer texture overrides and Forward lighting passes remain constraints; this does not batch every creature part.

Full-frame screenshot differences remain within the original/original variation: at most three RGB byte levels, with mean differences around 0.47 on a 0–255 scale. Screenshots were also visually inspected. Seven focused graphics tests compare base/pattern colors, non-primary colors, material defaults and per-renderer texture overrides against separate-material references; **all 74 content tests pass**. These test appearance, while the player counters establish the batching benefit. Both diagnostic players exit 0; the diagnostic build has zero errors/warnings. Temporary comparison code and baseline resources are archived outside `Assets`.

Both live DF tests pass across three loads, including hover/pinned inspection, retry and worker shutdown. The normal Windows release builds with the diagnostic code/resources removed; rendering, movement, zoom, orbit, elevation, pinned inspection and settings were checked. Loaded-map window close exits **0 in 0.99 seconds**. Checkout validation passes for **11,884 files**. User configuration, project settings and build settings match their pre-task backups. Other platforms, a busy mature fortress, startup/memory effects and the Deferred path remain unverified for this change.

Evidence, complete p95 statistics, fixture details, raw frames, screenshots and limitations: `Logs/CreatureInstancing-20260914/report.md` and its `comparison` / `comparison-repeat` folders.

Next actions:

1. Investigate reducing local map submissions; account for block textures, dirty updates, elevation visibility and inspection before proposing larger mesh groups.
2. Resolve the saved rendering-path setting separately: both cameras currently start in Forward mode despite `deferredRendering=true` until the settings UI applies it. Compare the intended appearance before changing the startup behavior.
3. Repeat profiling in a populated mature fortress when available, and finish usable-geometry startup timing and longer-session checks. Keep lighting/material tuning and broad restructuring deferred.

### Later visual clarity pass

- [ ] After the first performance pass, assess lighting, shadows, fog, and material readability using the current assets. Compare fixed-camera screenshots and GPU/frame-time measurements. Keep visual tuning separate from behavior-preserving performance changes and record the chosen appearance before expanding the work.

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

Priority: Remaining extractions are deferred unless needed by a measured optimization or feature. Dependencies: Phase 2.

- [x] Extract screenshots and data export from `GameMap`. Preserve file formats, naming behavior, and existing UI/shortcut entry points; release temporary screenshot textures after use. Its mesh-export method was an unused commented prototype; separate Collada/OBJ tools remain unchanged.
- [x] Extract cursor inspection and its UI formatting from map chunk management.
- [ ] Move map shortcuts/UI commands into a focused input/controller component using the existing action asset. Keep a clear handoff for commands that affect map rendering or DF.
- [x] Separate coordinate conversion and shared world constants from `GameMap` where they can become independent helpers. Add representative round-trip/boundary tests.
- [ ] Reduce `GameMap` to map presentation, visibility, and coordination with its renderer/mesher, extracting those further only where ownership becomes clearer.
- [x] Split `ContentLoader` into the loading process and the resulting content catalog used for material/sprite lookups. Publish an explicit ready result; document any data that must remain mutable after loading.
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

- [x] Inventory which previews actually require edit-time loading and what they depend on.
- [x] Replace unconditional synchronous loader execution in editor `Awake` with explicit preview/build commands or narrowly scoped, invalidated preview preparation.
- [x] Keep shared parsing/generation logic usable by both editor and runtime callers, with explicit ownership and cleanup of temporary resources in each context.
- [x] Provide a clear way to rebuild material arrays/definitions after source changes while preserving the current missing-resource generation behavior.
- [x] Verify startup and builds when generated resources are absent. Preserve existing references when regenerating assets that are already present.
- [x] Make release packaging check `BuildReport.summary.result` and stop on a failed player build before copying extras or creating an archive.

Acceptance:

- Opening scenes or recompiling does not unexpectedly perform a full content load merely to initialize a manager.
- Required previews and generated resources remain available through documented actions.
- A clean build prepares required resources and a failed build is reported as failed without packaging an incomplete release.

Editor preparation slice (2026-09-14):

- DF Raw Reader's equipment previews need prepared item/image content; ordinary scene opening and recompilation do not. `Mytools > Prepare Content Previews` now runs the shared loaders explicitly, and Read Raws invokes it after fetching definitions. Generic decoration shapes remain the fallback when standalone previews have no language definitions.
- Image preparation uses CPU pixels, preserves imported art, and avoids the 256 temporary glyph textures. Generated arrays/images/meshes are owned by their manager and released on rebuild, session stop, destruction, or editor disable/recompile. Runtime callback registration remains unchanged.
- The focused baseline reproduced eager loading in item and image managers. The final 67 content tests pass, including source preservation, stored color/normal parity, and ownership checks; 23 lifecycle tests and both live tests pass. Repeated live sessions verify five callbacks, stable creature sprite hashes, inspection, retry, and released arrays/workers. Input bindings were unchanged; the separate synthetic-input suite was not rerun in this slice.
- Explicit preview preparation rebuilt absent resources into 30 shape slices, 57 pattern slices, 668 material definitions, and 1,453 image slices. Repeating preparation preserved material GUIDs. Normal domain/scene reload startup also succeeded from absent resources with no diagnostics and stopped both workers. A Windows release starting without those resources rebuilt them and succeeded with zero warnings/errors; the final player rendered, pinned inspection, moved the camera, and closed normally without application errors. Other platforms remain unverified.
- The small magenta `REMAINS_601` entity used a transparent shader selected by name and omitted from the player. Graphics settings now explicitly include both runtime-selected transparent shaders, and checkout preflight detects missing inclusion. The affected renderer resolves to `Art/SingleImage Transparent`; the diagnostic player scan has zero error/unsupported materials, and the final player has no visible magenta artifact.
- Evidence: `Logs/RenderingAndEditorPrep-20260914/` contains test XML, preview/reload/build reports, shader diagnostics, and final player screenshots/log. Temporary diagnostic scripts are outside `Assets`. User build profiles/settings and original generated-resource metadata were preserved.

## Phase 5 — Organize files and enforce dependency boundaries

Priority: Backlog after the current profiling/optimization pass; move files only where the organization has a concrete benefit. Dependencies: Stable tests and ownership from Phases 3–4.

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
7. Windows player profiling and a repeatable performance baseline using existing assets (next).
8. One measured optimization at a time, with before/after captures and relevant regression/player checks.
9. Dependency inventory, feature folder moves, and further assembly boundaries where justified.
10. Fresh-checkout verification, remaining regression work, and architecture documentation.

Do not combine all slices into one change. Preserve a verified checkpoint before proceeding to the next slice. During shutdown work, characterize the current path before replacing it; complete the full coordinator migration afterward.

## Deferred unless a measured need or product decision justifies them

- Replacing asset collections, commissioning new artwork, or generating a new art set.
- Porting custom shaders to URP or broadly changing the rendering appearance. The limited visual-clarity pass above retains the current assets and Built-in pipeline.
- Map input/controller extraction and broad folder/assembly reorganization unless needed by a measured optimization or feature.
- Introducing Addressables, DOTS/ECS, or replacing the entire meshing implementation.
- A comprehensive settings/UI redesign or mandatory keybinding redesign.
- Rewriting stable third-party libraries or hand-editing generated protocol classes.
- Widespread renaming and formatting unrelated to the extracted responsibilities.

## Progress record

- 2026-09-14: Reprioritized the remaining roadmap around Windows player profiling and measured improvements using existing assets. Added comparable scenarios, CPU/GPU/allocation/memory checks, before/after acceptance, and a later limited visual-clarity pass. Structural cleanup and asset replacement are deferred. This is a planning update; no profiling run or optimization was performed for it.
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

- 2026-09-14: Completed the loaded-content split. ContentLoader builds a private ContentCatalog and publishes it only after all content callbacks succeed; GameMap explicitly passes it to mesher constructors. Texture/configuration/sprite access and material lookups moved into the catalog; static preview fallbacks and existing seasonal/cache mutability are documented. Shutdown unpublishes only the owning catalog after workers stop. All 86 maintained checks passed: 57 content/export/coordinate, 23 lifecycle, four input and two live tests. The live run verified new catalogs across three Play sessions, worker ownership, cancellation/retry and identical sprite mip hashes. The final Windows release build succeeded with 23 existing API/serialization warnings and no errors; the actual player rendered, moved/zoomed/orbited, pinned/released inspection, saved an F9 PNG, handled Ctrl+F9 without a minimap, and closed in 2.4 seconds without exceptions. Preflight passed. User build profiles/settings were preserved. Evidence: Logs/ReleaseAndPhase3-20260914, including catalog-*.xml, build-report.json and player-final.log. Normal domain/scene reload validation was not rerun in this slice; other platforms remain unverified. Remaining follow-ups: map input/controller extraction, editor preview preparation, existing warnings and the small magenta entity seen before and after these refactors.

- 2026-09-14: Resolved the 23 reported Unity 6.6 warnings: replaced deprecated scene searches and network-error checks, marked map meshes and the sprite tester's property block as nonserialized runtime state, and migrated profiling guards to UNITY_INCLUDE_INSTRUMENTATION. Scene-service lookups assume one active service; bulk updates no longer sort, with inactive-object filtering preserved. The main-thread profiling guard and connection-error behavior are unchanged. The development-build message now checks Debug.isDebugBuild at runtime. Editor compilation passed; the Windows release recompiled Assembly-CSharp and succeeded with zero warnings and errors. Both live DF tests passed, covering repeated Play, inspection, retry and worker shutdown; checkout preflight passed. Evidence: Logs/UnityWarnings-20260914. Other suites, a launch of the rebuilt player, and other platforms were not rerun for this warning cleanup.
