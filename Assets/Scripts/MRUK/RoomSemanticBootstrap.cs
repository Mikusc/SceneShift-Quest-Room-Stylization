using System;
using System.Text;
using Meta.XR.MRUtilityKit;
using UnityEngine;

[DisallowMultipleComponent]
public class RoomSemanticBootstrap : MonoBehaviour
{
    private static readonly MRUKAnchor.SceneLabels[] SummaryLabels =
    {
        MRUKAnchor.SceneLabels.FLOOR,
        MRUKAnchor.SceneLabels.CEILING,
        MRUKAnchor.SceneLabels.WALL_FACE,
        MRUKAnchor.SceneLabels.INNER_WALL_FACE,
        MRUKAnchor.SceneLabels.TABLE,
        MRUKAnchor.SceneLabels.SCREEN,
        MRUKAnchor.SceneLabels.STORAGE,
        MRUKAnchor.SceneLabels.COUCH,
        MRUKAnchor.SceneLabels.OTHER,
        MRUKAnchor.SceneLabels.GLOBAL_MESH,
    };

    [Header("References")]
    [SerializeField] private MRUK mruk;

    [Header("Logging")]
    [SerializeField] private bool logOnRoomCreated = true;
    [SerializeField] private bool logOnRoomUpdated;
    [SerializeField] private bool logAnchorDetails = true;
    [SerializeField] private bool includeBoundsSummary = true;

    public event Action SummaryChanged;

    public bool HasMrukReference => mruk != null;
    public bool IsMrukInitialized => mruk != null && mruk.IsInitialized;
    public bool HasReadyRoom => _currentRoom != null;
    public MRUK RoomSystem => mruk;
    public MRUKRoom CurrentRoom => _currentRoom;
    public string LatestReason => _latestReason;
    public string LatestPanelSummary => _latestPanelSummary;
    public string LatestDetailedSummary => _latestDetailedSummary;

    private bool _subscribed;
    private bool _hasLoggedReadyRoom;
    private MRUKRoom _currentRoom;
    private string _latestReason = "waiting";
    private string _latestPanelSummary = "[RoomSemanticBootstrap]\nRoom status: waiting\nMRUK: unresolved";
    private string _latestDetailedSummary = "[RoomSemanticBootstrap]\nRoom status: waiting\nMRUK: unresolved";

    private void Reset()
    {
        mruk = FindAnyObjectByType<MRUK>();
    }

    private void Awake()
    {
        if (mruk == null)
        {
            mruk = FindAnyObjectByType<MRUK>();
        }
    }

    private void OnEnable()
    {
        _hasLoggedReadyRoom = false;
        PublishWaitingState();
        Subscribe();
        TryLogCurrentRoom("already-initialized");
    }

    private void OnDisable()
    {
        Unsubscribe();
        _hasLoggedReadyRoom = false;
    }

    [ContextMenu("Log Current Room Summary")]
    public void LogCurrentRoomSummary()
    {
        TryLogCurrentRoom("manual");
    }

    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        if (mruk == null)
        {
            Debug.LogWarning("[RoomSemanticBootstrap] MRUK reference is missing. Assign an MRUK component in the scene.");
            PublishWaitingState("missing-mruk");
            return;
        }

        mruk.RegisterSceneLoadedCallback(HandleSceneLoaded);
        mruk.RoomCreatedEvent.AddListener(HandleRoomCreated);
        mruk.RoomUpdatedEvent.AddListener(HandleRoomUpdated);
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed || mruk == null)
        {
            return;
        }

        mruk.SceneLoadedEvent.RemoveListener(HandleSceneLoaded);
        mruk.RoomCreatedEvent.RemoveListener(HandleRoomCreated);
        mruk.RoomUpdatedEvent.RemoveListener(HandleRoomUpdated);
        _subscribed = false;
    }

    private void HandleSceneLoaded()
    {
        if (!logOnRoomCreated || mruk == null)
        {
            return;
        }

        var room = ResolveRoom();
        if (room != null)
        {
            LogRoomSummaryOnce("scene-loaded", room);
        }
    }

    private void HandleRoomCreated(MRUKRoom room)
    {
        if (logOnRoomCreated)
        {
            LogRoomSummaryOnce("room-created", room);
        }
    }

    private void HandleRoomUpdated(MRUKRoom room)
    {
        if (logOnRoomUpdated)
        {
            LogRoomSummary("room-updated", room);
        }
    }

    private void TryLogCurrentRoom(string reason)
    {
        if (mruk == null || !mruk.IsInitialized)
        {
            PublishWaitingState();
            return;
        }

        var room = ResolveRoom();
        if (room != null)
        {
            LogRoomSummaryOnce(reason, room);
        }
        else
        {
            PublishWaitingState("waiting-for-room");
        }
    }

    private MRUKRoom ResolveRoom()
    {
        if (mruk == null)
        {
            return null;
        }

        if (mruk.Rooms.Count > 0)
        {
            return mruk.Rooms[0];
        }

        try
        {
            return mruk.GetCurrentRoom();
        }
        catch
        {
            return null;
        }
    }

    private void LogRoomSummaryOnce(string reason, MRUKRoom room)
    {
        if (_hasLoggedReadyRoom)
        {
            return;
        }

        _hasLoggedReadyRoom = true;
        LogRoomSummary(reason, room);
    }

    private void LogRoomSummary(string reason, MRUKRoom room)
    {
        var panelSummary = BuildRoomSummary(reason, room, includePerAnchorDetails: false);
        var detailedSummary = BuildRoomSummary(reason, room, includePerAnchorDetails: logAnchorDetails);
        UpdateLatestSummary(reason, room, panelSummary, detailedSummary);
        Debug.Log(detailedSummary, room);
    }

    private string BuildRoomSummary(string reason, MRUKRoom room, bool includePerAnchorDetails)
    {
        var builder = new StringBuilder(1024);
        builder.AppendLine($"[RoomSemanticBootstrap] Room ready ({reason})");
        builder.AppendLine($"  Room Name: {room.name}");
        builder.AppendLine($"  Room Local: {room.IsLocal}");
        builder.AppendLine($"  Anchor Count: {room.Anchors.Count}");
        builder.AppendLine($"  Walls: {room.WallAnchors.Count}, Floors: {room.FloorAnchors.Count}, Ceilings: {room.CeilingAnchors.Count}");

        builder.Append("  Semantic Counts:");
        var hasSemanticCounts = false;
        foreach (var label in SummaryLabels)
        {
            var count = CountAnchorsWithLabel(room, label);
            if (count <= 0)
            {
                continue;
            }

            builder.Append(hasSemanticCounts ? "," : string.Empty);
            builder.Append($" {label}={count}");
            hasSemanticCounts = true;
        }

        if (!hasSemanticCounts)
        {
            builder.Append(" none");
        }

        builder.AppendLine();

        if (includePerAnchorDetails)
        {
            for (var index = 0; index < room.Anchors.Count; index++)
            {
                var anchor = room.Anchors[index];
                builder.Append($"  Anchor[{index:D2}] {anchor.name} labels={anchor.Label}");

                if (includeBoundsSummary)
                {
                    if (anchor.PlaneRect.HasValue)
                    {
                        var planeRect = anchor.PlaneRect.Value;
                        builder.Append($" plane=({planeRect.width:F2} x {planeRect.height:F2})");
                    }

                    if (anchor.VolumeBounds.HasValue)
                    {
                        var volumeBounds = anchor.VolumeBounds.Value;
                        builder.Append($" volume=({volumeBounds.size.x:F2}, {volumeBounds.size.y:F2}, {volumeBounds.size.z:F2})");
                    }
                }

                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private void UpdateLatestSummary(string reason, MRUKRoom room, string panelSummary, string detailedSummary)
    {
        _currentRoom = room;
        _latestReason = reason;
        _latestPanelSummary = panelSummary;
        _latestDetailedSummary = detailedSummary;
        SummaryChanged?.Invoke();
    }

    private void PublishWaitingState(string reason = "waiting")
    {
        _currentRoom = null;
        _latestReason = reason;
        _latestPanelSummary = BuildWaitingSummary(reason);
        _latestDetailedSummary = _latestPanelSummary;
        SummaryChanged?.Invoke();
    }

    private string BuildWaitingSummary(string reason)
    {
        var builder = new StringBuilder(256);
        builder.AppendLine("[RoomSemanticBootstrap]");
        builder.AppendLine($"Room status: {reason}");
        builder.AppendLine($"MRUK reference: {(HasMrukReference ? "present" : "missing")}");
        builder.AppendLine($"MRUK initialized: {IsMrukInitialized}");
        builder.Append("Hint: enter Play and wait for the simulator room to load.");
        return builder.ToString();
    }

    private static int CountAnchorsWithLabel(MRUKRoom room, MRUKAnchor.SceneLabels label)
    {
        var count = 0;
        foreach (var anchor in room.Anchors)
        {
            if (anchor.HasAnyLabel(label))
            {
                count++;
            }
        }

        return count;
    }
}
