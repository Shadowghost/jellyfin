using System;
using System.Collections.Generic;
using System.IO;

namespace Jellyfin.MediaEncoding.Keyframes.Mp4;

/// <summary>
/// The keyframe extractor for the ISO base media file format, covering mp4, m4v and mov.
/// </summary>
public static class Mp4KeyframeExtractor
{
    private static readonly KeyframeData _empty = new(0, Array.Empty<long>());

    /// <summary>
    /// Extracts the keyframes in ticks from the sample tables of the container.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>An instance of <see cref="KeyframeData"/>.</returns>
    public static KeyframeData GetKeyframeData(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
        return GetKeyframeData(stream);
    }

    /// <summary>
    /// Extracts the keyframes in ticks from the sample tables of the container.
    /// </summary>
    /// <param name="stream">The seekable stream holding the container.</param>
    /// <returns>An instance of <see cref="KeyframeData"/>.</returns>
    public static KeyframeData GetKeyframeData(Stream stream)
    {
        var reader = new Mp4BoxReader(stream);
        var fileEnd = reader.Length;
        var file = new Mp4Box(0, 0, fileEnd);

        if (!reader.TryFindChild(file, Mp4Constants.Moov, out var moov))
        {
            return _empty;
        }

        // Fragmented files describe their samples in the fragments rather than in the movie box.
        if (reader.TryFindChild(moov, Mp4Constants.Mvex, out _))
        {
            return _empty;
        }

        if (!reader.TryFindChild(moov, Mp4Constants.Mvhd, out var mvhd))
        {
            return _empty;
        }

        var movieTimescale = ReadTimescale(reader, mvhd);
        if (movieTimescale == 0)
        {
            return _empty;
        }

        reader.Seek(moov.ContentStart);
        while (reader.TryReadHeader(moov.End, out var child))
        {
            var nextSibling = child.End;
            // The demuxer exposes the tracks in the order they are stored, so the first video track
            // is the one that gets remuxed. If it yields nothing, no other track may stand in for it.
            if (child.Type == Mp4Constants.Trak && IsVideoTrack(reader, child, out var mdia))
            {
                return TryReadVideoTrack(reader, child, mdia, movieTimescale, out var keyframeData) ? keyframeData : _empty;
            }

            reader.Seek(nextSibling);
        }

        return _empty;
    }

    private static bool IsVideoTrack(Mp4BoxReader reader, Mp4Box trak, out Mp4Box mdia)
    {
        if (!reader.TryFindChild(trak, Mp4Constants.Mdia, out mdia)
            || !reader.TryFindChild(mdia, Mp4Constants.Hdlr, out var hdlr))
        {
            return false;
        }

        reader.ReadFullBoxVersion(hdlr);
        // Skip the remaining flags and the pre_defined field preceding the handler type.
        reader.Skip(7);
        return reader.ReadUInt32() == Mp4Constants.HandlerVideo;
    }

    private static bool TryReadVideoTrack(Mp4BoxReader reader, Mp4Box trak, Mp4Box mdia, uint movieTimescale, out KeyframeData keyframeData)
    {
        keyframeData = _empty;

        if (!reader.TryFindChild(mdia, Mp4Constants.Mdhd, out var mdhd))
        {
            return false;
        }

        var mediaTimescale = ReadTimescale(reader, mdhd);
        if (mediaTimescale == 0)
        {
            return false;
        }

        var mediaDuration = ReadDuration(reader, mdhd);

        if (!reader.TryFindChild(mdia, Mp4Constants.Minf, out var minf)
            || !reader.TryFindChild(minf, Mp4Constants.Stbl, out var stbl)
            || !reader.TryFindChild(stbl, Mp4Constants.Stts, out var stts))
        {
            return false;
        }

        // Without a sync sample box every sample is a keyframe, which means the muxer can cut
        // wherever it wants and equally sized segments already describe the output correctly.
        if (!reader.TryFindChild(stbl, Mp4Constants.Stss, out var stss))
        {
            return false;
        }

        // A random access point sample group marks further keyframes in a layout this extractor
        // does not model. Reporting a subset would make the playlist claim segments that are longer
        // than the ones the muxer cuts, so let another extractor handle the file.
        if (HasRandomAccessPointGroup(reader, stbl))
        {
            return false;
        }

        if (!TryReadTimeToSample(reader, stts, out var sampleRuns)
            || !TryReadSampleNumbers(reader, stss, out var syncSamples))
        {
            return false;
        }

        // Open GOP random access points live in their own box and are keyframes to the muxer as well.
        if (reader.TryFindChild(stbl, Mp4Constants.Stps, out var stps))
        {
            if (!TryReadSampleNumbers(reader, stps, out var partialSyncSamples))
            {
                return false;
            }

            syncSamples = Merge(syncSamples, partialSyncSamples);
        }

        IReadOnlyList<CompositionRun> compositionRuns = Array.Empty<CompositionRun>();
        if (reader.TryFindChild(stbl, Mp4Constants.Ctts, out var ctts) && !TryReadCompositionOffsets(reader, ctts, out compositionRuns))
        {
            return false;
        }

        if (!TryReadEditList(reader, trak, out var edit))
        {
            return false;
        }

        var keyframes = BuildKeyframes(sampleRuns, compositionRuns, syncSamples, mediaTimescale, movieTimescale, edit);
        if (keyframes.Count == 0)
        {
            return false;
        }

        // A non empty edit usually only compensates the composition delay, it does not shorten the
        // presentation, so the media header duration is used as is. The media timescale is also finer
        // grained than the movie timescale, so an edit that does trim the track merely caps the value.
        var duration = ToTicks(mediaDuration, mediaTimescale);
        if (edit.PresentedDuration > 0)
        {
            duration = Math.Min(duration, ToTicks(edit.PresentedDuration, movieTimescale));
        }

        // An empty edit delays the start of the presentation, which the keyframes account for as well.
        duration += ToTicks(edit.EmptyDuration, movieTimescale);

        keyframeData = new KeyframeData(duration, keyframes);
        return true;
    }

    private static List<long> BuildKeyframes(
        IReadOnlyList<SampleRun> sampleRuns,
        IReadOnlyList<CompositionRun> compositionRuns,
        IReadOnlyList<uint> syncSamples,
        uint mediaTimescale,
        uint movieTimescale,
        EditList edit)
    {
        var emptyEditTicks = ToTicks(edit.EmptyDuration, movieTimescale);
        var keyframes = new List<long>(syncSamples.Count);

        var sampleIndex = 0;
        var firstSampleInRun = 1L;
        var runStartTime = 0L;

        var compositionIndex = 0;
        var firstSampleInCompositionRun = 1L;

        var previous = long.MinValue;
        for (var i = 0; i < syncSamples.Count; i++)
        {
            var sampleNumber = syncSamples[i];

            // Sync samples are ordered, so a single forward pass over the runs is enough.
            while (sampleIndex < sampleRuns.Count && sampleNumber >= firstSampleInRun + sampleRuns[sampleIndex].SampleCount)
            {
                runStartTime += sampleRuns[sampleIndex].SampleCount * sampleRuns[sampleIndex].SampleDelta;
                firstSampleInRun += sampleRuns[sampleIndex].SampleCount;
                sampleIndex++;
            }

            if (sampleIndex == sampleRuns.Count)
            {
                break;
            }

            var decodeTime = runStartTime + ((sampleNumber - firstSampleInRun) * sampleRuns[sampleIndex].SampleDelta);

            long compositionOffset = 0;
            if (compositionRuns.Count > 0)
            {
                while (compositionIndex < compositionRuns.Count && sampleNumber >= firstSampleInCompositionRun + compositionRuns[compositionIndex].SampleCount)
                {
                    firstSampleInCompositionRun += compositionRuns[compositionIndex].SampleCount;
                    compositionIndex++;
                }

                if (compositionIndex < compositionRuns.Count)
                {
                    compositionOffset = compositionRuns[compositionIndex].SampleOffset;
                }
            }

            // The edit list maps media time onto the presentation timeline the demuxer reports.
            var presentationTime = ToTicks(decodeTime + compositionOffset - edit.MediaTime, mediaTimescale) + emptyEditTicks;
            if (presentationTime < 0)
            {
                presentationTime = 0;
            }

            if (presentationTime > previous)
            {
                keyframes.Add(presentationTime);
                previous = presentationTime;
            }
        }

        return keyframes;
    }

    private static bool TryReadTimeToSample(Mp4BoxReader reader, Mp4Box stts, out IReadOnlyList<SampleRun> runs)
    {
        runs = Array.Empty<SampleRun>();
        reader.ReadFullBoxVersion(stts);
        reader.Skip(3);
        if (!reader.TryReadEntryCount(stts, 8, out var entryCount) || entryCount == 0)
        {
            return false;
        }

        var result = new SampleRun[entryCount];
        for (var i = 0; i < entryCount; i++)
        {
            result[i] = new SampleRun(reader.ReadUInt32(), reader.ReadUInt32());
        }

        runs = result;
        return true;
    }

    private static bool TryReadCompositionOffsets(Mp4BoxReader reader, Mp4Box ctts, out IReadOnlyList<CompositionRun> runs)
    {
        runs = Array.Empty<CompositionRun>();
        var version = reader.ReadFullBoxVersion(ctts);
        reader.Skip(3);
        if (!reader.TryReadEntryCount(ctts, 8, out var entryCount))
        {
            return false;
        }

        var result = new CompositionRun[entryCount];
        for (var i = 0; i < entryCount; i++)
        {
            var sampleCount = reader.ReadUInt32();
            // Version 1 allows negative offsets, version 0 does not.
            long offset = version == 0 ? reader.ReadUInt32() : reader.ReadInt32();
            result[i] = new CompositionRun(sampleCount, offset);
        }

        runs = result;
        return true;
    }

    private static bool TryReadSampleNumbers(Mp4BoxReader reader, Mp4Box box, out IReadOnlyList<uint> sampleNumbers)
    {
        sampleNumbers = Array.Empty<uint>();
        reader.ReadFullBoxVersion(box);
        reader.Skip(3);
        if (!reader.TryReadEntryCount(box, 4, out var entryCount) || entryCount == 0)
        {
            return false;
        }

        var result = new uint[entryCount];
        for (var i = 0; i < entryCount; i++)
        {
            result[i] = reader.ReadUInt32();
        }

        sampleNumbers = result;
        return true;
    }

    private static IReadOnlyList<uint> Merge(IReadOnlyList<uint> first, IReadOnlyList<uint> second)
    {
        var merged = new SortedSet<uint>(first);
        for (var i = 0; i < second.Count; i++)
        {
            merged.Add(second[i]);
        }

        return [.. merged];
    }

    private static bool HasRandomAccessPointGroup(Mp4BoxReader reader, Mp4Box stbl)
    {
        reader.Seek(stbl.ContentStart);
        while (reader.TryReadHeader(stbl.End, out var child))
        {
            if (child.Type == Mp4Constants.Sbgp)
            {
                reader.ReadFullBoxVersion(child);
                reader.Skip(3);
                if (reader.ReadUInt32() == Mp4Constants.GroupingTypeRap)
                {
                    return true;
                }
            }

            reader.Seek(child.End);
        }

        return false;
    }

    private static bool TryReadEditList(Mp4BoxReader reader, Mp4Box trak, out EditList edit)
    {
        edit = default;
        if (!reader.TryFindChild(trak, Mp4Constants.Edts, out var edts)
            || !reader.TryFindChild(edts, Mp4Constants.Elst, out var elst))
        {
            return true;
        }

        var version = reader.ReadFullBoxVersion(elst);
        reader.Skip(3);
        var entrySize = version == 0 ? 12 : 20;
        if (!reader.TryReadEntryCount(elst, entrySize, out var entryCount))
        {
            return false;
        }

        var seenSegment = false;
        for (var i = 0; i < entryCount; i++)
        {
            long segmentDuration;
            long mediaTime;
            if (version == 0)
            {
                segmentDuration = reader.ReadUInt32();
                mediaTime = reader.ReadInt32();
            }
            else
            {
                segmentDuration = (long)reader.ReadUInt64();
                mediaTime = reader.ReadInt64();
            }

            // Skip the media rate.
            reader.Skip(4);

            if (mediaTime < 0)
            {
                // An empty edit delays the start of the presentation.
                edit = edit with { EmptyDuration = edit.EmptyDuration + segmentDuration };
                continue;
            }

            if (seenSegment)
            {
                // Anything beyond a single trim rearranges the timeline in ways this extractor
                // does not model, so let another extractor handle the file.
                return false;
            }

            seenSegment = true;
            edit = edit with { MediaTime = mediaTime, PresentedDuration = segmentDuration };
        }

        return true;
    }

    private static uint ReadTimescale(Mp4BoxReader reader, Mp4Box header)
    {
        var version = reader.ReadFullBoxVersion(header);
        // Skip the remaining flags and the creation and modification times.
        reader.Skip(version == 0 ? 11 : 19);
        return reader.ReadUInt32();
    }

    private static long ReadDuration(Mp4BoxReader reader, Mp4Box header)
    {
        var version = reader.ReadFullBoxVersion(header);
        reader.Skip(version == 0 ? 11 : 19);
        // Skip the timescale preceding the duration.
        reader.Skip(4);
        if (version == 0)
        {
            var duration = reader.ReadUInt32();
            // An unknown duration is stored as the largest representable value.
            return duration == uint.MaxValue ? 0 : duration;
        }

        var longDuration = reader.ReadUInt64();
        return longDuration == ulong.MaxValue ? 0 : (long)longDuration;
    }

    private static long ToTicks(long value, uint timescale)
        => (long)((Int128)value * TimeSpan.TicksPerSecond / timescale);

    private readonly record struct SampleRun(long SampleCount, long SampleDelta);

    private readonly record struct CompositionRun(long SampleCount, long SampleOffset);

    private readonly record struct EditList(long MediaTime, long EmptyDuration, long PresentedDuration);
}
