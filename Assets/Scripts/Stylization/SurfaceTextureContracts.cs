using System;
using System.Collections.Generic;

[Serializable]
public class SurfaceTexturePromptSet
{
    public string ThemeId;
    public string ThemeDisplayName;
    public string ThemeDescription;
    public string StyleVariantId;
    public string UserStyleIntent;
    public string StyleIntentSource;
    public string CreatedAtIsoUtc;
    public string JobFolder;
    public List<SurfaceTexturePromptEntry> Entries = new();
}

[Serializable]
public class SurfaceTexturePromptEntry
{
    public string RequestId;
    public string StyleVariantId;
    public string UserStyleIntent;
    public string StyleIntentSource;
    public string SemanticLabel;
    public ThemeSurfaceKind SurfaceKind;
    public string OutputRole;
    public string PromptVersion;
    public string Prompt;
    public string NegativePrompt;
    public string ImageSize;
    public string PromptPath;
    public string JobPath;
    public string OutputImagePath;
    public bool SeamlessTileable;
    public bool PbrMaterial;
    public bool RuntimeFallbackAvailable;
}

[Serializable]
public class SurfaceTextureJobRecord
{
    public string RequestId;
    public string ThemeId;
    public string ThemeDisplayName;
    public string StyleVariantId;
    public string UserStyleIntent;
    public string StyleIntentSource;
    public string SemanticLabel;
    public ThemeSurfaceKind SurfaceKind;
    public string OutputRole;
    public SurfaceTextureJobState State = SurfaceTextureJobState.PromptReady;
    public string PromptVersion;
    public string ImageSize;
    public string PromptArtifactPath;
    public string JobPath;
    public string BackendAdapterName;
    public string BackendRequestPath;
    public string BackendResultPath;
    public string BackendTransformId;
    public string OutputImagePath;
    public string OutputImageUrl;
    public string StatusNote;
    public string FailureReason;
    public string UpdatedAtIsoUtc;
}

public enum SurfaceTextureJobState
{
    PromptReady,
    BackendSubmitted,
    TextureReady,
    MaterialReady,
    Failed,
}
