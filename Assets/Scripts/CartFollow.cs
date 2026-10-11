using UnityEngine;

public class CartFollow : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The player's body, the object that moves.")]
    [SerializeField] private Transform player;

    [Tooltip("Optional. Object whose yaw sets the cart's direction, such as the camera. Uses the player when empty.")]
    [SerializeField] private Transform heading;

    [Header("Placement")]
    [Tooltip("Distance in front of the player to the cart's pivot.")]
    [SerializeField] private float distance = 1.1f;

    [Tooltip("Sideways offset. Positive values move the cart to the player's right.")]
    [SerializeField] private float sideOffset = 0f;

    [Tooltip("Height above the floor for the cart's pivot. Use 0 when the pivot is at the bottom of the wheels.")]
    [SerializeField] private float heightOffset = 0f;

    [Tooltip("Turns the cart model, for models that don't face +Z.")]
    [SerializeField] private float yawOffset = 0f;

    [Header("Smoothing")]
    [Tooltip("Seconds the cart takes to catch up to its target position. Lower is tighter.")]
    [SerializeField] private float positionSmoothTime = 0.08f;

    [Tooltip("How quickly the cart turns to face the player's direction. Higher is snappier.")]
    [SerializeField] private float turnSpeed = 10f;

    [Header("Floor")]
    [Tooltip("Layers the cart can rest on.")]
    [SerializeField] private LayerMask groundMask = ~0;

    [Tooltip("How far above the target point the floor check starts.")]
    [SerializeField] private float groundProbeHeight = 1.5f;

    [Header("Obstacles")]
    [Tooltip("Layers that push the cart closer to the player, such as shelves and walls. Leave empty to turn this off.")]
    [SerializeField] private LayerMask obstacleMask;

    [Tooltip("Radius of the cart for obstacle checks.")]
    [SerializeField] private float cartRadius = 0.35f;

    [Tooltip("Closest the cart gets to the player when something is in the way.")]
    [SerializeField] private float minDistance = 0.5f;

    [Tooltip("Turns off the cart's colliders so they can't block the player's movement.")]
    [SerializeField] private bool disableCartColliders = true;

    private Vector3 velocity;
    private float currentDistance;

    void Start()
    {
        if (disableCartColliders)
        {
            foreach (Collider collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
        }

        foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>()) body.isKinematic = true;

        currentDistance = distance;
        if (player != null)
        {
            transform.SetPositionAndRotation(TargetPosition(Forward()), TargetRotation(Forward()));
        }
    }

    
    void LateUpdate()
    {
        if (player == null) return;

        Vector3 forward = Forward();
        UpdateDistance(forward);

        Vector3 target = TargetPosition(forward);
        transform.position = Vector3.SmoothDamp(transform.position, target, ref velocity, positionSmoothTime);

        float blend = 1f - Mathf.Exp(-turnSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, TargetRotation(forward), blend);
    }

   
    Vector3 Forward()
    {
        Transform source = heading != null ? heading : player;
        Vector3 forward = source.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 1e-4f)
        {
           
            forward = source.up;
            forward.y = 0f;
        }

        return forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
    }

  
    void UpdateDistance(Vector3 forward)
    {
        float wanted = distance;

        if (obstacleMask.value != 0)
        {
            Vector3 origin = player.position + Vector3.up * (heightOffset + cartRadius + 0.1f);
            if (Physics.SphereCast(origin, cartRadius, forward, out RaycastHit hit, distance, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                wanted = Mathf.Max(minDistance, hit.distance);
            }
        }

        
        currentDistance = wanted < currentDistance
            ? wanted
            : Mathf.Lerp(currentDistance, wanted, 1f - Mathf.Exp(-6f * Time.deltaTime));
    }

    Vector3 TargetPosition(Vector3 forward)
    {
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 point = player.position + forward * currentDistance + right * sideOffset;

        Vector3 probe = point + Vector3.up * groundProbeHeight;
        if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, groundProbeHeight * 2f, groundMask, QueryTriggerInteraction.Ignore)
            && !hit.transform.IsChildOf(transform))
        {
            point.y = hit.point.y;
        }
        else
        {
            point.y = player.position.y;
        }

        point.y += heightOffset;
        return point;
    }

    Quaternion TargetRotation(Vector3 forward)
    {
        return Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, yawOffset, 0f);
    }

    void OnDrawGizmosSelected()
    {
        if (player == null) return;

        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
        Vector3 forward = Application.isPlaying ? Forward() : player.forward;
        Gizmos.DrawLine(player.position, player.position + forward * distance);
        Gizmos.DrawWireSphere(player.position + forward * distance, cartRadius);
    }
}