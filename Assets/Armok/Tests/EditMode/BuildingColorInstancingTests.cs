using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Armok.Tests
{
    public class BuildingColorInstancingTests
    {
        private readonly List<Object> owned = new List<Object>();
        private Scene scene;
        private Camera camera;
        private RenderTexture target;
        private bool bounded;

        [SetUp]
        public void SetUp()
        {
            if (!SystemInfo.supportsInstancing || !SystemInfo.supports2DArrayTextures ||
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Requires graphics with instancing and texture-array support.");
            bounded = Shader.IsKeywordEnabled("_BOUNDING_BOX_ENABLED");
            Shader.DisableKeyword("_BOUNDING_BOX_ENABLED");
            scene = EditorSceneManager.NewPreviewScene();
            var cameraObject = new GameObject("Pattern color test camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 2;
            camera.aspect = 2;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.renderingPath = RenderingPath.Forward;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            target = Own(new RenderTexture(256, 128, 24, RenderTextureFormat.ARGB32));
            target.Create();
            camera.targetTexture = target;
            var lightObject = new GameObject("Pattern color test light");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1;
            light.shadows = LightShadows.None;
        }

        [TearDown]
        public void TearDown()
        {
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            for (int i = owned.Count - 1; i >= 0; i--) Object.DestroyImmediate(owned[i]);
            owned.Clear();
            if (bounded) Shader.EnableKeyword("_BOUNDING_BOX_ENABLED");
        }

        private T Own<T>(T value) where T : Object
        {
            owned.Add(value);
            return value;
        }

        private Texture2D Texture(Color color)
        {
            var texture = Own(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private Texture2DArray Array(Color color)
        {
            var texture = Own(new Texture2DArray(1, 1, 1, TextureFormat.RGBA32, false, true));
            texture.SetPixels(new[] { color }, 0);
            texture.Apply();
            return texture;
        }

        private Color32[] Render()
        {
            var previous = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                return pixels.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(pixels);
            }
        }

        [TestCase(-1, false)]
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        public void SharedMaterialColorsMatchSeparateMaterials(int channel, bool textureOverride)
        {
            var shader = Shader.Find("Building/Opaque");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            var material = Own(new Material(shader));
            material.SetTexture("_MatTexArray", Array(Color.white));
            var shape = Array(new Color(1, .5f, 1, .5f));
            material.SetTexture("_ShapeMap", shape);
            material.SetColor("_MatColor", new Color(.2f, .4f, .6f, .5f));
            material.SetColor("_Color1", Color.red);
            material.SetColor("_Color2", Color.green);
            material.SetColor("_Color3", Color.blue);
            material.SetFloat("_Glossiness", 0);
            material.EnableKeyword("_PATTERN_MASK");
            var mask = Texture(channel < 0 ? Color.black : channel == 0 ? Color.red : channel == 1 ? Color.green : Color.blue);
            material.SetTexture("_PatternMask", textureOverride ? Texture(Color.black) : mask);
            var renderers = new MeshRenderer[8];
            var colors = new[] { Color.red, Color.green, Color.blue, new Color(.5f, .3f, .1f), Color.cyan, new Color(.7f, .2f, .4f) };
            for (int i = 0; i < renderers.Length; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                SceneManager.MoveGameObjectToScene(quad, scene);
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.position = new Vector3(-3 + (i % 4) * 2, i < 4 ? .8f : -.8f, 0);
                renderers[i] = quad.GetComponent<MeshRenderer>();
                renderers[i].lightProbeUsage = LightProbeUsage.Off;
                renderers[i].reflectionProbeUsage = ReflectionProbeUsage.Off;
                var individual = Own(new Material(material));
                // This uniform is outside the shader Properties block and is not retained by material cloning.
                individual.SetTexture("_ShapeMap", shape);
                individual.enableInstancing = false;
                if (i < colors.Length)
                {
                    individual.SetColor("_MatColor", new Color(colors[i].r, colors[i].g, colors[i].b, .5f));
                    for (int c = 0; c < 3; c++) individual.SetColor("_Color" + (c + 1), colors[(i + c) % colors.Length]);
                }
                if (textureOverride) individual.SetTexture("_PatternMask", mask);
                renderers[i].sharedMaterial = individual;
            }
            var expected = Render();
            material.enableInstancing = true;
            for (int i = 0; i < renderers.Length; i++)
            {
                var block = new MaterialPropertyBlock();
                if (i < colors.Length)
                {
                    block.SetColor("_MatColor", new Color(colors[i].r, colors[i].g, colors[i].b, .5f));
                    for (int c = 0; c < 3; c++) block.SetColor("_Color" + (c + 1), colors[(i + c) % colors.Length]);
                }
                if (textureOverride) block.SetTexture("_PatternMask", mask);
                renderers[i].sharedMaterial = material;
                renderers[i].SetPropertyBlock(block);
            }
            var actual = Render();
            int foreground = 0;
            int maximumDifference = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i].r > 32 || expected[i].g > 32 || expected[i].b > 32) foreground++;
                maximumDifference = Mathf.Max(maximumDifference, Mathf.Abs(expected[i].r - actual[i].r),
                    Mathf.Abs(expected[i].g - actual[i].g), Mathf.Abs(expected[i].b - actual[i].b));
            }
            Assert.That(foreground, Is.GreaterThan(1000), "The reference must contain visible colored geometry.");
            Assert.That(maximumDifference, Is.LessThanOrEqualTo(2), "Property overrides and material defaults must retain the reference colors.");
        }
    }
}
