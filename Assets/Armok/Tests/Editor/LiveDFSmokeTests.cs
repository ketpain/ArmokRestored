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
                double deadline = EditorApplication.timeSinceStartup + 180;
                while (completedLoads < iteration && EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.That(completedLoads, Is.EqualTo(iteration), "Each Play session must finish exactly one content load.");
                Assert.That(Singleton("ContentLoader") as Object, Is.Not.Null);
                Assert.That((bool)LegacyType("DFConnection").GetProperty("Connected").GetValue(null), Is.True);

                var callbacks = (IList)LegacyType("ContentLoader").GetField("LoadCallbacks", Fields).GetValue(null);
                Assert.That(callbacks.Cast<Delegate>().Distinct().Count(), Is.EqualTo(callbacks.Count), "Duplicate loading callback.");
                if (firstCallbackCount < 0) firstCallbackCount = callbacks.Count;
                Assert.That(callbacks.Count, Is.EqualTo(firstCallbackCount), "Callback registrations grew between sessions.");
                CaptureWorkers();

                // Allow some map rendering after content completion before checking shaders.
                for (int frame = 0; frame < 10; frame++) yield return null;
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

                yield return new ExitPlayMode();
                deadline = EditorApplication.timeSinceStartup + 5;
                while (workers.Any(worker => worker.IsAlive) && EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.That(workers.Any(worker => worker.IsAlive), Is.False, "A session worker survived exiting Play Mode.");
                Assert.That((bool)LegacyType("DFConnection").GetProperty("Connected").GetValue(null), Is.False);
                messages.Add($"Session {iteration}: callbacks={callbacks.Count}; all captured workers stopped.");
                workers.Clear();
            }
        }

        // Characterization bridge while the legacy scene components remain in Assembly-CSharp.
        // Replace this bridge with the session API as that boundary is extracted.
        private static Type LegacyType(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static object Singleton(string name) => LegacyType(name).GetProperty("Instance", Fields).GetValue(null);
        private static object Field(object owner, string name) => owner?.GetType().GetField(name, Fields)?.GetValue(owner);

        private void CaptureWorkers()
        {
            object manager = Field(Singleton("DFConnection"), "connectionManager");
            if (Field(manager, "connectionThread") is Thread connectionWorker) workers.Add(connectionWorker);
            object mesher = Field(Singleton("GameMap"), "mesher");
            if (Field(mesher, "threads") is Thread[] meshWorkers) workers.AddRange(meshWorkers.Where(worker => worker != null));
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
