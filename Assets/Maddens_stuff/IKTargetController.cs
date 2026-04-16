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

    [Header("Chest Collision")]
    public Transform chestTransform;

    [Header("Visual Feedback")]
    public Color idleColor = Color.red;
    public Color selectedColor = Color.yellow;

    private bool isSelected = false;
    private Renderer rend;
    private Camera mainCam;
    private BoxCollider chestBox;
    private Vector3 chestBoxCenter;
    private Vector3 chestBoxHalfSize;

    void Start()
    {
        mainCam = Camera.main;
        rend = GetComponent<Renderer>();
        if (rend != null)
            rend.material.color = idleColor;

        if (chestTransform != null)
        {
            chestBox = chestTransform.GetComponent<BoxCollider>();
            chestBoxHalfSize = chestBox != null
                ? Vector3.Scale(chestBox.size * 0.5f, chestTransform.lossyScale)
                : Vector3.Scale(Vector3.one * 0.5f, chestTransform.lossyScale);
            chestBoxHalfSize += Vector3.one * 0.15f;
            chestBoxCenter = chestTransform.position + (chestBox != null ? chestBox.center : Vector3.zero);
        }
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

            Vector3 newPos = transform.position + move.normalized * moveSpeed * Time.deltaTime;
            transform.position = ClampOutsideChest(newPos);
        }
    }

    Vector3 ClampOutsideChest(Vector3 desiredPos)
    {
        if (chestTransform == null) return desiredPos;

        Vector3 delta = desiredPos - chestBoxCenter;
        float ox = chestBoxHalfSize.x - Mathf.Abs(delta.x);
        float oy = chestBoxHalfSize.y - Mathf.Abs(delta.y);
        float oz = chestBoxHalfSize.z - Mathf.Abs(delta.z);

        if (ox > 0 && oy > 0 && oz > 0)
        {
            // Push out along the axis with least penetration
            Vector3 clamped = desiredPos;
            if (ox <= oy && ox <= oz)
                clamped.x = chestBoxCenter.x + Mathf.Sign(delta.x) * chestBoxHalfSize.x;
            else if (oy <= ox && oy <= oz)
                clamped.y = chestBoxCenter.y + Mathf.Sign(delta.y) * chestBoxHalfSize.y;
            else
                clamped.z = chestBoxCenter.z + Mathf.Sign(delta.z) * chestBoxHalfSize.z;
            return clamped;
        }

        return desiredPos;
    }

    void SetSelected(bool selected)
    {
        isSelected = selected;
        if (rend != null)
            rend.material.color = selected ? selectedColor : idleColor;
    }
}
