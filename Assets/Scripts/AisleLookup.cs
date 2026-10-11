using System.Collections.Generic;
using UnityEngine;



public class AisleLookup : MonoBehaviour
{
    [Tooltip("GroceryAisles.csv imported as a TextAsset.")]
    [SerializeField] private TextAsset table;

    [Tooltip("Allow close spellings, such as \"bannana\" for \"banana\".")]
    [SerializeField] private bool allowTypos = true;

    private readonly Dictionary<string, string> itemToAisle = new Dictionary<string, string>();
    private readonly Dictionary<string, string> itemToCanonical = new Dictionary<string, string>();

    public IEnumerable<string> AllItems => itemToAisle.Keys;

    void Awake()
    {
        Load();
    }

    void Load()
    {
        itemToAisle.Clear();
        itemToCanonical.Clear();

        if (table == null)
        {
            Debug.LogWarning("AisleLookup has no table assigned.");
            return;
        }

        string[] lines = table.text.Split('\n');

       
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) continue;

            string[] cols = line.Split(',');
            if (cols.Length < 3) continue;

            string item = cols[0].Trim();
            itemToCanonical[item] = cols[1].Trim();
            itemToAisle[item] = cols[2].Trim();
        }
    }

    
    public bool TryFind(string input, out string canonical, out string aisle)
    {
        canonical = null;
        aisle = null;

        string key = Normalize(input);
        if (key.Length == 0) return false;

        string match = null;

        if (itemToAisle.ContainsKey(key))
        {
            match = key;
        }
        else
        {
            foreach (string variant in PluralVariants(key))
            {
                if (itemToAisle.ContainsKey(variant))
                {
                    match = variant;
                    break;
                }
            }
        }

        if (match == null && allowTypos)
        {
            match = ClosestSpelling(key);
        }

        if (match == null) return false;

        canonical = itemToCanonical[match];
        aisle = itemToAisle[match];
        return true;
    }

    static IEnumerable<string> PluralVariants(string key)
    {
        if (key.EndsWith("ies")) yield return key.Substring(0, key.Length - 3) + "y";
        if (key.EndsWith("es")) yield return key.Substring(0, key.Length - 2);
        if (key.EndsWith("s")) yield return key.Substring(0, key.Length - 1);
        if (key.EndsWith("y")) yield return key.Substring(0, key.Length - 1) + "ies";
        yield return key + "s";
        yield return key + "es";
    }

   
    string ClosestSpelling(string key)
    {
        if (key.Length < 4) return null;

        int maxDistance = key.Length < 8 ? 1 : 2;
        string best = null;
        int bestDistance = maxDistance + 1;

        foreach (string item in itemToAisle.Keys)
        {
            if (Mathf.Abs(item.Length - key.Length) > maxDistance) continue;

            int distance = Levenshtein(key, item, bestDistance);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = item;
                if (distance == 1) break;
            }
        }

        return best;
    }

   
    static int Levenshtein(string a, string b, int limit)
    {
        int[] prev = new int[b.Length + 1];
        int[] curr = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            int rowMin = curr[0];

            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Mathf.Min(Mathf.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                rowMin = Mathf.Min(rowMin, curr[j]);
            }

            if (rowMin >= limit) return limit;

            int[] swap = prev;
            prev = curr;
            curr = swap;
        }

        return prev[b.Length];
    }

    static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

     
        string[] parts = s.Trim().ToLowerInvariant().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }
}