using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace LethalGoofballsPack.WesleyZh
{
    internal sealed class CaptionCatalog
    {
        private readonly Dictionary<string, List<CaptionTrack>> audio = new Dictionary<string, List<CaptionTrack>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<CaptionTrack>> video = new Dictionary<string, List<CaptionTrack>>(StringComparer.Ordinal);

        internal CaptionCatalog(Stream source)
        {
            using var reader = new StreamReader(source);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var columns = line.Split('\t');
                if (columns.Length != 7 || (columns[0] != "V" && columns[0] != "A") ||
                    (columns[5] != "D" && columns[5] != "W") || columns[6].Length == 0)
                    throw new InvalidDataException("Invalid Wesley subtitle row");
                var tracks = columns[0] == "V" ? video : audio;
                long identity = long.Parse(columns[2], CultureInfo.InvariantCulture);
                if (!tracks.TryGetValue(columns[1], out var variants))
                    tracks.Add(columns[1], variants = new List<CaptionTrack>());
                var track = variants.Find(item => item.Identity == identity);
                if (track == null)
                {
                    track = new CaptionTrack(identity);
                    variants.Add(track);
                }
                var lane = columns[5] == "W" ? track.Writing : track.Dialogue;
                var start = int.Parse(columns[3], CultureInfo.InvariantCulture);
                var end = int.Parse(columns[4], CultureInfo.InvariantCulture);
                if (start < 0 || end <= start || (lane.Count > 0 && start < lane[lane.Count - 1].End))
                    throw new InvalidDataException($"Invalid or overlapping Wesley subtitle times in {columns[1]}");
                lane.Add(new CaptionCue(start, end, columns[6].Replace("\\n", "\n")));
            }
            SourceCount = audio.Count + video.Count;
        }

        internal int SourceCount { get; }

        internal CaptionTrack ForVideo(string name, ulong frames) =>
            Find(video, name, (long)frames, 0);

        internal CaptionTrack ForAudio(string name, double seconds) =>
            Find(audio, name, (long)Math.Round(seconds * 1000), 12);

        private static CaptionTrack Find(Dictionary<string, List<CaptionTrack>> catalog, string name, long identity, int tolerance)
        {
            if (!catalog.TryGetValue(name, out var variants)) return null;
            CaptionTrack closest = null;
            long smallestDifference = tolerance + 1;
            foreach (var variant in variants)
            {
                long difference = Math.Abs(variant.Identity - identity);
                if (difference == smallestDifference)
                    closest = null;
                if (difference < smallestDifference)
                {
                    closest = variant;
                    smallestDifference = difference;
                }
            }
            return closest;
        }
    }

    internal sealed class CaptionTrack
    {
        internal CaptionTrack(long identity) => Identity = identity;
        internal long Identity { get; }
        internal List<CaptionCue> Dialogue { get; } = new List<CaptionCue>();
        internal List<CaptionCue> Writing { get; } = new List<CaptionCue>();

        // Look up the current time directly so seeking, looping and re-entry do not
        // depend on a previous cursor or on how often the subtitle view was updated.
        internal static string At(List<CaptionCue> cues, double seconds)
        {
            double time = seconds * 1000;
            int first = 0, last = cues.Count;
            while (first < last)
            {
                int middle = first + (last - first) / 2;
                if (cues[middle].Start <= time) first = middle + 1;
                else last = middle;
            }
            if (first == 0) return null;
            var cue = cues[first - 1];
            return time < cue.End ? cue.Text : null;
        }
    }

    internal readonly struct CaptionCue
    {
        internal CaptionCue(int start, int end, string text)
        {
            Start = start;
            End = end;
            Text = text;
        }
        internal int Start { get; }
        internal int End { get; }
        internal string Text { get; }
    }
}
