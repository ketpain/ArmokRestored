using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Armok.Tests
{
    public class EditorContentPreparationTests
    {
        [TestCase("ItemManager", "loaded")]
        [TestCase("ImageManager", "imageSpriteArray")]
        [TestCase("DecorationManager", "shapes")]
        public void OpeningManagerDoesNotPrepareContent(string name, string stateField)
        {
            var type = Type.GetType(name + ", Assembly-CSharp", true);
            var singleton = type.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = singleton.GetValue(null);
            var colorAtlas = Shader.GetGlobalTexture("_ImageAtlas");
            var normalAtlas = Shader.GetGlobalTexture("_ImageBumpAtlas");
            GameObject instance = null;
            try
            {
                singleton.SetValue(null, null);
                instance = Object.Instantiate(Resources.Load<GameObject>(name));
                var manager = instance.GetComponent(type);
                var value = type.GetField(stateField, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(manager);
                if (value is bool loaded) Assert.That(loaded, Is.False);
                else if (value is IDictionary dictionary) Assert.That(dictionary.Count, Is.Zero);
                else Assert.That((Object)value == null, Is.True, "Opening a scene must not allocate image atlases.");
                Assert.That(Shader.GetGlobalTexture("_ImageAtlas"), Is.SameAs(colorAtlas));
                Assert.That(Shader.GetGlobalTexture("_ImageBumpAtlas"), Is.SameAs(normalAtlas));
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                singleton.SetValue(null, previous);
                Shader.SetGlobalTexture("_ImageAtlas", colorAtlas);
                Shader.SetGlobalTexture("_ImageBumpAtlas", normalAtlas);
            }
        }
    }
}
