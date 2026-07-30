using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class RuntimeBackendConfigurationRunner
{
    private const string CanonicalScenePath = "Assets/Scenes/MR_RoomStylization.unity";
    private const string RuntimeBackendUrlEnvironmentVariable = "SCENESHIFT_RUNTIME_BACKEND_URL";
    private const string PublicBaseUrlEnvironmentVariable = "SCENESHIFT_PUBLIC_BASE_URL";
    private const string RuntimeGenerationEndpointPath = "/v1/runtime-generations";
    private const string DefaultLocalTestModelUrl = "https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Models/main/2.0/Box/glTF-Binary/Box.glb";

    [MenuItem("SceneShift/Runtime Backend/Report Runtime Backend Configuration")]
    public static void ReportRuntimeBackendConfiguration()
    {
        if (!TryGetClient(out var client))
        {
            Debug.LogWarning("[SceneShiftRuntimeBackendConfig] QuestRuntimeGenerationClient was not found in the loaded scene.");
            return;
        }

        var serializedClient = new SerializedObject(client);
        var clientMode = serializedClient.FindProperty("clientMode");
        var backendSubmitUrl = serializedClient.FindProperty("backendSubmitUrl")?.stringValue ?? string.Empty;
        var sendImage = serializedClient.FindProperty("sendCapturedImageWithBackendRequest")?.boolValue ?? false;
        var preferCrop = serializedClient.FindProperty("preferCroppedSourceImage")?.boolValue ?? false;
        Debug.Log(
            "[SceneShiftRuntimeBackendConfig]\n" +
            $"Mode: {clientMode?.enumDisplayNames[clientMode.enumValueIndex] ?? "unknown"}\n" +
            $"Backend URL: {(string.IsNullOrWhiteSpace(backendSubmitUrl) ? "empty" : backendSubmitUrl)}\n" +
            $"Send Image: {sendImage}\n" +
            $"Prefer Crop: {preferCrop}\n" +
            $"Endpoint Env: {RuntimeBackendUrlEnvironmentVariable}={(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RuntimeBackendUrlEnvironmentVariable)) ? "missing" : "set")}\n" +
            $"Public Base Env: {PublicBaseUrlEnvironmentVariable}={(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PublicBaseUrlEnvironmentVariable)) ? "missing" : "set")}");
    }

    [MenuItem("SceneShift/Runtime Backend/Configure LocalTestModelUrl Mode")]
    public static void ConfigureLocalTestMode()
    {
        if (!TryGetClient(out var client))
        {
            Debug.LogError("[SceneShiftRuntimeBackendConfig] QuestRuntimeGenerationClient was not found in the loaded scene.");
            return;
        }

        var serializedClient = new SerializedObject(client);
        SetClientMode(serializedClient, RuntimeGenerationClientMode.LocalTestModelUrl);
        serializedClient.FindProperty("backendSubmitUrl").stringValue = string.Empty;
        serializedClient.FindProperty("localTestModelUrl").stringValue = DefaultLocalTestModelUrl;
        serializedClient.FindProperty("sendCapturedImageWithBackendRequest").boolValue = true;
        serializedClient.FindProperty("preferCroppedSourceImage").boolValue = true;
        serializedClient.ApplyModifiedProperties();
        ConfigureRuntimeLoaderTestModelUrl(DefaultLocalTestModelUrl);
        SaveOwningScene(client);
        Debug.Log("[SceneShiftRuntimeBackendConfig] Configured QuestRuntimeGenerationClient for LocalTestModelUrl mode.");
    }

    [MenuItem("SceneShift/Runtime Backend/Configure HttpBackend From Environment")]
    public static void ConfigureHttpBackendFromEnvironment()
    {
        if (!TryResolveEndpointFromEnvironment(out var endpoint, out var error))
        {
            Debug.LogError($"[SceneShiftRuntimeBackendConfig] {error}");
            return;
        }

        if (!TryGetClient(out var client))
        {
            Debug.LogError("[SceneShiftRuntimeBackendConfig] QuestRuntimeGenerationClient was not found in the loaded scene.");
            return;
        }

        var serializedClient = new SerializedObject(client);
        SetClientMode(serializedClient, RuntimeGenerationClientMode.HttpBackend);
        serializedClient.FindProperty("backendSubmitUrl").stringValue = endpoint;
        serializedClient.FindProperty("localTestModelUrl").stringValue = string.Empty;
        serializedClient.FindProperty("sendCapturedImageWithBackendRequest").boolValue = true;
        serializedClient.FindProperty("preferCroppedSourceImage").boolValue = true;
        serializedClient.ApplyModifiedProperties();
        ConfigureRuntimeLoaderTestModelUrl(string.Empty);
        var disabledAdapters = DisableDirectServiceAdaptersForQuestPackage();
        SaveOwningScene(client);
        Debug.Log($"[SceneShiftRuntimeBackendConfig] Configured QuestRuntimeGenerationClient for HttpBackend: {endpoint}. Disabled direct service adapters: {disabledAdapters}");
    }

    private static bool TryGetClient(out QuestRuntimeGenerationClient client)
    {
        client = Object.FindAnyObjectByType<QuestRuntimeGenerationClient>(FindObjectsInactive.Include);
        if (client != null)
        {
            return true;
        }

        var activeScene = SceneManager.GetActiveScene();
        if (string.Equals(activeScene.path, CanonicalScenePath, StringComparison.Ordinal))
        {
            return false;
        }

        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorSceneManager.OpenScene(CanonicalScenePath, OpenSceneMode.Single);
            client = Object.FindAnyObjectByType<QuestRuntimeGenerationClient>(FindObjectsInactive.Include);
            return client != null;
        }

        return false;
    }

    private static void SetClientMode(SerializedObject serializedClient, RuntimeGenerationClientMode mode)
    {
        var clientMode = serializedClient.FindProperty("clientMode");
        if (clientMode == null)
        {
            throw new InvalidDataException("QuestRuntimeGenerationClient.clientMode serialized field was not found.");
        }

        clientMode.enumValueIndex = (int)mode;
    }

    private static bool TryResolveEndpointFromEnvironment(out string endpoint, out string error)
    {
        endpoint = Environment.GetEnvironmentVariable(RuntimeBackendUrlEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            var publicBaseUrl = Environment.GetEnvironmentVariable(PublicBaseUrlEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(publicBaseUrl))
            {
                endpoint = AppendEndpointPath(publicBaseUrl.Trim());
            }
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            error = $"Set {RuntimeBackendUrlEnvironmentVariable}=https://...{RuntimeGenerationEndpointPath} or {PublicBaseUrlEnvironmentVariable}=https://... before configuring HttpBackend.";
            return false;
        }

        endpoint = endpoint.Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            error = $"Runtime backend endpoint is not an absolute URL: {endpoint}";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            error = $"Runtime backend endpoint must be HTTPS for Quest builds: {endpoint}";
            return false;
        }

        if (EndpointLooksLikeSecretCarrier(uri))
        {
            error = "Runtime backend endpoint query looks like it contains a key/token/signature. Do not serialize secrets into the scene.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string AppendEndpointPath(string publicBaseUrl)
    {
        var trimmed = publicBaseUrl.TrimEnd('/');
        return trimmed.EndsWith(RuntimeGenerationEndpointPath, StringComparison.Ordinal)
            ? trimmed
            : $"{trimmed}{RuntimeGenerationEndpointPath}";
    }

    private static bool EndpointLooksLikeSecretCarrier(Uri uri)
    {
        var query = uri.Query ?? string.Empty;
        var needles = new[]
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

        for (var index = 0; index < needles.Length; index++)
        {
            if (query.IndexOf(needles[index], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void SaveOwningScene(Object sceneObject)
    {
        var component = sceneObject as Component;
        var scene = component != null ? component.gameObject.scene : SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static int DisableDirectServiceAdaptersForQuestPackage()
    {
        var count = 0;
        count += DisableAdapterObjects<ApimartSurfaceTextureBackendAdapter>(
            ("autoProcessJobsInPlay", false),
            ("apiKeyEnvironmentVariable", string.Empty));
        count += DisableAdapterObjects<ApimartImageBackendAdapter>(
            ("autoProcessJobsInPlay", false),
            ("apiKeyEnvironmentVariable", string.Empty));
        count += DisableAdapterObjects<HostedImageUploadBridge>(
            ("autoProcessJobsInPlay", false),
            ("uploadEndpoint", string.Empty),
            ("authTokenEnvironmentVariable", string.Empty));
        count += DisableAdapterObjects<Seed3DBackendAdapter>(
            ("autoProcessJobsInPlay", false),
            ("apiKeyEnvironmentVariable", string.Empty));
        count += DisableAdapterObjects<RuntimeStyleIntentController>(
            ("useDeepSeekStyleIntentProvider", false));
        count += DisableAdapterObjects<DeepSeekStyleIntentProvider>(
            ("useDeepSeek", false),
            ("apiKeyEnvironmentVariable", string.Empty));
        return count;
    }

    private static void ConfigureRuntimeLoaderTestModelUrl(string testModelUrl)
    {
        var loaders = Object.FindObjectsByType<RuntimeGeneratedModelLoader>(FindObjectsInactive.Include);
        for (var index = 0; index < loaders.Length; index++)
        {
            var serializedObject = new SerializedObject(loaders[index]);
            var property = serializedObject.FindProperty("testModelUrl");
            if (property != null)
            {
                property.stringValue = testModelUrl ?? string.Empty;
                serializedObject.ApplyModifiedProperties();
            }
        }
    }

    private static int DisableAdapterObjects<T>(params (string PropertyName, object Value)[] assignments) where T : Object
    {
        var objects = Object.FindObjectsByType<T>(FindObjectsInactive.Include);
        for (var index = 0; index < objects.Length; index++)
        {
            var serializedObject = new SerializedObject(objects[index]);
            for (var assignmentIndex = 0; assignmentIndex < assignments.Length; assignmentIndex++)
            {
                var (propertyName, value) = assignments[assignmentIndex];
                var property = serializedObject.FindProperty(propertyName);
                if (property == null)
                {
                    continue;
                }

                if (value is bool boolValue)
                {
                    property.boolValue = boolValue;
                }
                else if (value is string stringValue)
                {
                    property.stringValue = stringValue;
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        return objects.Length;
    }
}
