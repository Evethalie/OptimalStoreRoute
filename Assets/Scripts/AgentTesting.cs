using System.Collections.Generic;
using Pathfinding;
using UnityEngine;


public class AgentTesting : MonoBehaviour
{
    public enum RouteMode
    {
        // Held-Karp (exact) up to MaxExactItems, nearest neighbor above that.
        Auto,
        // Always use nearest neighbor (+ optional 2-opt).
        NearestNeighbor
    }

    [SerializeField] private AIPath agent;
    [SerializeField] private Seeker ai;

    
    [SerializeField] private Transform[] goals;

    [Header("Routing")]
    [SerializeField] private RouteMode routeMode = RouteMode.Auto;

    [Tooltip("After nearest neighbor, reverse route segments while that shortens the route.")]
    [SerializeField] private bool improveWithTwoOpt = true;

    [Header("Arrival")]
    [Tooltip("Horizontal distance (ignores height) at which a stop counts as visited.")]
    [SerializeField] private float arriveRadius = 0.75f;

    private Vector3[] stops;

    // d[i, j] = walking distance from stop i to stop j
    private float[,] d;

    // legs[i, j] = waypoints from stop i to stop j
    private List<Vector3>[,] legs;

    private int numGroc;
    private int[] route;
    private int routeIndex;

    // Held-Karp memory is 2^n * n, so cap it.
    private const int MaxExactItems = 16;

    void Awake()
    {
        stops = new Vector3[goals.Length];
        d = new float[goals.Length, goals.Length];
        legs = new List<Vector3>[goals.Length, goals.Length];

        numGroc = Mathf.Max(0, goals.Length - 2);
    }

    void Start()
    {
        if (goals.Length < 2)
        {
            Debug.LogWarning("Need at least an entrance and a checkout.");
            return;
        }

       
        SnapStopsToGraph();

        CalculateStops();
        route = SolveRoute();

        Debug.Log($"Route: {string.Join(" -> ", route)} (length {RouteLength(route):F2})");

        routeIndex = 1;
        GoToCurrentStop();
    }

    void Update()
    {
        if (route == null || routeIndex >= route.Length) return;

        if (HasArrived())
        {
            Debug.Log($"Visited stop {route[routeIndex]} ({goals[route[routeIndex]].name})");

            routeIndex++;
            if (routeIndex >= route.Length)
            {
                Debug.Log("Route complete.");
                return;
            }

            GoToCurrentStop();
        }
    }

    void GoToCurrentStop()
    {
        agent.destination = stops[route[routeIndex]];
        agent.SearchPath();
    }

    
    void SnapStopsToGraph()
    {
        for (int i = 0; i < goals.Length; i++)
        {
            Vector3 raw = goals[i].position;
            NNInfo nearest = AstarPath.active != null
                ? AstarPath.active.GetNearest(raw,NearestNodeConstraint.Walkable)
                : default;

            stops[i] = nearest.node != null ? nearest.position : raw;
        }
    }

    bool HasArrived()
    {
        
        if (agent.pathPending) return false;

        if (agent.reachedDestination) return true;

       
        if (agent.reachedEndOfPath) return true;

       
        Vector3 diff = agent.position - stops[route[routeIndex]];
        diff.y = 0f;
        return diff.sqrMagnitude <= arriveRadius * arriveRadius;
    }

    void CalculateStops()
    {
        for (int i = 0; i < stops.Length; i++)
        {
            for (int j = i + 1; j < stops.Length; j++)
            {
                Path p = ai.StartPath(stops[i], stops[j], null);
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
    }

    int[] SolveRoute()
    {
        int entrance = 0;
        int checkout = goals.Length - 1;

        if (numGroc == 0)
        {
            return new int[] { entrance, checkout };
        }

        if (routeMode == RouteMode.Auto && numGroc <= MaxExactItems)
        {
            return HeldKarp(entrance, checkout);
        }

        if (routeMode == RouteMode.Auto)
        {
            Debug.Log($"{numGroc} items is above {MaxExactItems}; using nearest neighbor.");
        }

        return NearestNeighborRoute(entrance, checkout);
    }

  
    int[] NearestNeighborRoute(int entrance, int checkout)
    {
        int n = numGroc;
        bool[] visited = new bool[n];

        int[] result = new int[n + 2];
        result[0] = entrance;
        result[n + 1] = checkout;

        int current = entrance;
        int step = 1;

        while (step <= n)
        {
            int best = -1;
            float bestDist = float.PositiveInfinity;

            for (int j = 0; j < n; j++)
            {
                if (visited[j]) continue;

                float dist = d[current, j + 1];
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = j;
                }
            }

           
            if (best == -1) break;

            visited[best] = true;
            result[step++] = best + 1;
            current = best + 1;
        }

     
        for (int j = 0; j < n; j++)
        {
            if (visited[j]) continue;

            Debug.LogWarning($"{goals[j + 1].name} has no path from the previous stop.");
            result[step++] = j + 1;
        }

        if (improveWithTwoOpt)
        {
            TwoOpt(result);
        }

        return result;
    }

    
    void TwoOpt(int[] r)
    {
        const int MaxPasses = 100;
        bool improved = true;
        int passes = 0;

        while (improved && passes++ < MaxPasses)
        {
            improved = false;

            for (int i = 1; i < r.Length - 2; i++)
            {
                for (int k = i + 1; k < r.Length - 1; k++)
                {
                    float before = d[r[i - 1], r[i]] + d[r[k], r[k + 1]];
                    float after = d[r[i - 1], r[k]] + d[r[i], r[k + 1]];

                    if (after + 0.001f < before)
                    {
                        System.Array.Reverse(r, i, k - i + 1);
                        improved = true;
                    }
                }
            }
        }
    }

    int[] HeldKarp(int entrance, int checkout)
    {
        int n = numGroc;
        int setCount = 1 << n;
        int fullSet = setCount - 1;

        float[,] cost = new float[setCount, n];
        int[,] prev = new int[setCount, n];

        for (int mask = 0; mask < setCount; mask++)
        {
            for (int j = 0; j < n; j++)
            {
                cost[mask, j] = float.PositiveInfinity;
                prev[mask, j] = -1;
            }
        }

        for (int j = 0; j < n; j++)
        {
            cost[1 << j, j] = d[entrance, j + 1];
        }

        for (int mask = 1; mask < setCount; mask++)
        {
            for (int j = 0; j < n; j++)
            {
                if ((mask & (1 << j)) == 0) continue;

                int without = mask & ~(1 << j);
                if (without == 0) continue;

                for (int k = 0; k < n; k++)
                {
                    if ((without & (1 << k)) == 0) continue;

                    float candidate = cost[without, k] + d[k + 1, j + 1];
                    if (candidate < cost[mask, j])
                    {
                        cost[mask, j] = candidate;
                        prev[mask, j] = k;
                    }
                }
            }
        }

        int bestEnd = -1;
        float bestCost = float.PositiveInfinity;
        for (int j = 0; j < n; j++)
        {
            float total = cost[fullSet, j] + d[j + 1, checkout];
            if (total < bestCost)
            {
                bestCost = total;
                bestEnd = j;
            }
        }

        if (bestEnd == -1)
        {
            Debug.LogWarning("No exact route reaches every item. Falling back to nearest neighbor.");
            return NearestNeighborRoute(entrance, checkout);
        }

        int[] result = new int[n + 2];
        result[0] = entrance;
        result[n + 1] = checkout;

        int position = n;
        int currentSet = fullSet;
        int current = bestEnd;

        while (current != -1)
        {
            result[position--] = current + 1;

            int before = prev[currentSet, current];
            currentSet &= ~(1 << current);
            current = before;
        }

        return result;
    }

    float RouteLength(int[] r)
    {
        float total = 0f;
        for (int i = 0; i < r.Length - 1; i++)
        {
            total += d[r[i], r[i + 1]];
        }
        return total;
    }

    void OnDrawGizmos()
    {
        if (stops == null || route == null) return;

        for (int i = 0; i < route.Length; i++)
        {
            Gizmos.color = i < routeIndex ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(stops[route[i]], arriveRadius);
        }
    }
}