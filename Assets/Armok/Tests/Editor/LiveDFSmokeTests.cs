using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Armok.Lifecycle;

// A separate namespace/category keeps live DF checks out of the default offline filter.
namespace Armok.LiveTests
{
    [Category("LiveDF")]
    public class LiveDFSmokeTests
    {
        private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private SceneSetup[] sceneSetup;
        private bool reloadOptionsEnabled;
        private EnterPlayModeOptions reloadOptions;
        private bool runInBackground;
        private int completedLoads;
        private int? originalMeshThreads;
        private readonly List<string> messages = new List<string>();
        private readonly List<Thread> workers = new List<Thread>();

        [SetUp]
        public void SetUp()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    Assert.Ignore("Save open scenes before running the live scene smoke test.");
            using (var probe = new TcpClient())
            {
                var connect = probe.ConnectAsync("127.0.0.1", 5000);
                try { if (!connect.Wait(1000) || !probe.Connected) Assert.Ignore("Start DF with DFHack on localhost:5000 first."); }
                catch (AggregateException) { Assert.Ignore("Start DF with DFHack on localhost:5000 first."); }
            }
            sceneSetup = EditorSceneManager.GetSceneManagerSetup();
            reloadOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            reloadOptions = EditorSettings.enterPlayModeOptions;
            runInBackground = Application.runInBackground;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            Application.runInBackground = true;
            messages.Clear(); workers.Clear(); completedLoads = 0;
            Application.logMessageReceived += CaptureLog;
            EditorApplication.update += Pump;
            Directory.CreateDirectory("TestResults");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (sceneSetup == null) yield break;
            RestoreMeshingSettings();
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            Application.logMessageReceived -= CaptureLog;
            EditorApplication.update -= Pump;
            Application.runInBackground = runInBackground;
            EditorSettings.enterPlayModeOptionsEnabled = reloadOptionsEnabled;
            EditorSettings.enterPlayModeOptions = reloadOptions;
            File.WriteAllLines("TestResults/live-stages.txt", messages);
            // The Test Runner can begin with an unnamed empty scene, which has
            // no restorable SceneSetup. It restores the user's scenes afterward.
            if (sceneSetup.Any(scene => scene.isLoaded) && sceneSetup.Count(scene => scene.isActive) == 1)
                EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
            else
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            sceneSetup = null;
        }

        private void CaptureLog(string message, string trace, LogType type)
        {
            if (message.StartsWith("Asset loading [Total]:")) completedLoads++;
            if (message.StartsWith("Asset loading [") || message.StartsWith("Creature sprite cache:") ||
                message.StartsWith("Connected to DF version") || type == LogType.Error || type == LogType.Exception)
                messages.Add(message);
        }

        private static void Pump()
        {
            if (EditorApplication.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
        }

        [UnityTest]
        public IEnumerator ThreePlaySessionsPreserveSpritesAndReleaseWorkers()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Map Mode.unity", OpenSceneMode.Single);
            string firstSprites = null;
            int firstCallbackCount = -1;
            for (int iteration = 1; iteration <= 3; iteration++)
            {
                yield return new EnterPlayMode(false);
                ConfigureWorkers(iteration != 2, iteration == 2 ? 0 : iteration == 3 ? 2 : 4);
                double deadline = EditorApplication.timeSinceStartup + 180;
                while (completedLoads < iteration && EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.That(completedLoads, Is.EqualTo(iteration), "Each Play session must finish exactly one content load.");
                Assert.That(Singleton("ContentLoader") as Object, Is.Not.Null);
                Assert.That((bool)LegacyType("DFConnection").GetProperty("Connected").GetValue(null), Is.True);

                var session = (SessionCoordinator)LegacyType("SessionRuntime").GetProperty("Current").GetValue(null);
                Assert.That(session.Stage, Is.EqualTo(SessionStage.Ready));
                int callbackCount = session.ContentCallbackCount;
                if (firstCallbackCount < 0) firstCallbackCount = callbackCount;
                Assert.That(callbackCount, Is.EqualTo(firstCallbackCount), "Callback registrations grew between sessions.");

                // Allow some map rendering after content completion before checking shaders.
                for (int frame = 0; frame < 10; frame++) yield return null;
                deadline = EditorApplication.timeSinceStartup + 30;
                while (!HasMapGeometry() && session.IsReady && EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.That(HasMapGeometry(), Is.True, "The session reached Ready but produced no active map geometry.");
                yield return CheckInspection(iteration);
                CaptureWorkers();
                string sprites = SpriteHashes();
                Assert.That(sprites, Is.Not.Empty, "The live fixture did not produce creature pages.");
                if (firstSprites == null) firstSprites = sprites;
                Assert.That(sprites, Is.EqualTo(firstSprites), "Sprite pixels or selection changed between identical-fixture sessions.");
                File.WriteAllText($"TestResults/live-{iteration}-sprites.txt", sprites);
                foreach (var shader in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude)
                    .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null)
                    .Select(material => material.shader).Distinct())
                {
                    Assert.That(shader.isSupported, Is.True, shader.name);
                    Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, shader.name);
                }

                RestoreMeshingSettings();
                yield return new ExitPlayMode();
                deadline = EditorApplication.timeSinceStartup + 5;
                while (workers.Any(worker => worker.IsAlive) && EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.That(workers.Any(worker => worker.IsAlive), Is.False, "A session worker survived exiting Play Mode.");
                Assert.That((bool)LegacyType("DFConnection").GetProperty("Connected").GetValue(null), Is.False);
                Assert.That(session.Stage, Is.EqualTo(SessionStage.Disconnected));
                Assert.That(session.ContentCallbackCount, Is.Zero);
                messages.Add($"Session {iteration}: callbacks={callbackCount}; all captured workers stopped.");
                workers.Clear();
            }
        }

        [UnityTest]
        public IEnumerator InterruptedLoadCanRetryAndPollingFailureCanStopTheNewSession()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Map Mode.unity", OpenSceneMode.Single);
            yield return new EnterPlayMode(false);
            var oldSession = (SessionCoordinator)LegacyType("SessionRuntime").GetProperty("Current").GetValue(null);
            Assert.That(oldSession.Stage, Is.EqualTo(SessionStage.LoadingContent));
            Assert.That(Singleton("ContentLoader") as Object, Is.Null);
            LegacyType("SessionRuntime").GetMethod("Stop").Invoke(null, new object[] { oldSession, null });
            Assert.That(oldSession.Stage, Is.EqualTo(SessionStage.Disconnected));
            Assert.That(oldSession.ContentCallbackCount, Is.Zero);
            Assert.That((ISessionWorker[])LegacyType("SessionRuntime").GetProperty("Workers").GetValue(null), Is.Empty);

            LegacyType("SessionRuntime").GetMethod("Retry").Invoke(null, null);
            var newSession = (SessionCoordinator)LegacyType("SessionRuntime").GetProperty("Current").GetValue(null);
            Assert.That(newSession, Is.Not.SameAs(oldSession));
            double deadline = EditorApplication.timeSinceStartup + 180;
            while (!newSession.IsReady && newSession.Stage != SessionStage.Failed && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(newSession.Stage, Is.EqualTo(SessionStage.Ready), newSession.Failure?.ToString());
            for (int frame = 0; frame < 10; frame++) yield return null;
            CaptureWorkers();

            // Interrupt the actual socket under the background polling worker.
            object connection = Singleton("DFConnection");
            object client = Field(connection, "networkClient");
            client.GetType().GetMethod("Interrupt").Invoke(client, null);
            deadline = EditorApplication.timeSinceStartup + 10;
            while (newSession.Stage != SessionStage.Failed && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(newSession.Stage, Is.EqualTo(SessionStage.Failed));
            Assert.That(workers.Any(worker => worker.IsAlive), Is.False);
            Assert.That(Field(connection, "networkClient"), Is.Null);
            Assert.That(newSession.ContentCallbackCount, Is.Zero);
            Assert.That(oldSession.Stage, Is.EqualTo(SessionStage.Disconnected), "Old owner interfered with retry.");
            yield return new ExitPlayMode();
        }

        private IEnumerator CheckInspection(int iteration)
        {
            var inspectorType = LegacyType("CursorInspection");
            var inspector = Object.FindObjectsByType(inspectorType, FindObjectsInactive.Exclude, FindObjectsSortMode.None).Single() as Component;
            var camera = inspector.GetComponent<Camera>();
            var inspect = inspectorType.GetMethod("InspectPointer");
            var selection = inspectorType.GetProperty("Selection");
            var pinned = inspectorType.GetProperty("IsPinned");
            var debug = Field(Singleton("GameSettings"), "debug");
            var debugFlag = debug.GetType().GetField("drawDebugInfo");
            bool wasDebug = (bool)debugFlag.GetValue(debug);
            try
            {
                debugFlag.SetValue(debug, false);
                var rect = camera.pixelRect;
                Vector2 point = default;
                // Find a visible map hit without depending on this particular fortress layout.
                double deadline = EditorApplication.timeSinceStartup + 30;
                while (selection.GetValue(inspector) == null && EditorApplication.timeSinceStartup < deadline)
                {
                    for (int y = 1; y < 10 && selection.GetValue(inspector) == null; y++)
                        for (int x = 1; x < 10 && selection.GetValue(inspector) == null; x++)
                        {
                            point = new Vector2(rect.x + rect.width * x / 10, rect.y + rect.height * y / 10);
                            inspect.Invoke(inspector, new object[] { point, false, false });
                        }
                    if (selection.GetValue(inspector) == null) yield return null;
                }
                var selected = selection.GetValue(inspector);
                Assert.That(selected, Is.Not.Null, $"Hovering the rendered fortress did not select a tile. Camera={camera.transform.position}, rect={rect}, mapZ={LegacyType("GameMap").GetProperty("PosZ").GetValue(Singleton("GameMap"))}");
                inspect.Invoke(inspector, new object[] { point, true, false });
                Assert.That(pinned.GetValue(inspector), Is.EqualTo(true));
                inspect.Invoke(inspector, new object[] { new Vector2(-100, -100), false, false });
                Assert.That(selection.GetValue(inspector), Is.EqualTo(selected), "Pinned tile followed the pointer.");
                inspect.Invoke(inspector, new object[] { point, true, true });
                Assert.That(pinned.GetValue(inspector), Is.EqualTo(true), "A UI click released the pinned tile.");
                yield return null;
                yield return null;
                var card = (RectTransform)Field(inspector, "card");
                Assert.That(card.gameObject.activeInHierarchy, Is.True);
                var labels = card.GetComponentsInChildren<UnityEngine.UI.Text>();
                Assert.That(labels.Any(label => label.text.Contains("X ") && label.text.Contains("Z ")), Is.True);
                Assert.That(labels.Any(label => label.name == "Details" && !string.IsNullOrEmpty(label.text)), Is.True);
                Assert.That(card.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
                Assert.That(card.GetComponentsInChildren<UnityEngine.UI.Graphic>().All(graphic => !graphic.raycastTarget), Is.True);
                var eventSystem = UnityEngine.EventSystems.EventSystem.current;
                var previous = eventSystem.currentSelectedGameObject;
                var button = new GameObject("Inspection focus test", typeof(RectTransform), typeof(UnityEngine.UI.Button));
                try
                {
                    eventSystem.SetSelectedGameObject(button);
                    Assert.That(LegacyType("MapInputUI").GetProperty("HasKeyboardFocus").GetValue(null), Is.EqualTo(false),
                        "A selected button must not lock out camera movement.");
                }
                finally
                {
                    eventSystem.SetSelectedGameObject(previous);
                    Object.DestroyImmediate(button);
                }
                foreach (string path in new[] { "Canvas/Dwarf UI", "Canvas/Adventure UI", "Canvas/Clock Panel", "Canvas/Properties", "DwarfModeMenu", "AdventureManager" })
                    Assert.That(GameObject.Find(path), Is.Null, "Retired UI reactivated: " + path);
                if (iteration == 1)
                {
                    ScreenCapture.CaptureScreenshot("TestResults/inspection.png");
                    yield return null;
                    yield return null;
                }
                inspect.Invoke(inspector, new object[] { new Vector2(-100, -100), true, false });
                Assert.That(pinned.GetValue(inspector), Is.EqualTo(false));
                Assert.That(selection.GetValue(inspector), Is.Null, "Clicking empty space retained stale tile data.");
                inspect.Invoke(inspector, new object[] { point, false, true });
                Assert.That(selection.GetValue(inspector), Is.Null, "Hovering UI selected the map underneath it.");
                messages.Add($"Session {iteration}: hover, pin/release, passive inspection HUD and retired panels verified with debug disabled.");
            }
            finally { debugFlag.SetValue(debug, wasDebug); }
        }

        private void ConfigureWorkers(bool backgroundPolling, int meshThreads)
        {
            var meshing = Field(Singleton("GameSettings"), "meshing");
            if (!originalMeshThreads.HasValue) originalMeshThreads = (int)Field(meshing, "meshingThreads");
            meshing.GetType().GetField("meshingThreads").SetValue(meshing, meshThreads);
            LegacyType("DFConnection").GetField("RunOnAlternateThread").SetValue(Singleton("DFConnection"), backgroundPolling);
        }

        private void RestoreMeshingSettings()
        {
            if (!originalMeshThreads.HasValue) return;
            var meshing = Field(Singleton("GameSettings"), "meshing");
            meshing.GetType().GetField("meshingThreads").SetValue(meshing, originalMeshThreads.Value);
            originalMeshThreads = null;
        }

        // Characterization bridge while the legacy scene components remain in Assembly-CSharp.
        // Replace this bridge with the session API as that boundary is extracted.
        private static Type LegacyType(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static object Singleton(string name) => LegacyType(name).GetProperty("Instance", Fields).GetValue(null);
        private static object Field(object owner, string name) => owner?.GetType().GetField(name, Fields)?.GetValue(owner);

        private static bool HasMapGeometry()
        {
            var blockType = LegacyType("BlockMeshSet");
            return Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude).Any(filter =>
                filter.sharedMesh != null && filter.sharedMesh.vertexCount > 0 && filter.GetComponentInParent(blockType) != null);
        }

        private void CaptureWorkers()
        {
            var owned = (ISessionWorker[])LegacyType("SessionRuntime").GetProperty("Workers").GetValue(null);
            Assert.That(owned.Length, Is.GreaterThan(0), "No registered session workers; shutdown coverage was lost.");
            object connection = Singleton("DFConnection");
            var polling = Field(connection, "connectionWorker") as PollingWorker;
            Assert.That(polling != null, Is.EqualTo((bool)Field(connection, "RunOnAlternateThread")));
            if (polling != null) workers.Add(polling.Thread);
            object mesher = Field(Singleton("GameMap"), "mesher");
            Assert.That(mesher, Is.Not.Null);
            int expectedThreads = (int)Field(Field(Singleton("GameSettings"), "meshing"), "meshingThreads");
            var meshWorkers = Field(mesher, "threads") as Thread[];
            Assert.That(meshWorkers?.Length ?? 0, Is.EqualTo(expectedThreads));
            if (meshWorkers != null) workers.AddRange(meshWorkers);
        }

        private static string SpriteHashes()
        {
            object loader = Singleton("ContentLoader");
            object manager = loader.GetType().GetProperty("SpriteManager").GetValue(loader);
            var pages = (IEnumerable)Field(manager, "tilePages");
            var result = new StringBuilder();
            foreach (object page in pages)
            {
                Type type = page.GetType();
                int count = (int)type.GetProperty("Count").GetValue(page);
                if (count == 0) continue;
                result.AppendLine(type.GetProperty("Name").GetValue(page) + ":" + count +
                    ":color=" + Hash((Texture2DArray)type.GetProperty("TileArray").GetValue(page)) +
                    ":normal=" + Hash((Texture2DArray)type.GetProperty("NormalArray").GetValue(page)));
            }
            return result.ToString();
        }

        private static string Hash(Texture2DArray texture)
        {
            using (var hash = SHA256.Create())
            {
                for (int slice = 0; slice < texture.depth; slice++)
                    for (int mip = 0; mip < texture.mipmapCount; mip++)
                    {
                        var pixels = texture.GetPixels32(slice, mip);
                        var bytes = new byte[pixels.Length * 4];
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            bytes[i * 4] = pixels[i].r; bytes[i * 4 + 1] = pixels[i].g;
                            bytes[i * 4 + 2] = pixels[i].b; bytes[i * 4 + 3] = pixels[i].a;
                        }
                        hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                    }
                hash.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(hash.Hash).Replace("-", "");
            }
        }
    }
}
