# Dependency provenance

## RemoteClientDF-Net

`Assets/RemoteClientDF-Net` is a vendored source dependency, committed with this project. It is not a Git submodule and needs no separate checkout.

- Upstream: https://github.com/JapaMala/RemoteClientDF-Net
- Revision: `b5cac3abd8f93f281ece7907b152b0bdea6f7fb2`
- License: MIT; retained at `Assets/RemoteClientDF-Net/LICENSE`.
- Baseline comparison: all 56 upstream files match in text content after normalizing line endings and text encoding. Unity metadata is retained. No source behavior changes were found during the 2026-09-13 baseline audit.
- Future modifications should be recorded here and reviewed against that revision. Do not replace the directory with a fresh upstream checkout without preserving local changes and asset GUIDs.

Local Phase 2 changes: `DFHack/RemoteClient.cs` now bounds connection/I/O waits, bypasses reverse DNS for numeric addresses, disposes failed connection attempts, and supports socket interruption without writing into an active RPC. Disconnect is idempotent and the finalizer only closes the transport. `DFHack/RemoteFunctionBase.cs` retains a socket reference per call, sends complete frames, disposes its serialization stream, and reports transport failures as exceptions so the session stops instead of consuming missing results. Optional unavailable RPCs still return their protocol result. Existing file-level license notices and Unity GUIDs are retained; altered files are marked in source. See `Docs/SessionLifecycle.md` and the controlled-peer tests before updating this dependency.

Follow-up during cursor/HUD validation: an RPC started after socket interruption now throws its closed-connection exception without also writing an error through the output stream. This lets the session owner report the failure once or recognize deliberate shutdown; the exception is still propagated.

The revision was obtained from the reference Armok Vision checkout at `3027c785a54d7a8d9a7a9f7f2a10a1815c3bb500`, which pins this dependency. The sibling checkout is not needed to build ArmokRestored.

## Protocol generation

`Assets/RemoteClientLocal` contains local protocol definitions, generated C# messages, and client integration. `ProtoGen` contains the existing generator and its licensing files. Its executable and protobuf DLL are stored with Git LFS. Keep the generator and definitions together when regenerating messages; document protocol version changes separately from application refactors.

## Other dependencies

Unity packages are recorded in `Packages/manifest.json` and `Packages/packages-lock.json`. Bundled libraries and art retain their existing license files. A full inventory and unused-dependency audit is scheduled in `CurrentTasks.md`; this document does not yet claim to inventory every asset package.
