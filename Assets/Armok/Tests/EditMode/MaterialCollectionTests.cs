using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Armok.Tests
{
    public class MaterialCollectionTests
    {
        [Test]
        public void CreatingReplacementDoesNotDestroyTheLoadedResource()
        {
            const string path = "Assets/Resources/materialDefinitions.asset";
            var type = Type.GetType("MaterialStore.MaterialCollection, Assembly-CSharp", true);
            var instance = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var original = (ScriptableObject)instance.GetValue(null);
            Assert.That(original != null, Is.True, "Prepare material resources before running content tests.");
            byte[] before = File.ReadAllBytes(path);
            ScriptableObject replacement = null;
            try
            {
                replacement = ScriptableObject.CreateInstance(type);
                Assert.That(original != null, Is.True, "Creating a replacement must not destroy the resource asset.");
                Assert.That(instance.GetValue(null), Is.SameAs(original));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            }
            finally
            {
                if (replacement != null) UnityEngine.Object.DestroyImmediate(replacement);
            }
        }
    }
}
