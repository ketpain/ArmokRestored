using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;

public class CameraRotate : MonoBehaviour {

    private float mouseX;

    public float rotateSpeed = 10.0f;

    public float moveSpeed = 1.0f;

    
	void Update()
    {
        HandleMouseRotation();

        mouseX = ArmokInput.PointerPosition.x;
    }

    public void HandleMouseRotation()
    {
        if (ArmokInput.Action("Orbit").IsPressed() && EventSystem.current.currentSelectedGameObject == null)
        {
            //horizontal rotation
            if(ArmokInput.PointerPosition.x != mouseX)
            {
                float cameraRotationY = (ArmokInput.PointerPosition.x - mouseX) * rotateSpeed * Time.unscaledDeltaTime;
                this.transform.Rotate(0, cameraRotationY, 0);
            }
        }
    }
}
