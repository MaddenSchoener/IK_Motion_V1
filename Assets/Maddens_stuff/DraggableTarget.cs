using UnityEngine;
using UnityEngine.InputSystem;


/// Allows the user to click and drag this object in the 3D scene.
/// Drags along a plane parallel to the camera at the object's depth.
/// Uses the new Input System.
/// 
/// Setup:
///   - Attach to a Sphere GameObject.
///   - Make sure the sphere has a Collider (SphereCollider is added by default).
///   - Requires a Camera tagged "MainCamera" in the scene.

public class DraggableTarget : MonoBehaviour
{
    private Camera mainCam;
    private bool isDragging = false;
    private float dragDepth;
    private Vector3 dragOffset;

    [Header("Visual Feedback")]
    [Tooltip("Color when not being dragged")]
    public Color idleColor = Color.red;

    [Tooltip("Color while being dragged")]
    public Color dragColor = Color.yellow;

    private Renderer rend;

    void Start()
    {
        mainCam = Camera.main;
        rend = GetComponent<Renderer>();

        if (rend != null)
        {
            rend.material.color = idleColor;
        }
    }

    void OnMouseDown()
    {
        if (mainCam == null) return;

        isDragging = true;

        // Calculate the depth of this object from the camera
        dragDepth = mainCam.WorldToScreenPoint(transform.position).z;

        // Calculate offset so the object doesn't snap to the mouse center
        Vector3 screenPos = new Vector3(Input.mousePosition.x, Input.mousePosition.y, dragDepth);
        dragOffset = transform.position - mainCam.ScreenToWorldPoint(screenPos);

        if (rend != null)
            rend.material.color = dragColor;
    }

    void OnMouseDrag()
    {
        if (!isDragging || mainCam == null) return;

        Vector3 screenPos = new Vector3(Input.mousePosition.x, Input.mousePosition.y, dragDepth);
        Vector3 worldPos = mainCam.ScreenToWorldPoint(screenPos) + dragOffset;
        transform.position = worldPos;
    }

    void OnMouseUp()
    {
        isDragging = false;

        if (rend != null)
            rend.material.color = idleColor;
    }

    /// <summary>
    /// Scroll wheel adjusts the drag depth (moves target closer/farther from camera).
    /// Only active while dragging.
    /// </summary>
    void Update()
    {
        if (isDragging)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                dragDepth += scroll * 0.01f; // new Input System gives larger raw values
                dragDepth = Mathf.Max(dragDepth, 1f);
            }
        }
    }
}
