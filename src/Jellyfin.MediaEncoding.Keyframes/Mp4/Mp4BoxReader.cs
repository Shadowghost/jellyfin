using System;
using System.Buffers.Binary;
using System.IO;

namespace Jellyfin.MediaEncoding.Keyframes.Mp4;

/// <summary>
/// Minimal reader for the box structure of the ISO base media file format.
/// </summary>
internal sealed class Mp4BoxReader
{
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[8];

    /// <summary>
    /// Initializes a new instance of the <see cref="Mp4BoxReader"/> class.
    /// </summary>
    /// <param name="stream">The seekable stream to read from.</param>
    public Mp4BoxReader(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// Gets the length of the underlying stream.
    /// </summary>
    public long Length => _stream.Length;

    /// <summary>
    /// Reads the header of the box at the current position.
    /// </summary>
    /// <param name="parentEnd">The end of the enclosing box.</param>
    /// <param name="box">The box that was read.</param>
    /// <returns>Whether a well formed box header was read.</returns>
    public bool TryReadHeader(long parentEnd, out Mp4Box box)
    {
        box = default;
        var start = _stream.Position;
        if (start + 8 > parentEnd)
        {
            return false;
        }

        _stream.ReadExactly(_buffer, 0, 8);
        long size = BinaryPrimitives.ReadUInt32BigEndian(_buffer);
        var type = BinaryPrimitives.ReadUInt32BigEndian(_buffer.AsSpan(4));
        var contentStart = start + 8;

        if (size == 1)
        {
            if (contentStart + 8 > parentEnd)
            {
                return false;
            }

            _stream.ReadExactly(_buffer, 0, 8);
            var largeSize = BinaryPrimitives.ReadUInt64BigEndian(_buffer);
            if (largeSize > long.MaxValue)
            {
                return false;
            }

            size = (long)largeSize;
            contentStart += 8;
        }
        else if (size == 0)
        {
            // A size of zero means the box extends to the end of its parent.
            size = parentEnd - start;
        }

        var end = start + size;
        if (end < contentStart || end > parentEnd)
        {
            return false;
        }

        box = new Mp4Box(type, contentStart, end);
        return true;
    }

    /// <summary>
    /// Finds the first child box of the given type.
    /// </summary>
    /// <param name="parent">The box to search in.</param>
    /// <param name="type">The box type to look for.</param>
    /// <param name="box">The box that was found.</param>
    /// <returns>Whether the box was found.</returns>
    public bool TryFindChild(Mp4Box parent, uint type, out Mp4Box box)
    {
        _stream.Position = parent.ContentStart;
        while (TryReadHeader(parent.End, out var child))
        {
            if (child.Type == type)
            {
                box = child;
                return true;
            }

            _stream.Position = child.End;
        }

        box = default;
        return false;
    }

    /// <summary>
    /// Positions the stream at the given offset.
    /// </summary>
    /// <param name="position">The offset to seek to.</param>
    public void Seek(long position)
    {
        _stream.Position = position;
    }

    /// <summary>
    /// Reads the version and flags of a full box, leaving the stream at the start of its payload.
    /// </summary>
    /// <param name="box">The full box to read.</param>
    /// <returns>The version of the box.</returns>
    public byte ReadFullBoxVersion(Mp4Box box)
    {
        _stream.Position = box.ContentStart;
        return ReadUInt8();
    }

    /// <summary>
    /// Reads an unsigned byte.
    /// </summary>
    /// <returns>The value that was read.</returns>
    public byte ReadUInt8()
    {
        _stream.ReadExactly(_buffer, 0, 1);
        return _buffer[0];
    }

    /// <summary>
    /// Reads a big endian unsigned 32 bit integer.
    /// </summary>
    /// <returns>The value that was read.</returns>
    public uint ReadUInt32()
    {
        _stream.ReadExactly(_buffer, 0, 4);
        return BinaryPrimitives.ReadUInt32BigEndian(_buffer);
    }

    /// <summary>
    /// Reads a big endian signed 32 bit integer.
    /// </summary>
    /// <returns>The value that was read.</returns>
    public int ReadInt32()
    {
        _stream.ReadExactly(_buffer, 0, 4);
        return BinaryPrimitives.ReadInt32BigEndian(_buffer);
    }

    /// <summary>
    /// Reads a big endian unsigned 64 bit integer.
    /// </summary>
    /// <returns>The value that was read.</returns>
    public ulong ReadUInt64()
    {
        _stream.ReadExactly(_buffer, 0, 8);
        return BinaryPrimitives.ReadUInt64BigEndian(_buffer);
    }

    /// <summary>
    /// Reads a big endian signed 64 bit integer.
    /// </summary>
    /// <returns>The value that was read.</returns>
    public long ReadInt64()
    {
        _stream.ReadExactly(_buffer, 0, 8);
        return BinaryPrimitives.ReadInt64BigEndian(_buffer);
    }

    /// <summary>
    /// Skips the given number of bytes.
    /// </summary>
    /// <param name="count">The number of bytes to skip.</param>
    public void Skip(int count)
    {
        _stream.Position += count;
    }

    /// <summary>
    /// Reads the entry count of a table and verifies that the entries fit into the box.
    /// </summary>
    /// <param name="box">The box holding the table.</param>
    /// <param name="entrySize">The size of a single entry in bytes.</param>
    /// <param name="entryCount">The number of entries in the table.</param>
    /// <returns>Whether the table is well formed.</returns>
    public bool TryReadEntryCount(Mp4Box box, int entrySize, out int entryCount)
    {
        entryCount = 0;
        var count = ReadUInt32();
        // Guard against a corrupt count claiming more entries than the box can possibly hold.
        if (count > int.MaxValue || (long)count * entrySize > box.End - _stream.Position)
        {
            return false;
        }

        entryCount = (int)count;
        return true;
    }
}
