using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public sealed class RuntimeGeneratedModelLoader : MonoBehaviour
{
    [Header("Folders")]
    [SerializeField] private string generatedObjectJobFolderName = "GeneratedObjectJobs";
    [SerializeField] private string runtimeModelFolderName = "GeneratedObjectRuntimeModels";
#if UNITY_EDITOR
    [SerializeField] private bool includeEditorLibraryJobs = true;
#endif

    [Header("Runtime Loading")]
    [SerializeField] private Transform runtimeModelRoot;
    [SerializeField] private string testModelUrl;
    [SerializeField, Min(1)] private int requestTimeoutSeconds = 120;
    [SerializeField] private bool clearPreviousOnLoad = true;
    [SerializeField] private bool writeJobRecords = true;
    [SerializeField] private bool fitToRequestBounds = true;
    [SerializeField] private bool stripGeneratedColliders = true;

    public event Action SummaryChanged;

    public RuntimeGeneratedModelInstance LastLoadedInstance => _lastLoadedInstance;
    public string LatestSummary => _latestSummary;

    private readonly List<GltfImport> _loadedImports = new();
    private RuntimeGeneratedModelInstance _lastLoadedInstance;
    private GameObject _lastLoadedRoot;
    private string _latestSummary = "[RuntimeGeneratedModelLoader]\nState: waiting";

    private void Reset()
    {
        EnsureRuntimeModelRoot();
    }

    private void Awake()
    {
        EnsureRuntimeModelRoot();
    }

    [ContextMenu("Load Test Runtime GLB URL")]
    public async void LoadTestModelUrl()
    {
        await LoadFromUrlAsync(testModelUrl);
    }

    [ContextMenu("Load Test Runtime GLB For Latest Request")]
    public async void LoadTestModelForLatestRequest()
    {
        if (string.IsNullOrWhiteSpace(testModelUrl))
        {
            PublishSummary("load-test-request", "Missing test model URL.");
            return;
        }

        if (!TryFindLatestGeneratedObjectJob(out var record, out var jobPath, false))
        {
            PublishSummary("load-test-request", "No generated-object job was found. Loading the test model without request bounds.");
            await LoadFromUrlAsync(testModelUrl);
            return;
        }

        record.RuntimeModelUrl = testModelUrl;
        record.State = GeneratedObjectJobState.RuntimeModelReady;
        record.StatusNote = "Runtime test GLB attached to latest generated-object request.";
        record.FailureReason = string.Empty;
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        WriteRecord(jobPath, record);
        await LoadFromRecordAsync(record, jobPath);
    }

    [ContextMenu("Load Latest Runtime Ready Job")]
    public async void LoadLatestRuntimeReadyJob()
    {
        if (!TryFindLatestGeneratedObjectJob(out var record, out var jobPath, true))
        {
            PublishSummary("load-latest", "No job with RuntimeModelUrl, RuntimeModelLocalPath, or GeneratedModelPath was found.");
            return;
        }

        await LoadFromRecordAsync(record, jobPath);
    }

    public Task<RuntimeGeneratedModelInstance> LoadFromRecordAsync(GeneratedAssetRecord record, string jobPath)
    {
        if (record == null)
        {
            PublishSummary("load-record", "Missing generated asset record.");
            return Task.FromResult<RuntimeGeneratedModelInstance>(null);
        }

        return LoadFromUrlAsync(ResolveRuntimeModelSource(record), record, jobPath);
    }

    public async Task<RuntimeGeneratedModelInstance> LoadFromUrlAsync(
        string modelSource,
        GeneratedAssetRecord record = null,
        string jobPath = null)
    {
        if (string.IsNullOrWhiteSpace(modelSource))
        {
            PublishSummary("load-url", "Missing model URL or local path.");
            return null;
        }

        if (!Application.isPlaying)
        {
            PublishSummary(
                "load-blocked",
                "Runtime GLB loading must run in Play Mode or a Quest build; glTFast runtime helpers use play-mode-only Unity APIs.");
            return null;
        }

        var workingRecord = record ?? CreateAdHocRecord(modelSource);
        try
        {
            EnsureRuntimeModelRoot();
            PublishSummary("load-start", Shorten(modelSource, 180));

            var localModelPath = await EnsureLocalModelAsync(modelSource, workingRecord, jobPath);
            if (string.IsNullOrWhiteSpace(localModelPath) || !File.Exists(localModelPath))
            {
                FailRecord(jobPath, workingRecord, $"Runtime model file was not available: {localModelPath}");
                return null;
            }

            if (clearPreviousOnLoad)
            {
                ClearLastLoadedModel();
            }

            var root = new GameObject($"RuntimeGeneratedModel_{ShortId(workingRecord.RequestId)}");
            root.transform.SetParent(runtimeModelRoot, false);
            _lastLoadedRoot = root;

            var importSettings = new ImportSettings
            {
                GenerateMipMaps = false,
                TexturesReadable = false,
                AnisotropicFilterLevel = 1,
                NodeNameMethod = NameImportMethod.OriginalUnique,
            };
            var gltf = new GltfImport(logger: new ConsoleLogger());
            _loadedImports.Add(gltf);

            var baseUri = new Uri(localModelPath);
            var loaded = await gltf.LoadFile(localModelPath, baseUri, importSettings);
            if (!loaded)
            {
                SafeDestroy(root);
                FailRecord(jobPath, workingRecord, $"glTFast failed to load runtime model: {localModelPath}");
                return null;
            }

            var instantiated = await gltf.InstantiateMainSceneAsync(root.transform);
            if (!instantiated)
            {
                SafeDestroy(root);
                FailRecord(jobPath, workingRecord, $"glTFast loaded the file but could not instantiate the main scene: {localModelPath}");
                return null;
            }

            if (stripGeneratedColliders)
            {
                RemoveGeneratedColliders(root);
            }

            NormalizeToCenteredBottomPivot(root.transform);
            if (!TryCalculateLocalBounds(root.transform, out var normalizedBounds))
            {
                SafeDestroy(root);
                FailRecord(jobPath, workingRecord, $"Runtime model has no renderable bounds: {localModelPath}");
                return null;
            }

            var sourceRequest = TryReadJson<GeneratedObjectRequest>(workingRecord.SourceRequestPath);
            var placedBounds = CalculateWorldBounds(root.transform) ?? new Bounds(root.transform.position, normalizedBounds.size);
            var appliedScale = Vector3.one;
            var appliedEuler = Vector3.zero;
            var placementStatus = "loaded_at_runtime_root";

            if (fitToRequestBounds &&
                sourceRequest != null &&
                TryFitToRequestBounds(root.transform, sourceRequest, normalizedBounds, out placedBounds, out appliedScale, out appliedEuler, out placementStatus))
            {
                root.name = $"RuntimeGeneratedModel_{ShortId(workingRecord.RequestId)}_{SafeName(sourceRequest.SemanticLabel)}";
            }

            var instance = root.AddComponent<RuntimeGeneratedModelInstance>();
            instance.Initialize(
                workingRecord,
                sourceRequest,
                jobPath,
                localModelPath,
                normalizedBounds,
                placedBounds,
                appliedScale,
                appliedEuler);

            var marker = root.AddComponent<StylizedFurnitureInstance>();
            marker.Initialize(
                workingRecord.RequestId,
                FirstNonEmpty(workingRecord.ObjectId, sourceRequest?.ObjectId),
                FirstNonEmpty(sourceRequest?.SemanticLabel, "generated_object"),
                "runtime_generated_model",
                Path.GetFileName(localModelPath));

            UpdateLoadedRecord(jobPath, workingRecord, localModelPath, normalizedBounds, appliedScale, appliedEuler, placementStatus);
            _lastLoadedInstance = instance;
            PublishSummary(
                "loaded",
                $"request={ShortId(workingRecord.RequestId)}, source={localModelPath}, bounds={FormatSize(normalizedBounds.size)}, scale={FormatSize(appliedScale)}, placement={placementStatus}");
            return instance;
        }
        catch (Exception exception)
        {
            FailRecord(jobPath, workingRecord, exception.Message);
            return null;
        }
    }

    [ContextMenu("Clear Last Runtime Model")]
    public void ClearLastLoadedModel()
    {
        if (_lastLoadedInstance != null)
        {
            TryReleaseRuntimeInstance(_lastLoadedInstance, "clear-last-runtime-model", out _);
            return;
        }

        if (_lastLoadedRoot != null)
        {
            var rootName = _lastLoadedRoot.name;
            _lastLoadedRoot.SetActive(false);
            SafeDestroy(_lastLoadedRoot);
            _lastLoadedRoot = null;
            PublishSummary("clear", $"Cleared last runtime generated model root {rootName}.");
            return;
        }

        PublishSummary("clear", "No runtime generated model instance is loaded.");
    }

    public bool TryReleaseRuntimeInstance(RuntimeGeneratedModelInstance instance, string reason, out string detail)
    {
        detail = string.Empty;
        if (instance == null)
        {
            detail = "instance is null.";
            return false;
        }

        var root = instance.gameObject;
        if (root == null)
        {
            detail = "instance root is null.";
            return false;
        }

        var rootName = root.name;
        var wasLastLoaded = _lastLoadedInstance == instance || _lastLoadedRoot == root;
        root.SetActive(false);
        SafeDestroy(root);

        if (wasLastLoaded)
        {
            _lastLoadedInstance = null;
            _lastLoadedRoot = null;
        }

        detail = $"released={rootName}, reason={reason}, wasLastLoaded={wasLastLoaded}";
        PublishSummary("released", detail);
        return true;
    }

    private async Task<string> EnsureLocalModelAsync(string modelSource, GeneratedAssetRecord record, string jobPath)
    {
        if (TryResolveLocalPath(modelSource, out var localPath))
        {
            MarkDownloaded(jobPath, record, localPath, "local_model_ready");
            return localPath;
        }

        if (!Uri.TryCreate(modelSource, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            FailRecord(jobPath, record, $"Runtime model source is neither a local file nor http(s): {modelSource}");
            return null;
        }

        var destinationPath = BuildRuntimeModelPath(record, uri);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? Application.persistentDataPath);
        if (File.Exists(destinationPath))
        {
            MarkDownloaded(jobPath, record, destinationPath, "reusing_downloaded_model");
            return destinationPath;
        }

        using var request = new UnityWebRequest(modelSource, UnityWebRequest.kHttpVerbGET);
        request.downloadHandler = new DownloadHandlerFile(destinationPath);
        request.timeout = Mathf.Max(1, requestTimeoutSeconds);
        var result = await SendRequestAsync(request);
        if (result != UnityWebRequest.Result.Success)
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            FailRecord(jobPath, record, $"Runtime model download failed: {request.error}");
            return null;
        }

        MarkDownloaded(jobPath, record, destinationPath, "downloaded_runtime_model");
        return destinationPath;
    }

    private static async Task<UnityWebRequest.Result> SendRequestAsync(UnityWebRequest request)
    {
        var operation = request.SendWebRequest();
        while (!operation.isDone)
        {
            await Task.Yield();
        }

        return request.result;
    }

    private void MarkDownloaded(string jobPath, GeneratedAssetRecord record, string localPath, string statusNote)
    {
        if (record == null)
        {
            return;
        }

        record.RuntimeModelLocalPath = localPath;
        record.RuntimeModelMimeType = ResolveMimeType(localPath);
        record.RuntimeModelHash = TryComputeSha256(localPath);
        record.State = GeneratedObjectJobState.RuntimeModelDownloaded;
        record.StatusNote = statusNote;
        record.FailureReason = string.Empty;
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        WriteRecord(jobPath, record);
    }

    private void UpdateLoadedRecord(
        string jobPath,
        GeneratedAssetRecord record,
        string localPath,
        Bounds normalizedBounds,
        Vector3 appliedScale,
        Vector3 appliedEuler,
        string placementStatus)
    {
        if (record == null)
        {
            return;
        }

        record.RuntimeModelLocalPath = localPath;
        record.RuntimeModelMimeType = ResolveMimeType(localPath);
        if (string.IsNullOrWhiteSpace(record.RuntimeModelHash))
        {
            record.RuntimeModelHash = TryComputeSha256(localPath);
        }

        record.RuntimeLoadedBounds = SerializableBounds.From(normalizedBounds.center, normalizedBounds.size);
        record.RuntimeLoadedScale = appliedScale;
        record.RuntimeLoadedEulerDegrees = appliedEuler;
        record.State = GeneratedObjectJobState.RuntimeLoaded;
        if (record.ReviewState == GeneratedObjectReviewState.None)
        {
            record.ReviewState = GeneratedObjectReviewState.Previewing;
        }

        record.StatusNote = $"Runtime GLB loaded; {placementStatus}.";
        record.FailureReason = string.Empty;
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        WriteRecord(jobPath, record);
    }

    private void FailRecord(string jobPath, GeneratedAssetRecord record, string reason)
    {
        if (record != null)
        {
            record.State = GeneratedObjectJobState.Failed;
            record.StatusNote = "Runtime model loading failed.";
            record.FailureReason = reason;
            record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
            WriteRecord(jobPath, record);
        }

        PublishSummary("failed", reason);
    }

    private void WriteRecord(string jobPath, GeneratedAssetRecord record)
    {
        if (!writeJobRecords || string.IsNullOrWhiteSpace(jobPath) || record == null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(jobPath) ?? Application.persistentDataPath);
            File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[RuntimeGeneratedModelLoader] Failed to write job record {jobPath}: {exception.Message}");
        }
    }

    private bool TryFindLatestGeneratedObjectJob(
        out GeneratedAssetRecord record,
        out string jobPath,
        bool requireRuntimeModelSource)
    {
        record = null;
        jobPath = null;
        var bestTime = DateTime.MinValue;

        foreach (var directory in GetJobDirectories())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var candidatePath in Directory.GetFiles(directory, "*.job.json", SearchOption.TopDirectoryOnly))
            {
                var candidate = TryReadJson<GeneratedAssetRecord>(candidatePath);
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.RequestId))
                {
                    continue;
                }

                if (requireRuntimeModelSource && string.IsNullOrWhiteSpace(ResolveRuntimeModelSource(candidate)))
                {
                    continue;
                }

                var updatedAt = DateTime.TryParse(candidate.UpdatedAtIsoUtc, out var parsed)
                    ? parsed.ToUniversalTime()
                    : File.GetLastWriteTimeUtc(candidatePath);
                if (record != null && updatedAt <= bestTime)
                {
                    continue;
                }

                record = candidate;
                jobPath = candidatePath;
                bestTime = updatedAt;
            }
        }

        return record != null;
    }

    private IEnumerable<string> GetJobDirectories()
    {
        yield return Path.Combine(Application.persistentDataPath, generatedObjectJobFolderName);
#if UNITY_EDITOR
        if (includeEditorLibraryJobs)
        {
            yield return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", generatedObjectJobFolderName));
        }
#endif
    }

    private static bool TryFitToRequestBounds(
        Transform root,
        GeneratedObjectRequest request,
        Bounds normalizedLocalBounds,
        out Bounds placedWorldBounds,
        out Vector3 appliedScale,
        out Vector3 appliedEuler,
        out string status)
    {
        placedWorldBounds = default;
        appliedScale = Vector3.one;
        appliedEuler = Vector3.zero;
        status = "request_bounds_unavailable";

        if (root == null || request == null || request.WorldBounds.Size.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        var targetSize = request.WorldBounds.Size;
        var sourceSize = normalizedLocalBounds.size;
        if (!IsUsableSize(sourceSize) || !IsUsableSize(targetSize))
        {
            status = $"invalid_bounds(source={FormatSize(sourceSize)}, target={FormatSize(targetSize)})";
            return false;
        }

        var footprintScale = Mathf.Max(0.01f, request.SafetyFootprintScale > 0.01f ? request.SafetyFootprintScale : 1f);
        var xScale = targetSize.x * footprintScale / sourceSize.x;
        var zScale = targetSize.z * footprintScale / sourceSize.z;
        var yScale = targetSize.y / sourceSize.y;

        if (request.VerticalFitMode == GeneratedObjectVerticalFitMode.BottomAlignOnly)
        {
            yScale = Mathf.Min(xScale, zScale);
        }

        appliedScale = new Vector3(
            SanitizeScale(xScale),
            SanitizeScale(yScale),
            SanitizeScale(zScale));

        var yawDegrees = TryGetYawDegrees(request.WorldPose.Rotation, out var yaw)
            ? yaw
            : request.BestViewYawDegrees;
        if (!request.PreserveYawOrientation)
        {
            yawDegrees = 0f;
        }

        appliedEuler = new Vector3(0f, yawDegrees, 0f);
        root.localScale = appliedScale;
        root.rotation = Quaternion.Euler(appliedEuler);
        root.position = request.WorldBounds.Center - Vector3.up * (targetSize.y * 0.5f);
        placedWorldBounds = CalculateWorldBounds(root) ?? new Bounds(request.WorldBounds.Center, targetSize);
        status = $"request_bounds_fit(target={FormatSize(targetSize)}, source={FormatSize(sourceSize)})";
        return true;
    }

    private static bool TryGetYawDegrees(Quaternion rotation, out float yawDegrees)
    {
        yawDegrees = 0f;
        if (Mathf.Abs(rotation.x) <= 0.00001f &&
            Mathf.Abs(rotation.y) <= 0.00001f &&
            Mathf.Abs(rotation.z) <= 0.00001f &&
            Mathf.Abs(rotation.w) <= 0.00001f)
        {
            return false;
        }

        yawDegrees = rotation.eulerAngles.y;
        return true;
    }

    private static void NormalizeToCenteredBottomPivot(Transform root)
    {
        if (root == null || !TryCalculateLocalBounds(root, out var bounds))
        {
            return;
        }

        var offset = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        for (var index = 0; index < root.childCount; index++)
        {
            root.GetChild(index).localPosition += offset;
        }
    }

    private static void RemoveGeneratedColliders(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        var colliders = root.GetComponentsInChildren<Collider>(true);
        foreach (var generatedCollider in colliders)
        {
            SafeDestroy(generatedCollider);
        }
    }

    private static bool TryCalculateLocalBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
        {
            return false;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var hasBounds = false;
        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer is ParticleSystemRenderer)
            {
                continue;
            }

            var localBounds = renderer.localBounds;
            var min = localBounds.min;
            var max = localBounds.max;
            for (var x = 0; x <= 1; x++)
            {
                for (var y = 0; y <= 1; y++)
                {
                    for (var z = 0; z <= 1; z++)
                    {
                        var localPoint = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        var worldPoint = renderer.transform.TransformPoint(localPoint);
                        var rootLocalPoint = root.InverseTransformPoint(worldPoint);
                        if (!hasBounds)
                        {
                            bounds = new Bounds(rootLocalPoint, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            bounds.Encapsulate(rootLocalPoint);
                        }
                    }
                }
            }
        }

        return hasBounds;
    }

    private static Bounds? CalculateWorldBounds(Transform root)
    {
        if (root == null)
        {
            return null;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds? bounds = null;
        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer is ParticleSystemRenderer || !renderer.enabled)
            {
                continue;
            }

            bounds = bounds.HasValue ? Encapsulate(bounds.Value, renderer.bounds) : renderer.bounds;
        }

        return bounds;
    }

    private static Bounds Encapsulate(Bounds current, Bounds next)
    {
        current.Encapsulate(next);
        return current;
    }

    private void EnsureRuntimeModelRoot()
    {
        if (runtimeModelRoot != null)
        {
            return;
        }

        var existing = GameObject.Find("RuntimeGeneratedModels");
        if (existing != null)
        {
            runtimeModelRoot = existing.transform;
            return;
        }

        var root = new GameObject("RuntimeGeneratedModels");
        root.transform.SetParent(transform, false);
        runtimeModelRoot = root.transform;
    }

    private string BuildRuntimeModelPath(GeneratedAssetRecord record, Uri uri)
    {
        var requestId = SafeName(!string.IsNullOrWhiteSpace(record?.RequestId)
            ? record.RequestId
            : DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
        var fileName = SafeName(Path.GetFileName(uri.LocalPath));
        if (string.IsNullOrWhiteSpace(fileName) || !Path.HasExtension(fileName))
        {
            fileName = $"{requestId}.glb";
        }

        if (!fileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".glb";
        }

        return Path.Combine(Application.persistentDataPath, runtimeModelFolderName, requestId, fileName);
    }

    private static bool TryResolveLocalPath(string source, out string path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeFile)
        {
            path = Path.GetFullPath(uri.LocalPath);
            return File.Exists(path);
        }

        if (File.Exists(source))
        {
            path = Path.GetFullPath(source);
            return true;
        }

        return false;
    }

    private static string ResolveRuntimeModelSource(GeneratedAssetRecord record)
    {
        if (record == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(record.RuntimeModelLocalPath))
        {
            return record.RuntimeModelLocalPath;
        }

        if (!string.IsNullOrWhiteSpace(record.RuntimeModelUrl))
        {
            return record.RuntimeModelUrl;
        }

        return record.GeneratedModelPath;
    }

    private static GeneratedAssetRecord CreateAdHocRecord(string modelSource)
    {
        return new GeneratedAssetRecord
        {
            RequestId = $"runtime_test_{DateTime.UtcNow:yyyyMMdd_HHmmss}",
            ObjectId = "runtime_test_object",
            ThemeId = "runtime_test",
            State = GeneratedObjectJobState.RuntimeModelReady,
            RuntimeModelUrl = modelSource,
            StatusNote = "Ad-hoc runtime GLB load test.",
            UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };
    }

    private static T TryReadJson<T>(string path) where T : class
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<T>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[RuntimeGeneratedModelLoader] Failed to read JSON {path}: {exception.Message}");
            return null;
        }
    }

    private static string TryComputeSha256(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return string.Empty;
            }

            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var value in hash)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[RuntimeGeneratedModelLoader] Failed to hash model {path}: {exception.Message}");
            return string.Empty;
        }
    }

    private static string ResolveMimeType(string path)
    {
        return path != null && path.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase)
            ? "model/gltf+json"
            : "model/gltf-binary";
    }

    private void PublishSummary(string state, string detail)
    {
        _latestSummary =
            "[RuntimeGeneratedModelLoader]\n" +
            $"State: {state}\n" +
            $"Detail: {detail}";
        SummaryChanged?.Invoke();
    }

    private static bool IsUsableSize(Vector3 size)
    {
        return size.x > 0.001f && size.y > 0.001f && size.z > 0.001f;
    }

    private static float SanitizeScale(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0.0001f ? value : 1f;
    }

    private static string SafeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "runtime";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Replace(' ', '_');
    }

    private static string ShortId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        return value.Length <= 18 ? value : value[..18];
    }

    private static string Shorten(string value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxCharacters)
        {
            return value;
        }

        return value[..Mathf.Max(0, maxCharacters - 3)] + "...";
    }

    private static string FormatSize(Vector3 value)
    {
        return FormattableString.Invariant($"{value.x:0.###}x{value.y:0.###}x{value.z:0.###}");
    }

    private static string FirstNonEmpty(string first, string second)
    {
        return !string.IsNullOrWhiteSpace(first) ? first : second;
    }

    private static void SafeDestroy(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}
