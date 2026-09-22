namespace Jellyfin.MediaEncoding.Keyframes.Mp4;

/// <summary>
/// ISO base media file format box types, as big endian four character codes.
/// </summary>
internal static class Mp4Constants
{
    /// <summary>
    /// The movie box, holding all metadata of a non fragmented file.
    /// </summary>
    public const uint Moov = 0x6D6F6F76;

    /// <summary>
    /// The movie header box.
    /// </summary>
    public const uint Mvhd = 0x6D766864;

    /// <summary>
    /// The movie extends box. Its presence marks the file as fragmented.
    /// </summary>
    public const uint Mvex = 0x6D766578;

    /// <summary>
    /// The track box.
    /// </summary>
    public const uint Trak = 0x7472616B;

    /// <summary>
    /// The edit box.
    /// </summary>
    public const uint Edts = 0x65647473;

    /// <summary>
    /// The edit list box.
    /// </summary>
    public const uint Elst = 0x656C7374;

    /// <summary>
    /// The media box.
    /// </summary>
    public const uint Mdia = 0x6D646961;

    /// <summary>
    /// The media header box.
    /// </summary>
    public const uint Mdhd = 0x6D646864;

    /// <summary>
    /// The handler reference box.
    /// </summary>
    public const uint Hdlr = 0x68646C72;

    /// <summary>
    /// The media information box.
    /// </summary>
    public const uint Minf = 0x6D696E66;

    /// <summary>
    /// The sample table box.
    /// </summary>
    public const uint Stbl = 0x7374626C;

    /// <summary>
    /// The decoding time to sample box.
    /// </summary>
    public const uint Stts = 0x73747473;

    /// <summary>
    /// The composition time to sample box.
    /// </summary>
    public const uint Ctts = 0x63747473;

    /// <summary>
    /// The sync sample box.
    /// </summary>
    public const uint Stss = 0x73747373;

    /// <summary>
    /// The partial sync sample box, listing open GOP random access points.
    /// </summary>
    public const uint Stps = 0x73747073;

    /// <summary>
    /// The sample to group box.
    /// </summary>
    public const uint Sbgp = 0x73626770;

    /// <summary>
    /// The random access point sample grouping type.
    /// </summary>
    public const uint GroupingTypeRap = 0x72617020;

    /// <summary>
    /// The video media handler type.
    /// </summary>
    public const uint HandlerVideo = 0x76696465;
}
