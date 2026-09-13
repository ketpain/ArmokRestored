using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Shared by the game and Standard Assets through the auto-referenced Armok.Input assembly.
public static class ArmokInput
{
    private static InputActionMap controls;
    private static readonly Dictionary<string, InputAction> actions = new Dictionary<string, InputAction>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        controls = null;
        actions.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        Action("Move");
    }

    public static InputAction Action(string name)
    {
        if (controls == null)
        {
            if (InputSystem.actions == null)
                throw new InvalidOperationException("Assign InputSystem_Actions as the project's Input Actions asset.");
            controls = InputSystem.actions.FindActionMap("Armok", true);
            controls.Enable();
        }

        InputAction action;
        if (!actions.TryGetValue(name, out action))
        {
            action = controls.FindAction(name, true);
            actions.Add(name, action);
        }
        return action;
    }

    public static Vector2 Move => Action("Move").ReadValue<Vector2>();
    public static float Elevation => Action("Elevation").ReadValue<float>();
    public static Vector2 Look => Action("Look").ReadValue<Vector2>();
    public static Vector3 PointerPosition => Action("Point").ReadValue<Vector2>();
    public static float Scroll => Action("Zoom").ReadValue<float>();
}
