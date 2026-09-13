using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Armok.Tests
{
    public class SpriteTextureCacheTests
    {
        private string directory;
        private readonly List<Object> textures = new List<Object>();
        private readonly Vector2Int[] coordinates = { new Vector2Int(3, 4), new Vector2Int(8, 9) };

        [SetUp]
        public void SetUp()
        {
            if (!SystemInfo.supports2DArrayTextures || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Sprite cache checks require a graphics device with texture-array support.");
            directory = Path.Combine(Path.GetTempPath(), "ArmokSpriteCacheTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var texture in textures) if (texture != null) Object.DestroyImmediate(texture);
            textures.Clear();
            if (directory == null || !Directory.Exists(directory)) return;
            // Only this test's unique direct-child files are created; no recursive deletion.
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }

        private SpriteTextureCache Cache(byte[] source = null, string storage = null)
        {
            return new SpriteTextureCache(Path.Combine(directory, "source.png"), "page",
                source ?? new byte[] { 1, 2, 3 }, storage ?? directory);
        }

        private Texture2DArray Texture(bool normal)
        {
            var texture = new Texture2DArray(4, 2, 2, TextureFormat.ARGB32, true, normal);
            textures.Add(texture);
            for (int slice = 0; slice < texture.depth; slice++)
                for (int mip = 0; mip < texture.mipmapCount; mip++)
                {
                    int count = Math.Max(1, texture.width >> mip) * Math.Max(1, texture.height >> mip);
                    var pixels = new Color32[count];
                    for (int i = 0; i < count; i++)
                        pixels[i] = new Color32((byte)(i + 17 * slice), (byte)(mip * 61 + 7),
                            (byte)(normal ? 219 : 37), (byte)(i + 109));
                    texture.SetPixels32(pixels, slice, mip);
                }
            texture.Apply(false, false);
            return texture;
        }

        private bool Load(SpriteTextureCache cache, byte[] fingerprint, out Texture2DArray colors, out Texture2DArray normals)
        {
            bool result = cache.TryLoad(fingerprint, 4, 2, 2, out colors, out normals);
            if (colors != null) textures.Add(colors);
            if (normals != null) textures.Add(normals);
            if (!result) { Assert.That(colors, Is.Null); Assert.That(normals, Is.Null); }
            return result;
        }

        [Test]
        public void MissingEntryIsACleanCacheMiss()
        {
            var cache = Cache();
            Assert.That(Load(cache, cache.Fingerprint(4, 2, 12, coordinates), out _, out _), Is.False);
        }

        [Test]
        public void RoundTripPreservesEveryColorAndNormalMip()
        {
            var cache = Cache(); var fingerprint = cache.Fingerprint(4, 2, 12, coordinates);
            var colors = Texture(false); var normals = Texture(true);
            Assert.That(cache.TrySave(fingerprint, colors, normals), Is.True);
            Assert.That(Load(cache, fingerprint, out var restoredColors, out var restoredNormals), Is.True);
            for (int slice = 0; slice < colors.depth; slice++)
                for (int mip = 0; mip < colors.mipmapCount; mip++)
                {
                    CollectionAssert.AreEqual(colors.GetPixels32(slice, mip), restoredColors.GetPixels32(slice, mip));
                    CollectionAssert.AreEqual(normals.GetPixels32(slice, mip), restoredNormals.GetPixels32(slice, mip));
                }
        }

        [Test]
        public void SourceAndLayoutChangesInvalidateExistingEntry()
        {
            var cache = Cache(); var fingerprint = cache.Fingerprint(4, 2, 12, coordinates);
            Assert.That(cache.TrySave(fingerprint, Texture(false), Texture(true)), Is.True);
            var changedSource = Cache(new byte[] { 1, 2, 4 });
            Assert.That(changedSource.CachePath, Is.EqualTo(cache.CachePath), "Edits should replace the existing slot.");
            foreach (var changed in new[] {
                changedSource.Fingerprint(4, 2, 12, coordinates),
                cache.Fingerprint(8, 2, 12, coordinates), cache.Fingerprint(4, 4, 12, coordinates),
                cache.Fingerprint(4, 2, 24, coordinates),
                cache.Fingerprint(4, 2, 12, new[] { coordinates[1], coordinates[0] }),
                cache.Fingerprint(4, 2, 12, new[] { coordinates[0] }) })
                Assert.That(Load(cache, changed, out _, out _), Is.False);
        }

        [TestCase("checksum")]
        [TestCase("truncated")]
        [TestCase("header")]
        [TestCase("extra-byte")]
        public void InvalidEntriesAreMissesAndCanBeReplaced(string corruption)
        {
            var cache = Cache(); var fingerprint = cache.Fingerprint(4, 2, 12, coordinates);
            var colors = Texture(false); var normals = Texture(true);
            Assert.That(cache.TrySave(fingerprint, colors, normals), Is.True);
            var expected = File.ReadAllBytes(cache.CachePath);
            var damaged = (byte[])expected.Clone();
            switch (corruption)
            {
                case "checksum": damaged[damaged.Length - 1] ^= 1; break;
                case "truncated": Array.Resize(ref damaged, damaged.Length - 7); break;
                case "header": damaged[0] ^= 1; break;
                case "extra-byte": Array.Resize(ref damaged, damaged.Length + 1); break;
            }
            File.WriteAllBytes(cache.CachePath, damaged);
            Assert.That(Load(cache, fingerprint, out _, out _), Is.False);
            Assert.That(cache.TrySave(fingerprint, colors, normals), Is.True);
            Assert.That(Load(cache, fingerprint, out _, out _), Is.True);
            CollectionAssert.AreEqual(expected, File.ReadAllBytes(cache.CachePath));
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }

        [Test]
        public void UnavailableStorageFallsBackWithoutTemporaryFiles()
        {
            string blocker = Path.Combine(directory, "not-a-directory");
            File.WriteAllText(blocker, "occupied");
            var cache = Cache(storage: blocker); var fingerprint = cache.Fingerprint(4, 2, 12, coordinates);
            Assert.That(cache.TrySave(fingerprint, Texture(false), Texture(true)), Is.False);
            Assert.That(Load(cache, fingerprint, out _, out _), Is.False);
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }
    }
}
