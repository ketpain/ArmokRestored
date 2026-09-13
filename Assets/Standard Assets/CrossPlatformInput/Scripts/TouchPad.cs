using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityStandardAssets.CrossPlatformInput
{
    [RequireComponent(typeof(Image))]
    public class TouchPad : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public enum AxisOption { Both, OnlyHorizontal, OnlyVertical }
        public enum ControlStyle { Absolute, Relative, Swipe }

        public AxisOption axesToUse = AxisOption.Both;
        public ControlStyle controlStyle = ControlStyle.Absolute;
        public string horizontalAxisName = "Horizontal";
        public string verticalAxisName = "Vertical";
        public float Xsensitivity = 1f;
        public float Ysensitivity = 1f;

        private bool useX;
        private bool useY;
        private bool dragging;
        private int pointerId;
        private Vector2 center;
        private CrossPlatformInputManager.VirtualAxis horizontalAxis;
        private CrossPlatformInputManager.VirtualAxis verticalAxis;

        private void OnEnable()
        {
            useX = axesToUse != AxisOption.OnlyVertical;
            useY = axesToUse != AxisOption.OnlyHorizontal;
            if (useX)
            {
                horizontalAxis = new CrossPlatformInputManager.VirtualAxis(horizontalAxisName);
                CrossPlatformInputManager.RegisterVirtualAxis(horizontalAxis);
            }
            if (useY)
            {
                verticalAxis = new CrossPlatformInputManager.VirtualAxis(verticalAxisName);
                CrossPlatformInputManager.RegisterVirtualAxis(verticalAxis);
            }
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (dragging)
                return;
            dragging = true;
            pointerId = data.pointerId;
            center = controlStyle == ControlStyle.Absolute
                ? RectTransformUtility.WorldToScreenPoint(data.pressEventCamera, transform.position)
                : data.position;
        }

        public void OnDrag(PointerEventData data)
        {
            if (!dragging || data.pointerId != pointerId)
                return;

            // Use the UI event's pointer identity and position for both mouse and touch.
            Vector2 delta = controlStyle == ControlStyle.Swipe ? data.delta : data.position - center;
            delta = delta.normalized;
            delta.x *= Xsensitivity;
            delta.y *= Ysensitivity;
            UpdateAxes(delta);
        }

        private void LateUpdate()
        {
            if (controlStyle == ControlStyle.Swipe)
                UpdateAxes(Vector2.zero);
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (!dragging || data.pointerId != pointerId)
                return;
            dragging = false;
            UpdateAxes(Vector2.zero);
        }

        private void UpdateAxes(Vector2 value)
        {
            if (useX)
                horizontalAxis.Update(value.x);
            if (useY)
                verticalAxis.Update(value.y);
        }

        private void OnDisable()
        {
            dragging = false;
            horizontalAxis?.Remove();
            verticalAxis?.Remove();
        }
    }
}
