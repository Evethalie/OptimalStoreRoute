using MoreMountains.Feedbacks;
using Pathfinding;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;



public class RouteGuide : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private Seeker seeker;

    [Tooltip("Optional. Without a checkout, the route ends at the last item.")]
    [SerializeField] private Transform checkout;

    [Header("Testing")]
    [Tooltip("Items to route through on Start, until the grocery list is in place.")]
    [SerializeField] private Transform[] testItems;
    [SerializeField] private bool planOnStart = true;

    [Header("Line")]
    [Tooltip("Optional. Uses Sprites/Default when empty.")]
    [SerializeField] private Material lineMaterial;
    [SerializeField] private Color lineColor = new Color(0.1f, 0.45f, 1f);
    [SerializeField] private float lineWidth = 0.25f;
    [SerializeField] private float heightOffset = 0.05f;

    [Tooltip("On: draw the whole remaining route. Off: draw only the path to the next stop.")]
    [SerializeField] private bool showFullRoute = true;

    [Tooltip("Radius used to round the drawn line's corners. Set to 0 for sharp corners.")]
    [SerializeField] private float cornerRadius = 0.6f;

    [Tooltip("Points per rounded corner. Higher values look smoother.")]
    [SerializeField, Range(2, 16)] private int cornerSegments = 6;

    [Tooltip("Corners that turn less than this many degrees are drawn straight. Removes small zigzags from grid paths.")]
    [SerializeField] private float straightenAngle = 12f;

    [Header("Arrow")]
    [SerializeField] private float arrowDistanceAhead = 1.5f;
    [SerializeField] private float arrowLength = 0.6f;
    [SerializeField] private float arrowWidth = 0.6f;
    [SerializeField] private Color arrowColor = Color.white;

    [Tooltip("Arrow glow strength when the line material uses emission.")]
    [SerializeField] private float arrowEmissionIntensity = 2f;

    [Tooltip("Outline thickness around the arrow, in world units. Set to 0 to hide it.")]
    [SerializeField] private float arrowOutlineWidth = 0.1f;
    [SerializeField] private Color arrowOutlineColor = new Color(0.02f, 0.05f, 0.15f);

    [Tooltip("Optional. When empty, the outline uses a copy of the line material with emission turned off.")]
    [SerializeField] private Material arrowOutlineMaterial;

    [Header("Progress")]
    [Tooltip("Checkout only. Horizontal distance at which the checkout counts as reached.")]
    [SerializeField] private float arriveRadius = 0.75f;

    [Tooltip("Checkout only. A stop also counts as reached when the player's progress along the route has reached it and the player is within this distance. Catches diagonal shortcuts past a stop.")]
    [SerializeField] private float passRadius = 2f;

    [Tooltip("Replan from the player's position when the player is this far from the line.")]
    [SerializeField] private float offRouteDistance = 2f;
    [SerializeField] private float replanCooldown = 1f;

    [Tooltip("How far ahead along the route, in world units, to look for the player's position each frame.")]
    [SerializeField] private float searchAheadDistance = 8f;

    [Tooltip("How quickly the line's start and the arrow catch up to the player. Higher is snappier.")]
    [SerializeField] private float progressSmoothing = 10f;

    [Tooltip("How quickly the arrow rotates to a new direction, in degrees per second.")]
    [SerializeField] private float arrowTurnSpeed = 360f;



    public event Action<Transform> ItemReached;


    public event Action RouteCompleted;

    public bool HasRoute => stops.Count > 0 && nextStop < stops.Count;
    public IReadOnlyList<Transform> RemainingItems => pendingItems;

    private struct Stop
    {
        public Transform target;
        public Vector3 point;
        public int pathIndex;
        public float distance;    // Distance along the path to this stop.
        public bool isCheckout;
    }
    [SerializeField] MMF_Player doneFeedback;
    private readonly List<Transform> pendingItems = new List<Transform>();
    private readonly List<Vector3> path = new List<Vector3>();
    private readonly List<Stop> stops = new List<Stop>();
    private readonly List<Vector3> renderPoints = new List<Vector3>();
    private readonly List<Vector3> rawPath = new List<Vector3>();
    private readonly List<Vector3> legPoints = new List<Vector3>();
    private readonly List<Vector3> smoothLeg = new List<Vector3>();
    private readonly List<float> pathDistance = new List<float>();
    private readonly Dictionary<Transform, Vector3> snappedTargets = new Dictionary<Transform, Vector3>();

    private LineRenderer line;
    private LineRenderer arrow;
    private LineRenderer arrowOutline;

    private int nextStop;
    private float progress;        // Smoothed distance along the path that the player has reached.
    private float targetProgress;  // Raw projected distance this frame.
    private Vector3 arrowDirection;
    private float lastReplanTime = float.NegativeInfinity;

    void Awake()
    {
        line = CreateLine("RouteLine");
        line.widthMultiplier = lineWidth;
        line.numCapVertices = 4;
        line.numCornerVertices = 4;

        // Draw order: route line, then the outline, then the arrow on top.
        line.sortingOrder = 0;

        arrowOutline = CreateArrow("RouteArrowOutline", arrowOutlineColor);
        arrowOutline.material = arrowOutlineMaterial != null
            ? arrowOutlineMaterial
            : TintedCopy(line.sharedMaterial, arrowOutlineColor, 0f);
        arrowOutline.sortingOrder = 1;

        arrow = CreateArrow("RouteArrow", arrowColor);
        arrow.material = TintedCopy(line.sharedMaterial, arrowColor, arrowEmissionIntensity);
        arrow.widthMultiplier = arrowWidth;
        arrow.sortingOrder = 2;
    }

    void Start()
    {
        if (planOnStart && testItems != null && testItems.Length > 0)
        {
            SetItems(testItems);
        }
    }


    public void SetItems(IEnumerable<Transform> items)
    {
        pendingItems.Clear();
        foreach (Transform item in items)
        {
            if (item != null && !pendingItems.Contains(item)) pendingItems.Add(item);
        }

        Replan();
    }

    public void ClearRoute()
    {
        pendingItems.Clear();
        path.Clear();
        stops.Clear();
        pathDistance.Clear();
        nextStop = 0;
        progress = 0f;
        targetProgress = 0f;
        HideVisuals();
    }


    public void Replan()
    {
        lastReplanTime = Time.time;

        if (player == null || seeker == null || AstarPath.active == null)
        {
            Debug.LogWarning("RouteGuide needs a player, a Seeker, and an active AstarPath.");
            return;
        }

        bool hasCheckout = checkout != null;
        if (pendingItems.Count == 0 && !hasCheckout)
        {
            ClearRoute();
            return;
        }

        List<Vector3> points = new List<Vector3> { Snap(player.position) };
        foreach (Transform item in pendingItems) points.Add(Snap(item.position));


        points.Add(hasCheckout ? Snap(checkout.position) : Vector3.zero);

        int count = points.Count;
        int end = count - 1;
        float[,] d = new float[count, count];
        List<Vector3>[,] legs = new List<Vector3>[count, count];

        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                if (!hasCheckout && j == end)
                {
                    d[i, j] = d[j, i] = 0f;
                    continue;
                }

                Path p = seeker.StartPath(points[i], points[j], null);
                p.BlockUntilCalculated();

                if (p.error)
                {
                    d[i, j] = d[j, i] = float.PositiveInfinity;
                    continue;
                }

                d[i, j] = d[j, i] = p.GetTotalLength();

                List<Vector3> forward = new List<Vector3>(p.vectorPath);
                List<Vector3> backward = new List<Vector3>(forward);
                backward.Reverse();

                legs[i, j] = forward;
                legs[j, i] = backward;
            }
        }

        int[] order = RoutePlanner.Solve(d);
        BuildPath(order, points, legs, hasCheckout);
    }

    void BuildPath(int[] order, List<Vector3> points, List<Vector3>[,] legs, bool hasCheckout)
    {
        int end = points.Count - 1;

        rawPath.Clear();
        stops.Clear();
        rawPath.Add(points[0]);

        for (int i = 1; i < order.Length; i++)
        {
            int a = order[i - 1];
            int b = order[i];
            bool isCheckout = b == end;

            if (isCheckout && !hasCheckout) break;

            Transform target = isCheckout ? checkout : pendingItems[b - 1];
            List<Vector3> leg = legs[a, b];

            if (leg == null)
            {
                Debug.LogWarning($"No path to {target.name}. Drawing a straight line instead.");
            }
            else
            {
                foreach (Vector3 waypoint in leg) AppendPoint(waypoint);
            }

            AppendPoint(points[b]);

            stops.Add(new Stop
            {
                target = target,
                point = points[b],
                pathIndex = rawPath.Count - 1,
                isCheckout = isCheckout
            });
        }

        SmoothPath();

        nextStop = 0;
        progress = 0f;
        targetProgress = 0f;
        arrowDirection = Vector3.zero;
    }

    void AppendPoint(Vector3 point)
    {
        if (rawPath.Count > 0 && (rawPath[rawPath.Count - 1] - point).sqrMagnitude < 0.0001f) return;
        rawPath.Add(point);
    }

    // Builds the route once, at plan time: each leg between stops is
    // straightened and its corners rounded. Stops stay exact vertices, and
    // every point gets its distance along the route. Doing this once keeps
    // the curve from changing shape as the player moves.
    void SmoothPath()
    {
        path.Clear();
        path.Add(rawPath[0]);
        int legStart = 0;

        for (int k = 0; k < stops.Count; k++)
        {
            Stop stop = stops[k];

            legPoints.Clear();
            for (int i = legStart; i <= stop.pathIndex; i++) legPoints.Add(rawPath[i]);

            Straighten(legPoints, straightenAngle);
            RoundCorners(legPoints, smoothLeg, cornerRadius, cornerSegments);

            // The leg's first point is the previous stop, already in path.
            for (int i = 1; i < smoothLeg.Count; i++)
            {
                if ((smoothLeg[i] - path[path.Count - 1]).sqrMagnitude > 0.0001f) path.Add(smoothLeg[i]);
            }

            legStart = stop.pathIndex;
            stop.pathIndex = path.Count - 1;
            stops[k] = stop;
        }

        pathDistance.Clear();
        pathDistance.Add(0f);
        for (int i = 1; i < path.Count; i++)
        {
            pathDistance.Add(pathDistance[i - 1] + HorizontalDistance(path[i - 1], path[i]));
        }

        for (int k = 0; k < stops.Count; k++)
        {
            Stop stop = stops[k];
            stop.distance = pathDistance[stop.pathIndex];
            stops[k] = stop;
        }
    }

    void Update()
    {
        if (!HasRoute)
        {
            HideVisuals();
            return;
        }

        bool offRoute = UpdateProgress();

        if (offRoute && Time.time - lastReplanTime >= replanCooldown)
        {
            Replan();
            if (!HasRoute) return;
            UpdateProgress();
        }

        CheckArrival();

        if (HasRoute)
        {
            DrawRoute();
        }
        else
        {
            HideVisuals();
        }
    }

    // Projects the player onto the route between the current progress and a
    // short distance ahead, then eases progress toward that point. Progress
    // only moves forward, so sideways or diagonal movement can't drag the
    // line's start and the arrow backward or make them jump between segments.
    // Returns true when the player is too far from the route.
    bool UpdateProgress()
    {
        Vector3 position = player.position;
        float stopDistance = stops[nextStop].distance;
        float searchEnd = Mathf.Min(progress + searchAheadDistance, stopDistance);

        float bestDistSqr = float.PositiveInfinity;
        float bestAlong = progress;

        if (path.Count < 2)
        {
            bestDistSqr = HorizontalDistanceSqr(position, path[0]);
        }
        else
        {
            for (int s = FindSegment(progress); s < path.Count - 1 && pathDistance[s] <= searchEnd; s++)
            {
                ClosestOnSegmentXZ(path[s], path[s + 1], position, out float distSqr, out float t);
                float along = Mathf.Clamp(pathDistance[s] + t * (pathDistance[s + 1] - pathDistance[s]), progress, searchEnd);

                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    bestAlong = along;
                }
            }
        }

        targetProgress = Mathf.Max(targetProgress, bestAlong);
        float blend = 1f - Mathf.Exp(-progressSmoothing * Time.deltaTime);
        progress = Mathf.Lerp(progress, targetProgress, blend);

        return bestDistSqr > offRouteDistance * offRouteDistance;
    }

    // Item stops wait for CompleteStop, which the pickup prompt calls. Only
    // the checkout completes on arrival.
    void CheckArrival()
    {
        if (nextStop >= stops.Count) return;

        Stop stop = stops[nextStop];
        if (!stop.isCheckout) return;

        float distSqr = HorizontalDistanceSqr(player.position, stop.point);
        bool atStop = distSqr <= arriveRadius * arriveRadius;
        bool passedStop = targetProgress >= stop.distance - 0.05f && distSqr <= passRadius * passRadius;
        if (!atStop && !passedStop) return;

        nextStop++;
        targetProgress = Mathf.Max(targetProgress, stop.distance);

        // Runs once, on the frame the player reaches checkout.
        if (doneFeedback != null)
        {
            doneFeedback.PlayFeedbacks();
           // Invoke(nameof(StopDoneFeedback), 5f);
        }

        RouteCompleted?.Invoke();
    }

    void StopDoneFeedback()
    {
        if (doneFeedback != null) doneFeedback.StopFeedbacks();
    }

    // Marks an item target as picked up, in any order, and replans the rest
    // of the route from the player's position.
    public void CompleteStop(Transform target)
    {
        if (target == null || !pendingItems.Remove(target)) return;

        ItemReached?.Invoke(target);

        if (pendingItems.Count == 0 && checkout == null)
        {
            ClearRoute();
            RouteCompleted?.Invoke();
            return;
        }

        Replan();
    }

    // Horizontal distance from the player to the walkable point nearest the
    // target, which is where the route ends for that target.
    public float DistanceTo(Transform target)
    {
        if (player == null || target == null) return float.PositiveInfinity;

        if (!snappedTargets.TryGetValue(target, out Vector3 point))
        {
            point = AstarPath.active != null ? Snap(target.position) : target.position;
            snappedTargets[target] = point;
        }

        return HorizontalDistance(player.position, point);
    }

    void DrawRoute()
    {
        float endDistance = showFullRoute ? pathDistance[pathDistance.Count - 1] : stops[nextStop].distance;
        Vector3 up = Vector3.up * heightOffset;

        renderPoints.Clear();
        renderPoints.Add(PointAt(progress) + up);
        for (int i = FindSegment(progress) + 1; i < path.Count && pathDistance[i] <= endDistance; i++)
        {
            if (pathDistance[i] > progress + 0.001f) renderPoints.Add(path[i] + up);
        }

        if (renderPoints.Count < 2)
        {
            HideVisuals();
            return;
        }

        line.enabled = true;
        line.positionCount = renderPoints.Count;
        for (int i = 0; i < renderPoints.Count; i++)
        {
            line.SetPosition(i, renderPoints[i]);
        }

        // The arrow never goes past the next stop. Past a stop, the route often
        // turns back the way it came, and an arrow there points backward.
        float arrowEnd = stops[nextStop].distance;
        float remaining = arrowEnd - progress;
        if (remaining < 0.01f)
        {
            arrow.enabled = false;
            arrowOutline.enabled = false;
            return;
        }

        // The arrow points from its base toward a point one arrow-length
        // further along the route, so it turns gradually through curves.
        float baseDistance = progress + Mathf.Min(arrowDistanceAhead, Mathf.Max(0f, remaining - arrowLength));
        Vector3 basePoint = PointAt(baseDistance) + up;
        Vector3 aim = PointAt(Mathf.Min(baseDistance + arrowLength, arrowEnd)) + up - basePoint;
        aim.y = 0f;

        if (aim.sqrMagnitude > 1e-6f)
        {
            aim.Normalize();

            // Turn around the vertical axis only. RotateTowards can tip the
            // arrow through vertical when the new direction is nearly opposite.
            if (arrowDirection == Vector3.zero)
            {
                arrowDirection = aim;
            }
            else
            {
                float maxStep = arrowTurnSpeed * Time.deltaTime;
                float angle = Mathf.Clamp(Vector3.SignedAngle(arrowDirection, aim, Vector3.up), -maxStep, maxStep);
                arrowDirection = Quaternion.AngleAxis(angle, Vector3.up) * arrowDirection;
            }
        }

        if (arrowDirection == Vector3.zero)
        {
            arrow.enabled = false;
            arrowOutline.enabled = false;
            return;
        }

        Vector3 lift = Vector3.up * 0.02f;
        arrow.enabled = true;
        arrow.SetPosition(0, basePoint + lift);
        arrow.SetPosition(1, basePoint + arrowDirection * arrowLength + lift);

        DrawArrowOutline(basePoint, arrowDirection);
    }

    // Index of the segment that contains the given distance along the path.
    int FindSegment(float distance)
    {
        int low = 0;
        int high = Mathf.Max(0, path.Count - 2);

        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (pathDistance[mid] <= distance) low = mid;
            else high = mid - 1;
        }

        return low;
    }

    Vector3 PointAt(float distance)
    {
        if (path.Count == 1) return path[0];

        int s = FindSegment(distance);
        float length = pathDistance[s + 1] - pathDistance[s];
        float t = length > 1e-5f ? Mathf.Clamp01((distance - pathDistance[s]) / length) : 0f;
        return Vector3.Lerp(path[s], path[s + 1], t);
    }

    // The outline is a larger triangle with the same tip angle. Moving the tip
    // forward by t / sin(halfAngle) and the base back by t keeps every edge
    // exactly t away from the arrow's edges.
    void DrawArrowOutline(Vector3 basePoint, Vector3 direction)
    {
        if (arrowOutlineWidth <= 0f || arrowLength <= 0f)
        {
            arrowOutline.enabled = false;
            return;
        }

        float t = arrowOutlineWidth;
        float halfAngle = Mathf.Atan2(arrowWidth * 0.5f, arrowLength);
        float tipExtension = t / Mathf.Max(Mathf.Sin(halfAngle), 0.01f);
        float outlineLength = arrowLength + t + tipExtension;

        Vector3 lift = Vector3.up * 0.01f;
        arrowOutline.enabled = true;
        arrowOutline.widthMultiplier = arrowWidth * outlineLength / arrowLength;
        arrowOutline.SetPosition(0, basePoint - direction * t + lift);
        arrowOutline.SetPosition(1, basePoint + direction * (arrowLength + tipExtension) + lift);
    }

    void HideVisuals()
    {
        if (line != null)
        {
            line.positionCount = 0;
            line.enabled = false;
        }

        if (arrow != null) arrow.enabled = false;
        if (arrowOutline != null) arrowOutline.enabled = false;
    }

    // Lit and emissive shaders ignore LineRenderer vertex colors, so the arrow
    // and outline each get their own material copy with the color set on it.
    static Material TintedCopy(Material source, Color color, float emission)
    {
        Material copy = new Material(source);

        // Turn off view-dependent shading. Without this, a lit material's
        // reflections and highlights shift color as the camera turns.
        SetFloatIfPresent(copy, "_Smoothness", 0f);
        SetFloatIfPresent(copy, "_Glossiness", 0f);
        SetFloatIfPresent(copy, "_Metallic", 0f);
        SetFloatIfPresent(copy, "_SpecularHighlights", 0f);
        SetFloatIfPresent(copy, "_EnvironmentReflections", 0f);
        SetFloatIfPresent(copy, "_GlossyReflections", 0f);
        copy.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        copy.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        copy.EnableKeyword("_GLOSSYREFLECTIONS_OFF");

        copy.name = $"{source.name} ({(emission > 0f ? "Arrow" : "Outline")})";

        if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", color);
        if (copy.HasProperty("_Color")) copy.SetColor("_Color", color);

        if (copy.HasProperty("_EmissionColor"))
        {
            if (emission > 0f)
            {
                copy.EnableKeyword("_EMISSION");
                copy.SetColor("_EmissionColor", color * emission);
            }
            else
            {
                copy.DisableKeyword("_EMISSION");
                copy.SetColor("_EmissionColor", Color.black);
            }
        }

        return copy;
    }

    LineRenderer CreateArrow(string objectName, Color color)
    {
        LineRenderer lr = CreateLine(objectName);
        lr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        lr.startColor = color;
        lr.endColor = color;
        lr.positionCount = 2;
        lr.enabled = false;
        return lr;
    }


    LineRenderer CreateLine(string objectName)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.alignment = LineAlignment.TransformZ;
        lr.material = lineMaterial != null ? lineMaterial : new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lineColor;
        lr.endColor = lineColor;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        // Gives lit shaders an upward normal, so shading doesn't change with
        // the line's direction.
        lr.generateLightingData = true;
        lr.positionCount = 0;

        return lr;
    }


    static Vector3 Snap(Vector3 position)
    {
        NNInfo nearest = AstarPath.active.GetNearest(position, NearestNodeConstraint.Walkable);
        return nearest.node != null ? nearest.position : position;
    }

    static void SetFloatIfPresent(Material material, string property, float value)
    {
        if (material.HasProperty(property)) material.SetFloat(property, value);
    }

    // Removes interior points where the line bends less than minAngle degrees.
    static void Straighten(List<Vector3> points, float minAngle)
    {
        if (minAngle <= 0f) return;

        for (int i = points.Count - 2; i >= 1; i--)
        {
            Vector3 a = points[i] - points[i - 1];
            Vector3 b = points[i + 1] - points[i];
            a.y = 0f;
            b.y = 0f;

            if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f || Vector3.Angle(a, b) < minAngle)
            {
                points.RemoveAt(i);
            }
        }
    }

    // Replaces each interior corner with a curve. The curve starts and ends
    // radius units from the corner, capped at half of each neighboring
    // segment so curves never overlap.
    static void RoundCorners(List<Vector3> input, List<Vector3> output, float radius, int segments)
    {
        output.Clear();
        if (input.Count == 0) return;

        output.Add(input[0]);

        for (int i = 1; i < input.Count - 1; i++)
        {
            Vector3 prev = input[i - 1];
            Vector3 corner = input[i];
            Vector3 next = input[i + 1];

            float inLength = Vector3.Distance(prev, corner);
            float outLength = Vector3.Distance(corner, next);
            float r = Mathf.Min(radius, inLength * 0.5f, outLength * 0.5f);

            if (r < 0.01f)
            {
                output.Add(corner);
                continue;
            }

            Vector3 start = corner + (prev - corner) / inLength * r;
            Vector3 end = corner + (next - corner) / outLength * r;

            // Quadratic Bezier with the corner as the control point.
            for (int s = 0; s <= segments; s++)
            {
                float t = s / (float)segments;
                float u = 1f - t;
                output.Add(u * u * start + 2f * u * t * corner + t * t * end);
            }
        }

        if (input.Count > 1) output.Add(input[input.Count - 1]);
    }

    static Vector3 ClosestOnSegmentXZ(Vector3 a, Vector3 b, Vector3 p, out float distSqr, out float t)
    {
        Vector3 ab = b - a;
        ab.y = 0f;
        Vector3 ap = p - a;
        ap.y = 0f;

        float lengthSqr = ab.sqrMagnitude;
        t = lengthSqr > 1e-6f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / lengthSqr) : 0f;

        Vector3 closest = Vector3.Lerp(a, b, t);
        distSqr = HorizontalDistanceSqr(p, closest);
        return closest;
    }

    static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        return Mathf.Sqrt(HorizontalDistanceSqr(a, b));
    }

    static float HorizontalDistanceSqr(Vector3 a, Vector3 b)
    {
        Vector3 diff = a - b;
        diff.y = 0f;
        return diff.sqrMagnitude;
    }
}