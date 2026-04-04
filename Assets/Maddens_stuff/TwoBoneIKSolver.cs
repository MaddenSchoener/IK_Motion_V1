using UnityEngine;

/// <summary>
/// Two-bone IK solver for a robot arm with three servos:
///
///   Servo 1 - Shoulder Pitch:  rotates around world X axis, swings upper arm forward/back.
///                               The upper arm is locked to the XY plane (no yaw/turntable).
///
///   Servo 2 - Upper Arm Roll:  rotates the upper arm around its own length axis.
///                               This doesn't move the upper arm, but it determines which
///                               direction the elbow hinge faces (forearm swings in/out).
///
///   Servo 3 - Elbow Pitch:     bends the forearm in the plane defined by the roll.
///                               Clamped to +/-90 deg from straight. Cannot fold through the upper arm.
///
/// Rest pose: arm hangs straight down (-Y), roll = 0, elbow straight.
///
/// Setup:
///   - Attach to an empty GameObject at the shoulder position.
///   - Create separate GameObjects for ElbowPivot, UpperArmCube, ForearmCube, target sphere.
///   - Do NOT parent visuals/elbow under the shoulder -- the script manages all transforms.
/// </summary>
public class TwoBoneIKSolver : MonoBehaviour
{
    [Header("References")]
    public Transform elbowPivot;
    public Transform upperArmVisual;
    public Transform forearmVisual;
    public Transform target;

    [Header("Arm Dimensions")]
    public float upperArmLength = 2f;
    public float forearmLength = 1.5f;

    [Header("Visual Settings")]
    public float armThickness = 0.3f;

    [Header("Timing")]
    [Tooltip("Seconds to animate to a new pose")]
    public float solveInterval = 1f;

    [Header("Dead Zone")]
    [Tooltip("Target must move this far to trigger a new solve")]
    public float moveThreshold = 0.05f;

    [Header("Elbow Limits")]
    [Tooltip("Max bend angle forward from straight (degrees)")]
    public float elbowMaxForward = 90f;
    [Tooltip("Max bend angle backward from straight (degrees)")]
    public float elbowMaxBackward = 90f;

    // --- Interpolation state ---
    private float shoulderPitchStart, shoulderPitchGoal;
    private float upperArmRollStart, upperArmRollGoal;
    private float elbowBendStart, elbowBendGoal;

    private float lerpT = 1f;
    private Vector3 lastSolvedTargetPos;
    private bool hasInitialized = false;

    // Current applied values
    private float currentPitch, currentRoll, currentElbowBend;

    void Start()
    {
        if (upperArmVisual != null)
            upperArmVisual.localScale = new Vector3(armThickness, armThickness, upperArmLength);
        if (forearmVisual != null)
            forearmVisual.localScale = new Vector3(armThickness, armThickness, forearmLength);

        if (target != null)
        {
            SolveIK(target.position, out shoulderPitchGoal, out upperArmRollGoal, out elbowBendGoal);
            shoulderPitchStart = shoulderPitchGoal;
            upperArmRollStart = upperArmRollGoal;
            elbowBendStart = elbowBendGoal;

            currentPitch = shoulderPitchGoal;
            currentRoll = upperArmRollGoal;
            currentElbowBend = elbowBendGoal;

            lastSolvedTargetPos = target.position;
            lerpT = 1f;
            hasInitialized = true;

            ApplyPose(currentPitch, currentRoll, currentElbowBend);
        }
    }

    void Update()
    {
        if (target == null || elbowPivot == null) return;

        if (hasInitialized)
        {
            float distMoved = Vector3.Distance(target.position, lastSolvedTargetPos);

            if (distMoved > moveThreshold && lerpT >= 1f)
            {
                BeginNewSolve();
            }
        }

        if (lerpT < 1f)
        {
            lerpT += Time.deltaTime / solveInterval;
            lerpT = Mathf.Clamp01(lerpT);

            float t = Mathf.SmoothStep(0f, 1f, lerpT);

            currentPitch = Mathf.Lerp(shoulderPitchStart, shoulderPitchGoal, t);
            currentRoll = Mathf.Lerp(upperArmRollStart, upperArmRollGoal, t);
            currentElbowBend = Mathf.Lerp(elbowBendStart, elbowBendGoal, t);

            ApplyPose(currentPitch, currentRoll, currentElbowBend);
        }
    }

    void BeginNewSolve()
    {
        shoulderPitchStart = currentPitch;
        upperArmRollStart = currentRoll;
        elbowBendStart = currentElbowBend;

        SolveIK(target.position, out shoulderPitchGoal, out upperArmRollGoal, out elbowBendGoal);

        // Unwrap angles so lerp takes the shortest path.
        // If the difference is more than 180 deg, adjust the goal by +/-360.
        shoulderPitchGoal = UnwrapAngle(shoulderPitchStart, shoulderPitchGoal);
        upperArmRollGoal = UnwrapAngle(upperArmRollStart, upperArmRollGoal);
        elbowBendGoal = UnwrapAngle(elbowBendStart, elbowBendGoal);

        lastSolvedTargetPos = target.position;
        lerpT = 0f;
    }

    /// <summary>
    /// Adjusts 'goal' so that the difference from 'start' is within -180 to +180 degrees.
    /// This ensures Mathf.Lerp takes the shortest rotational path.
    /// </summary>
    float UnwrapAngle(float start, float goal)
    {
        float diff = goal - start;
        while (diff > 180f) diff -= 360f;
        while (diff < -180f) diff += 360f;
        return start + diff;
    }

    /// <summary>
    /// Solves for the three servo angles given a target position.
    ///
    /// STEP 1 - SHOULDER PITCH + ELBOW BEND (2D triangle solve):
    ///   The shoulder pitches around X, so the upper arm swings in the YZ plane
    ///   (well, really the "down/forward" plane since it rests at -Y).
    ///   We compute the distance from the shoulder to the target, form a triangle
    ///   with the two arm segments, and use law of cosines to find the shoulder
    ///   pitch and the elbow bend angle. This gets the wrist to the correct
    ///   distance from the shoulder -- but only in the pitch plane.
    ///
    /// STEP 2 - UPPER ARM ROLL:
    ///   The pitch solve works in a 2D plane, but the target might be off to the
    ///   side (in X). The roll twists the upper arm so the elbow hinge faces the
    ///   right direction, swinging the forearm toward the target's X position.
    ///   
    ///   To find the roll angle: once pitch places the elbow in world space, we
    ///   look at where the target is relative to the elbow. The roll needs to
    ///   rotate the forearm's bend plane so it contains the target. This is
    ///   computed by finding the angle of the target around the upper arm axis.
    /// </summary>
    void SolveIK(Vector3 targetPos, out float shoulderPitch, out float upperArmRoll, out float elbowBend)
    {
        Vector3 shoulderPos = transform.position;
        Vector3 toTarget = targetPos - shoulderPos;

        // ============================
        // STEP 1: Shoulder Pitch + Elbow Bend
        // ============================
        // The pitch swings the arm in a plane containing the arm axis and the Y axis.
        // We need the distance from shoulder to target to solve the triangle.
        float d = toTarget.magnitude;

        float totalReach = upperArmLength + forearmLength;
        float minReach = Mathf.Abs(upperArmLength - forearmLength);
        d = Mathf.Clamp(d, minReach + 0.001f, totalReach - 0.001f);

        // Law of cosines: shoulder angle (angle between upper arm and line to target)
        float cosShoulderAngle = (upperArmLength * upperArmLength + d * d - forearmLength * forearmLength)
                                 / (2f * upperArmLength * d);
        cosShoulderAngle = Mathf.Clamp(cosShoulderAngle, -1f, 1f);
        float shoulderTriAngle = Mathf.Acos(cosShoulderAngle) * Mathf.Rad2Deg;

        // Angle from straight down (-Y) to the target, measured in the pitch plane.
        // We use the full 3D distance projected: vertical = -toTarget.y, horizontal = sqrt(x^2 + z^2)
        // But since pitch only operates in the vertical plane, horizontal distance is
        // the XZ distance, and vertical is Y.
        float horizontalDist = Mathf.Sqrt(toTarget.x * toTarget.x + toTarget.z * toTarget.z);
        float angleToTarget = Mathf.Atan2(horizontalDist, -toTarget.y) * Mathf.Rad2Deg;

        // Pitch = angle to target - triangle offset (elbow-forward solution)
        shoulderPitch = angleToTarget - shoulderTriAngle;

        // Law of cosines: elbow angle (interior angle at elbow vertex)
        float cosElbowAngle = (upperArmLength * upperArmLength + forearmLength * forearmLength - d * d)
                              / (2f * upperArmLength * forearmLength);
        cosElbowAngle = Mathf.Clamp(cosElbowAngle, -1f, 1f);
        float rawElbowAngle = Mathf.Acos(cosElbowAngle) * Mathf.Rad2Deg;

        // Convert: 180 deg interior = straight (0 deg bend), less interior = more bend
        elbowBend = 180f - rawElbowAngle;

        // Self-collision clamp
        elbowBend = Mathf.Clamp(elbowBend, -elbowMaxBackward, elbowMaxForward);

        // ============================
        // STEP 2: Upper Arm Roll
        // ============================
        // The pitch puts the arm in the correct vertical plane, but if the target
        // is off to the side (in X), the roll needs to twist the upper arm so the
        // elbow's bend plane contains the target.
        //
        // After pitch is applied, the upper arm direction is known. We find where
        // the elbow is, then compute the angle from the elbow to the target
        // around the upper arm axis.
        //
        // We work this out by temporarily computing the elbow position after pitch,
        // then projecting the target onto the plane perpendicular to the upper arm
        // at the elbow, and finding the angle.

        // Upper arm direction after pitch (pitch rotates around X, arm rests at -Y)
        Quaternion pitchRot = Quaternion.AngleAxis(-shoulderPitch, Vector3.right);
        Vector3 upperArmDir = pitchRot * Vector3.down; // rotated -Y

        Vector3 elbowPos = shoulderPos + upperArmDir * upperArmLength;
        Vector3 elbowToTarget = targetPos - elbowPos;

        // Project elbowToTarget onto the plane perpendicular to upperArmDir
        Vector3 projected = elbowToTarget - Vector3.Dot(elbowToTarget, upperArmDir) * upperArmDir;

        if (projected.sqrMagnitude < 0.0001f)
        {
            // Target is directly along the upper arm axis -- roll doesn't matter
            upperArmRoll = 0f;
        }
        else
        {
            // We need a reference direction in the perpendicular plane to measure the angle from.
            // The "default" bend direction (roll=0) after pitch is the direction the forearm
            // would go if it just continued bending in the pitch plane.
            // After pitching around X, the bend plane normal is the X axis,
            // and the default bend direction is perpendicular to upperArmDir in the YZ plane.
            Vector3 defaultBendDir = Vector3.Cross(Vector3.right, upperArmDir).normalized;

            // If this is zero (arm pointing along X, which shouldn't happen with X-axis pitch),
            // fall back to forward
            if (defaultBendDir.sqrMagnitude < 0.0001f)
                defaultBendDir = Vector3.forward;

            Vector3 perpRef = Vector3.Cross(upperArmDir, defaultBendDir).normalized;

            // Angle of the projected target direction relative to the default bend direction,
            // measured around the upper arm axis
            float projOnDefault = Vector3.Dot(projected.normalized, defaultBendDir);
            float projOnPerp = Vector3.Dot(projected.normalized, perpRef);

            upperArmRoll = Mathf.Atan2(projOnPerp, projOnDefault) * Mathf.Rad2Deg;
        }
    }

    /// <summary>
    /// Applies the three servo angles and positions all visuals.
    ///
    /// Build order:
    ///   1. Pitch rotates shoulder around world X axis - upper arm swings forward/back
    ///   2. Roll rotates upper arm around its own axis - changes elbow bend plane
    ///   3. Elbow bends forearm in the plane defined by pitch + roll
    /// </summary>
    void ApplyPose(float pitch, float roll, float elbowBendAngle)
    {
        // --- Shoulder Pitch ---
        // Rotate around world X. Negative so positive pitch swings arm forward.
        Quaternion pitchRot = Quaternion.AngleAxis(-pitch, Vector3.right);

        // Upper arm direction: -Y (down) rotated by pitch only
        // (roll doesn't change the arm direction, just twists around it)
        Vector3 upperArmDir = pitchRot * Vector3.down;
        Vector3 elbowPos = transform.position + upperArmDir * upperArmLength;
        elbowPivot.position = elbowPos;

        // --- Upper Arm Roll ---
        // Roll rotates the entire arm assembly around the upper arm's axis.
        // This determines which plane the elbow hinge bends in.
        Quaternion rollRot = Quaternion.AngleAxis(roll, upperArmDir);

        // Combined shoulder rotation: pitch then roll.
        // Both upper arm and forearm live in the plane this establishes.
        Quaternion shoulderRot = rollRot * pitchRot;
        transform.localRotation = shoulderRot;

        // --- Elbow Bend (hinge) ---
        // The bend axis is the shoulder's local X axis after pitch + roll.
        // This is the hinge axis -- perpendicular to the arm plane.
        Vector3 bendAxis = transform.right;

        // At bend=0 deg, forearm continues straight along upper arm.
        // Positive bend rotates the forearm within the arm plane.
        Quaternion elbowRot = Quaternion.AngleAxis(elbowBendAngle, bendAxis);
        Vector3 forearmDir = elbowRot * upperArmDir;

        elbowPivot.rotation = Quaternion.LookRotation(forearmDir, transform.forward);

        // --- Visuals ---
        // Both pieces share the same plane (defined by pitch + roll).
        // The only difference is the forearm is additionally rotated by the hinge.
        if (upperArmVisual != null)
        {
            upperArmVisual.position = transform.position + upperArmDir * (upperArmLength / 2f);
            upperArmVisual.rotation = Quaternion.LookRotation(upperArmDir, transform.forward);
            upperArmVisual.localScale = new Vector3(armThickness, armThickness, upperArmLength);
        }

        if (forearmVisual != null)
        {
            forearmVisual.position = elbowPos + forearmDir * (forearmLength / 2f);
            forearmVisual.rotation = Quaternion.LookRotation(forearmDir, transform.forward);
            forearmVisual.localScale = new Vector3(armThickness, armThickness, forearmLength);
        }
    }

    void OnDrawGizmos()
    {
        if (elbowPivot != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, elbowPivot.position);
        }

        if (target != null && forearmVisual != null)
        {
            Gizmos.color = Color.cyan;
            Vector3 forearmEnd = forearmVisual.position + forearmVisual.forward * (forearmLength / 2f);
            Gizmos.DrawLine(elbowPivot.position, forearmEnd);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(target.position, 0.15f);
        }

        // Draw pitch axis (world X)
        Gizmos.color = Color.green;
        Gizmos.DrawRay(transform.position, Vector3.right * 0.5f);

        // Draw upper arm roll axis
        if (Application.isPlaying)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawRay(transform.position, -(transform.up) * 0.5f);
        }
    }
}