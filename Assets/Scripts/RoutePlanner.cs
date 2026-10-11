using System.Collections.Generic;


public static class RoutePlanner
{
   
    public const int MaxExactItems = 16;

    private const int MaxPasses = 1000;
    private const float Epsilon = 0.001f;

    public static int[] Solve(float[,] d)
    {
        int count = d.GetLength(0);

        if (count <= 2)
        {
            int[] trivial = new int[count];
            for (int i = 0; i < count; i++) trivial[i] = i;
            return trivial;
        }

        if (count - 2 <= MaxExactItems)
        {
            int[] exact = HeldKarp(d);
            if (exact != null) return exact;
        }

        int[] route = NearestNeighbor(d);
        LocalSearch(d, route);
        return route;
    }

    public static float Length(float[,] d, int[] r)
    {
        float total = 0f;
        for (int i = 0; i < r.Length - 1; i++)
        {
            total += d[r[i], r[i + 1]];
        }
        return total;
    }

    
    static int[] HeldKarp(float[,] d)
    {
        int count = d.GetLength(0);
        int entrance = 0;
        int checkout = count - 1;

        int n = count - 2;
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

        if (bestEnd == -1) return null;

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

   
    static int[] NearestNeighbor(float[,] d)
    {
        int count = d.GetLength(0);
        int n = count - 2;
        bool[] used = new bool[n];

        int[] result = new int[count];
        result[0] = 0;
        result[count - 1] = count - 1;

        int current = 0;
        int step = 1;

        while (step <= n)
        {
            int best = -1;
            float bestDist = float.PositiveInfinity;

            for (int j = 0; j < n; j++)
            {
                if (used[j]) continue;

                float dist = d[current, j + 1];
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = j;
                }
            }

            if (best == -1) break;

            used[best] = true;
            result[step++] = best + 1;
            current = best + 1;
        }

        for (int j = 0; j < n; j++)
        {
            if (!used[j]) result[step++] = j + 1;
        }

        return result;
    }

   
    static void LocalSearch(float[,] d, int[] r)
    {
        int passes = 0;
        bool improved = true;

        while (improved && passes++ < MaxPasses)
        {
            improved = TwoOpt(d, r);
            improved |= OrOpt(d, r);
        }
    }

   
    static bool TwoOpt(float[,] d, int[] r)
    {
        bool any = false;
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

                    if (after + Epsilon < before)
                    {
                        System.Array.Reverse(r, i, k - i + 1);
                        improved = true;
                        any = true;
                    }
                }
            }
        }

        return any;
    }

   
    static bool OrOpt(float[,] d, int[] r)
    {
        List<int> list = new List<int>(r);
        bool any = false;
        bool improved = true;
        int passes = 0;

        while (improved && passes++ < MaxPasses)
        {
            improved = false;

            for (int segLen = 1; segLen <= 3 && !improved; segLen++)
            {
                for (int i = 1; i + segLen <= list.Count - 1 && !improved; i++)
                {
                    int a = list[i - 1];
                    int s0 = list[i];
                    int s1 = list[i + segLen - 1];
                    int b = list[i + segLen];

                    float removeGain = d[a, s0] + d[s1, b] - d[a, b];
                    if (!IsFinite(removeGain)) continue;

                    for (int j = 0; j < list.Count - 1; j++)
                    {
                        if (j >= i - 1 && j <= i + segLen - 1) continue;

                        int p = list[j];
                        int q = list[j + 1];

                        float addForward = d[p, s0] + d[s1, q] - d[p, q];
                        float addReversed = d[p, s1] + d[s0, q] - d[p, q];
                        bool reverse = addReversed < addForward;
                        float add = reverse ? addReversed : addForward;

                        if (!IsFinite(add) || add + Epsilon >= removeGain) continue;

                        List<int> segment = list.GetRange(i, segLen);
                        if (reverse) segment.Reverse();

                        list.RemoveRange(i, segLen);
                        int insertAt = j < i ? j + 1 : j + 1 - segLen;
                        list.InsertRange(insertAt, segment);

                        improved = true;
                        any = true;
                        break;
                    }
                }
            }
        }

        list.CopyTo(r);
        return any;
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}