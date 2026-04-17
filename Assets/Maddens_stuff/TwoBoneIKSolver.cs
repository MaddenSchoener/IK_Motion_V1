using UnityEngine;
using System.IO;

/// <summary>
/// Two-bone IK solver managing both arms from a single script.
/// Attach to the RightShoulderPivot. Left shoulder is a reference variable.
///
/// Both arms animate in parallel -- each arm progresses through its own
/// servo sequence (pitch, roll, elbow) at the same time. When instruction 1
/// finishes on both arms, both advance to instruction 2, etc.
///
/// CSV format (Instruction #, Servo, Degrees, Speed):
///   1,R1,45.00,50
///   1,L1,30.00,50
///   2,R2,60.00,50
///   2,L2,15.00,50
///   3,R3,20.00,50
///   3,L3,40.00,50
///
/// Setup:
///   - Attach to RightShoulderPivot.
///   - Assign all right and left arm references in the Inspector.
///   - Do NOT parent visuals/elbow pivots under shoulder pivots.
/// </summary>
public class TwoBoneIKSolver : MonoBehaviour
{
    [Header("=== RIGHT ARM ===")]
    public Transform rightElbowPivot;
    public Transform rightUpperArmVisual;
    public Transform rightForearmVisual;
    public Transform rightTarget;

    [Header("=== LEFT ARM ===")]
    public Transform leftShoulderPivot;
    public Transform leftElbowPivot;
    public Transform leftUpperArmVisual;
    public Transform leftForearmVisual;
    public Transform leftTarget;

    [Header("Arm Dimensions")]
    public float upperArmLength = 2f;
    public float forearmLength = 1.5f;

    [Header("Visual Settings")]
    public float armThickness = 0.3f;

    [Header("Timing")]
    [Tooltip("Seconds each servo takes to reach its goal")]
    public float servoMoveTime = 0.5f;

    [Header("Servo Speed")]
    [Tooltip("Speed value written to CSV (shared by all servos)")]
    public float servoSpeed = 50f;

    [Header("Dead Zone")]
    public float moveThreshold = 0.05f;

    [Header("Elbow Limits")]
    public float elbowMaxForward = 90f;
    public float elbowMaxBackward = 90f;

    [Header("Chest Avoidance")]
    public Transform chestTransform;
    [Tooltip("Extra padding around the chest bounding box")]
    public float avoidanceMargin = 0.3f;
    [Tooltip("How deep the arm path must penetrate the expanded box before full avoidance kicks in (smooth blend)")]
    public float avoidanceSmoothDist = 0.5f;

    [Header("CSV Output")]
    public string csvOutputFolder = "ServoCommands";

    // --- Per-arm state ---
    private class ArmState
    {
        // The three servo values: 0=pitch, 1=roll, 2=elbow
        public float[] servoStart = new float[3];
        public float[] servoGoal = new float[3];
        public float[] servoCurrent = new float[3];
        public Vector3 lastSolvedTargetPos;
        public bool hasInitialized;

        public float Pitch { get { return servoCurrent[0]; } }
        public float Roll { get { return servoCurrent[1]; } }
        public float Elbow { get { return servoCurrent[2]; } }
    }

    private ArmState rightArm = new ArmState();
    private ArmState leftArm = new ArmState();

    // Last confirmed collision-free servo angles for each arm (for safety revert)
    private float[] rightArmSafe = new float[3];
    private float[] leftArmSafe  = new float[3];

    // --- Parallel animation ---
    // Both arms go through 3 instructions (0, 1, 2) in sync.
    // Each instruction animates one servo per arm simultaneously.
    private int currentInstruction = -1; // -1 = idle, 0-2 = which servo pair is animating
    private float phaseT = 1f;

    // Cached chest geometry (computed once in Start)
    private BoxCollider chestBox;
    private Vector3 chestCenter;
    private Vector3 chestHalfSize; // world-space, without margin

    void Start()
    {
        if (chestTransform != null)
        {
            chestBox = chestTransform.GetComponent<BoxCollider>();
            chestHalfSize = chestBox != null
                ? Vector3.Scale(chestBox.size * 0.5f, chestTransform.lossyScale)
                : Vector3.Scale(Vector3.one * 0.5f, chestTransform.lossyScale);
            chestCenter = chestTransform.position + (chestBox != null ? chestBox.center : Vector3.zero);
        }

        SetVisualScale(rightUpperArmVisual, upperArmLength);
        SetVisualScale(rightForearmVisual, forearmLength);
        SetVisualScale(leftUpperArmVisual, upperArmLength);
        SetVisualScale(leftForearmVisual, forearmLength);

        // Initialize right arm
        if (rightTarget != null)
        {
            float p, r, e;
            Vector3 effR = ComputeEffectiveTarget(transform.position, rightTarget.position, true);
            SolveIK(transform.position, effR, out p, out r, out e);
            rightArm.servoStart[0] = p; rightArm.servoGoal[0] = p; rightArm.servoCurrent[0] = p;
            rightArm.servoStart[1] = r; rightArm.servoGoal[1] = r; rightArm.servoCurrent[1] = r;
            rightArm.servoStart[2] = e; rightArm.servoGoal[2] = e; rightArm.servoCurrent[2] = e;
            rightArm.lastSolvedTargetPos = rightTarget.position;
            rightArm.hasInitialized = true;
            ApplyPose(transform, rightElbowPivot, rightUpperArmVisual, rightForearmVisual,
                      p, r, e);
        }

        // Initialize left arm
        if (leftTarget != null && leftShoulderPivot != null)
        {
            float p, r, e;
            Vector3 effL = ComputeEffectiveTarget(leftShoulderPivot.position, leftTarget.position, false);
            SolveIK(leftShoulderPivot.position, effL, out p, out r, out e);
            leftArm.servoStart[0] = p; leftArm.servoGoal[0] = p; leftArm.servoCurrent[0] = p;
            leftArm.servoStart[1] = r; leftArm.servoGoal[1] = r; leftArm.servoCurrent[1] = r;
            leftArm.servoStart[2] = e; leftArm.servoGoal[2] = e; leftArm.servoCurrent[2] = e;
            leftArm.lastSolvedTargetPos = leftTarget.position;
            leftArm.hasInitialized = true;
            ApplyPose(leftShoulderPivot, leftElbowPivot, leftUpperArmVisual, leftForearmVisual,
                      p, r, e);
        }

        // Seed safe poses from initial state
        System.Array.Copy(rightArm.servoCurrent, rightArmSafe, 3);
        System.Array.Copy(leftArm.servoCurrent,  leftArmSafe,  3);
    }

    void Update()
    {
        // Check for new solve when idle
        if (currentInstruction < 0)
        {
            bool rightNeedsSolve = false;
            bool leftNeedsSolve = false;

            if (rightArm.hasInitialized && rightTarget != null)
            {
                if (Vector3.Distance(rightTarget.position, rightArm.lastSolvedTargetPos) > moveThreshold)
                    rightNeedsSolve = true;
            }

            if (leftArm.hasInitialized && leftTarget != null)
            {
                if (Vector3.Distance(leftTarget.position, leftArm.lastSolvedTargetPos) > moveThreshold)
                    leftNeedsSolve = true;
            }

            if (rightNeedsSolve || leftNeedsSolve)
            {
                BeginNewSolve(rightNeedsSolve, leftNeedsSolve);
            }
        }

        // Animate current instruction
        if (currentInstruction >= 0 && currentInstruction < 3)
        {
            phaseT += Time.deltaTime / servoMoveTime;
            phaseT = Mathf.Clamp01(phaseT);

            float t = Mathf.SmoothStep(0f, 1f, phaseT);
            int i = currentInstruction;

            // Lerp both arms' current servo in parallel
            rightArm.servoCurrent[i] = Mathf.Lerp(rightArm.servoStart[i], rightArm.servoGoal[i], t);
            leftArm.servoCurrent[i] = Mathf.Lerp(leftArm.servoStart[i], leftArm.servoGoal[i], t);

            // Apply both poses
            ApplyPose(transform, rightElbowPivot, rightUpperArmVisual, rightForearmVisual,
                      rightArm.Pitch, rightArm.Roll, rightArm.Elbow);

            if (leftShoulderPivot != null)
            {
                ApplyPose(leftShoulderPivot, leftElbowPivot, leftUpperArmVisual, leftForearmVisual,
                          leftArm.Pitch, leftArm.Roll, leftArm.Elbow);
            }

            // Advance when phase completes
            if (phaseT >= 1f)
            {
                currentInstruction++;
                if (currentInstruction >= 3)
                {
                    currentInstruction = -1; // idle
                    RunSafetyCheck();
                }
                else
                {
                    phaseT = 0f;
                }
            }
        }
    }

    void BeginNewSolve(bool solveRight, bool solveLeft)
    {
        // Solve right arm
        if (solveRight)
        {
            float p, r, e;
            Vector3 effR = ComputeEffectiveTarget(transform.position, rightTarget.position, true);
            SolveIK(transform.position, effR, out p, out r, out e);

            rightArm.servoStart[0] = rightArm.servoCurrent[0];
            rightArm.servoStart[1] = rightArm.servoCurrent[1];
            rightArm.servoStart[2] = rightArm.servoCurrent[2];

            rightArm.servoGoal[0] = UnwrapAngle(rightArm.servoStart[0], p);
            rightArm.servoGoal[1] = UnwrapAngle(rightArm.servoStart[1], r);
            rightArm.servoGoal[2] = UnwrapAngle(rightArm.servoStart[2], e);

            rightArm.lastSolvedTargetPos = rightTarget.position;
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                rightArm.servoStart[i] = rightArm.servoCurrent[i];
                rightArm.servoGoal[i] = rightArm.servoCurrent[i];
            }
        }

        // Solve left arm
        if (solveLeft && leftShoulderPivot != null)
        {
            float p, r, e;
            Vector3 effL = ComputeEffectiveTarget(leftShoulderPivot.position, leftTarget.position, false);
            SolveIK(leftShoulderPivot.position, effL, out p, out r, out e);

            leftArm.servoStart[0] = leftArm.servoCurrent[0];
            leftArm.servoStart[1] = leftArm.servoCurrent[1];
            leftArm.servoStart[2] = leftArm.servoCurrent[2];

            leftArm.servoGoal[0] = UnwrapAngle(leftArm.servoStart[0], p);
            leftArm.servoGoal[1] = UnwrapAngle(leftArm.servoStart[1], r);
            leftArm.servoGoal[2] = UnwrapAngle(leftArm.servoStart[2], e);

            leftArm.lastSolvedTargetPos = leftTarget.position;
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                leftArm.servoStart[i] = leftArm.servoCurrent[i];
                leftArm.servoGoal[i] = leftArm.servoCurrent[i];
            }
        }

        currentInstruction = 0;
        phaseT = 0f;
    }

    // ================================================================
    // Safety Check
    // ================================================================

    // Called after every completed move. Tests actual arm segments against the chest.
    // If no collision: saves current pose as the new safe baseline and writes CSV.
    // If collision: reverts both arms to the last safe pose.
    void RunSafetyCheck()
    {
        bool rightCollides = ArmCollidesWithChest(
            transform.position, rightElbowPivot, rightForearmVisual, rightArm);
        bool leftCollides = leftShoulderPivot != null && ArmCollidesWithChest(
            leftShoulderPivot.position, leftElbowPivot, leftForearmVisual, leftArm);

        if (rightCollides || leftCollides)
        {
            // Revert to last safe pose
            System.Array.Copy(rightArmSafe, rightArm.servoCurrent, 3);
            System.Array.Copy(leftArmSafe,  leftArm.servoCurrent,  3);
            System.Array.Copy(rightArmSafe, rightArm.servoGoal, 3);
            System.Array.Copy(leftArmSafe,  leftArm.servoGoal,  3);

            ApplyPose(transform, rightElbowPivot, rightUpperArmVisual, rightForearmVisual,
                      rightArm.Pitch, rightArm.Roll, rightArm.Elbow);
            if (leftShoulderPivot != null)
                ApplyPose(leftShoulderPivot, leftElbowPivot, leftUpperArmVisual, leftForearmVisual,
                          leftArm.Pitch, leftArm.Roll, leftArm.Elbow);

            Debug.LogWarning("Safety check failed — arm reverted to last safe pose.");
        }
        else
        {
            // Pose is safe: update baseline and write CSV
            System.Array.Copy(rightArm.servoCurrent, rightArmSafe, 3);
            System.Array.Copy(leftArm.servoCurrent,  leftArmSafe,  3);
            WriteCSV();
        }
    }

    // Returns true if either segment of the arm (upper or forearm) intersects the chest.
    bool ArmCollidesWithChest(Vector3 shoulderPos, Transform elbowPivot,
                               Transform forearmVis, ArmState arm)
    {
        if (chestTransform == null) return false;
        if (elbowPivot == null || forearmVis == null) return false;

        Vector3 elbowPos = elbowPivot.position;
        Vector3 handPos  = forearmVis.position + forearmVis.forward * (forearmLength * 0.5f);

        float tMin, depth;
        if (SegmentIntersectsExpandedChest(shoulderPos, elbowPos, out tMin, out depth)) return true;
        if (SegmentIntersectsExpandedChest(elbowPos,    handPos,  out tMin, out depth)) return true;
        return false;
    }

    // ================================================================
    // CSV Output
    // ================================================================

    void WriteCSV()
    {
        string folderPath = Path.Combine(Application.dataPath, "..", csvOutputFolder);
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        string filePath = Path.Combine(folderPath, "servo_commands.csv");
        string speed = servoSpeed.ToString("F0");

        string[] rightNames = { "R1", "R2", "R3" };
        string[] leftNames  = { "L1", "L2", "L3" };

        using (StreamWriter writer = new StreamWriter(filePath, false))
        {
            for (int i = 0; i < 3; i++)
            {
                int instruction = i + 1;
                writer.WriteLine(instruction + "," + rightNames[i] + "," +
                                 rightArm.servoGoal[i].ToString("F2") + "," + speed);
                writer.WriteLine(instruction + "," + leftNames[i] + "," +
                                 leftArm.servoGoal[i].ToString("F2") + "," + speed);
            }
        }

        Debug.Log("Servo commands written to: " + filePath);
    }

    // ================================================================
    // Helpers
    // ================================================================

    void SetVisualScale(Transform visual, float length)
    {
        if (visual != null)
            visual.localScale = new Vector3(armThickness, armThickness, length);
    }

    float UnwrapAngle(float start, float goal)
    {
        float diff = goal - start;
        while (diff > 180f) diff -= 360f;
        while (diff < -180f) diff += 360f;
        return start + diff;
    }

    // ================================================================
    // Chest Avoidance
    // ================================================================

    // Parametric slab test: does the segment a→b pass through the expanded chest AABB?
    // Returns true if it does, plus tMin (first contact fraction) and penetrationDepth.
    bool SegmentIntersectsExpandedChest(Vector3 a, Vector3 b,
                                        out float tMin, out float penetrationDepth)
    {
        tMin = 0f;
        penetrationDepth = 0f;

        if (chestTransform == null) return false;

        Vector3 halfSize = chestHalfSize + Vector3.one * avoidanceMargin;
        Vector3 center = chestCenter;

        Vector3 dir = b - a;
        float tEnter = 0f;
        float tExit  = 1f;

        for (int axis = 0; axis < 3; axis++)
        {
            float d = dir[axis];
            float o = a[axis] - center[axis];
            float h = halfSize[axis];

            if (Mathf.Abs(d) < 1e-6f)
            {
                // Parallel to this slab — if outside, no intersection
                if (Mathf.Abs(o) > h) return false;
            }
            else
            {
                float t1 = (-h - o) / d;
                float t2 = ( h - o) / d;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                tEnter = Mathf.Max(tEnter, t1);
                tExit  = Mathf.Min(tExit,  t2);
                if (tEnter > tExit) return false;
            }
        }

        if (tExit < 0f || tEnter > 1f) return false;

        tMin = tEnter;
        penetrationDepth = (tExit - Mathf.Max(tEnter, 0f)) * dir.magnitude;
        return true;
    }

    // Returns a chest-safe target for SolveIK. If the shoulder→rawTarget segment
    // doesn't hit the chest, returns rawTarget unchanged.
    Vector3 ComputeEffectiveTarget(Vector3 shoulderPos, Vector3 rawTarget, bool isRightArm)
    {
        float tMin, penetrationDepth;
        if (!SegmentIntersectsExpandedChest(shoulderPos, rawTarget, out tMin, out penetrationDepth))
            return rawTarget;

        // Natural outward side: right arm → +X, left arm → -X
        Vector3 bypassDir = isRightArm ? Vector3.right : Vector3.left;

        // Place waypoint beside the chest at mid-travel height
        float waypointX = chestCenter.x + bypassDir.x * (chestHalfSize.x + avoidanceMargin + 0.3f);
        float waypointY = Mathf.Lerp(shoulderPos.y, rawTarget.y, 0.5f);
        float waypointZ = Mathf.Lerp(shoulderPos.z, rawTarget.z, 0.5f);
        Vector3 waypoint = new Vector3(waypointX, waypointY, waypointZ);

        // Clamp waypoint to arm reach so SolveIK never gets an impossible target
        float maxReach = upperArmLength + forearmLength - 0.05f;
        Vector3 toWaypoint = waypoint - shoulderPos;
        if (toWaypoint.magnitude > maxReach)
            waypoint = shoulderPos + toWaypoint.normalized * maxReach;

        // Smooth blend: zero deflection at edge, full deflection when deeply inside
        float blendWeight = Mathf.Clamp01(penetrationDepth / avoidanceSmoothDist);
        return Vector3.Lerp(rawTarget, waypoint, blendWeight);
    }

    // ================================================================
    // IK Solve
    // ================================================================

    void SolveIK(Vector3 shoulderPos, Vector3 targetPos,
                 out float shoulderPitch, out float upperArmRoll, out float elbowBend)
    {
        Vector3 toTarget = targetPos - shoulderPos;

        float d = toTarget.magnitude;
        float totalReach = upperArmLength + forearmLength;
        float minReach = Mathf.Abs(upperArmLength - forearmLength);
        d = Mathf.Clamp(d, minReach + 0.001f, totalReach - 0.001f);

        float cosShoulderAngle = (upperArmLength * upperArmLength + d * d - forearmLength * forearmLength)
                                 / (2f * upperArmLength * d);
        cosShoulderAngle = Mathf.Clamp(cosShoulderAngle, -1f, 1f);
        float shoulderTriAngle = Mathf.Acos(cosShoulderAngle) * Mathf.Rad2Deg;

        float horizontalDist = Mathf.Sqrt(toTarget.x * toTarget.x + toTarget.z * toTarget.z);
        float angleToTarget = Mathf.Atan2(horizontalDist, -toTarget.y) * Mathf.Rad2Deg;

        shoulderPitch = angleToTarget - shoulderTriAngle;

        float cosElbowAngle = (upperArmLength * upperArmLength + forearmLength * forearmLength - d * d)
                              / (2f * upperArmLength * forearmLength);
        cosElbowAngle = Mathf.Clamp(cosElbowAngle, -1f, 1f);
        float rawElbowAngle = Mathf.Acos(cosElbowAngle) * Mathf.Rad2Deg;

        elbowBend = 180f - rawElbowAngle;
        elbowBend = Mathf.Clamp(elbowBend, -elbowMaxBackward, elbowMaxForward);

        Quaternion pitchRot = Quaternion.AngleAxis(-shoulderPitch, Vector3.right);
        Vector3 upperArmDir = pitchRot * Vector3.down;

        Vector3 elbowPos = shoulderPos + upperArmDir * upperArmLength;
        Vector3 elbowToTarget = targetPos - elbowPos;

        Vector3 projected = elbowToTarget - Vector3.Dot(elbowToTarget, upperArmDir) * upperArmDir;

        if (projected.sqrMagnitude < 0.0001f)
        {
            upperArmRoll = 0f;
        }
        else
        {
            Vector3 defaultBendDir = Vector3.Cross(Vector3.right, upperArmDir).normalized;
            if (defaultBendDir.sqrMagnitude < 0.0001f)
                defaultBendDir = Vector3.forward;

            Vector3 perpRef = Vector3.Cross(upperArmDir, defaultBendDir).normalized;

            float projOnDefault = Vector3.Dot(projected.normalized, defaultBendDir);
            float projOnPerp = Vector3.Dot(projected.normalized, perpRef);

            upperArmRoll = Mathf.Atan2(projOnPerp, projOnDefault) * Mathf.Rad2Deg;
        }
    }

    // ================================================================
    // Apply Pose
    // ================================================================

    void ApplyPose(Transform shoulderPivot, Transform elbowPivot,
                   Transform upperArmVis, Transform forearmVis,
                   float pitch, float roll, float elbowBendAngle)
    {
        if (shoulderPivot == null || elbowPivot == null) return;

        Quaternion pitchRot = Quaternion.AngleAxis(-pitch, Vector3.right);
        Vector3 upperArmDir = pitchRot * Vector3.down;
        Vector3 elbowPos = shoulderPivot.position + upperArmDir * upperArmLength;
        elbowPivot.position = elbowPos;

        Quaternion rollRot = Quaternion.AngleAxis(roll, upperArmDir);
        Quaternion shoulderRot = rollRot * pitchRot;
        shoulderPivot.localRotation = shoulderRot;

        Vector3 bendAxis = shoulderPivot.right;
        Quaternion elbowRot = Quaternion.AngleAxis(elbowBendAngle, bendAxis);
        Vector3 forearmDir = elbowRot * upperArmDir;

        elbowPivot.rotation = Quaternion.LookRotation(forearmDir, shoulderPivot.forward);

        if (upperArmVis != null)
        {
            upperArmVis.position = shoulderPivot.position + upperArmDir * (upperArmLength / 2f);
            upperArmVis.rotation = Quaternion.LookRotation(upperArmDir, shoulderPivot.forward);
            upperArmVis.localScale = new Vector3(armThickness, armThickness, upperArmLength);
        }

        if (forearmVis != null)
        {
            forearmVis.position = elbowPos + forearmDir * (forearmLength / 2f);
            forearmVis.rotation = Quaternion.LookRotation(forearmDir, shoulderPivot.forward);
            forearmVis.localScale = new Vector3(armThickness, armThickness, forearmLength);
        }
    }

    // ================================================================
    // Gizmos
    // ================================================================

    void OnDrawGizmos()
    {
        DrawArmGizmos(transform, rightElbowPivot, rightForearmVisual, rightTarget, Color.yellow);
        DrawArmGizmos(leftShoulderPivot, leftElbowPivot, leftForearmVisual, leftTarget, Color.cyan);
    }

    void DrawArmGizmos(Transform shoulder, Transform elbow, Transform forearm, Transform tgt, Color color)
    {
        if (shoulder == null || elbow == null) return;

        Gizmos.color = color;
        Gizmos.DrawLine(shoulder.position, elbow.position);

        if (forearm != null)
        {
            Vector3 forearmEnd = forearm.position + forearm.forward * (forearmLength / 2f);
            Gizmos.DrawLine(elbow.position, forearmEnd);
        }

        if (tgt != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(tgt.position, 0.15f);
        }
    }
}