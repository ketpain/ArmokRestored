using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Armok.Tests
{
    public class ImagePreparationTests
    {
        private Type managerType;
        private Component manager;
        private object previousManager;
        private object items;
        private object creatures;
        private object previousItems;
        private object previousCreatures;
        private Texture previousColorAtlas;
        private Texture previousNormalAtlas;

        [SetUp]
        public void SetUp()
        {
            managerType = Type.GetType("ImageManager, Assembly-CSharp", true);
            previousManager = managerType.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            previousColorAtlas = Shader.GetGlobalTexture("_ImageAtlas");
            previousNormalAtlas = Shader.GetGlobalTexture("_ImageBumpAtlas");
            items = Type.GetType("ItemRaws, Assembly-CSharp", true).GetProperty("Instance").GetValue(null);
            creatures = Type.GetType("CreatureRaws, Assembly-CSharp", true).GetProperty("Instance").GetValue(null);
            previousItems = ReplaceWithEmptyList(items, "ItemList");
            previousCreatures = ReplaceWithEmptyList(creatures, "CreatureList");
            managerType.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            var instance = Object.Instantiate(Resources.Load<GameObject>("ImageManager"));
            manager = instance.GetComponent(managerType);
        }

        [TearDown]
        public void TearDown()
        {
            if (manager != null) Object.DestroyImmediate(manager.gameObject);
            managerType.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, previousManager);
            items.GetType().GetProperty("ItemList").SetValue(items, previousItems);
            creatures.GetType().GetProperty("CreatureList").SetValue(creatures, previousCreatures);
            Shader.SetGlobalTexture("_ImageAtlas", previousColorAtlas);
            Shader.SetGlobalTexture("_ImageBumpAtlas", previousNormalAtlas);
        }

        [Test]
        public void RebuildingGlyphPreviewsReleasesOldArraysAndPreservesSourcePixels()
        {
            var source = (Texture2D)managerType.GetField("dfSpriteMap").GetValue(manager);
            var pixels = source.GetPixels();
            int width = source.width;
            int height = source.height;
            int textureCount = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            int arrayCount = Resources.FindObjectsOfTypeAll<Texture2DArray>().Length;
            LoadImages();
            var color = Atlas("imageSpriteArray");
            var normal = Atlas("imageSpriteNormals");
            Assert.That(color.depth, Is.EqualTo(256));
            Assert.That(normal.depth, Is.EqualTo(256));
            var firstPixels = color.GetPixels(7);
            var firstNormals = normal.GetPixels(7);
            LoadImages();
            Assert.That(color == null && normal == null, Is.True, "A rebuild must release its previous arrays.");
            Assert.That(Atlas("imageSpriteArray").GetPixels(7), Is.EqualTo(firstPixels));
            Assert.That(Atlas("imageSpriteNormals").GetPixels(7), Is.EqualTo(firstNormals));
            Assert.That(source.width, Is.EqualTo(width));
            Assert.That(source.height, Is.EqualTo(height));
            Assert.That(source.GetPixels(), Is.EqualTo(pixels));
            Assert.That(Resources.FindObjectsOfTypeAll<Texture2D>().Length, Is.EqualTo(textureCount), "Glyph preparation must not retain temporary textures.");
            Assert.That(Resources.FindObjectsOfTypeAll<Texture2DArray>().Length, Is.EqualTo(arrayCount + 2));
        }

        [Test]
        public void DisablingPreviewReleasesArraysUntilExplicitPreparation()
        {
            LoadImages();
            var color = Atlas("imageSpriteArray");
            var normal = Atlas("imageSpriteNormals");
            ((Behaviour)manager).enabled = false;
            Assert.That(color == null && normal == null, Is.True);
            ((Behaviour)manager).enabled = true;
            Assert.That(Atlas("imageSpriteArray") == null, Is.True);
            LoadImages();
            Assert.That(Atlas("imageSpriteArray") != null, Is.True);
        }

        [Test]
        public void PreparingCreatureAndItemArtPreservesImportedTextures()
        {
            var creatureType = Type.GetType("RemoteFortressReader.CreatureRaw, Assembly-CSharp", true);
            var creature = Activator.CreateInstance(creatureType);
            creatureType.GetProperty("creature_id").SetValue(creature, "DWARF");
            ((IList)creatures.GetType().GetProperty("CreatureList").GetValue(creatures)).Add(creature);
            var itemType = Type.GetType("RemoteFortressReader.MaterialDefinition, Assembly-CSharp", true);
            var item = Activator.CreateInstance(itemType);
            itemType.GetProperty("id").SetValue(item, "ARMOR");
            var pair = itemType.GetProperty("mat_pair");
            pair.SetValue(item, Activator.CreateInstance(pair.PropertyType));
            ((IList)items.GetType().GetProperty("ItemList").GetValue(items)).Add(item);

            var art = new[] { Resources.Load<Texture2D>("Images/Creatures/DWARF"), Resources.Load<Texture2D>("Images/Items/ARMOR") };
            var pixels = Array.ConvertAll(art, texture => texture.GetPixels());
            var sizes = Array.ConvertAll(art, texture => new Vector2Int(texture.width, texture.height));
            LoadImages();
            Assert.That(Atlas("imageSpriteArray").depth, Is.EqualTo(258), "Both illustrations must be included alongside the glyphs.");
            for (int i = 0; i < art.Length; i++)
            {
                Assert.That(new Vector2Int(art[i].width, art[i].height), Is.EqualTo(sizes[i]));
                Assert.That(art[i].GetPixels(), Is.EqualTo(pixels[i]));
            }
        }

        [Test]
        public void DestroyingOnePreviewDoesNotClearAnotherOwnersGlobalTextures()
        {
            LoadImages();
            var ownedColor = Atlas("imageSpriteArray");
            var ownedNormal = Atlas("imageSpriteNormals");
            var other = new Texture2DArray(1, 1, 1, TextureFormat.RGBA32, false);
            try
            {
                Shader.SetGlobalTexture("_ImageAtlas", other);
                Shader.SetGlobalTexture("_ImageBumpAtlas", other);
                Object.DestroyImmediate(manager.gameObject);
                Assert.That(ownedColor == null && ownedNormal == null, Is.True);
                Assert.That(Shader.GetGlobalTexture("_ImageAtlas"), Is.SameAs(other));
                Assert.That(Shader.GetGlobalTexture("_ImageBumpAtlas"), Is.SameAs(other));
            }
            finally { Object.DestroyImmediate(other); }
        }

        [TestCase(4, 8)]
        [TestCase(32, 32)]
        [TestCase(64, 48)]
        public void CpuSpritePreparationMatchesLegacyTextureResize(int width, int height)
        {
            var source = new Texture2D(width, height, TextureFormat.ARGB32, false);
            try
            {
                var pixels = new Color[width * height];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color((i % 7) / 6f, (i % 13) / 12f, (i % 17) / 16f, (i % 5) / 4f);
                source.SetPixels(pixels);
                source.Apply();
                var original = source.GetPixels();
                var prepare = managerType.GetMethod("PreparePixels", BindingFlags.Static | BindingFlags.NonPublic);
                var actual = (Color[])prepare.Invoke(null, new object[] { original, width, height });
                if (width != 32 || height != 32) TextureScale.Bilinear(source, 32, 32);
                var expected = source.GetPixels();
                for (int i = 0; i < expected.Length; i++)
                {
                    for (int channel = 0; channel < 4; channel++)
                        Assert.That(actual[i][channel], Is.EqualTo(expected[i][channel]).Within(0.000001f));
                    Assert.That((Color32)actual[i], Is.EqualTo((Color32)expected[i]));
                }
                var bevel = Type.GetType("TextureTools, Assembly-CSharp", true).GetMethod("Bevel");
                var expectedNormals = (Color[])bevel.Invoke(null, new object[] { expected, 32, 32 });
                var actualNormals = (Color[])bevel.Invoke(null, new object[] { actual, 32, 32 });
                for (int i = 0; i < expectedNormals.Length; i++)
                    Assert.That((Color32)actualNormals[i], Is.EqualTo((Color32)expectedNormals[i]));
            }
            finally { Object.DestroyImmediate(source); }
        }

        private static object ReplaceWithEmptyList(object owner, string propertyName)
        {
            var property = owner.GetType().GetProperty(propertyName);
            var previous = property.GetValue(owner);
            property.SetValue(owner, Activator.CreateInstance(property.PropertyType));
            return previous;
        }

        private Texture2DArray Atlas(string field) => (Texture2DArray)managerType.GetField(field).GetValue(manager);

        private void LoadImages()
        {
            var loader = (IEnumerator)managerType.GetMethod("LoadImages").Invoke(manager, null);
            try { while (loader.MoveNext()) { } }
            finally { (loader as IDisposable)?.Dispose(); }
        }
    }
}
