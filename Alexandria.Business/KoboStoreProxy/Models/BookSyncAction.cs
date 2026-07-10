namespace Alexandria.Business.KoboStoreProxy.Models;

/// <summary>
/// Sync flow a batched book enters for a device during a Kobo library sync,
/// resolved by <see cref="Services.KoboLibrarySyncService"/>'s classifier.
/// </summary>
internal enum BookSyncAction
{
    /// <summary>Archived and never synced — nothing to send.</summary>
    None,
    /// <summary>Device has never seen the book — send NewEntitlement.</summary>
    AddToDevice,
    /// <summary>Archived but still on the device — send ChangedEntitlement with IsRemoved=true.</summary>
    RemoveFromDevice,
    /// <summary>Metadata changed — Kobo ignores in-place entitlement updates, so remove and re-add.</summary>
    ReplaceOnDevice,
    /// <summary>Only reading progress changed (another device or external integration) — send bare ChangedReadingState.</summary>
    ReadingStateOnly,
    /// <summary>Unchanged mid-session — re-send as ChangedEntitlement to confirm the device still has it.</summary>
    ConfirmOnDevice
}
