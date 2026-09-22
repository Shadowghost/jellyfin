namespace Jellyfin.MediaEncoding.Keyframes.Mp4;

/// <summary>
/// The header of a box in the ISO base media file format.
/// </summary>
/// <param name="Type">The four character code identifying the box.</param>
/// <param name="ContentStart">The offset of the first payload byte.</param>
/// <param name="End">The offset directly after the box, i.e. the start of the next sibling.</param>
internal readonly record struct Mp4Box(uint Type, long ContentStart, long End);
