namespace ShiJi.Models;

/// <summary>
/// A process discovered at runtime that can be added to the watch list.
/// ExecutablePath may be null when Windows denies access (protected game or
/// elevated process); such an entry is matched by process name instead.
/// </summary>
public sealed class DetectedApp
{
    public string Name { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string? ExecutablePath { get; set; }
}
