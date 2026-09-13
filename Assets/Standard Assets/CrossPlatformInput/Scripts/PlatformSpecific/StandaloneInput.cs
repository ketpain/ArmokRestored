using System;
using UnityEngine;

namespace UnityStandardAssets.CrossPlatformInput.PlatformSpecific
{
    public class StandaloneInput : VirtualInput
    {
        public override float GetAxis(string name, bool raw)
        {
            switch (name)
            {
                case "Horizontal": return ArmokInput.Move.x;
                case "Vertical": return ArmokInput.Move.y;
                case "CamUpDown": return ArmokInput.Elevation;
                case "Mouse X": return ArmokInput.Look.x;
                case "Mouse Y": return ArmokInput.Look.y;
                case "Mouse ScrollWheel": return ArmokInput.Scroll;
                default: throw new ArgumentException("Unknown input axis: " + name, nameof(name));
            }
        }


        public override bool GetButton(string name)
        {
            return ArmokInput.Action(name).IsPressed();
        }


        public override bool GetButtonDown(string name)
        {
            return ArmokInput.Action(name).WasPressedThisFrame();
        }


        public override bool GetButtonUp(string name)
        {
            return ArmokInput.Action(name).WasReleasedThisFrame();
        }


        public override void SetButtonDown(string name)
        {
            throw new Exception(
                " This is not possible to be called for standalone input. Please check your platform and code where this is called");
        }


        public override void SetButtonUp(string name)
        {
            throw new Exception(
                " This is not possible to be called for standalone input. Please check your platform and code where this is called");
        }


        public override void SetAxisPositive(string name)
        {
            throw new Exception(
                " This is not possible to be called for standalone input. Please check your platform and code where this is called");
        }


        public override void SetAxisNegative(string name)
        {
            throw new Exception(
                " This is not possible to be called for standalone input. Please check your platform and code where this is called");
        }


        public override void SetAxisZero(string name)
        {
            throw new Exception(
                " This is not possible to be called for standalone input. Please check your platform and code where this is called");
        }


        public override void SetAxis(string name, float value)
        {
            throw new Exception(
                " This is not possible to be called for standalone input. Please check your platform and code where this is called");
        }


        public override Vector3 MousePosition()
        {
            return ArmokInput.PointerPosition;
        }
    }
}
