using DFHack;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Owns local inspection selection and its passive HUD, independently of chunk updates.
[RequireComponent(typeof(Camera))]
public sealed class CursorInspection : MonoBehaviour
{
    [SerializeField] private RectTransform hudParent;
    [SerializeField] private Font font;
    private Camera viewCamera;
    private RectTransform card;
    private RectTransform marker;
    private Text heading, title, coordinates, details, hint;
    private Armok.Lifecycle.SessionCoordinator session;
    private DFCoord? selection;
    private float nextRefresh;
    public bool IsPinned { get; private set; }
    public DFCoord? Selection => selection;

    private void OnEnable()
    {
        viewCamera = GetComponent<Camera>();
        ClearSelection();
    }

    private void LateUpdate()
    {
        if (!ReferenceEquals(session, SessionRuntime.Current))
        {
            session = SessionRuntime.Current;
            ClearSelection();
        }
        if (!SessionRuntime.Ready || GameMap.Instance == null || !GameMap.Instance.enabled ||
            !viewCamera.isActiveAndEnabled || GameSettings.Instance.game.showDFScreen)
        {
            ClearSelection();
            SetVisible(false);
            return;
        }

        EnsureView();
        SetVisible(true);
        bool blocked = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        blocked |= ArmokInput.Action("Orbit").IsPressed() || ArmokInput.Action("Pan").IsPressed();
        InspectPointer(ArmokInput.PointerPosition, ArmokInput.Action("Select").WasPressedThisFrame(), blocked);
        RefreshView();
    }

    // A click pins a tile; another click releases it. UI clicks never change selection.
    public void InspectPointer(Vector2 pointer, bool clicked, bool blocked)
    {
        if (!SessionRuntime.Ready) { ClearSelection(); return; }
        if (blocked)
        {
            if (!IsPinned) Select(null);
            return;
        }
        if (IsPinned)
        {
            if (!clicked) return;
            IsPinned = false;
        }
        else if (clicked)
        {
            var target = Pick(pointer);
            Select(target);
            IsPinned = target.HasValue;
            nextRefresh = 0;
            return;
        }
        Select(Pick(pointer));
    }

    private DFCoord? Pick(Vector2 pointer)
    {
        if (!SessionRuntime.Ready || MapDataStore.Main == null || !viewCamera.pixelRect.Contains(pointer)) return null;
        DFCoord target;
        RaycastHit hit;
        if (!MapDataStore.Raycast(viewCamera.ScreenPointToRay(pointer), out target, out hit, viewCamera.farClipPlane)) return null;
        // The ray marcher can traverse loaded tiles outside the renderer's draw range.
        return GameMap.IsInVisibleArea(target) ? target : (DFCoord?)null;
    }

    private void Select(DFCoord? target)
    {
        if (!System.Nullable.Equals(selection, target)) nextRefresh = 0;
        selection = target;
        // Keep the old serialized cursor fields available to remaining scene consumers.
        if (GameMap.Instance == null) return;
        GameMap.Instance.cursX = target?.x ?? -30000;
        GameMap.Instance.cursY = target?.y ?? -30000;
        GameMap.Instance.cursZ = target?.z ?? -30000;
    }

    public void ClearSelection()
    {
        IsPinned = false;
        Select(null);
        nextRefresh = 0;
    }

    private void RefreshView()
    {
        if (card == null) return;
        card.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Min(340, Mathf.Max(180, hudParent.rect.width - 40)));
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.1f;
            heading.text = IsPinned ? "INSPECT / PINNED" : "INSPECT";
            string tileTitle, tileDetails;
            if (selection.HasValue)
            {
                TileInspection.TryDescribe(selection.Value, out tileTitle, out tileDetails);
                var p = selection.Value;
                title.text = tileTitle;
                coordinates.text = $"X {p.x}   Y {p.y}   Z {p.z}";
                coordinates.gameObject.SetActive(true);
                details.text = tileDetails;
                hint.text = IsPinned ? "Click the world again to release" : "Click to pin this tile";
            }
            else
            {
                title.text = "Explore your fortress";
                coordinates.gameObject.SetActive(false);
                details.text = "Hover over the map to inspect a tile.";
                hint.text = "WASD move · Q/E height · Scroll zoom\nRight-drag orbit · Middle-drag pan · Esc settings";
            }
        }

        marker.gameObject.SetActive(false);
        if (!selection.HasValue || !GameMap.IsInVisibleArea(selection.Value)) return;
        var world = MapCoordinates.DFtoUnityCoord(selection.Value) + Vector3.up * MapCoordinates.FloorHeight;
        var screen = viewCamera.WorldToScreenPoint(world);
        if (screen.z <= 0 || !viewCamera.pixelRect.Contains(screen)) return;
        Vector2 local;
        var canvas = hudParent.GetComponentInParent<Canvas>();
        var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(hudParent, screen, uiCamera, out local);
        marker.anchoredPosition = local;
        marker.gameObject.SetActive(true);
    }

    private void EnsureView()
    {
        if (card != null || hudParent == null) return;
        card = Rect("Inspection", hudParent);
        card.anchorMin = card.anchorMax = card.pivot = Vector2.zero;
        card.anchoredPosition = new Vector2(20, 20);
        card.sizeDelta = new Vector2(340, 0);
        var group = card.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        var background = card.gameObject.AddComponent<Image>();
        background.color = new Color(0.045f, 0.065f, 0.085f, 0.94f);
        background.raycastTarget = false;
        var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 14, 14);
        layout.spacing = 7;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        heading = Label("Heading", 11, new Color(0.44f, 0.83f, 0.77f));
        title = Label("Title", 21, Color.white);
        coordinates = Label("Coordinates", 12, new Color(0.63f, 0.7f, 0.75f));
        details = Label("Details", 14, new Color(0.84f, 0.87f, 0.9f));
        hint = Label("Hint", 11, new Color(0.63f, 0.7f, 0.75f));
        marker = Rect("Inspection target", hudParent);
        marker.sizeDelta = new Vector2(18, 18);
        var cross = marker.gameObject.AddComponent<Text>();
        cross.font = font;
        cross.fontSize = 20;
        cross.text = "+";
        cross.alignment = TextAnchor.MiddleCenter;
        cross.color = new Color(0.44f, 0.95f, 0.84f);
        cross.raycastTarget = false;
    }

    private Text Label(string name, int size, Color color)
    {
        var label = Rect(name, card).gameObject.AddComponent<Text>();
        label.font = font;
        label.fontSize = size;
        label.color = color;
        label.supportRichText = false; // DF names are plain text, not UI markup.
        label.raycastTarget = false;
        return label;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private void SetVisible(bool visible)
    {
        if (card != null) card.gameObject.SetActive(visible);
        if (marker != null && !visible) marker.gameObject.SetActive(false);
    }

    private void OnDisable() { ClearSelection(); SetVisible(false); }
    private void OnDestroy()
    {
        if (card != null) Destroy(card.gameObject);
        if (marker != null) Destroy(marker.gameObject);
    }
}
