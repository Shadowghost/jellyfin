using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Jellyfin.MediaEncoding.Keyframes.Tests.Mp4;

/// <summary>
/// Builds the metadata of a minimal ISO base media file. Only the boxes the keyframe extractor
/// reads are written, the media data itself is irrelevant for the sample tables.
/// </summary>
internal sealed class Mp4FileBuilder
{
    private readonly List<byte[]> _trackBoxes = [];

    public uint MovieTimescale { get; set; } = 1000;

    public bool Fragmented { get; set; }

    public Mp4FileBuilder AddVideoTrack(
        uint mediaTimescale,
        long mediaDuration,
        IReadOnlyList<(uint SampleCount, uint SampleDelta)> timeToSample,
        IReadOnlyList<uint> syncSamples,
        IReadOnlyList<(uint SampleCount, int SampleOffset)>? compositionOffsets = null,
        IReadOnlyList<(long SegmentDuration, long MediaTime)>? editList = null,
        string handler = "vide",
        IReadOnlyList<uint>? partialSyncSamples = null,
        bool randomAccessPointGroup = false)
    {
        var stbl = new List<byte[]> { BuildTimeToSample(timeToSample) };
        if (syncSamples.Count > 0)
        {
            stbl.Add(BuildSyncSample(syncSamples));
        }

        if (partialSyncSamples is not null)
        {
            stbl.Add(BuildSampleNumbers("stps", partialSyncSamples));
        }

        if (randomAccessPointGroup)
        {
            stbl.Add(BuildRandomAccessPointGroup());
        }

        if (compositionOffsets is not null)
        {
            stbl.Add(BuildCompositionOffset(compositionOffsets));
        }

        var mdia = new List<byte[]>
        {
            BuildMediaHeader(mediaTimescale, mediaDuration),
            BuildHandler(handler),
            Box("minf", Box("stbl", stbl.ToArray()))
        };

        var trak = new List<byte[]>();
        if (editList is not null)
        {
            trak.Add(Box("edts", BuildEditList(editList)));
        }

        trak.Add(Box("mdia", mdia.ToArray()));
        _trackBoxes.Add(Box("trak", trak.ToArray()));
        return this;
    }

    public Stream Build()
    {
        var moovChildren = new List<byte[]> { BuildMovieHeader() };
        if (Fragmented)
        {
            moovChildren.Add(Box("mvex", Array.Empty<byte>()));
        }

        moovChildren.AddRange(_trackBoxes);

        var stream = new MemoryStream();
        // A real file starts with a brand and usually keeps the movie box behind the media data.
        Write(stream, Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\u0002\0isomiso2avc1mp41")));
        Write(stream, Box("mdat", new byte[64]));
        Write(stream, Box("moov", moovChildren.ToArray()));
        stream.Position = 0;
        return stream;
    }

    private static void Write(Stream stream, byte[] data) => stream.Write(data, 0, data.Length);

    private static byte[] Box(string type, params byte[][] payloads)
    {
        var length = 0;
        foreach (var payload in payloads)
        {
            length += payload.Length;
        }

        var result = new byte[8 + length];
        BinaryPrimitives.WriteUInt32BigEndian(result, (uint)result.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(result, 4);

        var offset = 8;
        foreach (var payload in payloads)
        {
            payload.CopyTo(result, offset);
            offset += payload.Length;
        }

        return result;
    }

    private byte[] BuildMovieHeader()
    {
        var writer = new BigEndianWriter();
        writer.WriteFullBoxHeader(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(MovieTimescale);
        writer.WriteUInt32(0);
        // The remaining fields of mvhd are never read.
        writer.WriteBytes(new byte[80]);
        return Box("mvhd", writer.ToArray());
    }

    private static byte[] BuildMediaHeader(uint timescale, long duration)
    {
        var writer = new BigEndianWriter();
        writer.WriteFullBoxHeader(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(timescale);
        writer.WriteUInt32((uint)duration);
        writer.WriteUInt32(0);
        return Box("mdhd", writer.ToArray());
    }

    private static byte[] BuildHandler(string handler)
    {
        var writer = new BigEndianWriter();
        writer.WriteFullBoxHeader(0);
        writer.WriteUInt32(0);
        writer.WriteBytes(Encoding.ASCII.GetBytes(handler));
        writer.WriteBytes(new byte[13]);
        return Box("hdlr", writer.ToArray());
    }

    private static byte[] BuildTimeToSample(IReadOnlyList<(uint SampleCount, uint SampleDelta)> entries)
    {
        var writer = new BigEndianWriter();
        writer.WriteFullBoxHeader(0);
        writer.WriteUInt32((uint)entries.Count);
        foreach (var (sampleCount, sampleDelta) in entries)
        {
            writer.WriteUInt32(sampleCount);
            writer.WriteUInt32(sampleDelta);
        }

        return Box("stts", writer.ToArray());
    }

    private static byte[] BuildCompositionOffset(IReadOnlyList<(uint SampleCount, int SampleOffset)> entries)
    {
        var writer = new BigEndianWriter();
        // Version 1 is used so that negative offsets round trip.
        writer.WriteFullBoxHeader(1);
        writer.WriteUInt32((uint)entries.Count);
        foreach (var (sampleCount, sampleOffset) in entries)
        {
            writer.WriteUInt32(sampleCount);
            writer.WriteInt32(sampleOffset);
        }

        return Box("ctts", writer.ToArray());
    }

    private static byte[] BuildSyncSample(IReadOnlyList<uint> syncSamples) => BuildSampleNumbers("stss", syncSamples);

    private static byte[] BuildSampleNumbers(string type, IReadOnlyList<uint> sampleNumbers)
    {
        var writer = new BigEndianWriter();
        writer.WriteFullBoxHeader(0);
        writer.WriteUInt32((uint)sampleNumbers.Count);
        foreach (var sample in sampleNumbers)
        {
            writer.WriteUInt32(sample);
        }

        return Box(type, writer.ToArray());
    }

    private static byte[] BuildRandomAccessPointGroup()
    {
        var writer = new BigEndianWriter();
        writer.WriteFullBoxHeader(0);
        writer.WriteBytes(Encoding.ASCII.GetBytes("rap "));
        writer.WriteUInt32(0);
        return Box("sbgp", writer.ToArray());
    }

    private static byte[] BuildEditList(IReadOnlyList<(long SegmentDuration, long MediaTime)> entries)
    {
        var writer = new BigEndianWriter();
        writer.WriteFullBoxHeader(0);
        writer.WriteUInt32((uint)entries.Count);
        foreach (var (segmentDuration, mediaTime) in entries)
        {
            writer.WriteUInt32((uint)segmentDuration);
            writer.WriteInt32((int)mediaTime);
            writer.WriteUInt32(0x00010000);
        }

        return Box("elst", writer.ToArray());
    }

    private sealed class BigEndianWriter
    {
        private readonly List<byte> _bytes = [];

        public void WriteFullBoxHeader(byte version)
        {
            _bytes.Add(version);
            _bytes.AddRange(new byte[3]);
        }

        public void WriteUInt32(uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            _bytes.AddRange(buffer);
        }

        public void WriteInt32(int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            _bytes.AddRange(buffer);
        }

        public void WriteBytes(byte[] value) => _bytes.AddRange(value);

        public byte[] ToArray() => _bytes.ToArray();
    }
}
