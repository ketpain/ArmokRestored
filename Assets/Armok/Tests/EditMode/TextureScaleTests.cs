using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Armok.Tests
{
    public class TextureScaleTests
    {
        private static readonly Color[] Corners = { Color.red, Color.green, Color.blue, Color.white };

        [Test]
        public void PointUpscaleRepeatsEachSourcePixel()
        {
            var result = TextureScale.ThreadedScale(Corners, 2, 2, 4, 4, false);
            CollectionAssert.AreEqual(new[] {
                Color.red, Color.red, Color.green, Color.green,
                Color.red, Color.red, Color.green, Color.green,
                Color.blue, Color.blue, Color.white, Color.white,
                Color.blue, Color.blue, Color.white, Color.white }, result);
        }

        [Test]
        public void PointDownscaleSamplesTheExpectedLocations()
        {
            var source = Enumerable.Range(0, 16).Select(i => new Color(i, -i, i / 2f, 1)).ToArray();
            CollectionAssert.AreEqual(new[] { source[0], source[2], source[8], source[10] },
                TextureScale.ThreadedScale(source, 4, 4, 2, 2, false));
        }

        [Test]
        public void SameSizeBilinearPreservesLegacySampling()
        {
            CollectionAssert.AreEqual(new[] {
                Color.red, new Color(0.5f, 0.5f, 0, 1),
                new Color(0.5f, 0, 0.5f, 1), new Color(0.5f, 0.5f, 0.5f, 1) },
                TextureScale.ThreadedScale(Corners, 2, 2, 2, 2, true));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SameSizePointAndSingleDestinationKeepTopLeftSample(bool bilinear)
        {
            CollectionAssert.AreEqual(new[] { Color.red }, TextureScale.ThreadedScale(Corners, 2, 2, 1, 1, bilinear));
            if (!bilinear)
                CollectionAssert.AreEqual(Corners, TextureScale.ThreadedScale(Corners, 2, 2, 2, 2, false));
        }

        [TestCase(1, 1, 9, 7)]
        [TestCase(1, 3, 4, 6)]
        [TestCase(3, 1, 6, 4)]
        public void DegenerateDimensionsPreserveConstantColor(int width, int height, int outputWidth, int outputHeight)
        {
            var color = new Color(0.125f, 0.25f, 0.5f, 0.75f);
            var source = Enumerable.Repeat(color, width * height).ToArray();
            foreach (bool bilinear in new[] { false, true })
                Assert.That(TextureScale.ThreadedScale(source, width, height, outputWidth, outputHeight, bilinear),
                    Is.All.EqualTo(color));
        }

        [TestCase(8, 6)]
        [TestCase(512, 512)]
        public void BilinearInterpolatesIndependentColorAndAlphaChannels(int width, int height)
        {
            // A planar ramp has a known result throughout the image, including the parallel path.
            var source = new[] { new Color(0, 0, 0, 0), new Color(1, 0, 0, 0.5f),
                new Color(0, 1, 0, 0.5f), new Color(1, 1, 0, 1) };
            var result = TextureScale.ThreadedScale(source, 2, 2, width, height, true);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var pixel = result[y * width + x];
                    Assert.That(pixel.r, Is.EqualTo((float)x / width).Within(0.000001f));
                    Assert.That(pixel.g, Is.EqualTo((float)y / height).Within(0.000001f));
                    Assert.That(pixel.a, Is.EqualTo(((float)x / width + (float)y / height) / 2).Within(0.000001f));
                }
        }

        [Test]
        public void IndependentConcurrentResizesDoNotSharePixelState()
        {
            var results = new Color[32][];
            Parallel.For(0, results.Length, i =>
            {
                var color = new Color(i, i / 2f, -i, 1);
                results[i] = TextureScale.ThreadedScale(Enumerable.Repeat(color, 12).ToArray(), 3, 4, 37, 29, true);
            });
            for (int i = 0; i < results.Length; i++)
                Assert.That(results[i], Is.All.EqualTo(new Color(i, i / 2f, -i, 1)));
        }

        [Test]
        public void ResizingDoesNotMutateSourcePixels()
        {
            var source = (Color[])Corners.Clone();
            TextureScale.ThreadedScale(source, 2, 2, 7, 9, true);
            CollectionAssert.AreEqual(Corners, source);
        }

        [Test]
        public void InvalidDimensionsAndPixelCountsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => TextureScale.ThreadedScale(null, 2, 2, 2, 2, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => TextureScale.ThreadedScale(Corners, 0, 2, 2, 2, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => TextureScale.ThreadedScale(Corners, 2, -1, 2, 2, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => TextureScale.ThreadedScale(Corners, 2, 2, 0, 2, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => TextureScale.ThreadedScale(Corners, 2, 2, 2, -1, true));
            Assert.Throws<ArgumentException>(() => TextureScale.ThreadedScale(Corners, 3, 2, 2, 2, true));
            Assert.Throws<OverflowException>(() => TextureScale.ThreadedScale(Corners, 2, 2, int.MaxValue, 2, true));
        }
    }
}
