using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Object = UnityEngine.Object;

public static class PreDeviceBuildReadinessReportRunner
{
    private const string CanonicalScenePath = "Assets/Scenes/MR_RoomStylization.unity";
    private const string ReportFolderName = "PreDeviceBuildReadinessReports";
    private const string GeneratedObjectJobFolderName = "GeneratedObjectJobs";
    private const string GeneratedObjectRuntimeModelFolderName = "GeneratedObjectRuntimeModels";
    private const string PreDeviceRequestPrefix = "predevice_room_loop";

    [MenuItem("SceneShift/Validation/Run Pre-Device Build Readiness Report")]
    public static PreDeviceBuildReadinessReport RunReport()
    {
        var report = new PreDeviceBuildReadinessReport
        {
            ReportId = $"predevice_build_readiness_{DateTime.UtcNow:yyyyMMddHHmmss}",
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };

        CheckBuildTarget(report);
        CheckAndroidBuildEnvironment(report);
        CheckBuildScenes(report);
        CheckAndroidPlayerSettings(report);
        CheckAndroidNetworkPermission(report);
        CheckQuestAndroidManifest(report);
        CheckQuestPackages(report);
        CheckXrSettings(report);
        CheckRuntimeSceneWiring(report);
        CheckRuntimeGenerationSecurity(report);
        CheckLocalEvidence(report);
        CheckTerminalPreflightTools(report);

        report.OverallStatus = BuildOverallStatus(report);
        report.SuggestedNextActions = BuildSuggestedNextActions(report);
        var path = WriteReport(report);
        Debug.Log($"[PreDeviceBuildReadiness] {report.OverallStatus} report written: {path}");
        return report;
    }

    private static void CheckBuildTarget(PreDeviceBuildReadinessReport report)
    {
        AddCheck(
            report,
            "active_build_target",
            EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Warn,
            $"active={EditorUserBuildSettings.activeBuildTarget}, selectedGroup={EditorUserBuildSettings.selectedBuildTargetGroup}. Switch to Android before building the MQDH/test-channel package.");
    }

    private static void CheckAndroidBuildEnvironment(PreDeviceBuildReadinessReport report)
    {
        var androidPlaybackEnginePath = GetAndroidPlaybackEnginePath();
        var hasAndroidPlaybackEngine = Directory.Exists(androidPlaybackEnginePath);
        var buildTargetSupported = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android);

        AddCheck(
            report,
            "android_build_support_installed",
            hasAndroidPlaybackEngine && buildTargetSupported ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            $"BuildPipelineSupported={buildTargetSupported}, AndroidPlayerPathExists={hasAndroidPlaybackEngine}, path={androidPlaybackEnginePath}, candidates={string.Join(";", GetAndroidPlaybackEngineCandidatePaths())}");
    }

    private static string GetAndroidPlaybackEnginePath()
    {
        foreach (var candidate in GetAndroidPlaybackEngineCandidatePaths())
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return GetAndroidPlaybackEngineCandidatePaths()[0];
    }

    private static string[] GetAndroidPlaybackEngineCandidatePaths()
    {
        var embeddedPath = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer");
        var appBundlePath = Directory.GetParent(EditorApplication.applicationContentsPath)?.FullName;
        var editorRootPath = string.IsNullOrWhiteSpace(appBundlePath)
            ? null
            : Directory.GetParent(appBundlePath)?.FullName;
        var hubModulePath = string.IsNullOrWhiteSpace(editorRootPath)
            ? embeddedPath
            : Path.Combine(editorRootPath, "PlaybackEngines", "AndroidPlayer");

        return new[] { hubModulePath, embeddedPath };
    }

    private static void CheckBuildScenes(PreDeviceBuildReadinessReport report)
    {
        var enabledScenes = new List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled)
            {
                enabledScenes.Add(scene.path);
            }
        }

        AddCheck(
            report,
            "canonical_scene_in_build",
            enabledScenes.Contains(CanonicalScenePath) ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            enabledScenes.Count == 0 ? "No enabled build scenes." : string.Join(", ", enabledScenes));
    }

    private static void CheckAndroidPlayerSettings(PreDeviceBuildReadinessReport report)
    {
        var applicationId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        AddCheck(
            report,
            "android_application_id",
            string.IsNullOrWhiteSpace(applicationId) ? PreDeviceBuildReadinessStatus.Fail : PreDeviceBuildReadinessStatus.Pass,
            applicationId);

        AddCheck(
            report,
            "android_version_code",
            PlayerSettings.Android.bundleVersionCode > 0 ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            PlayerSettings.Android.bundleVersionCode.ToString());

        var minSdk = (int)PlayerSettings.Android.minSdkVersion;
        AddCheck(
            report,
            "android_min_sdk",
            minSdk >= 32 ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            $"{PlayerSettings.Android.minSdkVersion} ({minSdk})");

        var targetSdk = (int)PlayerSettings.Android.targetSdkVersion;
        AddCheck(
            report,
            "android_target_sdk",
            targetSdk >= 34 ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Warn,
            $"{PlayerSettings.Android.targetSdkVersion} ({targetSdk})");

        var architectures = PlayerSettings.Android.targetArchitectures;
        AddCheck(
            report,
            "android_arm64",
            (architectures & AndroidArchitecture.ARM64) != 0 ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            architectures.ToString());

        var scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
        AddCheck(
            report,
            "android_scripting_backend",
            scriptingBackend == ScriptingImplementation.IL2CPP ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            scriptingBackend.ToString());
    }

    private static void CheckAndroidNetworkPermission(PreDeviceBuildReadinessReport report)
    {
        var projectSettings = ReadProjectFile("ProjectSettings/ProjectSettings.asset");
        var androidManifest = ReadProjectFile("Assets/Plugins/Android/AndroidManifest.xml");
        var openXrSettings = ReadProjectFile("Assets/XR/Settings/OpenXRPackageSettings.asset");
        var customManifestEnabled = projectSettings.Contains("useCustomMainManifest: 1", StringComparison.Ordinal);
        var forceInternetPermission = projectSettings.Contains("ForceInternetPermission: 1", StringComparison.Ordinal);
        var customManifestHasInternet = androidManifest.Contains("android.permission.INTERNET", StringComparison.Ordinal);
        var openXrRemovesInternetPermission = openXrSettings.Contains("forceRemoveInternetPermission: 1", StringComparison.Ordinal);
        var hasNetworkPermission = (forceInternetPermission || customManifestHasInternet) && !openXrRemovesInternetPermission;

        AddCheck(
            report,
            "android_internet_permission",
            hasNetworkPermission ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            $"customManifest={customManifestEnabled}, manifestInternet={customManifestHasInternet}, forceInternet={forceInternetPermission}, openXrRemovesInternet={openXrRemovesInternetPermission}. Runtime GLB download and HTTP backend submission require Android internet access.");
    }

    private static void CheckQuestAndroidManifest(PreDeviceBuildReadinessReport report)
    {
        var projectSettings = ReadProjectFile("ProjectSettings/ProjectSettings.asset");
        var androidManifest = ReadProjectFile("Assets/Plugins/Android/AndroidManifest.xml");
        var customManifestEnabled = projectSettings.Contains("useCustomMainManifest: 1", StringComparison.Ordinal);
        var customManifestExists = !string.IsNullOrWhiteSpace(androidManifest);
        AddCheck(
            report,
            "android_custom_manifest_enabled",
            customManifestEnabled && customManifestExists ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            $"useCustomMainManifest={customManifestEnabled}, manifestExists={customManifestExists}");

        var hasScenePermission = androidManifest.Contains("com.oculus.permission.USE_SCENE", StringComparison.Ordinal);
        var hasAnchorPermission = androidManifest.Contains("com.oculus.permission.USE_ANCHOR_API", StringComparison.Ordinal);
        AddCheck(
            report,
            "quest_scene_anchor_permissions",
            hasScenePermission && hasAnchorPermission ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            $"USE_SCENE={hasScenePermission}, USE_ANCHOR_API={hasAnchorPermission}");

        var hasHeadsetCameraPermission = androidManifest.Contains("horizonos.permission.HEADSET_CAMERA", StringComparison.Ordinal);
        var hasPassthroughFeature = androidManifest.Contains("com.oculus.feature.PASSTHROUGH", StringComparison.Ordinal);
        AddCheck(
            report,
            "quest_pca_passthrough_manifest",
            hasHeadsetCameraPermission && hasPassthroughFeature ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            $"HEADSET_CAMERA={hasHeadsetCameraPermission}, PASSTHROUGH={hasPassthroughFeature}");

        var hasQuest3 = androidManifest.Contains("quest3", StringComparison.OrdinalIgnoreCase);
        var hasQuest3S = androidManifest.Contains("quest3s", StringComparison.OrdinalIgnoreCase);
        AddCheck(
            report,
            "quest_supported_devices_manifest",
            hasQuest3 && hasQuest3S ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Warn,
            ExtractManifestAttribute(androidManifest, "com.oculus.supportedDevices", "android:value"));

        var horizonMinSdk = ExtractIntegerAttribute(androidManifest, "horizonos:minSdkVersion");
        var horizonTargetSdk = ExtractIntegerAttribute(androidManifest, "horizonos:targetSdkVersion");
        AddCheck(
            report,
            "quest_horizonos_sdk_manifest",
            horizonMinSdk >= 60 && horizonTargetSdk >= 201 ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Warn,
            $"horizonos:minSdkVersion={horizonMinSdk}, horizonos:targetSdkVersion={horizonTargetSdk}");

        var skipPermissionsDialogValue = ExtractManifestAttribute(androidManifest, "unityplayer.SkipPermissionsDialog", "android:value");
        AddCheck(
            report,
            "quest_permissions_dialog_enabled",
            string.Equals(skipPermissionsDialogValue, "false", StringComparison.OrdinalIgnoreCase)
                ? PreDeviceBuildReadinessStatus.Pass
                : PreDeviceBuildReadinessStatus.Warn,
            $"unityplayer.SkipPermissionsDialog={skipPermissionsDialogValue}");

        var hasVrLaunchCategory = androidManifest.Contains("com.oculus.intent.category.VR", StringComparison.Ordinal);
        var hasHeadTrackingFeature = androidManifest.Contains("android.hardware.vr.headtracking", StringComparison.Ordinal);
        AddCheck(
            report,
            "quest_vr_launch_manifest",
            hasVrLaunchCategory && hasHeadTrackingFeature ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            $"VRCategory={hasVrLaunchCategory}, HeadTracking={hasHeadTrackingFeature}");
    }

    private static void CheckQuestPackages(PreDeviceBuildReadinessReport report)
    {
        var manifest = ReadProjectFile("Packages/manifest.json");
        var lockFile = ReadProjectFile("Packages/packages-lock.json");

        AddPackageCheck(report, manifest, "com.meta.xr.sdk.core");
        AddPackageCheck(report, manifest, "com.meta.xr.mrutilitykit");
        AddPackageCheck(report, manifest, "com.meta.xr.sdk.interaction");
        AddPackageCheck(report, manifest, "com.unity.xr.openxr");
        AddCheck(
            report,
            "package_gltfast_runtime",
            lockFile.Contains("\"com.unity.cloud.gltfast\"", StringComparison.Ordinal) ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            "Runtime GLB loading depends on com.unity.cloud.gltfast from the lock file.");
    }

    private static void AddPackageCheck(PreDeviceBuildReadinessReport report, string manifest, string packageName)
    {
        AddCheck(
            report,
            $"package_{packageName}",
            manifest.Contains($"\"{packageName}\"", StringComparison.Ordinal) ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            packageName);
    }

    private static void CheckXrSettings(PreDeviceBuildReadinessReport report)
    {
        var xrSettings = ReadProjectFile("Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
        var openXrLoaderMeta = ReadProjectFile("Assets/XR/Loaders/OpenXRLoader.asset.meta");
        var openXrSettings = ReadProjectFile("Assets/XR/Settings/OpenXRPackageSettings.asset");
        var openXrLoaderGuid = ExtractGuid(openXrLoaderMeta);

        AddCheck(
            report,
            "android_openxr_loader",
            !string.IsNullOrWhiteSpace(openXrLoaderGuid) && xrSettings.Contains(openXrLoaderGuid, StringComparison.Ordinal)
                ? PreDeviceBuildReadinessStatus.Pass
                : PreDeviceBuildReadinessStatus.Fail,
            string.IsNullOrWhiteSpace(openXrLoaderGuid) ? "OpenXRLoader guid not found." : $"OpenXRLoader guid={openXrLoaderGuid}");

        AddCheck(
            report,
            "android_metaxr_openxr_feature",
            openXrSettings.Contains("m_Name: MetaXRFeature Android", StringComparison.Ordinal) &&
            openXrSettings.Contains("featureIdInternal: com.meta.openxr.feature.metaxr", StringComparison.Ordinal) &&
            openXrSettings.Contains("m_enabled: 1", StringComparison.Ordinal)
                ? PreDeviceBuildReadinessStatus.Pass
                : PreDeviceBuildReadinessStatus.Warn,
            "MetaXRFeature Android should be enabled in OpenXR package settings.");
    }

    private static void CheckRuntimeSceneWiring(PreDeviceBuildReadinessReport report)
    {
        AddLoadedComponentCheck<RoomSemanticBootstrap>(report, "scene_room_semantic_bootstrap");
        AddLoadedComponentCheck<SceneShiftUISetDashboard>(report, "scene_dashboard");
        AddLoadedComponentCheck<PreDeviceRuntimeLoopValidator>(report, "scene_predevice_runtime_loop_validator");
        AddLoadedComponentCheck<PreDeviceSmokeReportRunner>(report, "scene_predevice_smoke_report_runner");
        AddLoadedComponentCheck<QuestRuntimeGenerationClient>(report, "scene_runtime_generation_client");
        AddLoadedComponentCheck<RuntimeGeneratedModelLoader>(report, "scene_runtime_generated_model_loader");
        AddLoadedComponentCheck<GeneratedObjectReviewController>(report, "scene_generated_object_review_controller");
        AddLoadedComponentCheck<CorrectionModeController>(report, "scene_correction_controller");
        AddLoadedComponentCheck<AnchorThemeApplier>(report, "scene_anchor_theme_applier");
        CheckPassthroughOnlyToggleBootstrap(report);
    }

    private static void AddLoadedComponentCheck<T>(PreDeviceBuildReadinessReport report, string name) where T : Object
    {
        var found = Object.FindObjectsByType<T>(FindObjectsInactive.Include).Length > 0;
        AddCheck(
            report,
            name,
            found ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            found ? typeof(T).Name : $"{typeof(T).Name} was not found in the loaded scene.");
    }

    private static void CheckRuntimeGenerationSecurity(PreDeviceBuildReadinessReport report)
    {
        var client = Object.FindAnyObjectByType<QuestRuntimeGenerationClient>(FindObjectsInactive.Include);
        if (client == null)
        {
            AddCheck(report, "runtime_client_security", PreDeviceBuildReadinessStatus.Fail, "QuestRuntimeGenerationClient is missing.");
            return;
        }

        var serializedClient = new SerializedObject(client);
        var clientModeProperty = serializedClient.FindProperty("clientMode");
        var clientMode = clientModeProperty != null
            ? clientModeProperty.enumDisplayNames[clientModeProperty.enumValueIndex]
            : string.Empty;
        var backendSubmitUrl = serializedClient.FindProperty("backendSubmitUrl")?.stringValue ?? string.Empty;
        var localTestModelUrl = serializedClient.FindProperty("localTestModelUrl")?.stringValue ?? string.Empty;
        var hasBackendSubmitUrl = !string.IsNullOrWhiteSpace(backendSubmitUrl);
        var backendSubmitUrlIsHttps = hasBackendSubmitUrl &&
            Uri.TryCreate(backendSubmitUrl, UriKind.Absolute, out var backendUri) &&
            backendUri.Scheme == Uri.UriSchemeHttps;
        var backendSubmitUrlLooksSecretBearing = BackendSubmitUrlLooksLikeSecretCarrier(backendSubmitUrl);
        var isLocalTestMode = clientModeProperty != null &&
            clientModeProperty.enumValueIndex == (int)RuntimeGenerationClientMode.LocalTestModelUrl;
        var isHttpBackendMode = clientModeProperty != null &&
            clientModeProperty.enumValueIndex == (int)RuntimeGenerationClientMode.HttpBackend;
        var runtimeModeStatus = PreDeviceBuildReadinessStatus.Fail;
        var runtimeModeDetailSuffix = "unsupported mode for headset package.";
        if (clientModeProperty != null)
        {
            if (isLocalTestMode)
            {
                runtimeModeStatus = PreDeviceBuildReadinessStatus.Pass;
                runtimeModeDetailSuffix = "known-GLB local runtime loading spike.";
            }
            else if (isHttpBackendMode)
            {
                runtimeModeStatus = hasBackendSubmitUrl && backendSubmitUrlIsHttps && !backendSubmitUrlLooksSecretBearing
                    ? PreDeviceBuildReadinessStatus.Pass
                    : PreDeviceBuildReadinessStatus.Fail;
                runtimeModeDetailSuffix = runtimeModeStatus == PreDeviceBuildReadinessStatus.Pass
                    ? "secure HTTPS backend runtime generation package."
                    : "HttpBackend requires a configured HTTPS endpoint with no query/header secret material.";
            }
        }

        AddCheck(
            report,
            "runtime_client_mode",
            runtimeModeStatus,
            $"mode={clientMode}, backendSubmitUrl={(hasBackendSubmitUrl ? "configured" : "empty")}, {runtimeModeDetailSuffix}");

        var localTestModelUrlValid = Uri.TryCreate(localTestModelUrl, UriKind.Absolute, out var localTestModelUri);
        AddCheck(
            report,
            "runtime_local_test_model_url",
            isLocalTestMode && !localTestModelUrlValid
                ? PreDeviceBuildReadinessStatus.Fail
                : PreDeviceBuildReadinessStatus.Pass,
            isHttpBackendMode && string.IsNullOrWhiteSpace(localTestModelUrl)
                ? "empty because HttpBackend package should not rely on the fixed Box.glb test URL."
                : localTestModelUrl);

        AddCheck(
            report,
            "runtime_local_test_model_https",
            isLocalTestMode && (!localTestModelUrlValid || localTestModelUri.Scheme != Uri.UriSchemeHttps)
                ? PreDeviceBuildReadinessStatus.Fail
                : PreDeviceBuildReadinessStatus.Pass,
            isHttpBackendMode && string.IsNullOrWhiteSpace(localTestModelUrl)
                ? "not required for HttpBackend mode"
                : localTestModelUrlValid ? localTestModelUri.Scheme : "invalid-url");

        var adapterDetail = "not required for LocalTestModelUrl mode";
        var directAdaptersReady = !isHttpBackendMode;
        if (isHttpBackendMode)
        {
            directAdaptersReady = DirectServiceAdaptersDisabledForHttpBackend(out adapterDetail);
        }

        AddCheck(
            report,
            "direct_service_adapters_disabled_for_http_backend",
            directAdaptersReady ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            adapterDetail);

        var runtimeLoader = Object.FindAnyObjectByType<RuntimeGeneratedModelLoader>(FindObjectsInactive.Include);
        var runtimeLoaderTestModelUrl = runtimeLoader != null
            ? new SerializedObject(runtimeLoader).FindProperty("testModelUrl")?.stringValue ?? string.Empty
            : string.Empty;
        var runtimeLoaderTestModelUrlValid = Uri.TryCreate(runtimeLoaderTestModelUrl, UriKind.Absolute, out var runtimeLoaderTestModelUri);
        var runtimeLoaderTestModelReady = true;
        var runtimeLoaderTestModelDetail = "not required for HttpBackend mode";
        if (isHttpBackendMode && !string.IsNullOrWhiteSpace(runtimeLoaderTestModelUrl))
        {
            runtimeLoaderTestModelReady = false;
            runtimeLoaderTestModelDetail = "HttpBackend package should not serialize the fixed Box.glb loader test URL.";
        }
        else if (isLocalTestMode)
        {
            runtimeLoaderTestModelReady = runtimeLoaderTestModelUrlValid &&
                runtimeLoaderTestModelUri.Scheme == Uri.UriSchemeHttps;
            runtimeLoaderTestModelDetail = runtimeLoaderTestModelUrlValid
                ? runtimeLoaderTestModelUri.Scheme
                : "invalid-url";
        }

        AddCheck(
            report,
            "runtime_loader_test_model_url_for_mode",
            runtimeLoaderTestModelReady ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            runtimeLoaderTestModelDetail);

        AddCheck(
            report,
            "runtime_backend_submit_url_https",
            !hasBackendSubmitUrl || backendSubmitUrlIsHttps
                ? PreDeviceBuildReadinessStatus.Pass
                : PreDeviceBuildReadinessStatus.Fail,
            !hasBackendSubmitUrl ? "empty for LocalTestModelUrl mode" : backendSubmitUrl);

        CheckRuntimeLoadingImplementation(report);

        var sceneText = ReadProjectFile(CanonicalScenePath);
        AddCheck(
            report,
            "scene_api_key_overrides_empty",
            HasOnlyEmptySerializedField(sceneText, "apiKeyOverride")
                ? PreDeviceBuildReadinessStatus.Pass
                : PreDeviceBuildReadinessStatus.Fail,
            "The legacy apiKeyOverride field must be absent or empty. SceneShiftCredentialBuildGuard scans all packaged scenes and assets before a build.");

        AddCheck(
            report,
            "scene_backend_submit_url_secret_scan",
            backendSubmitUrlLooksSecretBearing
                ? PreDeviceBuildReadinessStatus.Fail
                : PreDeviceBuildReadinessStatus.Pass,
            !hasBackendSubmitUrl
                ? "empty for LocalTestModelUrl mode"
                : "configured endpoint contains no obvious query/header secret material.");

        AddCheck(
            report,
            "no_obvious_embedded_secret",
            ContainsObviousEmbeddedSecret(sceneText) ? PreDeviceBuildReadinessStatus.Fail : PreDeviceBuildReadinessStatus.Pass,
            "Scanned canonical scene for common embedded secret prefixes.");

        var packagedConfigPaths = CollectPackagedConfigPaths();
        var packagedSecretHits = FindSecretHits(packagedConfigPaths);
        AddCheck(
            report,
            "packaged_config_secret_scan",
            packagedSecretHits.Count == 0 ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            packagedSecretHits.Count == 0
                ? $"scanned={packagedConfigPaths.Count} packaged config/asset files"
                : BuildSecretHitSummary(packagedSecretHits));

        var generatedRecordPaths = CollectGeneratedRecordPaths();
        var generatedRecordHits = FindSecretHits(generatedRecordPaths);
        AddCheck(
            report,
            "generated_record_secret_scan",
            generatedRecordPaths.Count == 0
                ? PreDeviceBuildReadinessStatus.Warn
                : generatedRecordHits.Count == 0
                    ? PreDeviceBuildReadinessStatus.Pass
                    : PreDeviceBuildReadinessStatus.Fail,
            generatedRecordPaths.Count == 0
                ? "No generated-object or surface job JSON records found to scan."
                : generatedRecordHits.Count == 0
                    ? $"scanned={generatedRecordPaths.Count} generated job JSON files"
                    : BuildSecretHitSummary(generatedRecordHits));
    }

    private static void CheckRuntimeLoadingImplementation(PreDeviceBuildReadinessReport report)
    {
        var loaderSource = ReadProjectFile("Assets/Scripts/Perception/RuntimeGeneratedModelLoader.cs");
        var usesPersistentDataPath = loaderSource.Contains("Application.persistentDataPath", StringComparison.Ordinal);
        var usesUnityWebRequest = loaderSource.Contains("UnityWebRequest", StringComparison.Ordinal);
        var usesDownloadHandlerFile = loaderSource.Contains("DownloadHandlerFile", StringComparison.Ordinal);
        var usesGltfRuntimeLoad = loaderSource.Contains(".LoadFile(", StringComparison.Ordinal) ||
                                  loaderSource.Contains("LoadGltfBinary", StringComparison.Ordinal);
        var referencesAssetDatabase = loaderSource.Contains("AssetDatabase", StringComparison.Ordinal);
        AddCheck(
            report,
            "runtime_loader_assetdatabase_free",
            usesPersistentDataPath &&
            usesUnityWebRequest &&
            usesDownloadHandlerFile &&
            usesGltfRuntimeLoad &&
            !referencesAssetDatabase
                ? PreDeviceBuildReadinessStatus.Pass
                : PreDeviceBuildReadinessStatus.Fail,
            $"persistentDataPath={usesPersistentDataPath}, UnityWebRequest={usesUnityWebRequest}, DownloadHandlerFile={usesDownloadHandlerFile}, gltfRuntimeLoad={usesGltfRuntimeLoad}, referencesAssetDatabase={referencesAssetDatabase}");

        var clientSource = ReadProjectFile("Assets/Scripts/Perception/QuestRuntimeGenerationClient.cs");
        var clientUsesPersistentDataPath = clientSource.Contains("Application.persistentDataPath", StringComparison.Ordinal);
        var clientUsesUnityWebRequest = clientSource.Contains("UnityWebRequest", StringComparison.Ordinal);
        var clientWritesRuntimeArtifacts =
            clientSource.Contains("WriteRuntimeBackendArtifact", StringComparison.Ordinal) &&
            clientSource.Contains("runtime-submission", StringComparison.Ordinal) &&
            clientSource.Contains("runtime-result", StringComparison.Ordinal) &&
            clientSource.Contains("RuntimeBackendSubmissionPath", StringComparison.Ordinal) &&
            clientSource.Contains("RuntimeBackendResultPath", StringComparison.Ordinal);
        AddCheck(
            report,
            "runtime_backend_client_runtime_path",
            clientUsesPersistentDataPath && clientUsesUnityWebRequest && clientWritesRuntimeArtifacts
                ? PreDeviceBuildReadinessStatus.Pass
                : PreDeviceBuildReadinessStatus.Fail,
            $"persistentDataPath={clientUsesPersistentDataPath}, UnityWebRequest={clientUsesUnityWebRequest}, runtimeArtifacts={clientWritesRuntimeArtifacts}");
    }

    private static void CheckLocalEvidence(PreDeviceBuildReadinessReport report)
    {
        var projectRoot = GetProjectRoot();
        var smokeDir = Path.Combine(projectRoot, "Library", "PreDeviceSmokeReports");
        var visualDir = Path.Combine(projectRoot, "Library", "PreDeviceVisualEvidence");
        var latestSmokeReport = GetLatestFile(smokeDir, "predevice_smoke_*.json");
        var latestVisualReview = GetLatestFile(visualDir, "predevice_visual_review_*.md");
        var latestVisualImage = GetLatestFile(visualDir, "*.png");
        PreDeviceSmokeReport smokeReport = null;

        AddCheck(
            report,
            "latest_smoke_report_exists",
            !string.IsNullOrWhiteSpace(latestSmokeReport)
                ? PreDeviceBuildReadinessStatus.Pass
                : PreDeviceBuildReadinessStatus.Warn,
            string.IsNullOrWhiteSpace(latestSmokeReport) ? smokeDir : latestSmokeReport);

        if (!string.IsNullOrWhiteSpace(latestSmokeReport))
        {
            var smokeStatus = ReadSmokeReportStatus(latestSmokeReport);
            var smokeReadinessStatus = smokeStatus switch
            {
                "Pass" => PreDeviceBuildReadinessStatus.Pass,
                "PassWithManualVisualChecks" => PreDeviceBuildReadinessStatus.Pass,
                "PassWithWarnings" => PreDeviceBuildReadinessStatus.Warn,
                "Fail" => PreDeviceBuildReadinessStatus.Fail,
                _ => PreDeviceBuildReadinessStatus.Warn,
            };
            AddCheck(
                report,
                "latest_smoke_report_status",
                smokeReadinessStatus,
                $"status={smokeStatus}, file={latestSmokeReport}");

            smokeReport = ReadSmokeReport(latestSmokeReport);
            CheckLatestSmokeRuntimeEvidence(report, smokeReport, latestSmokeReport);
        }

        CheckActivePreDeviceRuntimeArtifacts(report, latestSmokeReport);

        AddCheck(
            report,
            "visual_evidence_exists",
            !string.IsNullOrWhiteSpace(latestVisualReview) && !string.IsNullOrWhiteSpace(latestVisualImage)
                ? PreDeviceBuildReadinessStatus.Pass
                : PreDeviceBuildReadinessStatus.Warn,
            $"review={(string.IsNullOrWhiteSpace(latestVisualReview) ? "missing" : latestVisualReview)}, image={(string.IsNullOrWhiteSpace(latestVisualImage) ? "missing" : latestVisualImage)}");

        if (!string.IsNullOrWhiteSpace(latestSmokeReport) && !string.IsNullOrWhiteSpace(latestVisualReview))
        {
            AddCheck(
                report,
                "visual_evidence_matches_latest_smoke",
                File.GetLastWriteTimeUtc(latestVisualReview) >= File.GetLastWriteTimeUtc(latestSmokeReport)
                    ? PreDeviceBuildReadinessStatus.Pass
                    : PreDeviceBuildReadinessStatus.Warn,
                $"smoke={latestSmokeReport}, visualReview={latestVisualReview}");
        }

        if (!string.IsNullOrWhiteSpace(latestSmokeReport) && !string.IsNullOrWhiteSpace(latestVisualImage))
        {
            AddCheck(
                report,
                "visual_image_matches_latest_smoke",
                File.GetLastWriteTimeUtc(latestVisualImage) >= File.GetLastWriteTimeUtc(latestSmokeReport)
                    ? PreDeviceBuildReadinessStatus.Pass
                    : PreDeviceBuildReadinessStatus.Warn,
                $"smoke={latestSmokeReport}, visualImage={latestVisualImage}");
        }

        if (!string.IsNullOrWhiteSpace(latestSmokeReport) && !string.IsNullOrWhiteSpace(latestVisualReview))
        {
            var visualReviewText = ReadFileOrEmpty(latestVisualReview);
            var smokeId = !string.IsNullOrWhiteSpace(smokeReport?.ReportId)
                ? smokeReport.ReportId
                : Path.GetFileNameWithoutExtension(latestSmokeReport);
            var smokeJsonFileName = Path.GetFileName(latestSmokeReport);
            var smokeMarkdownFileName = Path.GetFileName(Path.ChangeExtension(latestSmokeReport, ".md"));
            var referencesLatestSmoke = ContainsAny(
                visualReviewText,
                smokeId,
                smokeJsonFileName,
                smokeMarkdownFileName);
            AddCheck(
                report,
                "visual_review_references_latest_smoke",
                referencesLatestSmoke ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Warn,
                $"smokeId={smokeId}, visualReview={latestVisualReview}");

            if (!string.IsNullOrWhiteSpace(latestVisualImage))
            {
                var visualImageFileName = Path.GetFileName(latestVisualImage);
                AddCheck(
                    report,
                    "visual_review_references_latest_image",
                    visualReviewText.Contains(visualImageFileName, StringComparison.Ordinal)
                        ? PreDeviceBuildReadinessStatus.Pass
                        : PreDeviceBuildReadinessStatus.Warn,
                    $"image={visualImageFileName}, visualReview={latestVisualReview}");
            }
        }
    }

    private static void CheckTerminalPreflightTools(PreDeviceBuildReadinessReport report)
    {
        AddToolCapabilityCheck(
            report,
            "tool_install_unity_android_support",
            "Tools/install_unity_android_support.sh",
            "Unity Android Support Installer",
            "install-modules",
            "android-sdk-ndk-tools",
            "--wait-for-close",
            "RefusedRunningUnityOrHub",
            "AndroidSupportInstallLogs");

        AddToolCapabilityCheck(
            report,
            "tool_check_unity_android_support",
            "Tools/check_unity_android_support.sh",
            "AndroidPlayer exists",
            "SDK exists",
            "OpenJDK exists",
            "android-open-jdk");

        AddToolCapabilityCheck(
            report,
            "tool_check_android_support_recovery",
            "Tools/check_android_support_recovery.sh",
            "MissingAndroidSupport",
            "NeedsUnityEvidenceRefresh",
            "ReadyForAndroidSwitchGate",
            "run_mqdh_terminal_prepackage_suite.sh",
            "android-sdk-ndk-tools");

        AddToolCapabilityCheck(
            report,
            "tool_scan_predevice_secrets",
            "Tools/scan_predevice_secrets.sh",
            "Pre-Device Secret Scan",
            "Findings");

        AddToolCapabilityCheck(
            report,
            "tool_handoff_bundle_writer",
            "Tools/write_mqdh_handoff_bundle.sh",
            "secret_scan",
            "Bundle Files");

        AddToolCapabilityCheck(
            report,
            "tool_handoff_bundle_verifier",
            "Tools/verify_mqdh_handoff_bundle.sh",
            "Bundle verification: Pass",
            "files/secret_scan/secret_scan.md");

        AddToolCapabilityCheck(
            report,
            "tool_mqdh_terminal_prepackage_suite",
            "Tools/run_mqdh_terminal_prepackage_suite.sh",
            "write_mqdh_handoff_bundle.sh",
            "run_predevice_local_gate.sh",
            "verify_predevice_local_gate.sh",
            "show_mqdh_handoff_status.sh");

        AddToolCapabilityCheck(
            report,
            "tool_mqdh_package_build_runner",
            "Assets/Scripts/Editor/MqdhPackageBuildRunner.cs",
            "BuildPipeline.BuildPlayer",
            "Build MQDH Test Package",
            "run_predevice_local_gate.sh --package-artifact",
            "verify_predevice_local_gate.sh --require-package-artifact");

        AddToolCapabilityCheck(
            report,
            "tool_mqdh_package_build_report_verifier",
            "Tools/verify_mqdh_package_build_report.sh",
            "BuiltAndVerified",
            "--allow-blocked",
            "MQDH package build report verification");

        AddToolCapabilityCheck(
            report,
            "tool_predevice_local_gate",
            "Tools/run_predevice_local_gate.sh",
            "--package-artifact",
            "MQDH package artifact verification");

        AddToolCapabilityCheck(
            report,
            "tool_predevice_local_gate_verifier",
            "Tools/verify_predevice_local_gate.sh",
            "--require-package-artifact",
            "Final package gate requires --package-artifact");

        AddToolCapabilityCheck(
            report,
            "tool_predevice_gate_selftest",
            "Tools/test_predevice_gate_scripts.sh",
            "credential-bearing fixture",
            "Pre-device gate self-test: Pass");

        AddToolCapabilityCheck(
            report,
            "tool_true_device_preflight_audit",
            "Tools/audit_true_device_preflight.sh",
            "SceneShift True-Device Preflight Audit",
            "Final package local gate verifier",
            "--require-ready");

        AddToolCapabilityCheck(
            report,
            "tool_package_artifact_verifier",
            "Tools/verify_mqdh_package_artifact.sh",
            "arm64-v8a/libil2cpp.so",
            "Artifact strings contain likely long-lived credentials");

        AddToolCapabilityCheck(
            report,
            "tool_headset_evidence_collector",
            "Tools/collect_mqdh_headset_evidence.sh",
            "adb",
            "logcat",
            "screencap");

        AddToolCapabilityCheck(
            report,
            "tool_headset_evidence_verifier",
            "Tools/verify_mqdh_headset_evidence.sh",
            "MQDH headset evidence verification",
            "package dump",
            "screenshot");
    }

    private static void AddToolCapabilityCheck(
        PreDeviceBuildReadinessReport report,
        string name,
        string relativePath,
        params string[] requiredNeedles)
    {
        var path = Path.Combine(GetProjectRoot(), relativePath);
        if (!File.Exists(path))
        {
            AddCheck(report, name, PreDeviceBuildReadinessStatus.Fail, $"{relativePath} is missing.");
            return;
        }

        var text = File.ReadAllText(path);
        var missingNeedles = new List<string>();
        foreach (var needle in requiredNeedles)
        {
            if (!string.IsNullOrWhiteSpace(needle) &&
                !text.Contains(needle, StringComparison.Ordinal))
            {
                missingNeedles.Add(needle);
            }
        }

        AddCheck(
            report,
            name,
            missingNeedles.Count == 0 ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            missingNeedles.Count == 0
                ? $"{relativePath} present"
                : $"{relativePath} missing expected capability markers: {string.Join(", ", missingNeedles)}");
    }

    private static void CheckLatestSmokeRuntimeEvidence(
        PreDeviceBuildReadinessReport report,
        PreDeviceSmokeReport smokeReport,
        string smokeReportPath)
    {
        if (smokeReport == null)
        {
            AddCheck(
                report,
                "latest_smoke_runtime_loaded_evidence",
                PreDeviceBuildReadinessStatus.Warn,
                $"Could not parse latest smoke report: {smokeReportPath}");
            return;
        }

        var safeTableCheck = FindSmokeCheck(smokeReport, "safe_table_target");
        AddCheck(
            report,
            "latest_smoke_safe_table_target",
            string.Equals(safeTableCheck?.Status, "Pass", StringComparison.Ordinal)
                ? PreDeviceBuildReadinessStatus.Pass
                : PreDeviceBuildReadinessStatus.Warn,
            safeTableCheck?.Detail ?? "safe_table_target check not found in latest smoke report.");

        var planText = FindSmokeCheck(smokeReport, "stylization_plan")?.Detail ?? smokeReport.PlanSummary ?? string.Empty;
        var plannerWarnings = ExtractNamedCounter(planText, "warnings");
        AddCheck(
            report,
            "latest_smoke_plan_warnings_zero",
            plannerWarnings == 0 ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Warn,
            plannerWarnings >= 0
                ? $"warnings={plannerWarnings}, file={smokeReportPath}"
                : $"warnings counter missing, file={smokeReportPath}");

        var queueText = FindSmokeCheck(smokeReport, "queue_status")?.Detail ?? smokeReport.QueueSummary ?? string.Empty;
        var runtimeLoadedCount = ExtractNamedCounter(queueText, "runtimeLoaded");
        AddCheck(
            report,
            "latest_smoke_runtime_loaded_evidence",
            runtimeLoadedCount > 0 ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Warn,
            $"runtimeLoaded={runtimeLoadedCount}, file={smokeReportPath}");

        var runtimeInstanceCheck = FindSmokeCheck(smokeReport, "runtime_loaded_instance_metadata");
        AddCheck(
            report,
            "latest_smoke_runtime_instance_metadata",
            string.Equals(runtimeInstanceCheck?.Status, "Pass", StringComparison.Ordinal)
                ? PreDeviceBuildReadinessStatus.Pass
                : PreDeviceBuildReadinessStatus.Warn,
            runtimeInstanceCheck?.Detail ?? "runtime_loaded_instance_metadata check not found in latest smoke report.");

        var requestJobContractCheck = FindSmokeCheck(smokeReport, "runtime_request_job_contract");
        AddCheck(
            report,
            "latest_smoke_request_job_contract",
            string.Equals(requestJobContractCheck?.Status, "Pass", StringComparison.Ordinal)
                ? PreDeviceBuildReadinessStatus.Pass
                : PreDeviceBuildReadinessStatus.Warn,
            requestJobContractCheck?.Detail ?? "runtime_request_job_contract check not found in latest smoke report.");

        var runtimeBackendArtifactCheck = FindSmokeCheck(smokeReport, "runtime_backend_artifact_contract");
        AddCheck(
            report,
            "latest_smoke_backend_artifact_contract",
            string.Equals(runtimeBackendArtifactCheck?.Status, "Pass", StringComparison.Ordinal)
                ? PreDeviceBuildReadinessStatus.Pass
                : PreDeviceBuildReadinessStatus.Warn,
            runtimeBackendArtifactCheck?.Detail ?? "runtime_backend_artifact_contract check not found in latest smoke report.");

        var editabilityCheck = FindSmokeCheck(smokeReport, "runtime_review_editability_persistence");
        AddCheck(
            report,
            "latest_smoke_review_editability_persistence",
            string.Equals(editabilityCheck?.Status, "Pass", StringComparison.Ordinal)
                ? PreDeviceBuildReadinessStatus.Pass
                : PreDeviceBuildReadinessStatus.Warn,
            editabilityCheck?.Detail ?? "runtime_review_editability_persistence check not found in latest smoke report.");

        var resetFallbackCheck = FindSmokeCheck(smokeReport, "runtime_reset_deterministic_fallback");
        AddCheck(
            report,
            "latest_smoke_reset_deterministic_fallback",
            string.Equals(resetFallbackCheck?.Status, "Pass", StringComparison.Ordinal)
                ? PreDeviceBuildReadinessStatus.Pass
                : PreDeviceBuildReadinessStatus.Warn,
            resetFallbackCheck?.Detail ?? "runtime_reset_deterministic_fallback check not found in latest smoke report.");

        var releasePolicyCheck = FindSmokeCheck(smokeReport, "runtime_reject_reset_release_policy");
        AddCheck(
            report,
            "latest_smoke_reject_reset_release_policy",
            string.Equals(releasePolicyCheck?.Status, "Pass", StringComparison.Ordinal)
                ? PreDeviceBuildReadinessStatus.Pass
                : PreDeviceBuildReadinessStatus.Warn,
            releasePolicyCheck?.Detail ?? "runtime_reject_reset_release_policy check not found in latest smoke report.");

        var dashboardText = smokeReport.DashboardSummary ?? string.Empty;
        var hasSubmitLoad = dashboardText.Contains("Submit+Load", StringComparison.OrdinalIgnoreCase);
        var hasLoadTestGlb = dashboardText.Contains("Load Test GLB", StringComparison.OrdinalIgnoreCase);
        var hasLoadLatestJob = dashboardText.Contains("Load Latest Job", StringComparison.OrdinalIgnoreCase);
        var hasAccept = dashboardText.Contains("Accept", StringComparison.OrdinalIgnoreCase);
        var hasReject = dashboardText.Contains("Reject", StringComparison.OrdinalIgnoreCase);
        var hasReset = dashboardText.Contains("Reset", StringComparison.OrdinalIgnoreCase);
        var hasRotate = dashboardText.Contains("Rotate 90", StringComparison.OrdinalIgnoreCase);
        var controlsPresent = hasSubmitLoad && hasLoadTestGlb && hasLoadLatestJob && hasAccept && hasReject && hasReset && hasRotate;
        AddCheck(
            report,
            "latest_smoke_runtime_review_controls",
            controlsPresent ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Warn,
            $"Submit+Load={hasSubmitLoad}, LoadTestGLB={hasLoadTestGlb}, LoadLatestJob={hasLoadLatestJob}, Accept={hasAccept}, Reject={hasReject}, Reset={hasReset}, Rotate90={hasRotate}");
    }

    private static void CheckActivePreDeviceRuntimeArtifacts(
        PreDeviceBuildReadinessReport report,
        string latestSmokeReport)
    {
        var artifactSets = CollectActivePreDeviceRuntimeArtifactSets();
        if (artifactSets.Count == 0)
        {
            AddCheck(
                report,
                "active_predevice_runtime_artifact_set",
                PreDeviceBuildReadinessStatus.Warn,
                $"No active {PreDeviceRequestPrefix}_*.job.json set found under Library/{GeneratedObjectJobFolderName}.");
            return;
        }

        artifactSets.Sort((left, right) =>
        {
            var timeComparison = right.UpdatedAtUtc.CompareTo(left.UpdatedAtUtc);
            return timeComparison != 0
                ? timeComparison
                : string.Compare(right.RequestId, left.RequestId, StringComparison.OrdinalIgnoreCase);
        });

        var latest = artifactSets[0];
        AddCheck(
            report,
            "active_predevice_runtime_artifact_set",
            artifactSets.Count == 1 ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Warn,
            $"sets={artifactSets.Count}, latest={latest.RequestId}, stale={Math.Max(0, artifactSets.Count - 1)}. Use `SceneShift/Generated Objects/Archive Pre-Device Runtime Artifacts - Keep Latest` before packaging if stale sets remain.");

        AddCheck(
            report,
            "active_predevice_runtime_artifact_files",
            latest.HasCompleteRuntimeEvidence ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Warn,
            latest.FormatEvidenceSummary());

        if (!string.IsNullOrWhiteSpace(latestSmokeReport))
        {
            var smokeText = ReadFileOrEmpty(latestSmokeReport);
            AddCheck(
                report,
                "active_predevice_runtime_artifact_matches_latest_smoke",
                smokeText.Contains(latest.RequestId, StringComparison.Ordinal)
                    ? PreDeviceBuildReadinessStatus.Pass
                    : PreDeviceBuildReadinessStatus.Warn,
                $"request={latest.RequestId}, smoke={latestSmokeReport}");
        }
    }

    private static List<PreDeviceRuntimeArtifactSet> CollectActivePreDeviceRuntimeArtifactSets()
    {
        var sets = new List<PreDeviceRuntimeArtifactSet>();
        var jobDirectory = Path.Combine(GetProjectRoot(), "Library", GeneratedObjectJobFolderName);
        if (!Directory.Exists(jobDirectory))
        {
            return sets;
        }

        foreach (var jobPath in Directory.GetFiles(jobDirectory, $"{PreDeviceRequestPrefix}_*.job.json", SearchOption.TopDirectoryOnly))
        {
            var record = ReadGeneratedAssetRecord(jobPath);
            var requestId = !string.IsNullOrWhiteSpace(record?.RequestId)
                ? record.RequestId
                : ExtractRequestIdFromJobPath(jobPath);
            if (string.IsNullOrWhiteSpace(requestId) ||
                !requestId.StartsWith(PreDeviceRequestPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var runtimeModelFolder = Path.Combine(Application.persistentDataPath, GeneratedObjectRuntimeModelFolderName, requestId);
            var set = new PreDeviceRuntimeArtifactSet
            {
                RequestId = requestId,
                JobPath = jobPath,
                RequestPath = Path.Combine(jobDirectory, $"{requestId}.request.json"),
                PromptPath = Path.Combine(jobDirectory, $"{requestId}.prompt.txt"),
                RuntimeSubmissionPath = Path.Combine(jobDirectory, $"{requestId}.runtime-submission.json"),
                RuntimeResultPath = Path.Combine(jobDirectory, $"{requestId}.runtime-result.json"),
                RuntimeModelFolder = runtimeModelFolder,
                RuntimeModelFileCount = CountFiles(runtimeModelFolder, "*.glb"),
                UpdatedAtUtc = ParseUtcOrFileTime(record?.UpdatedAtIsoUtc, jobPath),
            };
            sets.Add(set);
        }

        return sets;
    }

    private static GeneratedAssetRecord ReadGeneratedAssetRecord(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<GeneratedAssetRecord>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractRequestIdFromJobPath(string jobPath)
    {
        var fileName = Path.GetFileName(jobPath);
        const string suffix = ".job.json";
        return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? fileName.Substring(0, fileName.Length - suffix.Length)
            : Path.GetFileNameWithoutExtension(jobPath);
    }

    private static DateTime ParseUtcOrFileTime(string isoUtc, string fallbackPath)
    {
        if (!string.IsNullOrWhiteSpace(isoUtc) &&
            DateTime.TryParse(isoUtc, out var parsed))
        {
            return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
        }

        return File.Exists(fallbackPath) ? File.GetLastWriteTimeUtc(fallbackPath) : DateTime.MinValue;
    }

    private static int CountFiles(string directory, string searchPattern)
    {
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
            ? Directory.GetFiles(directory, searchPattern, SearchOption.AllDirectories).Length
            : 0;
    }

    private static void CheckPassthroughOnlyToggleBootstrap(PreDeviceBuildReadinessReport report)
    {
        var foundInLoadedScene = Object.FindObjectsByType<PassthroughOnlyVisibilityToggle>(FindObjectsInactive.Include).Length > 0;
        var source = ReadProjectFile("Assets/Scripts/UI/PassthroughOnlyVisibilityToggle.cs");
        var hasRuntimeBootstrap =
            source.Contains("[RuntimeInitializeOnLoadMethod", StringComparison.Ordinal) &&
            source.Contains("DontDestroyOnLoad(runtimeObject)", StringComparison.Ordinal) &&
            source.Contains("AddComponent<PassthroughOnlyVisibilityToggle>()", StringComparison.Ordinal);

        AddCheck(
            report,
            "runtime_passthrough_only_toggle",
            foundInLoadedScene || hasRuntimeBootstrap ? PreDeviceBuildReadinessStatus.Pass : PreDeviceBuildReadinessStatus.Fail,
            foundInLoadedScene
                ? "PassthroughOnlyVisibilityToggle exists in the loaded scene."
                : hasRuntimeBootstrap
                    ? "PassthroughOnlyVisibilityToggle is created at runtime after scene load; Play smoke verifies the toggle behavior."
                    : "PassthroughOnlyVisibilityToggle was not found and no runtime bootstrap was detected.");
    }

    private static string WriteReport(PreDeviceBuildReadinessReport report)
    {
        var reportDirectory = Path.Combine(GetProjectRoot(), "Library", ReportFolderName);
        Directory.CreateDirectory(reportDirectory);
        var jsonPath = Path.Combine(reportDirectory, $"{report.ReportId}.json");
        File.WriteAllText(jsonPath, JsonUtility.ToJson(report, true));
        File.WriteAllText(Path.Combine(reportDirectory, $"{report.ReportId}.md"), BuildMarkdown(report));
        return jsonPath;
    }

    private static string BuildMarkdown(PreDeviceBuildReadinessReport report)
    {
        var builder = new StringBuilder(2048);
        builder.AppendLine($"# {report.ReportId}");
        builder.AppendLine();
        builder.AppendLine($"- Created: `{report.CreatedAtIsoUtc}`");
        builder.AppendLine($"- Overall: `{report.OverallStatus}`");
        builder.AppendLine();
        builder.AppendLine("## Checks");
        builder.AppendLine();
        builder.AppendLine("| Check | Status | Detail |");
        builder.AppendLine("| --- | --- | --- |");
        foreach (var check in report.Checks)
        {
            builder.Append("| ");
            builder.Append(EscapeMarkdown(check.Name));
            builder.Append(" | `");
            builder.Append(EscapeMarkdown(check.Status));
            builder.Append("` | ");
            builder.Append(EscapeMarkdown(check.Detail));
            builder.AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("## Interpretation");
        builder.AppendLine();
        builder.AppendLine("- `Pass` means the item is ready for the next MQDH/test-channel packaging step.");
        builder.AppendLine("- `Warn` means the item is acceptable for local pre-device work but must be resolved or consciously accepted before publishing to the headset.");
        builder.AppendLine("- `Fail` means do not publish a headset build until the issue is fixed.");

        if (report.SuggestedNextActions != null && report.SuggestedNextActions.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Suggested Next Actions");
            builder.AppendLine();
            foreach (var action in report.SuggestedNextActions)
            {
                builder.Append("- ");
                builder.AppendLine(action);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildOverallStatus(PreDeviceBuildReadinessReport report)
    {
        var hasFail = false;
        var hasWarn = false;
        foreach (var check in report.Checks)
        {
            hasFail |= string.Equals(check.Status, PreDeviceBuildReadinessStatus.Fail.ToString(), StringComparison.Ordinal);
            hasWarn |= string.Equals(check.Status, PreDeviceBuildReadinessStatus.Warn.ToString(), StringComparison.Ordinal);
        }

        if (hasFail)
        {
            return "Fail";
        }

        return hasWarn ? "PassWithWarnings" : "Pass";
    }

    private static List<string> BuildSuggestedNextActions(PreDeviceBuildReadinessReport report)
    {
        var actions = new List<string>();
        if (HasCheck(report, "android_build_support_installed", PreDeviceBuildReadinessStatus.Fail))
        {
            actions.Add($"Install Android Build Support for Unity {Application.unityVersion} with `bash Tools/install_unity_android_support.sh --run --wait-for-close --version {Application.unityVersion}` or Unity Hub UI, including Android SDK & NDK Tools and OpenJDK. The helper will wait while you close Unity Editor and Unity Hub manually. Reopen the project and rerun this report afterward.");
        }

        if (HasCheck(report, "active_build_target", PreDeviceBuildReadinessStatus.Warn))
        {
            actions.Add("After Android Build Support is installed and this report has no failures, switch the Editor build target to Android before creating the MQDH/test-channel package.");
        }

        if (HasCheck(report, "canonical_scene_in_build", PreDeviceBuildReadinessStatus.Fail))
        {
            actions.Add($"Add `{CanonicalScenePath}` as an enabled scene in Build Settings before packaging.");
        }

        if (HasCheck(report, "android_internet_permission", PreDeviceBuildReadinessStatus.Fail))
        {
            actions.Add("Enable Android internet access before packaging; the runtime GLB loader and backend client use UnityWebRequest on the headset.");
        }

        if (HasCheck(report, "android_custom_manifest_enabled", PreDeviceBuildReadinessStatus.Fail) ||
            HasCheck(report, "quest_scene_anchor_permissions", PreDeviceBuildReadinessStatus.Fail) ||
            HasCheck(report, "quest_pca_passthrough_manifest", PreDeviceBuildReadinessStatus.Fail) ||
            HasCheck(report, "quest_vr_launch_manifest", PreDeviceBuildReadinessStatus.Fail))
        {
            actions.Add("Fix the custom Android manifest before packaging; MRUK scene loading, anchors, passthrough/PCA, and VR launch metadata must be present for the headset validation flow.");
        }

        if (HasCheck(report, "runtime_client_mode", PreDeviceBuildReadinessStatus.Fail))
        {
            actions.Add("Use `LocalTestModelUrl` for the first known-GLB headset spike, or configure `HttpBackend` with a secure HTTPS backend endpoint before testing real cloud generation. Do not embed service credentials in the APK.");
        }

        if (HasCheck(report, "runtime_local_test_model_https", PreDeviceBuildReadinessStatus.Fail) ||
            HasCheck(report, "runtime_backend_submit_url_https", PreDeviceBuildReadinessStatus.Fail))
        {
            actions.Add("Use HTTPS URLs for the local test GLB and any runtime backend endpoint before publishing a headset package.");
        }

        if (HasCheck(report, "direct_service_adapters_disabled_for_http_backend", PreDeviceBuildReadinessStatus.Fail))
        {
            actions.Add("For an `HttpBackend` Quest package, disable direct APIMart, Seed3D, upload, and DeepSeek scene adapters so the APK cannot call provider APIs outside the secure backend.");
        }

        if (HasCheck(report, "runtime_loader_assetdatabase_free", PreDeviceBuildReadinessStatus.Fail) ||
            HasCheck(report, "runtime_backend_client_runtime_path", PreDeviceBuildReadinessStatus.Fail))
        {
            actions.Add("Fix the runtime generated-object path so the Quest app uses persistentDataPath + UnityWebRequest/glTF runtime loading instead of Editor-only import APIs.");
        }

        if (HasCheck(report, "latest_smoke_report_exists", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "latest_smoke_report_status", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "latest_smoke_report_status", PreDeviceBuildReadinessStatus.Fail) ||
            HasCheck(report, "latest_smoke_safe_table_target", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "latest_smoke_plan_warnings_zero", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "latest_smoke_runtime_loaded_evidence", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "latest_smoke_runtime_instance_metadata", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "latest_smoke_request_job_contract", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "latest_smoke_backend_artifact_contract", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "latest_smoke_review_editability_persistence", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "latest_smoke_reset_deterministic_fallback", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "latest_smoke_reject_reset_release_policy", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "latest_smoke_runtime_review_controls", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "visual_evidence_exists", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "visual_evidence_matches_latest_smoke", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "visual_image_matches_latest_smoke", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "visual_review_references_latest_smoke", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "visual_review_references_latest_image", PreDeviceBuildReadinessStatus.Warn))
        {
            actions.Add("Run the Play Mode pre-device smoke report until it includes a safe TABLE target, zero planner warnings, runtimeLoaded evidence, runtime-loaded instance metadata, request/job contract evidence, runtime backend artifact evidence, editability/persistence evidence, reset-to-deterministic-fallback evidence, reject/reset release policy evidence, and runtime/review dashboard controls, then capture local visual evidence after that latest smoke pass and reference both the smoke report and screenshot in the visual review note.");
        }

        if (HasCheck(report, "active_predevice_runtime_artifact_set", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "active_predevice_runtime_artifact_files", PreDeviceBuildReadinessStatus.Warn) ||
            HasCheck(report, "active_predevice_runtime_artifact_matches_latest_smoke", PreDeviceBuildReadinessStatus.Warn))
        {
            actions.Add("Run `SceneShift/Generated Objects/Archive Pre-Device Runtime Artifacts - Keep Latest`, confirm the latest smoke-linked request/job/prompt/runtime-submission/runtime-result files and runtime GLB folder remain active, then rerun this readiness report.");
        }

        if (HasCheck(report, "packaged_config_secret_scan", PreDeviceBuildReadinessStatus.Fail) ||
            HasCheck(report, "generated_record_secret_scan", PreDeviceBuildReadinessStatus.Fail))
        {
            actions.Add("Remove service credentials from packaged project files and generated job records; the Quest app should use environment-backed Editor tools or a secure backend, not embedded API keys.");
        }

        if (HasAnyFailedToolCheck(report))
        {
            actions.Add("Restore or update the preflight/build tools; readiness now requires Android-support recovery, terminal suite, package build runner, local gate, package artifact verification, gate self-test, handoff bundle, and headset evidence scripts before the MQDH/test-channel path is trusted.");
        }

        if (actions.Count == 0 && string.Equals(report.OverallStatus, "Pass", StringComparison.Ordinal))
        {
            actions.Add("Build readiness is clean; proceed to Android switch, package creation, MQDH/test-channel install, and headset validation.");
        }

        return actions;
    }

    private static bool HasAnyFailedToolCheck(PreDeviceBuildReadinessReport report)
    {
        if (report?.Checks == null)
        {
            return false;
        }

        foreach (var check in report.Checks)
        {
            if (check.Name.StartsWith("tool_", StringComparison.Ordinal) &&
                string.Equals(check.Status, PreDeviceBuildReadinessStatus.Fail.ToString(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasCheck(
        PreDeviceBuildReadinessReport report,
        string name,
        PreDeviceBuildReadinessStatus status)
    {
        if (report?.Checks == null)
        {
            return false;
        }

        foreach (var check in report.Checks)
        {
            if (string.Equals(check.Name, name, StringComparison.Ordinal) &&
                string.Equals(check.Status, status.ToString(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddCheck(
        PreDeviceBuildReadinessReport report,
        string name,
        PreDeviceBuildReadinessStatus status,
        string detail)
    {
        report.Checks.Add(new PreDeviceBuildReadinessCheck
        {
            Name = name,
            Status = status.ToString(),
            Detail = detail ?? string.Empty,
        });
    }

    private static string ReadProjectFile(string relativePath)
    {
        var path = Path.Combine(GetProjectRoot(), relativePath);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private static bool DirectServiceAdaptersDisabledForHttpBackend(out string detail)
    {
        var failures = new List<string>();
        CheckAdapterObjects<ApimartSurfaceTextureBackendAdapter>(
            failures,
            nameof(ApimartSurfaceTextureBackendAdapter),
            ("autoProcessJobsInPlay", false),
            ("apiKeyEnvironmentVariable", string.Empty));
        CheckAdapterObjects<ApimartImageBackendAdapter>(
            failures,
            nameof(ApimartImageBackendAdapter),
            ("autoProcessJobsInPlay", false),
            ("apiKeyEnvironmentVariable", string.Empty));
        CheckAdapterObjects<HostedImageUploadBridge>(
            failures,
            nameof(HostedImageUploadBridge),
            ("autoProcessJobsInPlay", false),
            ("uploadEndpoint", string.Empty),
            ("authTokenEnvironmentVariable", string.Empty));
        CheckAdapterObjects<Seed3DBackendAdapter>(
            failures,
            nameof(Seed3DBackendAdapter),
            ("autoProcessJobsInPlay", false),
            ("apiKeyEnvironmentVariable", string.Empty));
        CheckAdapterObjects<RuntimeStyleIntentController>(
            failures,
            nameof(RuntimeStyleIntentController),
            ("useDeepSeekStyleIntentProvider", false));
        CheckAdapterObjects<DeepSeekStyleIntentProvider>(
            failures,
            nameof(DeepSeekStyleIntentProvider),
            ("useDeepSeek", false),
            ("apiKeyEnvironmentVariable", string.Empty));

        detail = failures.Count == 0
            ? "direct APIMart, Seed3D, upload, and DeepSeek scene adapters are disabled for the secure HttpBackend package."
            : string.Join("; ", failures);
        return failures.Count == 0;
    }

    private static void CheckAdapterObjects<T>(
        ICollection<string> failures,
        string label,
        params (string PropertyName, object ExpectedValue)[] expectations) where T : Object
    {
        var objects = Object.FindObjectsByType<T>(FindObjectsInactive.Include);
        for (var index = 0; index < objects.Length; index++)
        {
            var serializedObject = new SerializedObject(objects[index]);
            for (var expectationIndex = 0; expectationIndex < expectations.Length; expectationIndex++)
            {
                var (propertyName, expectedValue) = expectations[expectationIndex];
                var property = serializedObject.FindProperty(propertyName);
                if (property == null)
                {
                    failures.Add($"{label}.{propertyName}=missing");
                    continue;
                }

                if (expectedValue is bool expectedBool && property.boolValue != expectedBool)
                {
                    failures.Add($"{label}.{propertyName}={property.boolValue}");
                }
                else if (expectedValue is string expectedString &&
                         !string.Equals(property.stringValue ?? string.Empty, expectedString, StringComparison.Ordinal))
                {
                    failures.Add($"{label}.{propertyName}=configured");
                }
            }
        }
    }

    private static string ReadFileOrEmpty(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        if (string.IsNullOrWhiteSpace(text) || needles == null)
        {
            return false;
        }

        foreach (var needle in needles)
        {
            if (!string.IsNullOrWhiteSpace(needle) &&
                text.Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractManifestAttribute(string manifestText, string elementNameNeedle, string attributeName)
    {
        if (string.IsNullOrWhiteSpace(manifestText))
        {
            return string.Empty;
        }

        foreach (var line in manifestText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.Contains(elementNameNeedle, StringComparison.Ordinal))
            {
                continue;
            }

            var match = Regex.Match(line, Regex.Escape(attributeName) + "\\s*=\\s*\"([^\"]*)\"");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return string.Empty;
    }

    private static int ExtractIntegerAttribute(string text, string attributeName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return -1;
        }

        var match = Regex.Match(text, Regex.Escape(attributeName) + "\\s*=\\s*\"([0-9]+)\"");
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : -1;
    }

    private static List<string> CollectPackagedConfigPaths()
    {
        var paths = new List<string>();
        AddIfExists(paths, Path.Combine(GetProjectRoot(), "Packages", "manifest.json"));
        AddIfExists(paths, Path.Combine(GetProjectRoot(), "Packages", "packages-lock.json"));
        AddFiles(paths, Path.Combine(GetProjectRoot(), "ProjectSettings"), new[] { ".asset", ".json" });
        AddFiles(paths, Path.Combine(GetProjectRoot(), "Assets"), new[] { ".unity", ".prefab", ".asset", ".json", ".asmdef", ".inputactions" });
        return paths;
    }

    private static List<string> CollectGeneratedRecordPaths()
    {
        var paths = new List<string>();
        AddFiles(paths, Path.Combine(GetProjectRoot(), "Library", "GeneratedObjectJobs"), new[] { ".json" });
        AddFiles(paths, Path.Combine(GetProjectRoot(), "Library", "SurfaceTextureJobs"), new[] { ".json" });
        return paths;
    }

    private static void AddIfExists(List<string> paths, string path)
    {
        if (File.Exists(path))
        {
            paths.Add(path);
        }
    }

    private static void AddFiles(List<string> paths, string directory, IReadOnlyCollection<string> extensions)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(path);
            if (!string.IsNullOrWhiteSpace(extension) && HasExtension(extensions, extension))
            {
                paths.Add(path);
            }
        }
    }

    private static bool HasExtension(IEnumerable<string> extensions, string extension)
    {
        foreach (var candidate in extensions)
        {
            if (string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static List<SecretScanHit> FindSecretHits(IEnumerable<string> paths)
    {
        var hits = new List<SecretScanHit>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (TryMatchLikelySecret(line, out var patternName))
                {
                    hits.Add(new SecretScanHit(path, lineNumber, patternName));
                }
            }
        }

        return hits;
    }

    private static bool TryMatchLikelySecret(string line, out string patternName)
    {
        patternName = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var checks = new[]
        {
            ("openai-style-key", "sk-[A-Za-z0-9_\\-]{20,}"),
            ("bearer-token-value", "Bearer\\s+[A-Za-z0-9_\\-\\.]{20,}"),
            ("service-env-assignment", "(APIMART_API_KEY|ARK_API_KEY|DEEPSEEK_API_KEY|SCENESHIFT_UPLOAD_TOKEN)\\s*=\\s*[^\\s\\\"']{8,}"),
            ("serialized-secret-value", "(api[_-]?key|token|secret|authorization)\\s*[:=]\\s*(?!\\\"?(APIMART_API_KEY|ARK_API_KEY|DEEPSEEK_API_KEY|SCENESHIFT_UPLOAD_TOKEN)\\\"?,?\\s*$)(?!\\\"?(Bearer\\s*)\\\"?,?\\s*$)\\\"?[A-Za-z0-9_\\-\\.]{20,}"),
        };

        foreach (var (name, pattern) in checks)
        {
            if (Regex.IsMatch(line, pattern, RegexOptions.IgnoreCase))
            {
                patternName = name;
                return true;
            }
        }

        return false;
    }

    private static string BuildSecretHitSummary(IReadOnlyList<SecretScanHit> hits)
    {
        var builder = new StringBuilder();
        builder.Append("hits=");
        builder.Append(hits.Count);
        builder.Append(" first=");
        var count = Math.Min(3, hits.Count);
        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                builder.Append("; ");
            }

            var hit = hits[index];
            builder.Append(MakeProjectRelativePath(hit.Path));
            builder.Append(':');
            builder.Append(hit.LineNumber);
            builder.Append(" (");
            builder.Append(hit.PatternName);
            builder.Append(')');
        }

        return builder.ToString();
    }

    private static string MakeProjectRelativePath(string path)
    {
        var root = GetProjectRoot();
        if (!string.IsNullOrWhiteSpace(path) &&
            path.StartsWith(root, StringComparison.Ordinal))
        {
            return path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return path;
    }

    private static string GetLatestFile(string directory, string searchPattern)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return string.Empty;
        }

        string latestPath = null;
        var latestTime = DateTime.MinValue;
        foreach (var path in Directory.GetFiles(directory, searchPattern))
        {
            var writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime <= latestTime)
            {
                continue;
            }

            latestTime = writeTime;
            latestPath = path;
        }

        return latestPath ?? string.Empty;
    }

    private static string ReadSmokeReportStatus(string jsonPath)
    {
        var report = ReadSmokeReport(jsonPath);
        return !string.IsNullOrWhiteSpace(report?.OverallStatus) ? report.OverallStatus : File.Exists(jsonPath) ? "unknown" : "missing";
    }

    private static PreDeviceSmokeReport ReadSmokeReport(string jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<PreDeviceSmokeReport>(File.ReadAllText(jsonPath));
        }
        catch
        {
            return null;
        }
    }

    private static PreDeviceSmokeCheck FindSmokeCheck(PreDeviceSmokeReport report, string name)
    {
        if (report?.Checks == null)
        {
            return null;
        }

        foreach (var check in report.Checks)
        {
            if (string.Equals(check.Name, name, StringComparison.Ordinal))
            {
                return check;
            }
        }

        return null;
    }

    private static int ExtractNamedCounter(string text, string counterName)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(counterName))
        {
            return -1;
        }

        var match = Regex.Match(text, Regex.Escape(counterName) + "\\s*=\\s*([0-9]+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : -1;
    }

    private static string GetProjectRoot()
    {
        return Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
    }

    private static string ExtractGuid(string metaText)
    {
        using var reader = new StringReader(metaText ?? string.Empty);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.StartsWith("guid:", StringComparison.Ordinal))
            {
                return line.Substring("guid:".Length).Trim();
            }
        }

        return string.Empty;
    }

    private static bool HasOnlyEmptySerializedField(string text, string fieldName)
    {
        using var reader = new StringReader(text ?? string.Empty);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith($"{fieldName}:", StringComparison.Ordinal))
            {
                continue;
            }

            var value = trimmed.Substring(fieldName.Length + 1).Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool BackendSubmitUrlLooksLikeSecretCarrier(string backendSubmitUrl)
    {
        if (string.IsNullOrWhiteSpace(backendSubmitUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(backendSubmitUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var sensitiveNeedles = new[]
        {
            "key=",
            "api_key=",
            "apikey=",
            "token=",
            "secret=",
            "signature=",
            "sig=",
            "authorization=",
            "bearer=",
        };

        var query = uri.Query ?? string.Empty;
        foreach (var needle in sensitiveNeedles)
        {
            if (query.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsObviousEmbeddedSecret(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var needles = new[]
        {
            "sk-",
            "Bearer ",
            "x-api-key:",
            "api_key:",
            "apiKey:",
            "secret:",
            "SCENESHIFT_UPLOAD_TOKEN=",
            "APIMART_API_KEY=",
            "ARK_API_KEY=",
            "DEEPSEEK_API_KEY=",
        };
        foreach (var needle in needles)
        {
            if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string EscapeMarkdown(string value)
    {
        return (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }
}

[Serializable]
public sealed class PreDeviceBuildReadinessReport
{
    public string ReportId;
    public string CreatedAtIsoUtc;
    public string OverallStatus;
    public List<PreDeviceBuildReadinessCheck> Checks = new();
    public List<string> SuggestedNextActions = new();
}

[Serializable]
public sealed class PreDeviceBuildReadinessCheck
{
    public string Name;
    public string Status;
    public string Detail;
}

public enum PreDeviceBuildReadinessStatus
{
    Pass,
    Warn,
    Fail,
}

internal readonly struct SecretScanHit
{
    public SecretScanHit(string path, int lineNumber, string patternName)
    {
        Path = path;
        LineNumber = lineNumber;
        PatternName = patternName;
    }

    public readonly string Path;
    public readonly int LineNumber;
    public readonly string PatternName;
}

internal sealed class PreDeviceRuntimeArtifactSet
{
    public string RequestId;
    public string JobPath;
    public string RequestPath;
    public string PromptPath;
    public string RuntimeSubmissionPath;
    public string RuntimeResultPath;
    public string RuntimeModelFolder;
    public int RuntimeModelFileCount;
    public DateTime UpdatedAtUtc;

    public bool HasCompleteRuntimeEvidence =>
        File.Exists(JobPath) &&
        File.Exists(RequestPath) &&
        File.Exists(PromptPath) &&
        File.Exists(RuntimeSubmissionPath) &&
        File.Exists(RuntimeResultPath) &&
        Directory.Exists(RuntimeModelFolder) &&
        RuntimeModelFileCount > 0;

    public string FormatEvidenceSummary()
    {
        return $"request={RequestId}, jobFile={File.Exists(JobPath)}, requestFile={File.Exists(RequestPath)}, promptFile={File.Exists(PromptPath)}, runtimeSubmission={File.Exists(RuntimeSubmissionPath)}, runtimeResult={File.Exists(RuntimeResultPath)}, runtimeModelFolder={Directory.Exists(RuntimeModelFolder)}, glbFiles={RuntimeModelFileCount}";
    }
}
