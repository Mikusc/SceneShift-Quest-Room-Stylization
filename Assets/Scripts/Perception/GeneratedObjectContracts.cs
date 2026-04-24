using System;
using UnityEngine;

[Serializable]
public class GeneratedObjectRequest
{
    public string RequestId;
    public string ObjectId;
    public string RoomId;
    public string ThemeId;
    public string ThemeDisplayName;
    public string ThemeShortDescription;
    public string SemanticLabel;
    public string FunctionTag;
    public string SourceAnchorName;
    public int SourceAnchorIndex;
    public SerializablePose WorldPose;
    public SerializableBounds WorldBounds;
    public Vector3 Dimensions;
    public bool CollisionSensitive;
    public ReplacementMode PlannedReplacementMode = ReplacementMode.ProxyPrefab;
    public string PlannedReplacementId;
    public string PlannedReplacementDisplayName;
    public bool PreserveFootprint = true;
    public bool PreserveYawOrientation = true;
    public BestViewCaptureSourceMode CaptureSourceMode = BestViewCaptureSourceMode.ExternalScreenshot;
    public string SourceOriginalInputPath;
    public string SourceImagePath;
    public string SourceFullFrameImagePath;
    public string SourceCroppedImagePath;
    public string SourceMetadataPath;
    public string SourceRequestPath;
    public SerializableRect NormalizedCropRect = SerializableRect.FullFrame;
    public SerializablePose BestViewCameraPose;
    public float BestViewYawDegrees;
    public Vector3 ScaffoldLongestAxis;
    public float VisibilityScore;
    public string PromptVersion;
    public string AppearancePrompt;
    public string ImageStylizationPrompt;
    public string CreatedAtIsoUtc;
}

[Serializable]
public class GeneratedAssetRecord
{
    public string RequestId;
    public string ObjectId;
    public string ThemeId;
    public BestViewCaptureSourceMode CaptureSourceMode = BestViewCaptureSourceMode.ExternalScreenshot;
    public GeneratedObjectJobState State = GeneratedObjectJobState.Pending;
    public string SourceInputImagePath;
    public string SourceRequestPath;
    public string CoordinatorJobPath;
    public string StatusNote;
    public string BackendAdapterName;
    public string BackendRequestPath;
    public string PromptVersion;
    public string PromptArtifactPath;
    public string BackendResultPath;
    public string BackendResultTemplatePath;
    public string BackendTransformId;
    public string StylizedImagePath;
    public string GeneratedModelPath;
    public string ImportedPrefabPath;
    public string PreviewImagePath;
    public SerializableBounds ImportedBounds;
    public float SourceYawDegrees;
    public Vector3 RegisteredScale = Vector3.one;
    public Vector3 RegisteredEulerDegrees;
    public float RegistrationIoUScore;
    public string FailureReason;
    public string UpdatedAtIsoUtc;
}

[Serializable]
public class GeneratedImageBackendResult
{
    public string RequestId;
    public string ObjectId;
    public string ThemeId;
    public string PromptVersion;
    public string PromptArtifactPath;
    public string SourceInputImagePath;
    public string SourceRequestPath;
    public string OutputImagePath;
    public string BackendAdapterName;
    public string AppliedTransformId;
    public bool PromptArtifactConsumed;
    public GeneratedObjectJobState OutputState = GeneratedObjectJobState.StylizedImageReady;
    public string StatusNote;
    public string CreatedAtIsoUtc;
}

[Serializable]
public class GeneratedImageBackendSubmission
{
    public string RequestId;
    public string ObjectId;
    public string ThemeId;
    public string PromptVersion;
    public string PromptArtifactPath;
    public string SourceInputImagePath;
    public string SourceRequestPath;
    public string RequestedOutputImagePath;
    public string RequestedResultPath;
    public string ResultTemplatePath;
    public string BackendAdapterName;
    public string SubmissionNote;
    public string CreatedAtIsoUtc;
}

public enum GeneratedObjectJobState
{
    Pending,
    CaptureReady,
    StylizedImageReady,
    ModelReady,
    Imported,
    Failed,
    BackendSubmitted,
}

public enum BestViewCaptureSourceMode
{
    ExternalScreenshot,
    UnityFramebufferDebug,
    DevicePassthroughReserved,
}

[Serializable]
public struct SerializablePose
{
    public Vector3 Position;
    public Quaternion Rotation;

    public static SerializablePose From(Vector3 position, Quaternion rotation)
    {
        return new SerializablePose
        {
            Position = position,
            Rotation = rotation,
        };
    }
}

[Serializable]
public struct SerializableBounds
{
    public Vector3 Center;
    public Vector3 Size;

    public static SerializableBounds From(Vector3 center, Vector3 size)
    {
        return new SerializableBounds
        {
            Center = center,
            Size = size,
        };
    }
}

[Serializable]
public struct SerializableRect
{
    public float X;
    public float Y;
    public float Width;
    public float Height;

    public static SerializableRect FullFrame => new SerializableRect
    {
        X = 0f,
        Y = 0f,
        Width = 1f,
        Height = 1f,
    };

    public static SerializableRect From(Rect rect)
    {
        return new SerializableRect
        {
            X = rect.x,
            Y = rect.y,
            Width = rect.width,
            Height = rect.height,
        };
    }
}
