using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShoppingCart : MonoBehaviour
{
    [Serializable]
    public class ItemModel
    {
        [Tooltip("Item name as it appears on the grocery list, such as \"Banana\".")]
        public string item;
        public GameObject prefab;

        public ItemModel(string item)
        {
            this.item = item;
        }
    }

    [Header("Models")]
    [Tooltip("Model for each item. Items without a prefab use the random pool.")]
    [SerializeField]
    private ItemModel[] itemModels =
    {
        // Fresh Produce
        new ItemModel("Banana"), new ItemModel("Apple"), new ItemModel("Orange"),
        new ItemModel("Lemon"), new ItemModel("Lime"), new ItemModel("Grapes"),
        new ItemModel("Strawberries"), new ItemModel("Blueberries"), new ItemModel("Avocado"),
        new ItemModel("Tomato"), new ItemModel("Potato"), new ItemModel("Onion"),
        new ItemModel("Carrot"), new ItemModel("Lettuce"), new ItemModel("Broccoli"),
        new ItemModel("Cucumber"), new ItemModel("Bell Pepper"), new ItemModel("Watermelon"),
        new ItemModel("Pineapple"),
        // Dairy&Eggs
        new ItemModel("Milk"), new ItemModel("Eggs"), new ItemModel("Butter"),
        new ItemModel("Cheese"), new ItemModel("Yogurt"),
        // Meat&Fish
        new ItemModel("Chicken"), new ItemModel("Ground Beef"), new ItemModel("Steak"),
        new ItemModel("Bacon"), new ItemModel("Sausage"), new ItemModel("Fish"),
        new ItemModel("Shrimp"),
        // Deli
        new ItemModel("Deli Meat"), new ItemModel("Hummus"),
        // Bakery
        new ItemModel("Bread"), new ItemModel("Bagels"), new ItemModel("Buns"),
        new ItemModel("Tortillas"),
        // Pastries
        new ItemModel("Croissant"), new ItemModel("Muffin"), new ItemModel("Donut"),
        new ItemModel("Cake"), new ItemModel("Cookies"), new ItemModel("Pie"),
        // Beverages
        new ItemModel("Water"), new ItemModel("Soda"), new ItemModel("Juice"),
        new ItemModel("Coffee"), new ItemModel("Tea"), new ItemModel("Sports Drink"),
        // Cereal&Breakfast
        new ItemModel("Cereal"), new ItemModel("Oatmeal"), new ItemModel("Granola"),
        new ItemModel("Pancake Mix"), new ItemModel("Syrup"), new ItemModel("Honey"),
        // FrozenFood
        new ItemModel("Ice Cream"), new ItemModel("Frozen Pizza"),
        new ItemModel("Frozen Vegetables"), new ItemModel("French Fries"),
        // PetFood
        new ItemModel("Dog Food"), new ItemModel("Cat Food"), new ItemModel("Cat Litter"),
        // BabyFood
        new ItemModel("Baby Food"), new ItemModel("Baby Formula"), new ItemModel("Diapers"),
        new ItemModel("Baby Wipes"),
        // Cleaning&Hygiene
        new ItemModel("Paper Towels"), new ItemModel("Toilet Paper"), new ItemModel("Dish Soap"),
        new ItemModel("Laundry Detergent"), new ItemModel("Trash Bags"), new ItemModel("Shampoo"),
        new ItemModel("Toothpaste"),
    };

    [Tooltip("Models used for items without their own prefab. One is picked at random.")]
    [SerializeField] private GameObject[] randomPool;

    [Header("Basket")]
    [Tooltip("Center of the basket's inside, in the cart's local space. Shown as a green box when the cart is selected.")]
    [SerializeField] private Vector3 basketCenter = new Vector3(0f, 0.7f, 0f);

    [Tooltip("Size of the basket's inside, in the cart's local space.")]
    [SerializeField] private Vector3 basketSize = new Vector3(0.5f, 0.4f, 0.8f);

    [Tooltip("Largest dimension of each item, in the cart's local units. Each item is scaled to fit.")]
    [SerializeField] private float itemSize = 0.18f;

    [Tooltip("Extra layers allowed above the basket's rim once the basket is full.")]
    [SerializeField] private int pileLayers = 2;

    [Tooltip("Random offset applied to each item's position, as a fraction of the item size.")]
    [SerializeField, Range(0f, 0.5f)] private float jitter = 0.15f;

    [Header("Drop Animation")]
    [SerializeField] private float dropHeight = 0.6f;
    [SerializeField] private float dropDuration = 0.45f;

    private readonly List<Vector3> slots = new List<Vector3>();
    private readonly List<GameObject> contents = new List<GameObject>();
    private Transform contentsRoot;

    public int Count => contents.Count;

    void Awake()
    {
        contentsRoot = new GameObject("CartContents").transform;
        contentsRoot.SetParent(transform, false);
        BuildSlots();
    }

    public void AddItem(string itemName)
    {
        GameObject prefab = FindPrefab(itemName);
        if (prefab == null && randomPool != null && randomPool.Length > 0)
        {
            prefab = randomPool[UnityEngine.Random.Range(0, randomPool.Length)];
        }

        if (prefab == null)
        {
            Debug.LogWarning($"ShoppingCart has no model for \"{itemName}\" and an empty random pool.");
            return;
        }

        Vector3 slot = NextSlot();
        GameObject model = Instantiate(prefab);
        model.name = $"Cart_{itemName}";
        DisablePhysics(model);

      
        float worldItemSize = itemSize * MaxComponent(contentsRoot.lossyScale);
        if (TryGetBounds(model, out Bounds bounds))
        {
            float largest = MaxComponent(bounds.size);
            if (largest > 1e-4f) model.transform.localScale *= worldItemSize / largest;
        }

        model.transform.SetParent(contentsRoot, true);
        model.transform.localRotation = Quaternion.Euler(
            UnityEngine.Random.Range(-8f, 8f),
            UnityEngine.Random.Range(0f, 360f),
            UnityEngine.Random.Range(-8f, 8f));
        model.transform.localPosition = slot;

     
        if (TryGetBounds(model, out bounds))
        {
            Vector3 slotWorld = contentsRoot.TransformPoint(slot);
            Vector3 bottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            model.transform.position += slotWorld - bottomCenter;
        }

        contents.Add(model);
        StartCoroutine(DropIn(model.transform, model.transform.localPosition, model.transform.localScale));
    }

    public void Clear()
    {
        foreach (GameObject model in contents)
        {
            if (model != null) Destroy(model);
        }
        contents.Clear();
    }

    
    GameObject FindPrefab(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return null;

        string lowered = " " + itemName.Trim().ToLowerInvariant() + " ";
        GameObject best = null;
        int bestLength = 0;

        foreach (ItemModel model in itemModels)
        {
            if (model == null || model.prefab == null || string.IsNullOrWhiteSpace(model.item)) continue;

            string key = model.item.Trim().ToLowerInvariant();
            if (lowered.Trim() == key) return model.prefab;

            bool whole = lowered.Contains(" " + key + " ")
                || lowered.Contains(" " + key + "s ")
                || (key.EndsWith("s") && lowered.Contains(" " + key.Substring(0, key.Length - 1) + " "));

            if (whole && key.Length > bestLength)
            {
                best = model.prefab;
                bestLength = key.Length;
            }
        }

        return best;
    }

   
    void BuildSlots()
    {
        slots.Clear();
        float size = Mathf.Max(0.01f, itemSize);

        int columns = Mathf.Max(1, Mathf.FloorToInt(basketSize.x / size));
        int rows = Mathf.Max(1, Mathf.FloorToInt(basketSize.z / size));
        int layers = Mathf.Max(1, Mathf.FloorToInt(basketSize.y / size)) + Mathf.Max(0, pileLayers);

        Vector3 min = basketCenter - basketSize * 0.5f;
        float cellX = basketSize.x / columns;
        float cellZ = basketSize.z / rows;
        System.Random random = new System.Random(GetInstanceID());

        for (int layer = 0; layer < layers; layer++)
        {
            List<Vector3> layerSlots = new List<Vector3>();
            for (int c = 0; c < columns; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    layerSlots.Add(new Vector3(
                        min.x + (c + 0.5f) * cellX,
                        min.y + layer * size,
                        min.z + (r + 0.5f) * cellZ));
                }
            }

            for (int i = layerSlots.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (layerSlots[i], layerSlots[j]) = (layerSlots[j], layerSlots[i]);
            }

            slots.AddRange(layerSlots);
        }
    }

    
    Vector3 NextSlot()
    {
        Vector3 slot;
        if (contents.Count < slots.Count)
        {
            slot = slots[contents.Count];
        }
        else
        {
            int topLayerStart = slots.Count - Mathf.Max(1, Mathf.FloorToInt(basketSize.x / itemSize) * Mathf.FloorToInt(basketSize.z / itemSize));
            slot = slots[UnityEngine.Random.Range(Mathf.Max(0, topLayerStart), slots.Count)];
        }

        float offset = itemSize * jitter;
        slot.x += UnityEngine.Random.Range(-offset, offset);
        slot.z += UnityEngine.Random.Range(-offset, offset);
        return slot;
    }

   
    IEnumerator DropIn(Transform model, Vector3 target, Vector3 scale)
    {
        Vector3 start = target + Vector3.up * dropHeight;

        for (float t = 0f; t < dropDuration; t += Time.deltaTime)
        {
            if (model == null) yield break;

            float k = t / dropDuration;
            model.localPosition = Vector3.Lerp(start, target, k * k);
            model.localScale = scale * Mathf.Lerp(0.7f, 1f, k);
            yield return null;
        }

        const float squashTime = 0.12f;
        for (float t = 0f; t < squashTime; t += Time.deltaTime)
        {
            if (model == null) yield break;

            float squash = Mathf.Sin(t / squashTime * Mathf.PI) * 0.12f;
            model.localPosition = target;
            model.localScale = new Vector3(scale.x * (1f + squash), scale.y * (1f - squash), scale.z * (1f + squash));
            yield return null;
        }

        if (model == null) yield break;
        model.localPosition = target;
        model.localScale = scale;
    }

    // Models ride along in the cart, so their physics would only push the player.
    static void DisablePhysics(GameObject model)
    {
        foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>())
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        foreach (Collider collider in model.GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }
    }

    static bool TryGetBounds(GameObject model, out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
        {
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    static float MaxComponent(Vector3 v)
    {
        return Mathf.Max(v.x, Mathf.Max(v.y, v.z));
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
        Gizmos.DrawWireCube(basketCenter, basketSize);
    }
}