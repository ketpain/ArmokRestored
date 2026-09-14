using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Armok.Tests
{
    public class CreatureArtTests
    {
        // ImageManager remains in Assembly-CSharp during the content extraction.
        private static Texture2D Load(string token)
        {
            var manager = Type.GetType("ImageManager, Assembly-CSharp", true);
            var load = manager.GetMethod("LoadCreatureArt", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(load, Is.Not.Null);
            return (Texture2D)load.Invoke(null, new object[] { token });
        }

        [TestCase("DWARF", "DWARF")]
        [TestCase("BIRD_EAGLE", "BIRD_EAGLE")]
        [TestCase("CAMEL_1_HUMP", "CAMEL_1_HUMP")]
        [TestCase("FORGOTTEN_BEAST_75", "FORGOTTEN_BEAST")]
        [TestCase("DEMON_22", "DEMON")]
        [TestCase("NIGHT_CREATURE_44", "NIGHT_CREATURE")]
        [TestCase("TITAN_6", "TITAN")]
        [TestCase("BIRD_BLUEJAY", "BLUEJAY")]
        [TestCase("BIRD_CARDINAL", "CARDINAL")]
        [TestCase("BIRD_GRACKLE", "GRACKLE")]
        [TestCase("BIRD_ORIOLE", "ORIOLE")]
        [TestCase("BIRD_RW_BLACKBIRD", "RW_BLACKBIRD")]
        [TestCase("GIANT PEREGRINE FALCON", "GIANT PAREGRINE FALCON")]
        [TestCase("MARMOT_HOARY_MAN", "MARMOT_MAN")]
        [TestCase("GIANT_MARMOT_HOARY", "GIANT_MARMOT")]
        public void LoadsExistingArtwork(string token, string resource)
        {
            var expected = Resources.Load<Texture2D>("Images/Creatures/" + resource);
            Assert.That(expected, Is.Not.Null, "Bundled artwork is missing: " + resource);
            Assert.That(Load(token), Is.SameAs(expected));
            Assert.That(expected.isReadable, Is.True, "Engraving atlas creation reads the source pixels.");
            Assert.That(expected.GetPixels(), Has.Length.EqualTo(expected.width * expected.height));
        }

        [TestCase("ARMOK_TEST_UNKNOWN_123")]
        [TestCase("CAMBRIAN_HALLUCIGENIA_MAN")]
        [TestCase("TOAD")]
        public void MissingOptionalArtworkReturnsNoIllustration(string token)
        {
            Assert.That(Load(token), Is.Null);
        }
    }
}
