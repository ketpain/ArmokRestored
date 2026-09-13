using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace Armok.Tests
{
    public class ModernInputTests
    {
        private InputActionMap map;
        private Keyboard keyboard;
        private Mouse mouse;
        private Keyboard originalKeyboard;
        private Mouse originalMouse;

        [SetUp]
        public void Setup()
        {
            var projectActions = InputSystem.actions;
            Assert.That(projectActions, Is.Not.Null, "Assign the project's Input Actions asset.");
            originalKeyboard = Keyboard.current;
            originalMouse = Mouse.current;
            map = projectActions.FindActionMap("Armok", true).Clone();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            map.devices = new InputDevice[] { keyboard, mouse };
            map.Enable();
        }

        [TearDown]
        public void TearDown()
        {
            map?.Dispose();
            map = null;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (originalKeyboard != null && originalKeyboard.added) originalKeyboard.MakeCurrent();
            if (originalMouse != null && originalMouse.added) originalMouse.MakeCurrent();
        }

        // The cloned map is restricted to these synthetic devices. Preserve the
        // project's action asset and physical devices throughout the test run.
        private static void Press(ButtonControl button) => SetButton(button, 1);
        private static void Release(ButtonControl button) => SetButton(button, 0);
        private static void SetButton(ButtonControl button, float value)
        {
            using (StateEvent.From(button.device, out var inputEvent))
            {
                button.WriteValueIntoEvent(value, inputEvent);
                InputSystem.QueueEvent(inputEvent);
            }
            InputSystem.Update();
        }

        [Test]
        public void CameraMovementAndElevationUseProjectBindings()
        {
            Press(keyboard.wKey); Press(keyboard.dKey);
            var move = map["Move"].ReadValue<Vector2>();
            Assert.That(move.x, Is.GreaterThan(0)); Assert.That(move.y, Is.GreaterThan(0));
            Assert.That(move.magnitude, Is.EqualTo(1).Within(0.0001f));
            Release(keyboard.wKey); Release(keyboard.dKey);
            Press(keyboard.eKey); Assert.That(map["Elevation"].ReadValue<float>(), Is.EqualTo(1));
            Release(keyboard.eKey);
            Press(keyboard.qKey); Assert.That(map["Elevation"].ReadValue<float>(), Is.EqualTo(-1));
        }

        [Test]
        public void ButtonEdgesAreNotRepeatedWhileHeld()
        {
            Press(keyboard.fKey);
            Assert.That(map["FollowDF"].WasPressedThisFrame(), Is.True);
            InputSystem.Update();
            Assert.That(map["FollowDF"].IsPressed(), Is.True);
            Assert.That(map["FollowDF"].WasPressedThisFrame(), Is.False);
            Release(keyboard.fKey);
            Assert.That(map["FollowDF"].WasReleasedThisFrame(), Is.True);
        }

        [Test]
        public void MouseSelectionLookAndZoomUseProjectSensitivity()
        {
            InputSystem.QueueStateEvent(mouse, new MouseState {
                position = new Vector2(128, 64), delta = new Vector2(20, -10), scroll = Vector2.up
            }.WithButton(MouseButton.Left));
            InputSystem.Update();
            Assert.That(map["Select"].WasPressedThisFrame(), Is.True);
            Assert.That(map["Point"].ReadValue<Vector2>(), Is.EqualTo(new Vector2(128, 64)));
            Assert.That(map["Look"].ReadValue<Vector2>(), Is.EqualTo(new Vector2(2, -1)));
            Assert.That(map["Zoom"].ReadValue<float>(), Is.EqualTo(0.1f).Within(0.0001f));
            InputSystem.Update();
            Assert.That(map["Zoom"].ReadValue<float>(), Is.Zero);
        }

        [Test]
        public void RemovingDevicesClearsHeldControls()
        {
            Press(keyboard.wKey); Press(mouse.leftButton);
            InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
            InputSystem.Update();
            Assert.That(map["Move"].ReadValue<Vector2>(), Is.EqualTo(Vector2.zero));
            Assert.That(map["Select"].IsPressed(), Is.False);
        }
    }
}
