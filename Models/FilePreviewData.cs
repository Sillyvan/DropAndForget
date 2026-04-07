namespace DropAndForget.Models;

/// <summary>
/// Holds loaded preview content for a file.
/// </summary>
/// <param name="Title">Display title.</param>
/// <param name="Kind">Preview renderer kind.</param>
/// <param name="BinaryContent">Binary payload for image or PDF previews.</param>
/// <param name="TextContent">Decoded text for text previews.</param>
public sealed record FilePreviewData(string Title, FilePreviewKind Kind, byte[]? BinaryContent = null, string? TextContent = null);
