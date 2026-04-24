using System;
using System.Collections.Generic;

[Serializable]
public class SurfaceTexturePromptSet
{
    public string ThemeId;
    public string ThemeDisplayName;
    public string ThemeDescription;
    public string CreatedAtIsoUtc;
    public string JobFolder;
    public List<SurfaceTexturePromptEntry> Entries = new();
}

[Serializable]
public class SurfaceTexturePromptEntry
{
    public string SemanticLabel;
    public ThemeSurfaceKind SurfaceKind;
    public string OutputRole;
    public string Prompt;
    public string NegativePrompt;
    public string PromptPath;
    public bool SeamlessTileable;
    public bool PbrMaterial;
    public bool RuntimeFallbackAvailable;
}
