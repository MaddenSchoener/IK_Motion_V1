using UnityEngine;

public class XRHandsetIKDriver : MonoBehaviour
{
    [Header("XR Controllers (source)")]
    [Tooltip("Right controller Transform from the XR Origin rig")]
    public Transform rightController;
    [Tooltip("Left controller Transform from the XR Origin rig")]
    public Transform leftController;

    [Header("IK Targets (destination)")]
    public Transform rightTarget;
    public Transform leftTarget;

    [Header("Options")]
    [Tooltip("If true, copies controller rotation too. The solver ignores it, but keeps the gizmo aligned with the handset.")]
    public bool copyRotation = true;

    void LateUpdate()
    {
        if (rightController != null && rightTarget != null)
        {
            rightTarget.position = rightController.position;
            if (copyRotation) rightTarget.rotation = rightController.rotation;
        }

        if (leftController != null && leftTarget != null)
        {
            leftTarget.position = leftController.position;
            if (copyRotation) leftTarget.rotation = leftController.rotation;
        }
    }
}
