using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple camera controller.
/// W/S = move forward/back, A/D = turn left/right.
/// Hold right mouse button + move mouse to look up/down.
/// </summary>
public class CameraController : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float turnSpeed = 90f;
    public float lookSpeed = 2f;

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        // Forward / back
        if (kb.wKey.isPressed)
            transform.position += transform.forward * moveSpeed * Time.deltaTime;
        if (kb.sKey.isPressed)
            transform.position -= transform.forward * moveSpeed * Time.deltaTime;

        // Turn left / right
        if (kb.aKey.isPressed)
            transform.Rotate(0f, -turnSpeed * Time.deltaTime, 0f);
        if (kb.dKey.isPressed)
            transform.Rotate(0f, turnSpeed * Time.deltaTime, 0f);

        // Right mouse + mouse Y = look up/down
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.isPressed)
        {
            float mouseY = mouse.delta.ReadValue().y * lookSpeed * Time.deltaTime;
            transform.Rotate(-mouseY, 0f, 0f);
        }
    }
}
