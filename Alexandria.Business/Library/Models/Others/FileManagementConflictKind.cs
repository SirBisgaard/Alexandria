namespace Alexandria.Business.Library.Models.Others;

public enum FileManagementConflictKind
{
    /// <summary>Two or more books render to the same target path under the template.</summary>
    Duplicate,

    /// <summary>The target path exists on disk and is not owned by any book in the plan.</summary>
    ExistingFile
}
