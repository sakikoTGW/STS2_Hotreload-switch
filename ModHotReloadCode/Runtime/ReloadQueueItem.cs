namespace ModHotReload.Runtime;

internal readonly record struct ReloadQueueItem(string Folder, string TriggerPath, ReloadChangeKind Kind);
