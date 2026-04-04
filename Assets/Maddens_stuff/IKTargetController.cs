using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Click the target sphere to select it.
/// While selected: WASD moves it horizontally, Q/E moves it up/down.
/// Click again on empty space or press Escape to deselect.
///
/// Setup:
///   - Attach to a Sphere with a Collider.
///   - Requires a Camera tagged "MainCamera".
/// </summary>
public class IKTargetController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 3f;

    [Header("Visual Feedback")]
    public Color idleColor = Color.red;
    public Color selectedColor = Color.yellow;

    private bool isSelected = false;
    private Renderer rend;
    private Camera mainCam;

    void Start()
    {
        mainCam = Camera.main;
        rend = GetComponent<Renderer>();
        if (rend != null)
            rend.material.color = idleColor;
    }

    void Update()
    {
        Mouse mouse = Mouse.current;
        Keyboard kb = Keyboard.current;
        if (mouse == null || kb == null) return;

        // Click to select / deselect
        if (mouse.leftButton.wasPressedThisFrame)
        {
            Ray ray = mainCam.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.transform == transform)
                {
                    // Clicked on this target -- toggle selection
                    SetSelected(!isSelected);
                }
                else if (isSelected)
                {
                    // Clicked on something else -- deselect
                    SetSelected(false);
                }
            }
            else if (isSelected)
            {
                // Clicked on nothing -- deselect
                SetSelected(false);
            }
        }

        // Escape to deselect
        if (kb.escapeKey.wasPressedThisFrame && isSelected)
        {
            SetSelected(false);
        }

        // Move target while selected
        if (isSelected)
        {
            Vector3 move = Vector3.zero;

            // Horizontal movement relative to camera's facing direction (projected onto XZ)
            Vector3 camForward = mainCam.transform.forward;
            camForward.y = 0f;
            camForward.Normalize();

            Vector3 camRight = mainCam.transform.right;
            camRight.y = 0f;
            camRight.Normalize();

            if (kb.wKey.isPressed) move += camForward;
            if (kb.sKey.isPressed) move -= camForward;
            if (kb.dKey.isPressed) move += camRight;
            if (kb.aKey.isPressed) move -= camRight;

            // Vertical
            if (kb.eKey.isPressed) move += Vector3.up;
            if (kb.qKey.isPressed) move -= Vector3.up;

            transform.position += move.normalized * moveSpeed * Time.deltaTime;
        }
    }

    void SetSelected(bool selected)
    {
        isSelected = selected;
        if (rend != null)
            rend.material.color = selected ? selectedColor : idleColor;
    }
}
