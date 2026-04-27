using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class DeepSeekStyleIntentProvider : MonoBehaviour
{
    [Header("DeepSeek API")]
    [SerializeField] private bool useDeepSeek = true;
    [SerializeField] private string endpointUrl = "https://api.deepseek.com/chat/completions";
    [SerializeField] private string model = "deepseek-v4-flash";
    [SerializeField] private string apiKeyEnvironmentVariable = "DEEPSEEK_API_KEY";
    [SerializeField, TextArea(1, 2)] private string apiKeyOverride = string.Empty;

    [Header("Request")]
    [SerializeField, Range(0f, 1f)] private float temperature = 0.2f;
    [SerializeField] private int maxTokens = 700;
    [SerializeField] private int timeoutSeconds = 30;
    [SerializeField] private bool requestJsonMode = true;
    [SerializeField] private bool disableThinkingMode = true;

    [Header("Runtime State")]
    [SerializeField, TextArea(3, 6)] private string latestStatus = "[DeepSeekStyleIntent]\nState: idle";

    private Coroutine activeRequest;

    public bool IsEnabled => useDeepSeek;
    public string LatestStatus => latestStatus;
    public string Model => model;

    public bool HasApiKey()
    {
        return !string.IsNullOrWhiteSpace(ResolveApiKey());
    }

    public bool RequestStyleIntent(string userStyleIntent, Action<RuntimeStyleIntent> onSuccess, Action<string> onFailure)
    {
        if (!useDeepSeek)
        {
            onFailure?.Invoke("DeepSeek provider is disabled.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(userStyleIntent))
        {
            onFailure?.Invoke("User style intent is empty.");
            return false;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            onFailure?.Invoke($"Missing API key. Set {apiKeyEnvironmentVariable} or apiKeyOverride.");
            return false;
        }

        if (activeRequest != null)
        {
            StopCoroutine(activeRequest);
            activeRequest = null;
        }

        activeRequest = StartCoroutine(RequestStyleIntentCoroutine(userStyleIntent.Trim(), apiKey, onSuccess, onFailure));
        return true;
    }

    private IEnumerator RequestStyleIntentCoroutine(
        string userStyleIntent,
        string apiKey,
        Action<RuntimeStyleIntent> onSuccess,
        Action<string> onFailure)
    {
        PublishStatus("requesting", $"Intent: {userStyleIntent}");

        var payload = BuildRequestPayload(userStyleIntent);
        using var request = new UnityWebRequest(endpointUrl, UnityWebRequest.kHttpVerbPOST);
        var bodyBytes = Encoding.UTF8.GetBytes(payload);
        request.uploadHandler = new UploadHandlerRaw(bodyBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = Mathf.Max(1, timeoutSeconds);
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

        yield return request.SendWebRequest();
        activeRequest = null;

        if (request.result != UnityWebRequest.Result.Success)
        {
            var failure = $"HTTP {request.responseCode}: {request.error}";
            if (!string.IsNullOrWhiteSpace(request.downloadHandler.text))
            {
                failure += $" | {Shorten(request.downloadHandler.text, 240)}";
            }

            PublishStatus("failed", failure);
            onFailure?.Invoke(failure);
            yield break;
        }

        if (!TryExtractAssistantContent(request.downloadHandler.text, out var content, out var extractError))
        {
            PublishStatus("failed", extractError);
            onFailure?.Invoke(extractError);
            yield break;
        }

        var json = StripJsonFences(content);
        if (!TryParseStyleIntentJson(userStyleIntent, json, out var parsedIntent, out var parseError))
        {
            PublishStatus("failed", parseError);
            onFailure?.Invoke(parseError);
            yield break;
        }

        parsedIntent.Source = $"deepseek_api:{model}";
        parsedIntent.CreatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        PublishStatus("completed", $"Model: {model}\nKeywords: {JoinPreview(parsedIntent.StyleKeywords)}");
        onSuccess?.Invoke(parsedIntent);
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(apiKeyOverride))
        {
            return apiKeyOverride.Trim();
        }

        return string.IsNullOrWhiteSpace(apiKeyEnvironmentVariable)
            ? string.Empty
            : (Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable) ?? string.Empty).Trim();
    }

    private string BuildRequestPayload(string userStyleIntent)
    {
        var systemPrompt = BuildSystemPrompt();
        var userPrompt = $"User style intent: \"{userStyleIntent}\"";

        var builder = new StringBuilder(4096);
        builder.Append('{');
        AppendJsonProperty(builder, "model", model);
        builder.Append(',');
        builder.Append("\"messages\":[");
        AppendMessage(builder, "system", systemPrompt);
        builder.Append(',');
        AppendMessage(builder, "user", userPrompt);
        builder.Append(']');
        builder.Append(',');
        builder.Append("\"stream\":false");
        builder.Append(',');
        builder.Append("\"temperature\":").Append(temperature.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        builder.Append(',');
        builder.Append("\"max_tokens\":").Append(Mathf.Max(128, maxTokens));
        if (requestJsonMode)
        {
            builder.Append(',');
            builder.Append("\"response_format\":{\"type\":\"json_object\"}");
        }

        if (disableThinkingMode)
        {
            builder.Append(',');
            builder.Append("\"thinking\":{\"type\":\"disabled\"}");
        }

        builder.Append('}');
        return builder.ToString();
    }

    private static string BuildSystemPrompt()
    {
        return
            "You are a style-intent parser for a mixed-reality room stylization system.\n" +
            "Convert the user's freeform style intent into concrete visual keywords for stylizing real room objects.\n" +
            "Output valid JSON only. Do not include Markdown. Do not include explanations.\n" +
            "Keep every keyword concrete and visual.\n" +
            "Do not invent object placement, room geometry, furniture dimensions, or furniture categories.\n" +
            "Preserve real object function, footprint, proportions, contact surfaces, structural geometry, and yaw.\n" +
            "The JSON output must follow this exact schema:\n" +
            "{\n" +
            "  \"global_style_summary\": \"one concise sentence describing the visual direction\",\n" +
            "  \"style_keywords\": [\"5 to 8 visual style keywords\"],\n" +
            "  \"material_keywords\": [\"3 to 6 material or surface finish keywords\"],\n" +
            "  \"color_keywords\": [\"3 to 6 color or lighting keywords\"],\n" +
            "  \"motif_keywords\": [\"3 to 6 repeated motif or detail keywords\"],\n" +
            "  \"negative_style_keywords\": [\"3 to 6 visual styles to avoid\"],\n" +
            "  \"object_style_directive\": \"one sentence explaining how any object should inherit the style while preserving function, footprint, proportions, contact surfaces, structural geometry, and yaw\"\n" +
            "}";
    }

    private static void AppendMessage(StringBuilder builder, string role, string content)
    {
        builder.Append('{');
        AppendJsonProperty(builder, "role", role);
        builder.Append(',');
        AppendJsonProperty(builder, "content", content);
        builder.Append('}');
    }

    private static void AppendJsonProperty(StringBuilder builder, string name, string value)
    {
        builder.Append('"').Append(EscapeJson(name)).Append("\":\"").Append(EscapeJson(value)).Append('"');
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
    }

    private static bool TryExtractAssistantContent(string responseJson, out string content, out string error)
    {
        content = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(responseJson))
        {
            error = "DeepSeek response is empty.";
            return false;
        }

        try
        {
            var response = JsonUtility.FromJson<DeepSeekChatCompletionResponse>(responseJson);
            if (!string.IsNullOrWhiteSpace(response?.error?.message))
            {
                error = response.error.message;
                return false;
            }

            if (response?.choices == null || response.choices.Length == 0)
            {
                error = $"DeepSeek response has no choices: {Shorten(responseJson, 240)}";
                return false;
            }

            content = response.choices[0]?.message?.content ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                error = "DeepSeek response content is empty.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse DeepSeek chat response: {ex.Message}";
            return false;
        }
    }

    private static bool TryParseStyleIntentJson(
        string userStyleIntent,
        string json,
        out RuntimeStyleIntent intent,
        out string error)
    {
        intent = null;
        error = string.Empty;

        try
        {
            var payload = JsonUtility.FromJson<StyleIntentJsonPayload>(json);
            if (payload == null)
            {
                error = "Style JSON parsed to null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(payload.global_style_summary) ||
                payload.style_keywords == null ||
                payload.material_keywords == null ||
                payload.color_keywords == null ||
                payload.motif_keywords == null ||
                payload.negative_style_keywords == null ||
                string.IsNullOrWhiteSpace(payload.object_style_directive))
            {
                error = $"Style JSON is missing required fields: {Shorten(json, 240)}";
                return false;
            }

            intent = new RuntimeStyleIntent
            {
                UserIntent = userStyleIntent,
                GlobalStyleSummary = payload.global_style_summary.Trim(),
                StyleKeywords = CopyClean(payload.style_keywords),
                MaterialKeywords = CopyClean(payload.material_keywords),
                ColorKeywords = CopyClean(payload.color_keywords),
                MotifKeywords = CopyClean(payload.motif_keywords),
                NegativeStyleKeywords = CopyClean(payload.negative_style_keywords),
                ObjectStyleDirective = payload.object_style_directive.Trim()
            };

            if (intent.StyleKeywords.Count == 0 ||
                intent.MaterialKeywords.Count == 0 ||
                intent.ColorKeywords.Count == 0 ||
                intent.MotifKeywords.Count == 0 ||
                intent.NegativeStyleKeywords.Count == 0)
            {
                error = $"Style JSON has empty keyword lists: {Shorten(json, 240)}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse style JSON: {ex.Message} | {Shorten(json, 240)}";
            return false;
        }
    }

    private static List<string> CopyClean(List<string> source)
    {
        var result = new List<string>();
        if (source == null)
        {
            return result;
        }

        for (var index = 0; index < source.Count; index++)
        {
            var value = source[index];
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(value.Trim());
            }
        }

        return result;
    }

    private static string StripJsonFences(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
            {
                text = text.Substring(firstNewline + 1);
            }

            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
            {
                text = text.Substring(0, lastFence);
            }
        }

        return text.Trim();
    }

    private void PublishStatus(string state, string details = "")
    {
        var builder = new StringBuilder(256);
        builder.AppendLine("[DeepSeekStyleIntent]");
        builder.AppendLine($"State: {state}");
        builder.AppendLine($"Model: {model}");
        if (!string.IsNullOrWhiteSpace(details))
        {
            builder.Append(details.Trim());
        }

        latestStatus = builder.ToString().TrimEnd();
    }

    private static string JoinPreview(List<string> values)
    {
        if (values == null || values.Count == 0)
        {
            return "none";
        }

        var count = Mathf.Min(values.Count, 5);
        return string.Join(", ", values.GetRange(0, count));
    }

    private static string Shorten(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value.Substring(0, maxLength) + "...";
    }

    [Serializable]
    private class DeepSeekChatCompletionResponse
    {
        public DeepSeekChoice[] choices;
        public DeepSeekError error;
    }

    [Serializable]
    private class DeepSeekChoice
    {
        public DeepSeekMessage message;
    }

    [Serializable]
    private class DeepSeekMessage
    {
        public string content;
    }

    [Serializable]
    private class DeepSeekError
    {
        public string message;
        public string type;
        public string code;
    }

    [Serializable]
    private class StyleIntentJsonPayload
    {
        public string global_style_summary;
        public List<string> style_keywords;
        public List<string> material_keywords;
        public List<string> color_keywords;
        public List<string> motif_keywords;
        public List<string> negative_style_keywords;
        public string object_style_directive;
    }
}
