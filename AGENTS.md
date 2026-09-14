# Engineering Instructions

Write code that is modern, professional, concise, simple, and easy to inspect.

Prefer durable solutions over patchy fixes, but do not add speculative abstractions, features, configurability, or infrastructure.

Simplify aggressively without making the code obscure.

Follow the existing repository style and conventions. For new brace-based code in files without an established style, use Allman-style bracing.

## Think Before Coding

Do not assume silently. Surface material assumptions, ambiguity, and tradeoffs.

Before implementing:

* State assumptions that affect correctness, behavior, compatibility, or scope.
* When multiple reasonable interpretations exist, identify them before choosing.
* Prefer the simplest approach that fully solves the request.
* Push back on unnecessary complexity.
* Ask for clarification only when a reasonable, reversible assumption cannot be made safely.
* When proceeding under an assumption, state it clearly.

For multi-step tasks, provide a brief plan that includes verification:

1. Change X → verify with Y.
2. Change Z → verify with tests, build output, or a focused manual check.

Keep the plan proportional to the task. Do not add process overhead to trivial changes.

## Simplicity First

Use the minimum code required to solve the problem correctly.

* Do not add features beyond the request.
* Do not create abstractions for one-off behavior unless they materially improve clarity or testability.
* Do not add configurability that was not requested.
* Avoid unnecessary frameworks, layers, adapters, factories, managers, wrappers, and helpers.
* Avoid defensive code for states that are impossible because an invariant is explicitly enforced.
* Treat external input, I/O, concurrency, network calls, and integration boundaries as fallible.
* Prefer straightforward control flow over cleverness.
* Avoid deep inheritance, excessive indirection, hidden behavior, and action at a distance.
* Prefer plain functions, plain data, and small modules when they fit the problem.
* Do not flatten code so aggressively that responsibilities or ownership become unclear.

When code can be substantially shorter without reducing clarity, simplify it.

## Data And State

Prefer explicit data flow and explicit state.

Organize code around the shape, ownership, lifetime, access patterns, and transformation of data.

Use data-oriented design where it provides a practical benefit, especially in performance-sensitive, batch-processing, simulation, game, or systems code.

Favor:

* Predictable iteration
* Compact and appropriate data structures
* Clear ownership
* Explicit state transitions
* Local reasoning
* Minimal hidden mutation
* Minimal global state

Do not force data-oriented patterns into code where a simpler domain model is clearer.

## Clarity And Performance

Optimize for clarity first and measured performance second.

Make reasonable performance choices without speculative optimization.

* Keep hot paths simple.
* Avoid needless allocations and repeated work.
* Prefer algorithms and data structures appropriate to the expected scale.
* Structure code so profiling and later optimization remain straightforward.
* Use concrete, meaningful types.
* Avoid generic code unless the generic form has clear present value.
* Handle failures directly and predictably.
* Use explicit validation at system boundaries.
* Return useful error messages without leaking sensitive details.

Do not claim a performance improvement without evidence from complexity analysis, profiling, benchmarking, or an obvious reduction in work.

## Comments

Use comments when they add information that the code cannot communicate clearly on its own.

Comments should explain:

* Why a decision was made
* Why a tradeoff is acceptable
* Which invariant must remain true
* Why code is structured in an unusual way
* Why an optimization or workaround exists
* What ownership, lifecycle, or synchronization constraint applies
* What external behavior or compatibility requirement must be preserved

Do not comment obvious syntax or restate names.

Prefer:

> Store active items contiguously so the update loop can scan them without pointer chasing.

Avoid:

> Loop through the active items.

Functions, methods, fields, and blocks do not require comments when their purpose is already clear from names, types, structure, and surrounding context.

Add documentation when public APIs, non-obvious behavior, invariants, ownership rules, or maintenance risks require it.

Keep comments short, accurate, and current.

## Names And Structure

Use names that describe the role of the data or operation without requiring unnecessary navigation.

* Prefer precise names over clever or abbreviated names.
* Keep functions focused, but do not fragment logic into trivial wrappers.
* Split code when it improves comprehension, reuse, ownership, or testability.
* Keep modules cohesive.
* Keep project structure as shallow as practical, but preserve clear boundaries.
* Avoid catch-all utility files and vague modules such as `Manager`, `Helper`, or `Common` unless the name is genuinely accurate.

## Surgical Changes

When modifying existing code:

* Touch only what is needed for the requested result.
* Preserve the existing style and architecture unless they directly prevent a correct solution.
* Make the smallest complete change.
* Avoid unrelated refactors.
* Do not reformat unrelated code.
* Do not rewrite nearby comments unless the change makes them inaccurate.
* Mention unrelated problems instead of fixing them unless they block the requested work.
* Remove imports, variables, functions, tests, and files made unused by your change.
* Do not remove pre-existing dead code unless requested or required for correctness.
* Include necessary tests, documentation, migrations, generated artifacts, and cleanup caused directly by the change.
* Explain unusual decisions when the reason would not be obvious to a future maintainer.

Every changed line should have a clear relationship to the requested behavior or to the work required to verify and support it.

## Tests And Verification

Make behavior verifiable.

For bugs:

1. Reproduce the failure with a focused test or reliable verification case.
2. Implement the smallest correct fix.
3. Verify the original failure is resolved.
4. Check for closely related regressions.

For new behavior:

* Test important behavior.
* Test meaningful edge cases.
* Test data transformations and validation boundaries.
* Keep tests simple, deterministic, and readable.
* Prefer behavior-focused tests over implementation-detail tests.

For refactoring:

* Verify existing behavior before the change when practical.
* Keep behavior unchanged unless the request says otherwise.
* Run the relevant tests after the change.

Do not add low-value tests merely to increase coverage. Verification should be proportional to the risk and nature of the change.

When automated tests are not practical, use the clearest available alternative:

* Build or type-check
* Lint
* Static analysis
* Focused manual reproduction
* Snapshot or fixture comparison
* Benchmark
* Integration check

State what was verified and what was not.

## Final Review

Before finishing, review the change for:

* Correctness
* Simplicity
* Clear data flow
* Appropriate scope
* Useful naming
* Necessary comments only
* Explicit state and ownership
* Avoidance of unnecessary abstraction
* Reasonable performance characteristics
* Important edge cases
* Relevant tests or another clear verification path
* Accidental unrelated changes

Do not describe work as complete when verification has not been performed.

## Project Architecture

ArmokRestored is a Unity restoration of Armok Vision, a 3D client for a running
Dwarf Fortress game through DFHack and RemoteFortressReader. DF owns the game
simulation; this project fetches its definitions and live state, loads matching
visual content, and renders the map, creatures, items, and buildings.

The restoration is incremental. Most application code still lives in Unity's
predefined assemblies under `Assets/Scripts`. The extracted lifecycle, input,
and texture helpers live under `Assets/Armok`. Folder names do not imply that
every subsystem has its own assembly or that the remaining legacy dependencies
have already been separated.

### Runtime Flow

The main scene is `Assets/Scenes/Map Mode.unity`. Its major relationships are:

```text
Unity scene startup
|- SessionRuntime -> SessionCoordinator / LoadOperation / worker shutdown
|- DFConnection -> vendored DFHack client + local protocol messages -> running DF
|  `- fetched definitions and buffered live map/entity updates
|- ContentLoader -> StreamingAssets index + material resources + sprite cache
|  `- content matching and registered loaders -> published ContentCatalog
|- GameMap -> MapDataStore + BlockMesher -> CPU mesh results -> Unity meshes
|  `- map presentation, creature/building/item managers, distant terrain
`- camera and UI -> ArmokInput + InputSystem_Actions
   `- CursorInspection -> TileInspection -> current map data
```

Definitions must arrive before content can be matched to DF identifiers. Content
loading must finish before the session becomes Ready and live polling and map
presentation begin. This is the actual startup dependency, even though several
legacy components still communicate through static properties and scene references.

### Session And DF Connection

* `Assets/Armok/Runtime/App` contains the Unity-independent `Armok.Lifecycle`
  assembly. `SessionCoordinator` owns session stages and callback registrations;
  `LoadOperation` owns nested loading enumerators and their cancellation/disposal;
  `PollingWorker` and the worker shutdown helpers implement cooperative stopping;
  `SuspendedOperation` pairs acknowledged DF suspension with resume and failure
  handling. Stages are Disconnected, Connecting, LoadingContent, Ready, Stopping,
  and Failed.
* `Assets/Armok/Runtime/Integration/SessionRuntime.cs` is the Unity composition
  host. It is created before scene startup, connects scene components to the
  coordinator, registers content participants, owns worker cleanup, and presents
  failure/retry UI. Retry reloads the current scene only after the old session
  stops. This file remains in `Assembly-CSharp` so it can reference legacy types.
* `Assets/Scripts/MapGen/DFConnection.cs` performs connection/plugin checks, RPC
  binding, definition fetches, and live polling. It adapts the client to the
  session lifecycle and supplies the buffered data consumed by scene components.
  Polling supports both the background worker and the existing main-thread mode.
* `Assets/RemoteClientDF-Net` is the vendored DFHack transport, core messages,
  remote functions, and supporting enums. It is tracked source, not a submodule.
  Its local timeout, cancellation, and transport fixes must be preserved when
  updating upstream; revision and license details are in `Docs/ThirdParty.md`.
* `Assets/RemoteClientLocal` contains local `.proto` definitions, generated
  `protos.cs`, and client/output integration. `ProtoGen` contains the generation
  tools. The editor's Build Proto command lives in
  `Assets/Scripts/Editor/BuildPipeline.cs`; keep generation reproducible instead
  of manually editing generated message classes.

Workers receive stop requests before joining, with one shared 500 ms main-thread
wait budget. If workers are still exiting, the session retains ownership and
stays Stopping; cleanup and a new session must not overlap. Socket interruption
wakes blocked I/O. No shutdown path uses `Thread.Abort`. Both normal Play reloads
and the project's disabled domain/scene reload configuration are supported.
See `Docs/SessionLifecycle.md` for detailed ordering and failure behavior.

### Content And Rendering

* `Assets/Scripts/MapGen/ContentLoader.cs` loads the index at
  `Assets/StreamingAssets/index.txt`, parses content definitions, builds shared
  texture/sprite data, matches DF materials, and runs registered content loaders.
  It builds a private `ContentCatalog`, then publishes `Catalog` and
  `ContentCatalog.Current` only after successful completion of all registered
  loaders. `Assets/Armok/Runtime/Content/ContentCatalog.cs` holds loaded texture
  layouts, configurations, sprite access and material lookup logic. Both remain
  in `Assembly-CSharp`. GameMap passes the published catalog to mesher workers;
  shutdown unpublishes it only after workers stop. Static material lookups retain
  editor-preview fallbacks and existing DF/material singleton dependencies.
  Configurations/layout are fixed after loading; lookup caches, seasonal colors,
  sprite selection and main-thread transparent material variants remain mutable.
* `Assets/Scripts/MapGen/ContentType`, `TileConfiguration`, `MultiMatcher`, and
  `RawParser` contain visual content definitions, selection rules, token matching,
  and raw parsing. `MaterialStore` and `TextureAtlas` contain material collections,
  texture arrays, sprite pages, and their assembly/loading helpers. These names
  are subdirectories of `Assets/Scripts/MapGen`.
* `Assets/Armok/Runtime/Content/Textures` contains `TextureScale` and
  `SpriteTextureCache` in `Armok.Textures`. The cache stores generated creature
  color/normal arrays, including mip levels, under
  `Application.persistentDataPath/Cache/CreatureSprites`. Source/layout
  fingerprints invalidate stale entries; corrupt or unavailable entries fall
  back to generation. Cached graphics never replace live DF state.
* `Assets/Scripts/MapGen/MapDataStore.cs` holds tile/block data, coordinate and
  neighborhood queries, and map raycasting. `GameMap.cs` consumes updates,
  schedules dirty blocks, manages the visible region, and applies mesh results.
  Shared coordinates and tile/block constants live in
  `Assets/Armok/Runtime/World/MapCoordinates.cs` in `Assembly-CSharp`. GameMap
  updates its synchronized vertical offset from DF's embark position. A DF map
  block is 16 by 16 by 1 tiles.
* `Assets/Scripts/MapGen/Meshing/BlockMesher.cs` generates CPU mesh data using the
  current content. It supports zero, one, or multiple worker threads and
  cooperative cancellation. `Meshing/MeshData.cs` defines `CPUMesh`;
  `MapBlock/BlockMeshSet.cs` holds the map's Unity mesh presentation. These paths
  are under `Assets/Scripts/MapGen`. Keep Unity object creation, destruction,
  texture uploads, and mesh application on the main thread.
* `Assets/Scripts/MapGen/Buildings`, `Items`, `Images`, and `DistantTerrain`
  contain their respective presentation systems. `CreatureSpriteManager.cs`
  manages creature sprites, while `Assets/Scripts/UnitCreator` handles creature
  body/model construction. `Assets/Scripts/CelestialScripts` supplies time,
  lighting, sky, and related visual behavior.

The project uses the Built-in Render Pipeline with its existing shaders and
materials, including the material texture arrays. URP is present in the package
manifest but is not the active rendering pipeline. Preserve the Graphics and
Quality settings when making unrelated changes.

### Input, Inspection, And Settings

* `Assets/InputSystem_Actions.inputactions` is the shared modern Input System
  asset. `Assets/Armok/Runtime/Input/ArmokInput.cs`, in `Armok.Input`, exposes the
  Armok action map. Active Input Handling is Input System Package (New).
* `Assets/Scripts/CameraScripts` contains camera movement, orbit, zoom, selection,
  and panning components. `MapSelection` retains camera panning; cursor inspection
  has moved into `Assets/Armok/Runtime/UI`.
* `CursorInspection` owns hover/pinned selection and its passive card/marker UI;
  `TileInspection` formats current main-thread map data without sending commands
  to DF. `MapInputUI` handles pointer-over-UI and actual keyboard focus checks.
  These three files remain in `Assembly-CSharp` because they use legacy map and
  UI types. Inspection hides unrevealed tile details unless Show Hidden Tiles is
  enabled. Behavior and controls are documented in `Docs/MapInspection.md`.
* The old dwarf/adventure command panels, clock, raw cursor text, log UI, and mode
  switcher are inactive in Map Mode. Their serialized objects/references are
  retained; they have not been deleted across the project. Settings, connection
  dialogs, and loading progress remain available. Other sample and VR scenes
  have not received the same UI redesign.
* `Assets/Scripts/IniFileParser/GameSettings.cs` loads/saves JSON settings despite
  the historical folder name. Configuration is stored in the user's Documents
  folder under `Application.productName/Config.json`. The component saves on
  destruction, so tests must restore temporary settings before destroying scenes.
  `Assets/Scripts/MapGen/UserSettings`, `Assets/Ui`, and `Assets/UIScripts` contain
  existing settings and UI components.

### Assembly Boundaries

| Assembly | Responsibility and dependencies |
| --- | --- |
| `Armok.Lifecycle` | Session state, load ownership, suspension, and worker shutdown; no Unity engine references. |
| `Armok.Input` | Shared actions wrapper; references `Unity.InputSystem`. |
| `Armok.Textures` | Resizing and sprite cache helpers; uses Unity types but no DF types. The internal cache grants friend access to `Assembly-CSharp` and its tests. |
| `Assembly-CSharp` | Most legacy runtime code, `SessionRuntime`, coordinates, content catalog, exports and inspection UI. |
| Unity predefined editor/first-pass assemblies | Existing Editor, Plugins, and Standard Assets code follows Unity's special-folder compilation rules where not covered by an asmdef. |

The three extracted production assemblies are auto-referenced. Custom test
assemblies cannot directly reference classes in `Assembly-CSharp`; existing
integration tests use reflection where necessary. Do not add an asmdef over all
of `Assets/Scripts` as a shortcut: dependency direction and scene references
must be accounted for explicitly.

### Editor Tools, Exports, And Assets

* `Assets/Scripts/Editor/MaterialCollector.cs` builds `shapeTextures`,
  `patternTextures`, and `materialDefinitions` resources from tracked material
  sources. It prepares missing resources before Play Mode; the explicit command
  is `Mytools > Build Material Collection`. Generated resources and DF raw
  snapshots under `Assets/Resources` are intentionally ignored by Git.
* `Assets/Scripts/Editor/ProjectValidation.cs` provides repeatable offline content
  preparation. `SessionReloadValidation.cs` in the same folder checks a live
  session with normal reloads enabled. They are separate from runtime loading.
* `Assets/Scripts/Editor/BuildPipeline.cs` contains the legacy `BuildFactory`
  release commands and protocol generation. Windows release builds explicitly
  select Map Mode, prepare materials, and package extras from `ReleaseFiles`.
  `Assets/Plugins/BuildSettings.cs` supplies release configuration. The default
  committed `ProjectSettings/EditorBuildSettings.asset` still lists SampleScene;
  local build profiles can override it. The release command requires build
  success before packaging; Unity's debugging
  backup is excluded from the archive. The Windows release player has a recorded
  live check; other platforms remain unverified.
* `Assets/MeshSerializer` contains the mesh export data/component and its editor
  inspector. JSON and XML exports use data contract serializers to preserve
  readonly `CPUMesh` fields. The old BinaryFormatter `.avmesh` output is retired;
  format compatibility is described in `Docs/Development.md`.
  `Assets/Scripts/Collada-IO` and `Assets/OBJ-IO` contain separate Collada and OBJ
  facilities. `Assets/Armok/Runtime/Export` contains `MapScreenshot` (F9 view /
  modified F9 minimap capture) and `MapDataExport` (material/item/creature JSON
  and CSV, building/tile/plant CSV). GameMap retains the shortcut/settings
  triggers and supplies definitions and paths. The unused commented mesh-export
  prototype was removed; the separate Collada and OBJ tools remain separate.
* `Assets/StreamingAssets` contains runtime content files. `Assets/Resources`,
  `Materials`, `Shaders`, `Meshes`, `Textures`, and `Prefabs` contain Unity assets;
  the latter names are folders directly under `Assets`. Root `Content` contains
  source art/mesh files outside Unity's imported asset tree. Preserve asset
  `.meta` files and GUIDs when moving assets.
* `ProjectSettings/ProjectVersion.txt` pins Unity `6000.6.0f1`.
  `Packages/manifest.json` and `Packages/packages-lock.json` record Unity package
  dependencies. Git LFS stores required binary payloads. The sibling upstream
  checkout is not a build dependency; dependency provenance is tracked in
  `Docs/ThirdParty.md`.
* `Library`, `Temp`, `Logs`, `UserSettings`, `TestResults`, and generated build
  output are disposable/local. The root `lfs` folder is ignored recovery staging,
  not the Git LFS object store or a required source directory.

### Verification And Documentation

* `Assets/Armok/Tests/EditMode` (`Armok.Content.Tests`) covers texture resizing and
  sprite cache correctness, invalidation, and failure recovery.
* `Assets/Armok/Tests/PlayMode` (`Armok.Input.Tests`) covers modern actions with
  synthetic input devices.
* `Assets/Armok/Tests/Lifecycle` (`Armok.Lifecycle.Tests`) covers session ordering,
  cancellation, meshing shutdown, and DFHack transport failure cases using a
  controlled local peer; it needs no running DF game.
* `Assets/Armok/Tests/Editor` (`Armok.Tests.Editor`) contains `ValidationRunner`
  and live DF scene checks. The live tests exercise repeated Play, content and
  rendering, inspection, retry, and worker shutdown against a running fortress.
  Live and graphics-dependent checks must be distinguished from offline checks.
* `Tools/VerifyCheckout.ps1` checks dependency payloads, LFS placeholders, asset
  metadata, ignored source, and required input/rendering configuration.
  `Tools/RunUnityValidation.ps1` runs content preparation, selected test suites,
  or the normal-reload live check in a closed project or separate checkout and
  records results and the Unity process exit code. It must not launch a second
  editor on an already-open checkout.
* `Docs/Development.md` describes checkout, configuration, export formats, and
  build verification. `Docs/Testing.md` describes test menus, commands, fixtures,
  and limitations. `Docs/SessionLifecycle.md`, `Docs/MapInspection.md`, and
  `Docs/ThirdParty.md` document their corresponding boundaries and dependencies.
* `CurrentTasks.md` is the maintenance roadmap and verification record. Phases
  0-2 are complete; Phase 3 has begun with inspection and legacy HUD retirement.
  Coordinates, screenshots/data exports and the loaded content catalog are now
  extracted. Further map input/presentation separation, editor preparation,
  folder/assembly restructuring and final regression work remain. Do not describe
  planned modules as existing architecture.
* `README.md` retains upstream project context; use the restoration documents
  above for the current checkout and workflow. Keep this architecture section
  current when responsibilities or major repository paths change.
