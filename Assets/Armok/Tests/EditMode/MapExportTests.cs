using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Armok.Tests
{
    public class MapExportTests
    {
        private string directory;
        private static Type Legacy(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static object Message(string name) => Activator.CreateInstance(Legacy("RemoteFortressReader." + name));
        private static void Set(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);
        private static void Export(string method, object data, string path) => Legacy("MapDataExport").GetMethod(method).Invoke(null, new[] { data, path });

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "ArmokExport-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, true);

        [Serializable] private class MaterialFile { public MaterialRow[] list; }
        [Serializable] private class MaterialRow { public string name; public string id; public Pair mat_pair; }
        [Serializable] private class Pair { public int mat_type; public int mat_index; }

        [Test]
        public void MaterialCsvAndJsonKeepRowsIdentifiersAndUnicode()
        {
            var definition = Message("MaterialDefinition");
            Set(definition, "name", "café stone");
            Set(definition, "id", "INORGANIC:TEST");
            var pair = Message("MatPair");
            Set(pair, "mat_type", 0); Set(pair, "mat_index", 42);
            Set(definition, "mat_pair", pair);
            var keyType = Legacy("MatPairStruct");
            var data = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, definition.GetType()));
            data.Add(Activator.CreateInstance(keyType, 0, 42), definition);
            string path = Path.Combine(directory, "MaterialList.csv");
            Export("SaveMaterialList", data, path);
            Assert.That(File.ReadAllText(path), Is.EqualTo("\"café stone\",\"INORGANIC:TEST\",0,42" + Environment.NewLine));
            var json = JsonUtility.FromJson<MaterialFile>(File.ReadAllText(Path.ChangeExtension(path, "json")));
            Assert.That(json.list, Has.Length.EqualTo(1));
            Assert.That(json.list[0].name, Is.EqualTo("café stone"));
            Assert.That(json.list[0].id, Is.EqualTo("INORGANIC:TEST"));
            Assert.That(json.list[0].mat_pair.mat_type, Is.Zero);
            Assert.That(json.list[0].mat_pair.mat_index, Is.EqualTo(42));
            data.Clear();
            Export("SaveMaterialList", data, path);
            Assert.That(File.ReadAllText(path), Is.Empty);
            Assert.That(JsonUtility.FromJson<MaterialFile>(File.ReadAllText(Path.ChangeExtension(path, "json"))).list, Is.Empty);
        }

        [Test]
        public void BuildingCsvKeepsQuotedNamesAndColonSeparatedType()
        {
            var definition = Message("BuildingDefinition");
            Set(definition, "name", "test building"); Set(definition, "id", "TEST");
            var kind = Message("BuildingType");
            Set(kind, "building_type", 4); Set(kind, "building_subtype", -1); Set(kind, "building_custom", 8);
            Set(definition, "building_type", kind);
            var list = Message("BuildingList");
            ((IList)list.GetType().GetProperty("building_list").GetValue(list)).Add(definition);
            string path = Path.Combine(directory, "BuildingList.csv");
            Export("SaveBuildingList", list, path);
            Assert.That(File.ReadAllText(path), Is.EqualTo("\"test building\",\"TEST\",4:-1:8" + Environment.NewLine));
            Export("SaveBuildingList", null, path);
            Assert.That(File.ReadAllText(path), Is.Not.Empty, "Absent definitions must leave an existing export alone.");
        }

        [Test]
        public void ScreenshotNumberingSkipsExistingFilesAndKeepsSessionCounter()
        {
            File.WriteAllText(Path.Combine(directory, "screenshot1.png"), "existing");
            var type = Legacy("MapScreenshot");
            var capture = Activator.CreateInstance(type);
            var next = type.GetMethod("NextFilename", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(next.Invoke(capture, new object[] { directory }), Is.EqualTo(Path.Combine(directory, "screenshot2.png")));
            Assert.That(next.Invoke(capture, new object[] { directory }), Is.EqualTo(Path.Combine(directory, "screenshot3.png")));
            Assert.That(File.ReadAllText(Path.Combine(directory, "screenshot1.png")), Is.EqualTo("existing"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MinimapCaptureRestoresRenderTargetAndReleasesPhotoEvenWhenWritingFails(bool fail)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("Requires a graphics device.");
            var source = new RenderTexture(4, 4, 0);
            var prior = new RenderTexture(2, 2, 0);
            var original = RenderTexture.active;
            Texture2D decoded = null;
            try
            {
                source.Create(); prior.Create();
                RenderTexture.active = source;
                GL.Clear(false, true, Color.red);
                RenderTexture.active = prior;
                int textures = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
                string path = fail ? Path.Combine(directory, "missing", "image.png") : Path.Combine(directory, "image.png");
                var save = Legacy("MapScreenshot").GetMethod("SaveRenderTexture", BindingFlags.NonPublic | BindingFlags.Static);
                if (fail)
                    Assert.That(Assert.Throws<TargetInvocationException>(() => save.Invoke(null, new object[] { source, path })).InnerException, Is.TypeOf<DirectoryNotFoundException>());
                else
                    save.Invoke(null, new object[] { source, path });
                Assert.That(RenderTexture.active, Is.SameAs(prior));
                Assert.That(Resources.FindObjectsOfTypeAll<Texture2D>().Length, Is.EqualTo(textures));
                if (!fail)
                {
                    decoded = new Texture2D(2, 2);
                    Assert.That(decoded.LoadImage(File.ReadAllBytes(path)), Is.True);
                    Assert.That(decoded.width, Is.EqualTo(4));
                    Assert.That(decoded.GetPixel(0, 0).r, Is.GreaterThan(0.99f));
                    Assert.That(decoded.GetPixel(0, 0).g, Is.LessThan(0.01f));
                }
            }
            finally
            {
                RenderTexture.active = original;
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(prior);
                if (decoded != null) UnityEngine.Object.DestroyImmediate(decoded);
            }
        }
    }
}
