using UnityEngine;


/// Two-bone IK solver with a 1-DOF shoulder (pitch only) and 3-DOF elbow.
/// When the target moves, the arm smoothly animates to the new pose over solveInterval seconds.
/// If the target hasn't moved, the arm stays still
/// 
/// Setup:
///   - Attach this script to an empty GameObject (the "shoulder pivot").
///   - Create two cubes (UpperArm, Forearm) and an elbow pivot — NOT parented to shoulder.
///   - Create a sphere as the drag target.
///   - Assign all references in the Inspector.

public class TwoBoneIKSolver : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The transform that acts as the elbow pivot point")] //where the elbow pivot will end up being, this script puts it automatically
    public Transform elbowPivot;

    [Tooltip("The upper arm visual (cube)")] //the cube that is for the visual and detection of the program
    public Transform upperArmVisual;

    [Tooltip("The forearm visual (cube)")] //this will be the forearm, same as above, it is for visual and collision detection
    public Transform forearmVisual;

    [Tooltip("The draggable target sphere")] //this is the IK position point, which will end up being controlled by VR
    public Transform target;

    [Header("Arm Dimensions")] //This is where we will create the dimensions for the arms
    public float upperArmLength = 2f;
    public float forearmLength = 1.5f;

    [Header("Visual Settings")] //this is the thickness of the arms
    public float armThickness = 0.3f;

    [Header("Timing")] //the interval over which the arm will move to the new position
    [Tooltip("Duration in seconds for the arm to animate to a new pose")]
    public float solveInterval = 1f;

    [Header("Dead Zone")] //to keep the arm from bouncing, once it is close enough to the target, it will rest and be happy
    [Tooltip("Minimum distance the target must move before a new solve is triggered")]
    public float moveThreshold = 0.05f;

    // --- Interpolation state ---
    private Quaternion shoulderRotStart;
    private Quaternion shoulderRotGoal;
    private Quaternion elbowRotStart;
    private Quaternion elbowRotGoal;

    private float lerpT = 1f; // 0 = at start pose, 1 = at goal pose
    private Vector3 lastSolvedTargetPos;
    private bool hasInitialized = false;

    void Start()
    {
        // Scale visuals
        if (upperArmVisual != null)
            upperArmVisual.localScale = new Vector3(armThickness, armThickness, upperArmLength);
        if (forearmVisual != null)
            forearmVisual.localScale = new Vector3(armThickness, armThickness, forearmLength);

        // Solve immediately to set initial pose
        if (target != null)
        {
            SolveIKPose(target.position, out shoulderRotGoal, out elbowRotGoal);
            shoulderRotStart = shoulderRotGoal;
            elbowRotStart = elbowRotGoal;
            lastSolvedTargetPos = target.position;
            lerpT = 1f;
            hasInitialized = true;

            ApplyPose(shoulderRotGoal, elbowRotGoal);
        }
    }

    void Update()
    {
        if (target == null || elbowPivot == null) return;

        if (hasInitialized)
        {
            float distMoved = Vector3.Distance(target.position, lastSolvedTargetPos);

            // Only start a new solve if the target moved enough AND the last animation finished
            if (distMoved > moveThreshold && lerpT >= 1f)
            {
                BeginNewSolve();
            }
        }

        // Animate toward the goal
        if (lerpT < 1f)
        {
            lerpT += Time.deltaTime / solveInterval;
            lerpT = Mathf.Clamp01(lerpT);

            // SmoothStep for nice ease-in / ease-out
            float t = Mathf.SmoothStep(0f, 1f, lerpT);

            Quaternion shoulderRot = Quaternion.Slerp(shoulderRotStart, shoulderRotGoal, t);
            Quaternion elbowRot = Quaternion.Slerp(elbowRotStart, elbowRotGoal, t);

            ApplyPose(shoulderRot, elbowRot);
        }
    }


    /// Begins a new interpolation from the current pose to a freshly solved pose.

    void BeginNewSolve()
    {
        // Snapshot current pose as the start
        shoulderRotStart = transform.localRotation;
        elbowRotStart = elbowPivot.rotation;

        // Solve for the new goal
        SolveIKPose(target.position, out shoulderRotGoal, out elbowRotGoal);

        lastSolvedTargetPos = target.position;
        lerpT = 0f;
    }


    /// Pure math — computes shoulder and elbow rotations for a given target
    /// without applying them. Keeps the solve separate from the animation.

    void SolveIKPose(Vector3 targetPos, out Quaternion shoulderRot, out Quaternion elbowRot)
    {
        // Temporarily reset shoulder to identity so we get a clean local-space target
        Quaternion originalRot = transform.localRotation;
        transform.localRotation = Quaternion.identity;

        Vector3 localTarget = transform.InverseTransformPoint(targetPos);

        // Distance in the YZ plane (shoulder pitches around X axis)
        float distYZ = Mathf.Sqrt(localTarget.y * localTarget.y + localTarget.z * localTarget.z);

        float totalReach = upperArmLength + forearmLength;
        float minReach = Mathf.Abs(upperArmLength - forearmLength);
        float d = Mathf.Clamp(distYZ, minReach + 0.001f, totalReach - 0.001f);

        // Law of cosines — shoulder offset angle
        float cosShoulderOffset = (upperArmLength * upperArmLength + d * d - forearmLength * forearmLength)
                                  / (2f * upperArmLength * d);
        cosShoulderOffset = Mathf.Clamp(cosShoulderOffset, -1f, 1f);
        float shoulderOffset = Mathf.Acos(cosShoulderOffset) * Mathf.Rad2Deg;

        // Angle from forward (local Z) to target in YZ plane
        float angleToTarget = Mathf.Atan2(localTarget.y, localTarget.z) * Mathf.Rad2Deg;

        // Always use elbow-down solution (subtract offset) to prevent flipping
        float shoulderPitch = angleToTarget - shoulderOffset;
        shoulderRot = Quaternion.AngleAxis(-shoulderPitch, Vector3.right);

        // Temporarily apply to find where the elbow ends up in world space
        transform.localRotation = shoulderRot;
        Vector3 elbowPos = transform.position + transform.forward * upperArmLength;

        // Elbow aims at target (full 3-DOF)
        Vector3 elbowToTarget = targetPos - elbowPos;
        if (elbowToTarget.sqrMagnitude > 0.0001f)
        {
            elbowRot = Quaternion.LookRotation(elbowToTarget.normalized);
        }
        else
        {
            elbowRot = transform.rotation;
        }

        // Restore rotation — the caller will apply the interpolated version
        transform.localRotation = originalRot;
    }


    /// Applies shoulder + elbow rotations and positions all visuals.

    void ApplyPose(Quaternion shoulderRot, Quaternion elbowRot)
    {
        transform.localRotation = shoulderRot;
        elbowPivot.rotation = elbowRot;

        // Position elbow pivot at end of upper arm
        elbowPivot.position = transform.position + transform.forward * upperArmLength;

        // Upper arm visual
        if (upperArmVisual != null)
        {
            upperArmVisual.position = transform.position + transform.forward * (upperArmLength / 2f);
            upperArmVisual.rotation = transform.rotation;
            upperArmVisual.localScale = new Vector3(armThickness, armThickness, upperArmLength);
        }

        // Forearm visual
        if (forearmVisual != null)
        {
            forearmVisual.position = elbowPivot.position + elbowPivot.forward * (forearmLength / 2f);
            forearmVisual.rotation = elbowPivot.rotation;
            forearmVisual.localScale = new Vector3(armThickness, armThickness, forearmLength);
        }
    }

    void OnDrawGizmos()
    {
        if (elbowPivot == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, elbowPivot.position);

        if (target != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(elbowPivot.position, target.position);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(target.position, 0.15f);
        }

        Gizmos.color = Color.green;
        Gizmos.DrawRay(transform.position, transform.right * 0.5f);
    }
}