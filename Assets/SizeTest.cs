using UnityEngine.InputSystem;
using UnityEngine;
using System.Collections;

public class SizeTest : MonoBehaviour
{

    float size = 1.0f;
    public UnityEngine.UI.Text sizetext;

    // Use this for initialization
    void Start()
    {
        sizetext.text = "Size: " + size.ToString();
        transform.localScale = new Vector3(size, size, size);
    }

    // Update is called once per frame
    void Update()
    {
        if ((Keyboard.current?.rightArrowKey.wasPressedThisFrame == true))
        {
            size += 0.1f;
            transform.localScale = new Vector3(size, size, size);
            sizetext.text = "Size: " + size.ToString();
        }
        if ((Keyboard.current?.leftArrowKey.wasPressedThisFrame == true))
        {
            size -= 0.1f;
            transform.localScale = new Vector3(size, size, size);
            sizetext.text = "Size: " + size.ToString();
        }
    }
}
