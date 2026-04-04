using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Orbit camera for inspecting the arm.
/// Right click + drag = orbit around focus point.
/// Scroll wheel = zoom in/out.
/// Middle click + drag = pan.
///
/// Setup:
///   - Attach to Main Camera.
///   - Optionally assign a focus target (defaults to origin).
/// </summary>
public class OrbitCamera : MonoBehaviour
{
    [Header("Focus")]
    [Tooltip("Point to orbit around. If null, orbits around origin.")]
    public Transform focusTarget;

    [Header("Settings")]
    public float orbitSpeed = 5f;
    public float zoomSpeed = 2f;
    public float panSpeed = 0.01f;
    public float minDistance = 2f;
    public float maxDistance = 30f;

    private Vector3 focusPoint;
    private float distance;
    private float yaw;
    private float pitch;

    void Start()
    {
        focusPoint = focusTarget != null ? focusTarget.position : Vector3.zero;

        // Initialize from current camera position
        Vector3 offset = transform.position - focusPoint;
        distance = offset.magnitude;
        yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        pitch = Mathf.Asin(offset.y / distance) * Mathf.Rad2Deg;

        UpdateCameraPosition();
    }

    void LateUpdate()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        // Right click + drag = orbit
        if (mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * orbitSpeed * Time.deltaTime;
            pitch -= delta.y * orbitSpeed * Time.deltaTime;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
        }

        // Middle click + drag = pan
        if (mouse.middleButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            Vector3 right = transform.right * (-delta.x * panSpeed * distance);
            Vector3 up = transform.up * (-delta.y * panSpeed * distance);
            focusPoint += right + up;
        }

        // Scroll = zoom
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            distance -= scroll * zoomSpeed * Time.deltaTime;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        // Update focus if tracking a target
        if (focusTarget != null)
            focusPoint = focusTarget.position;

        UpdateCameraPosition();
    }

    void UpdateCameraPosition()
    {
        float pitchRad = pitch * Mathf.Deg2Rad;
        float yawRad = yaw * Mathf.Deg2Rad;

        Vector3 offset = new Vector3(
            Mathf.Cos(pitchRad) * Mathf.Sin(yawRad),
            Mathf.Sin(pitchRad),
            Mathf.Cos(pitchRad) * Mathf.Cos(yawRad)
        ) * distance;

        transform.position = focusPoint + offset;
        transform.LookAt(focusPoint);
    }
}
