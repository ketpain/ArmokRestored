using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Armok.Tests
{
    public class BlockVisibilityTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private GameObject root;
        private Component block;
        private Type blockType;
        private Type visibilityType;
        private Type meshType;
        private MeshCollider collider;
        private MeshRenderer wall;
        private MeshRenderer top;
        private object meshData;

        [SetUp]
        public void SetUp()
        {
            blockType = Type.GetType("BlockMeshSet, Assembly-CSharp", true);
            visibilityType = blockType.GetNestedType("Visibility");
            meshType = blockType.GetNestedType("MeshType", BindingFlags.NonPublic);
            root = new GameObject("Visibility test block");
            block = root.AddComponent(blockType);
            var collision = new GameObject("Collision");
            collision.transform.SetParent(root.transform);
            collider = collision.AddComponent<MeshCollider>();
            blockType.GetField("collisionBlocks").SetValue(block, collider);
            var source = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            try { meshData = Activator.CreateInstance(Type.GetType("CPUMesh, Assembly-CSharp", true), new object[] { source }); }
            finally { Object.DestroyImmediate(source); }
            wall = AddMesh("Tiles");
            top = AddMesh("TopTiles");
        }

        [TearDown]
        public void TearDown()
        {
            if (root == null) return;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                Object.DestroyImmediate(filter.sharedMesh);
            Object.DestroyImmediate(root);
        }

        [TestCase("None")]
        [TestCase("Shadows")]
        [TestCase("Walls")]
        [TestCase("All")]
        public void FirstRequestAndRepeatedRequestsApplyVisibility(string mode)
        {
            SetVisibility(mode);
            AssertVisibility(mode);
            SetVisibility(mode);
            AssertVisibility(mode);
        }

        [Test]
        public void EveryVisibilityTransitionPreservesShadowsAndCollisionLayer()
        {
            var modes = new[] { "None", "Shadows", "Walls", "All" };
            foreach (string from in modes)
                foreach (string to in modes)
                {
                    SetVisibility(from);
                    SetVisibility(to);
                    AssertVisibility(to);
                }
        }

        [TestCase("None")]
        [TestCase("Shadows")]
        [TestCase("Walls")]
        [TestCase("All")]
        public void NewlyArrivingMeshReceivesUnchangedVisibility(string mode)
        {
            SetVisibility(mode);
            var arriving = AddMesh("TopTransparentTiles");
            SetVisibility(mode);
            AssertVisibility(mode);
            if (mode != "None")
                Assert.That(arriving.shadowCastingMode, Is.EqualTo(mode == "All" ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly));
            SetVisibility("All");
            Assert.That(arriving.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
        }

        private void SetVisibility(string mode) => blockType.GetMethod("UpdateVisibility").Invoke(block, new[] { Enum.Parse(visibilityType, mode) });

        private MeshRenderer AddMesh(string kind)
        {
            object key = Enum.Parse(meshType, kind);
            // Exercise the mesh-arrival path without loading global material resources.
            blockType.GetMethod("CopyMesh", Members).Invoke(block, new[] { key, meshData, "test" });
            return (MeshRenderer)((IDictionary)blockType.GetField("meshRenderers", Members).GetValue(block))[key];
        }

        private void AssertVisibility(string mode)
        {
            Assert.That(root.activeSelf, Is.EqualTo(mode != "None"), mode);
            if (mode == "None") return;
            Assert.That(wall.shadowCastingMode, Is.EqualTo(mode == "Shadows" ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On), mode);
            Assert.That(top.shadowCastingMode, Is.EqualTo(mode == "All" ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly), mode);
            Assert.That(collider.gameObject.layer, Is.EqualTo(mode == "Shadows" ? 2 : 0), mode);
        }
    }
}
