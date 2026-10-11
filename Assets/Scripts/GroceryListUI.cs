using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif



public class GroceryListUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private AisleLookup lookup;
    [SerializeField] private RouteGuide routeGuide;

    [Tooltip("Optional. Cart that shows a model for each picked-up item.")]
    [SerializeField] private ShoppingCart cart;

    [Tooltip("Parent of the aisle groups (SuperMarket).")]
    [SerializeField] private Transform superMarket;

    [Header("Input")]
    [Tooltip("Panel shown while typing. Uses the input field's own object when empty.")]
    [SerializeField] private GameObject inputPanel;
    [SerializeField] private TMP_InputField inputField;

#if ENABLE_INPUT_SYSTEM
    [SerializeField] private Key openKey = Key.G;
#else
    [SerializeField] private KeyCode openKey = KeyCode.G;
#endif

    [Tooltip("Optional on-screen button that also opens the input field.")]
    [SerializeField] private Button openButton;
    [SerializeField] private Button startNavigationButton;

    [Tooltip("Player scripts to turn off while typing, such as movement and mouse look.")]
    [SerializeField] private Behaviour[] disableWhileTyping;
    [SerializeField] private bool unlockCursorWhileTyping = true;

    [Header("List")]
    [Tooltip("Object with a VerticalLayoutGroup that holds the list entries.")]
    [SerializeField] private RectTransform listContent;

    [Tooltip("Optional. Needs a TMP_Text. Can also have an Image named \"Check\" and a Button that removes the entry.")]
    [SerializeField] private GameObject entryPrefab;

    [Tooltip("Optional. Shows messages such as \"Couldn't find ...\".")]
    [SerializeField] private TMP_Text feedbackText;

    [Header("Pickup")]
    [Tooltip("Button shown near an aisle with items on the list. Its text reads \"Pick up {item}?\".")]
    [SerializeField] private Button pickupButton;

    [Tooltip("Optional. Uses the button's own TMP_Text when empty.")]
    [SerializeField] private TMP_Text pickupLabel;

    [Tooltip("How close the player has to be to an aisle's stop point for the prompt to appear.")]
    [SerializeField] private float pickupRadius = 2.5f;

#if ENABLE_INPUT_SYSTEM
    [SerializeField] private Key pickupKey = Key.E;
#else
    [SerializeField] private KeyCode pickupKey = KeyCode.E;
#endif

    [Header("Checkmark")]
    [Tooltip("The checkmark spritesheet frames, in order.")]
    [SerializeField] private Sprite[] checkFrames;
    [SerializeField] private float checkFps = 24f;
    [SerializeField] private Color doneTextColor = new Color(1f, 1f, 1f, 0.5f);

    [Tooltip("Seconds a checked item stays on the list after its checkmark finishes.")]
    [SerializeField] private float removeDelay = 0.6f;

    [Tooltip("Seconds a checked item takes to fade out before it's removed.")]
    [SerializeField] private float fadeDuration = 0.25f;

    private class Entry
    {
        public string canonical;
        public string aisle;
        public Transform target;
        public GameObject ui;
        public TMP_Text label;
        public Image check;
        public bool done;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private readonly Dictionary<string, Transform> aisleTargets = new Dictionary<string, Transform>();

    private bool navigating;
    private Entry pickupEntry;
    private CursorLockMode previousLockMode;
    private bool previousCursorVisible;

    private GameObject Panel => inputPanel != null ? inputPanel : inputField.gameObject;
    private bool IsOpen => Panel.activeSelf;

    void OnEnable()
    {
        inputField.onSubmit.AddListener(OnSubmit);
        if (openButton != null) openButton.onClick.AddListener(OpenInput);
        if (startNavigationButton != null) startNavigationButton.onClick.AddListener(StartNavigation);
        if (pickupButton != null) pickupButton.onClick.AddListener(OnPickupClicked);

        if (routeGuide != null) routeGuide.RouteCompleted += OnRouteCompleted;
    }

    void OnDisable()
    {
        inputField.onSubmit.RemoveListener(OnSubmit);
        if (openButton != null) openButton.onClick.RemoveListener(OpenInput);
        if (startNavigationButton != null) startNavigationButton.onClick.RemoveListener(StartNavigation);
        if (pickupButton != null) pickupButton.onClick.RemoveListener(OnPickupClicked);

        if (routeGuide != null) routeGuide.RouteCompleted -= OnRouteCompleted;
    }

    void Start()
    {
        Panel.SetActive(false);
        if (feedbackText != null) feedbackText.text = string.Empty;
        if (pickupLabel == null && pickupButton != null) pickupLabel = pickupButton.GetComponentInChildren<TMP_Text>(true);

        SetVisible(pickupButton, false);
        UpdateStartButton();
    }

    void Update()
    {
        if (!IsOpen && OpenPressed())
        {
            OpenInput();
        }
        else if (IsOpen && EscapePressed())
        {
            CloseInput();
        }

        UpdateStartButton();
        UpdatePickupPrompt();

        // The key is ignored while typing, so it doesn't fire on the letter E.
        if (!IsOpen && pickupEntry != null && PickupPressed())
        {
            PickUp(pickupEntry);
        }
    }

    // ---------- Input field ----------

    public void OpenInput()
    {
        if (!IsOpen)
        {
            Panel.SetActive(true);
            SetTyping(true);
        }

        StartCoroutine(FocusNextFrame());
    }

    public void CloseInput()
    {
        if (!IsOpen) return;

        inputField.DeactivateInputField();
        Panel.SetActive(false);
        SetTyping(false);
    }

    // Waiting a frame keeps the open key's letter out of the field, and lets the
    // field reactivate after Enter deactivates it.
    IEnumerator FocusNextFrame()
    {
        yield return null;
        inputField.text = string.Empty;
        inputField.ActivateInputField();
        inputField.Select();
    }

    void OnSubmit(string text)
    {
        // Enter on an empty field closes it.
        if (string.IsNullOrWhiteSpace(text))
        {
            CloseInput();
            return;
        }

        AddItem(text);

        if (IsOpen) StartCoroutine(FocusNextFrame());
    }

    void SetTyping(bool typing)
    {
        foreach (Behaviour behaviour in disableWhileTyping)
        {
            if (behaviour != null) behaviour.enabled = !typing;
        }

        if (!unlockCursorWhileTyping) return;

        if (typing)
        {
            previousLockMode = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = previousLockMode;
            Cursor.visible = previousCursorVisible;
        }
    }

    bool OpenPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current[openKey].wasPressedThisFrame;
#else
        return Input.GetKeyDown(openKey);
#endif
    }

    bool PickupPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current[pickupKey].wasPressedThisFrame;
#else
        return Input.GetKeyDown(pickupKey);
#endif
    }

    bool EscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    // ---------- List ----------

    public void AddItem(string text)
    {
        text = text.Trim();

        if (!lookup.TryFind(text, out string canonical, out string aisle))
        {
            ShowFeedback($"Couldn't find \"{text}\". Try another name.");
            return;
        }

        if (entries.Exists(e => e.canonical == canonical && !e.done))
        {
            ShowFeedback($"{Capitalize(canonical)} is already on the list.");
            return;
        }

        Transform target = ResolveAisleTarget(aisle);
        if (target == null)
        {
            ShowFeedback($"No aisle object found for {aisle}.");
            return;
        }

        Entry entry = new Entry { canonical = canonical, aisle = aisle, target = target };
        CreateEntryUI(entry);
        entries.Add(entry);

        ShowFeedback($"Added {Capitalize(canonical)} ({aisle}).");

        if (navigating) RefreshRoute();
    }

    public void RemoveItem(string canonical)
    {
        Entry entry = entries.Find(e => e.canonical == canonical);
        if (entry != null) RemoveEntry(entry);
    }

    // Removing a checked item doesn't replan: the route already moved on
    // when the item was picked up.
    void RemoveEntry(Entry entry)
    {
        if (entry.ui != null) Destroy(entry.ui);
        entries.Remove(entry);

        if (navigating && !entry.done) RefreshRoute();
    }

    void CreateEntryUI(Entry entry)
    {
        GameObject go;

        if (entryPrefab != null)
        {
            go = Instantiate(entryPrefab, listContent);
        }
        else
        {
            go = new GameObject("GroceryEntry", typeof(RectTransform));
            go.transform.SetParent(listContent, false);

            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = 28f;
            text.margin = new Vector4(0f, 0f, 48f, 0f);   // room for the checkmark

            LayoutElement layout = go.AddComponent<LayoutElement>();
            layout.preferredHeight = 40f;

            // Checkmark on the right side of the row, outside the layout.
            GameObject checkGo = new GameObject("Check", typeof(RectTransform));
            checkGo.transform.SetParent(go.transform, false);
            RectTransform rect = (RectTransform)checkGo.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(40f, 40f);
            checkGo.AddComponent<Image>().preserveAspect = true;
        }

        go.name = $"Entry_{entry.canonical}";
        entry.ui = go;
        entry.label = go.GetComponentInChildren<TMP_Text>();
        entry.label.text = $"{Capitalize(entry.canonical)}  <size=70%><color=#9AA4B2>{entry.aisle}</color></size>";

        Transform checkTransform = FindChildRecursive(go.transform, "Check");
        entry.check = checkTransform != null ? checkTransform.GetComponent<Image>() : null;
        if (entry.check != null) entry.check.enabled = false;

        Button removeButton = go.GetComponentInChildren<Button>();
        if (removeButton != null) removeButton.onClick.AddListener(() => RemoveEntry(entry));
    }

    void MarkDone(Entry entry)
    {
        entry.done = true;
        entry.label.color = doneTextColor;
        entry.label.fontStyle |= FontStyles.Strikethrough;

        StartCoroutine(FinishEntry(entry));
    }

    // Plays the checkmark, holds briefly, fades the row out, then removes it.
    IEnumerator FinishEntry(Entry entry)
    {
        if (entry.check != null && checkFrames != null && checkFrames.Length > 0)
        {
            yield return PlayCheck(entry.check);
        }

        yield return new WaitForSeconds(removeDelay);

        // The row may have been removed with its remove button meanwhile.
        if (entry.ui == null || !entries.Contains(entry)) yield break;

        if (fadeDuration > 0f)
        {
            CanvasGroup group = entry.ui.GetComponent<CanvasGroup>();
            if (group == null) group = entry.ui.AddComponent<CanvasGroup>();

            for (float t = 0f; t < fadeDuration; t += Time.deltaTime)
            {
                if (entry.ui == null) yield break;
                group.alpha = 1f - t / fadeDuration;
                yield return null;
            }
        }

        RemoveEntry(entry);
    }

    IEnumerator PlayCheck(Image image)
    {
        image.enabled = true;
        WaitForSeconds wait = new WaitForSeconds(1f / Mathf.Max(1f, checkFps));

        foreach (Sprite frame in checkFrames)
        {
            image.sprite = frame;
            yield return wait;
        }
    }

    // ---------- Navigation ----------

    public void StartNavigation()
    {
        List<Transform> targets = PendingTargets();
        if (targets.Count == 0)
        {
            ShowFeedback("Add an item before starting navigation.");
            return;
        }

        CloseInput();
        navigating = true;
        routeGuide.SetItems(targets);

        ShowFeedback($"Route planned through {targets.Count} aisle(s).");
    }

    // Replans after the list changes during navigation.
    void RefreshRoute()
    {
        List<Transform> targets = PendingTargets();

        if (targets.Count == 0)
        {
            routeGuide.ClearRoute();
            navigating = false;
            return;
        }

        routeGuide.SetItems(targets);
    }

    // One target per aisle that still has unchecked items, in the order they were added.
    List<Transform> PendingTargets()
    {
        List<Transform> targets = new List<Transform>();
        foreach (Entry entry in entries)
        {
            if (!entry.done && !targets.Contains(entry.target)) targets.Add(entry.target);
        }
        return targets;
    }

    // ---------- Pickup ----------

    // Start Navigation shows only while the input field is open, and hides
    // once navigation starts. It returns after the route completes.
    void UpdateStartButton()
    {
        SetVisible(startNavigationButton, IsOpen && !navigating);
    }

    // Shows the prompt for the closest unchecked item whose aisle is within
    // pickupRadius. Items in the same aisle come up one at a time, in the
    // order they were added.
    void UpdatePickupPrompt()
    {
        Entry best = null;
        float bestDistance = pickupRadius;

        foreach (Entry entry in entries)
        {
            if (entry.done) continue;

            float distance = routeGuide.DistanceTo(entry.target);
            if (distance <= bestDistance && (best == null || entry.target != best.target))
            {
                best = entry;
                bestDistance = distance;
            }
        }

        if (best != pickupEntry)
        {
            pickupEntry = best;
            if (pickupLabel != null && best != null) pickupLabel.text = $"Press E to pick up {Capitalize(best.canonical)}";
        }

        SetVisible(pickupButton, pickupEntry != null);
    }

    void OnPickupClicked()
    {
        if (pickupEntry != null) PickUp(pickupEntry);
    }

    void PickUp(Entry entry)
    {
        MarkDone(entry);
        ShowFeedback($"Picked up {Capitalize(entry.canonical)}.");

        if (cart != null) cart.AddItem(entry.canonical);

        // The route moves on once every item in this aisle is picked up.
        bool aisleDone = !entries.Exists(e => !e.done && e.target == entry.target);
        if (navigating && aisleDone) routeGuide.CompleteStop(entry.target);

        UpdatePickupPrompt();
    }

    static void SetVisible(Component component, bool visible)
    {
        if (component != null && component.gameObject.activeSelf != visible)
        {
            component.gameObject.SetActive(visible);
        }
    }

    void OnRouteCompleted()
    {
        navigating = false;
        ShowFeedback("Route complete.");
    }


    Transform ResolveAisleTarget(string aisle)
    {
        if (aisleTargets.TryGetValue(aisle, out Transform cached) && cached != null) return cached;

        Transform target = null;
        Transform group = superMarket != null ? superMarket.Find(aisle) : null;


        if (group != null)
        {
            foreach (Transform child in group)
            {
                if (child.name.Trim().EndsWith("Aisle", System.StringComparison.OrdinalIgnoreCase))
                {
                    target = child;
                    break;
                }
            }
        }


        if (target == null && superMarket != null)
        {
            string wanted = Squash(aisle) + "aisle";
            foreach (Transform t in superMarket.GetComponentsInChildren<Transform>(true))
            {
                if (Squash(t.name) == wanted)
                {
                    target = t;
                    break;
                }
            }
        }


        if (target == null && group != null)
        {
            Debug.LogWarning($"No \"{aisle}Aisle\" object found. Using the {aisle} group position.");
            target = group;
        }

        if (target != null) aisleTargets[aisle] = target;
        return target;
    }

    // ---------- Helpers ----------

    void ShowFeedback(string message)
    {
        if (feedbackText != null) feedbackText.text = message;
        Debug.Log(message);
    }

    static string Squash(string s)
    {
        return s.Replace(" ", string.Empty).Trim().ToLowerInvariant();
    }

    static string Capitalize(string s)
    {
        return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    static Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;

            Transform found = FindChildRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }
}