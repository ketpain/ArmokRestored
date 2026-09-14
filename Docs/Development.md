# Development and verification

## Restore a checkout

1. Install Git LFS and run `git lfs install` for your user.
2. Clone the ArmokRestored repository, then run `git lfs pull` in the checkout. No submodule initialization is required; see [dependency provenance](ThirdParty.md).
3. On Windows, run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/VerifyCheckout.ps1`.
4. Open the checkout with the exact editor recorded in `ProjectSettings/ProjectVersion.txt` (currently `6000.6.0f1`). Allow Unity Package Manager and asset imports to finish.
5. Open `Assets/Scenes/Map Mode.unity`. Missing material arrays and definitions are generated before entering Play Mode. `Mytools > Build Material Collection` explicitly rebuilds them after material source changes and is also used by the release build command.

Use the restored project's repository URL when it is published. The historical upstream clone command in the older contributor documentation describes upstream Armok Vision, not a checkout containing these restoration changes. A local baseline can also be cloned from its filesystem path; verify LFS payloads in the resulting checkout before opening Unity.

Track source, asset `.meta` files, packages and their lock file, and project settings. `Library`, `Temp`, `Logs`, `UserSettings`, build output, and the root `lfs/` recovery staging directory are disposable/local. The real LFS object store is managed by Git under `.git/lfs`.

The generated `shapeTextures`, `patternTextures`, `materialDefinitions`, and DF raw resource snapshots are intentionally excluded from Git. Required material resources must be rebuildable from the tracked source assets. Generated creature graphics in `Application.persistentDataPath/Cache/CreatureSprites` are also disposable; live DF data is never restored from that cache.

## Required project configuration

- Keep Active Input Handling set to Input System Package (New).
- Keep Built-in rendering selected in Graphics settings and every Quality preset. Existing terrain/material surface shaders need this configuration.
- Preserve the current reload-disabled Play Mode configuration while validating repeat sessions; explicitly test other reload modes when changing lifecycle code.

## Verification procedure

Record editor version, revision, scene, quality preset, DF/DFHack/plugin versions, the fixture used, and whether the creature cache was cold or warm. A live fixture can be identified locally without committing a personal save.

1. Run `Tools/VerifyCheckout.ps1`, then `git lfs fsck` after committing/restoring the revision being checked.
2. Confirm editor compilation finishes without errors. Fresh-checkout validation must use a separate checkout with no copied `Library` or local validation helpers.
3. Verify missing material resources can be generated, then run the maintained [regression tests](Testing.md).
4. With a suitable DF fortress open and DFHack available, enter Map Mode. Confirm connection, material preparation, terrain and creatures, camera controls, pointer/UI input, and absence of unexpected exceptions.
5. Stop and start Play at least three times. Check that loading callbacks do not multiply, no previous session continues polling/meshing, and stopping produces no new exceptions. Record reload settings and restore any temporary editor options.
6. For content changes, record cold-cache and warm-cache stage timings separately. Compare sprite pixels at all mip levels against the same fixture when changing texture generation. The content timer excludes imports, compilation, and initial connection and includes yielded frames.
7. Use `Mytools > Build Release > Windows` for the supported release path. Check the actual player build result and run the produced player against DF. Until the Phase 4 build-result fix lands, the legacy release command may continue packaging after a build failure; an archive alone is not evidence of success.

Checks needing a graphics device or a live DF process must be reported separately from offline tests. Do not report platforms, player behavior, or live integration as verified based only on editor compilation.

For a separate checkout, run Unity with `-batchmode -quit -projectPath <checkout> -executeMethod Armok.Editor.ProjectValidation.PrepareContent -logFile <log-path>` to compile and rebuild the material resources without DF. Supply the full path to the pinned Unity editor executable. Use a graphics-capable environment, and check both the process exit code and `Logs/project-validation.json` in that checkout. Do not open the same checkout in two editors simultaneously.

## MeshSerializer editor exports

The `MeshSerializer` inspector exports `.json` with `DataContractJsonSerializer` or `.xml` with `DataContractSerializer`. Both preserve the `[Serializable]` mesh data, including `CPUMesh`'s readonly arrays, vertex count, name, layer, and rotation. JSON stores enum values as numbers; XML uses the data contract's element names and namespaces. Export streams are closed before the operation returns.

The former BinaryFormatter `.avmesh` output was retired on 2026-09-14. There is no `.avmesh` reader or stored `.avmesh` asset in this project. JSON is a new format, not a compatible replacement for existing binary files; renaming an old `.avmesh` file will not convert it. The XML export now uses a data contract instead of the previous `XmlSerializer`, which cannot serialize the current `CPUMesh` class without a parameterless constructor. External tools expecting that old XML layout would need updating. These inspector exports are separate from the runtime content loaders and OBJ exports.

API references: [Unity named build targets](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Build.NamedBuildTarget.FromBuildTargetGroup.html) and [Microsoft data contract JSON serialization](https://learn.microsoft.com/en-us/dotnet/framework/wcf/feature-details/stand-alone-json-serialization).

## Original restoration measurements

The 2026-09-13 validation used Unity 6000.6.0f1 and a DF 53.16 / DFHack 53.16-r1.1 / RemoteFortressReader 0.21.0 connection. It produced 4,271 sprites over 39 pages. Content loading measured approximately 10 seconds warm and 12 seconds with an empty creature cache; the original measurement was approximately 167 seconds. All color/normal mip pixels matched the original output. These figures describe that run, not a portable timing threshold.

Local historical reports remain under `Logs/LoadingPerformance-20260913-173013`. Fresh checkouts and maintained tests must not depend on that directory.
