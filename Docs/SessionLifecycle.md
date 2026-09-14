# Session lifecycle

`Armok.Lifecycle` contains the state machine, nested loading operation, suspension scope, polling worker, and shared shutdown deadline. It has no Unity references. `SessionRuntime` composes these with the existing scene components in `Assembly-CSharp`; it is created automatically before scene startup and requires no prefab or scene changes.

## Startup dependencies

1. Scene `Awake` prepares the shape/special texture stores and registers content managers. `Start` registers the loader and map presentation. Registrations return disposable handles owned by those components. The session also registers `ISessionContentParticipant` components after scene loading: `ExecuteInEditMode` managers may otherwise skip their runtime registration when reloads are disabled. Each participant registers once per coordinator, including after retry. Map Mode consistently has five content callbacks in both reload modes.
2. `DFConnection` enters **Connecting**, opens the DFHack connection, checks the plugin, binds RPCs, and fetches materials, item types, tile types, buildings, creature/plant raws, and initial view/world data. Token lists and `MapDataStore` are initialized from these definitions. Plugin confirmation can keep this stage pending.
3. Definition callbacks dispatch in `Update`, after all scene `Start` methods have run. The loader enters **LoadingContent**. It parses the external content index, builds shape/special arrays and creature sprites, then matches the material collection against DF material IDs. Registered building/layered sprite/item/image loaders run after this shared preparation. Their registrations depend on which managers the scene contains.
4. Only after all nested enumerators finish successfully does the loader publish `ContentLoader.Instance` and enter **Ready**. Map and distant terrain presentation start from ready callbacks. The mesher captures its content owner at construction. DF polling starts at this point, in either its background worker or the existing main-thread mode.

The loader reports stage text and progress through `SessionCoordinator.Changed`. Its existing serialized progress bars are a presentation adapter. It no longer calls the map's help-panel methods. Detailed sub-progress bars remain local to the existing content managers; splitting the content catalog and editor preview preparation belongs to Phases 3–4.

`LoadOperation` owns the complete stack of nested enumerators. A child exception produces a failed result and disposes parent/child enumerators. Cancellation does the same without publishing content as ready. Child routines are yielded directly, so Unity cannot log a child exception while silently leaving the parent load waiting.

## Failure and retry

Connection/setup failures, content failures, and polling/meshing failures enter **Stopping** and then **Failed** after cleanup. The error is retained, loading/presentation stops, and a Retry/Quit panel explains the failure. A missing loaded DF world and an older plugin retain their explicit confirmation prompts during Connecting.

Retry is deliberate and reloads the current scene. It creates a new coordinator only after old workers finish. Old components retain their old coordinator and disposable registrations, so their destruction cannot stop or clear the new session. New managers receive fresh RPC bindings, buffers, content callbacks, and map state. Content/cache formats and input/render pipeline settings are unchanged.

Subsystem registration resets session state for both normal Play reloads and the project's disabled domain/scene reload settings. Exiting Play, quitting, or destroying the connection/content/map owner requests session shutdown. Cleanup compares owner identity before clearing shared state.

## Worker and transport shutdown

- Every worker is registered with the session. All receive a stop request before any join begins. The main thread shares a **500 ms** wait budget across the network worker and all meshing threads, rather than spending that time per thread.
- Idle meshing threads wait on their queue and are awakened by new work or cancellation. Active jobs check cancellation between tiles/vertices; cancelled results are not published. Statistics and worker faults are handled on the main thread.
- The polling worker's refresh delay is interruptible. Stop closes the owned socket without sending an interleaved RPC, waking blocked receives. The worker closes its own transport in `finally`, including after a polling exception. It never clears a newer connection's field.
- If the shared deadline expires, the coordinator remains **Stopping**, retains worker references and deferred cleanup, and observes completion in subsequent updates. Retry is unavailable until cleanup finishes. A warning reports this exceptional case. A new Play session refuses to overlap retained workers.
- Main-thread polling has no separate thread to join; it uses the same exception-safe suspension and bounded transport as background polling.
- Numeric addresses bypass DNS. Hostname lookup and connection attempts are bounded; normal socket I/O uses a 5-second timeout. Graceful disconnect has a short send timeout and always closes the socket. Forced interruption does not send a Quit frame into an in-flight RPC.

No shutdown path uses `Thread.Abort`.

## DF suspension is separate from loading

`SuspendedOperation` requires a successful suspend acknowledgement, executes a batch, and attempts exactly one resume, including when the batch throws. A batch failure is preserved; if resume also fails, both errors are retained. Unconfirmed resume closes the transport and fails the session. The client cannot claim the server resumed without an acknowledgement: the server owns cleanup of its client-scoped suspension when the connection closes. Deliberate cancellation also closes that connection, and interruption exceptions are not treated as a new polling failure.

Offline tests use a controlled DFHack protocol peer to exercise rejected setup, invalid/stalled handshakes, exceptions inside a suspended batch, missing resume acknowledgements, and cancellation during a blocked receive. The peer verifies that the client actually disconnects; it does not substitute for live DFHack verification.

## Validation

Run `Mytools/Tests/Run Lifecycle Tests` for offline lifecycle/transport checks, and `Mytools/Tests/Run Live DF Smoke Test` with a loaded DF save for scene/rendering checks. The content and input suites remain separate. See `Docs/Testing.md` for command-line execution.
