using UnityEngine;

/// <summary>
/// Attach to the Chest GameObject.
/// Changes color when any arm piece collides with it, reverts when collision ends.
///
/// Setup:
///   - Chest needs a Collider (BoxCollider recommended). Check "Is Trigger" on it.
///   - Each arm cube (UpperArmCube, ForearmCube) needs:
///       1. A Collider (BoxCollider, should already exist since they're Cubes)
///       2. A Rigidbody with "Is Kinematic" checked and "Use Gravity" unchecked
///          (needed so Unity fires trigger events on script-moved objects)
/// </summary>
public class ChestCollisionDetector : MonoBehaviour
{
    [Header("Colors")]
    public Color defaultColor = Color.gray;
    public Color collisionColor = Color.red;

    private Renderer rend;
    private int collisionCount = 0;

    void Start()
    {
        rend = GetComponent<Renderer>();
        if (rend != null)
            rend.material.color = defaultColor;
    }

    void OnTriggerEnter(Collider other)
    {
        collisionCount++;
        if (rend != null)
            rend.material.color = collisionColor;
    }

    void OnTriggerExit(Collider other)
    {
        collisionCount--;
        if (collisionCount <= 0)
        {
            collisionCount = 0;
            if (rend != null)
                rend.material.color = defaultColor;
        }
    }
}
