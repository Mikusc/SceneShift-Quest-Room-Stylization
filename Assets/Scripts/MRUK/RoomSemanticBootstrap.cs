using System;
using System.Collections;
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
        MRUKAnchor.SceneLabels.DOOR_FRAME,
        MRUKAnchor.SceneLabels.WINDOW_FRAME,
        MRUKAnchor.SceneLabels.TABLE,
        MRUKAnchor.SceneLabels.SCREEN,
        MRUKAnchor.SceneLabels.STORAGE,
        MRUKAnchor.SceneLabels.COUCH,
        MRUKAnchor.SceneLabels.BED,
        MRUKAnchor.SceneLabels.LAMP,
        MRUKAnchor.SceneLabels.PLANT,
        MRUKAnchor.SceneLabels.WALL_ART,
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
    [SerializeField] private bool logWaitingDiagnostics = true;
    [SerializeField] private float waitingDiagnosticDelaySeconds = 3f;
    [SerializeField] private bool retryDeviceLoadOnStartup = true;
    [SerializeField] private float deviceLoadRetryDelaySeconds = 4f;
    [SerializeField] private bool retryRequestSceneCaptureIfNoDataFound;

    [Header("Current Room Tracking")]
    [SerializeField, Tooltip("In multi-room captures, keep CurrentRoom aligned with the room the headset is actually in.")]
    private bool refreshCurrentRoomInPlay = true;
    [SerializeField, Min(0.1f)] private float currentRoomRefreshIntervalSeconds = 0.5f;
    [SerializeField] private bool logRoomSwitches = true;

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
    private bool _deviceLoadRetryInProgress;
    private Coroutine _startupDiagnosticsRoutine;
    private MRUKRoom _currentRoom;
    private float _nextCurrentRoomRefreshTime;
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
        StartStartupDiagnostics();
    }

    private void OnDisable()
    {
        StopStartupDiagnostics();
        Unsubscribe();
        _hasLoggedReadyRoom = false;
    }

    private void Update()
    {
        if (!Application.isPlaying || !refreshCurrentRoomInPlay)
        {
            return;
        }

        if (Time.unscaledTime < _nextCurrentRoomRefreshTime)
        {
            return;
        }

        _nextCurrentRoomRefreshTime = Time.unscaledTime + currentRoomRefreshIntervalSeconds;
        RefreshCurrentRoom("current-room-refresh", onlyIfChanged: true);
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
        if (mruk == null)
        {
            return;
        }

        RefreshCurrentRoom("scene-loaded", onlyIfChanged: false);
    }

    private void HandleRoomCreated(MRUKRoom room)
    {
        RefreshCurrentRoom("room-created", onlyIfChanged: false);
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

        RefreshCurrentRoom(reason, onlyIfChanged: false);
    }

    private void RefreshCurrentRoom(string reason, bool onlyIfChanged)
    {
        if (mruk == null || !mruk.IsInitialized)
        {
            return;
        }

        var room = ResolveRoom();
        if (room != null)
        {
            if (onlyIfChanged && IsSameRoom(_currentRoom, room))
            {
                return;
            }

            _hasLoggedReadyRoom = true;
            if (!onlyIfChanged || logRoomSwitches)
            {
                LogRoomSummary(reason, room);
            }
            else
            {
                UpdateLatestSummary(
                    reason,
                    room,
                    BuildRoomSummary(reason, room, includePerAnchorDetails: false),
                    BuildRoomSummary(reason, room, includePerAnchorDetails: false));
            }
        }
        else
        {
            PublishWaitingState("waiting-for-room");
        }
    }

    private void StartStartupDiagnostics()
    {
        StopStartupDiagnostics();

        if (!logWaitingDiagnostics && !retryDeviceLoadOnStartup)
        {
            return;
        }

        _startupDiagnosticsRoutine = StartCoroutine(StartupDiagnosticsRoutine());
    }

    private void StopStartupDiagnostics()
    {
        if (_startupDiagnosticsRoutine == null)
        {
            return;
        }

        StopCoroutine(_startupDiagnosticsRoutine);
        _startupDiagnosticsRoutine = null;
    }

    private IEnumerator StartupDiagnosticsRoutine()
    {
        yield return new WaitForSeconds(Mathf.Max(0.1f, waitingDiagnosticDelaySeconds));

        if (!_hasLoggedReadyRoom && logWaitingDiagnostics)
        {
            Debug.Log(BuildWaitingSummary("startup-delay"), this);
        }

        yield return new WaitForSeconds(Mathf.Max(0f, deviceLoadRetryDelaySeconds - waitingDiagnosticDelaySeconds));

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!_hasLoggedReadyRoom && retryDeviceLoadOnStartup)
        {
            RetryLoadSceneFromDevice("startup-timeout");
        }
#endif
    }

    private async void RetryLoadSceneFromDevice(string reason)
    {
        if (mruk == null || _deviceLoadRetryInProgress)
        {
            return;
        }

        _deviceLoadRetryInProgress = true;

        try
        {
            Debug.Log($"[RoomSemanticBootstrap] Retrying MRUK.LoadSceneFromDevice ({reason}); {BuildMrukStateLine()}");
            var result = await mruk.LoadSceneFromDevice(retryRequestSceneCaptureIfNoDataFound);
            Debug.Log($"[RoomSemanticBootstrap] MRUK.LoadSceneFromDevice result={result}; {BuildMrukStateLine()}");
            TryLogCurrentRoom($"device-retry-{result}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[RoomSemanticBootstrap] MRUK.LoadSceneFromDevice threw {ex.GetType().Name}: {ex.Message}\n{ex}");
            PublishWaitingState("device-retry-exception");
        }
        finally
        {
            _deviceLoadRetryInProgress = false;
        }
    }

    private MRUKRoom ResolveRoom()
    {
        if (mruk == null)
        {
            return null;
        }

        try
        {
            var currentRoom = mruk.GetCurrentRoom();
            if (currentRoom != null)
            {
                return currentRoom;
            }
        }
        catch
        {
            // MRUK may throw before device room localization is ready. Fall back below
            // so simulator/prefab iteration still has a debuggable room.
        }

        if (mruk.Rooms.Count == 1)
        {
            return mruk.Rooms[0];
        }

        return null;
    }

    private static bool IsSameRoom(MRUKRoom a, MRUKRoom b)
    {
        return ReferenceEquals(a, b) ||
               (a != null &&
                b != null &&
                string.Equals(a.name, b.name, StringComparison.Ordinal));
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
        builder.AppendLine(BuildMrukStateLine());
        if (mruk != null && mruk.Rooms.Count > 1)
        {
            builder.AppendLine("Hint: multiple MRUK rooms are loaded; waiting for MRUK.GetCurrentRoom() instead of falling back to Rooms[0].");
        }

        builder.Append("Hint: on Quest, keep the app focused and confirm any Guardian or spatial data prompt.");
        return builder.ToString();
    }

    private string BuildMrukStateLine()
    {
        if (mruk == null)
        {
            return "MRUK state: missing";
        }

        var settings = mruk.SceneSettings;
        if (settings == null)
        {
            return $"MRUK state: initialized={mruk.IsInitialized}, rooms={mruk.Rooms.Count}, settings=missing";
        }

        return $"MRUK state: initialized={mruk.IsInitialized}, rooms={mruk.Rooms.Count}, dataSource={settings.DataSource}, loadOnStartup={settings.LoadSceneOnStartup}, roomIndex={settings.RoomIndex}";
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
