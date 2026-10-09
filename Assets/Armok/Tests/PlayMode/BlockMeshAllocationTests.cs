using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Armok.Tests
{
    public class BlockMeshAllocationTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private GameObject root;
        private Component block;
        private Type blockType;
        private Type meshType;
        private object empty;
        private object triangle;

        [SetUp]
        public void SetUp()
        {
            blockType = Type.GetType("BlockMeshSet, Assembly-CSharp", true);
            meshType = blockType.GetNestedType("MeshType", BindingFlags.NonPublic);
            root = new GameObject("Block mesh allocation test");
            block = root.AddComponent(blockType);
            var collision = new GameObject("Collision");
            collision.transform.SetParent(root.transform);
            blockType.GetField("collisionBlocks").SetValue(block, collision.AddComponent<MeshCollider>());
            empty = CreateData(true);
            triangle = CreateData(false);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                Object.DestroyImmediate(filter.sharedMesh);
            Object.DestroyImmediate(root);
        }

        [TestCase("Tiles")]
        [TestCase("TopTiles")]
        [TestCase("StencilTiles")]
        [TestCase("TopStencilTiles")]
        [TestCase("TransparentTiles")]
        [TestCase("TopTransparentTiles")]
        [TestCase("Terrain")]
        [TestCase("TopTerrain")]
        [TestCase("Water")]
        [TestCase("Magma")]
        public void EmptyFirstResultDoesNotCreatePresentation(string kind)
        {
            Copy(kind, empty);
            Assert.That(root.GetComponentsInChildren<MeshRenderer>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<MeshFilter>(true), Is.Empty);
            Assert.That(root.transform.childCount, Is.EqualTo(1), "Only the collision object should exist.");
        }

        [TestCase("Tiles")]
        [TestCase("Terrain")]
        [TestCase("TopTerrain")]
        public void EmptyUpdateHidesAndReusesExistingMesh(string kind)
        {
            Copy(kind, triangle);
            var renderer = root.GetComponentInChildren<MeshRenderer>();
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            SetVisibility("Walls");
            ShadowCastingMode shadows = renderer.shadowCastingMode;

            Copy(kind, empty);
            SetVisibility("Walls");
            Assert.That(renderer.enabled, Is.False);
            Assert.That(mesh.vertexCount, Is.Zero);

            Copy(kind, triangle);
            SetVisibility("Walls");
            Assert.That(renderer.enabled, Is.True);
            Assert.That(renderer.shadowCastingMode, Is.EqualTo(shadows));
            Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            Assert.That(mesh.triangles, Is.EqualTo(new[] { 0, 1, 2 }));
            var expectedNormal = kind == "Tiles" ? Vector3.back : Vector3.forward;
            Assert.That(mesh.normals, Is.EqualTo(new[] { expectedNormal, expectedNormal, expectedNormal }));
            if (kind != "Tiles")
            {
                var expectedTangent = new Vector4(1, 0, 0, 1);
                Assert.That(mesh.tangents, Is.EqualTo(new[] { expectedTangent, expectedTangent, expectedTangent }));
            }
            Assert.That(root.GetComponentsInChildren<MeshRenderer>(true).Length, Is.EqualTo(1));
        }

        [Test]
        public void EmptyTerrainLoadNeedsNoRendererOrMaterialResources()
        {
            Type resultType = Type.GetType("BlockMesher+Result, Assembly-CSharp", true);
            object result = Activator.CreateInstance(resultType);
            resultType.GetField("terrainMesh").SetValue(result, empty);
            resultType.GetField("topTerrainMesh").SetValue(result, empty);
            blockType.GetMethod("LoadMeshes", Members).Invoke(block, new[] { result, "empty" });
            Assert.That(root.GetComponentsInChildren<MeshRenderer>(true), Is.Empty);
        }

        [Test]
        public void MapClearDisablesPresentationUntilGeometryReturns()
        {
            Copy("Tiles", triangle);
            var renderer = root.GetComponentInChildren<MeshRenderer>();
            blockType.GetMethod("Clear", Members).Invoke(block, null);
            SetVisibility("All");
            Assert.That(renderer.enabled, Is.False);
            Copy("Tiles", triangle);
            SetVisibility("All");
            Assert.That(renderer.enabled, Is.True);
            Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
        }

        [Test]
        public void BlockPrefabAllocatesRenderersOnlyWhenGeometryArrives()
        {
            var prefab = Resources.Load<GameObject>("MapBlock");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<MeshRenderer>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<MeshFilter>(true), Is.Empty);
            Assert.That(blockType.GetField("collisionBlocks").GetValue(prefab.GetComponent(blockType)), Is.Not.Null);
        }

        private void Copy(string kind, object data) => blockType.GetMethod("CopyMesh", Members).Invoke(block, new[] { Enum.Parse(meshType, kind), data, "test" });

        private void SetVisibility(string kind) => blockType.GetMethod("UpdateVisibility").Invoke(block, new[] { Enum.Parse(blockType.GetNestedType("Visibility"), kind) });

        private static object CreateData(bool empty)
        {
            int count = empty ? 0 : 3;
            return Activator.CreateInstance(Type.GetType("CPUMesh, Assembly-CSharp", true), new object[]
            {
                empty ? Array.Empty<Vector3>() : new[] { Vector3.zero, Vector3.right, Vector3.up },
                empty ? Array.Empty<Vector3>() : new[] { Vector3.back, Vector3.back, Vector3.back },
                new Vector4[count],
                empty ? Array.Empty<Vector2>() : new[] { Vector2.zero, Vector2.right, Vector2.up },
                new Vector2[count], new Vector2[count], new Color[count],
                empty ? Array.Empty<int>() : new[] { 0, 1, 2 }, "test"
            });
        }
    }
}
