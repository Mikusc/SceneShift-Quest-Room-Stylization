using UnityEngine;

[DisallowMultipleComponent]
public sealed class StylizedFurnitureInstance : MonoBehaviour
{
    [SerializeField] private string entryId;
    [SerializeField] private string objectId;
    [SerializeField] private string semanticLabel;
    [SerializeField] private string prefabSource;
    [SerializeField] private string prefabName;

    public string EntryId => entryId;
    public string ObjectId => objectId;
    public string SemanticLabel => semanticLabel;
    public string PrefabSource => prefabSource;
    public string PrefabName => prefabName;

    public string DisplayLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(objectId))
            {
                return objectId;
            }

            return string.IsNullOrWhiteSpace(entryId) ? name : entryId;
        }
    }

    public void Initialize(
        string planEntryId,
        string planObjectId,
        string originalSemanticLabel,
        string replacementSource,
        string replacementPrefabName)
    {
        entryId = planEntryId;
        objectId = planObjectId;
        semanticLabel = originalSemanticLabel;
        prefabSource = replacementSource;
        prefabName = replacementPrefabName;
    }
}
