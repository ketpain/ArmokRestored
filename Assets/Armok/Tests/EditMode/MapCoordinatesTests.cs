using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Armok.Tests
{
    public class MapCoordinatesTests
    {
        private Type coordinates;
        private Type dfCoord;
        private PropertyInfo offset;
        private int previousOffset;

        [SetUp]
        public void SetUp()
        {
            coordinates = Type.GetType("MapCoordinates, Assembly-CSharp", true);
            dfCoord = Type.GetType("DFHack.DFCoord, Assembly-CSharp", true);
            offset = coordinates.GetProperty("MapZOffset");
            previousOffset = (int)offset.GetValue(null);
            offset.SetValue(null, 7);
        }

        [TearDown]
        public void TearDown() => offset.SetValue(null, previousOffset);

        private object Coord(int x, int y, int z) => Activator.CreateInstance(dfCoord, x, y, z);

        private object Call(string name, params object[] args)
        {
            return coordinates.GetMethod(name, Array.ConvertAll(args, arg => arg.GetType())).Invoke(null, args);
        }

        [TestCase(0, 0, 0)]
        [TestCase(15, 16, 170)]
        [TestCase(-17, -16, -9)]
        public void TileAnchorsRoundTrip(int x, int y, int z)
        {
            var source = Coord(x, y, z);
            var world = (Vector3)Call("DFtoUnityCoord", source);
            Assert.That(world, Is.EqualTo(new Vector3(x * 2, (z + 7) * 3, -y * 2)));
            Assert.That(Call("UnityToDFCoord", world), Is.EqualTo(source));
            Assert.That(Call("DFtoUnityCoord", x, y, z), Is.EqualTo(world));
            Assert.That(Call("DFtoUnityCoord", (float)x, (float)y, (float)z), Is.EqualTo(world));
        }

        [TestCase(0.999f, -0.001f, -0.999f, 0, 0, -8)]
        [TestCase(1f, 0f, -1f, 0, 0, -7)]
        [TestCase(1.001f, 2.999f, -1.001f, 1, 1, -7)]
        [TestCase(-3f, 3f, 3f, -2, -2, -6)]
        public void NearestTileUsesEvenTiesAndFloorsVerticalPosition(float x, float y, float z, int tileX, int tileY, int tileZ)
        {
            Assert.That(Call("UnityToDFCoord", new Vector3(x, y, z)), Is.EqualTo(Coord(tileX, tileY, tileZ)));
        }

        [Test]
        public void DirectionsCentersAndFloatingCoordinatesKeepTheirDistinctOrigins()
        {
            var tile = Coord(2, 3, 4);
            Assert.That(Call("DFtoUnityDirection", tile), Is.EqualTo(new Vector3(4, 12, -6)));
            Assert.That(Call("DFtoUnityDirection", 2f, 3f, 4f), Is.EqualTo(new Vector3(4, 12, -6)));
            Assert.That(Call("DFtoUnityTileCenter", tile), Is.EqualTo(new Vector3(4, 34.5f, -6)));
            Assert.That(Call("DFtoUnityBottomCorner", tile), Is.EqualTo(new Vector3(3, 33, -7)));
            Assert.That(Call("UnityToFloatingDFCoord", new Vector3(4, 33, -6)), Is.EqualTo(new Vector3(2.5f, 3.5f, 4)));
        }

        [TestCase(0, 0, true)]
        [TestCase(16, -16, true)]
        [TestCase(15, 16, false)]
        [TestCase(16, -17, false)]
        public void BlockCornersDependOnBothHorizontalAxes(int x, int y, bool expected)
        {
            Assert.That(Call("IsBlockCorner", Coord(x, y, 9)), Is.EqualTo(expected));
        }
    }
}
