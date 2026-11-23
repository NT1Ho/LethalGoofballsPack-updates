using System;
using System.Collections.Generic;
using System.Globalization;

namespace WesleyImageReader
{
    internal static class LineBreaks
    {
        const string NoStart = "，。、．？！：；）］｝〉》」』】〕〗〙〛’”％…—,.;:!?)]}%";
        const string NoEnd = "（［｛〈《「『【〔〖〘〚‘“([{";

        internal static List<string> Wrap(string text, float width, Func<string, float> measure)
        {
            var lines = new List<string>();
            var starts = StringInfo.ParseCombiningCharacters(text);
            int count = starts.Length;
            int start = 0;
            while (start < count)
            {
                if (char.IsWhiteSpace(text[starts[start]])) { start++; continue; }
                int natural = start, emergency = start, end;
                for (end = start + 1; end <= count; end++)
                {
                    int limit = end == count ? text.Length : starts[end];
                    if (measure(text.Substring(starts[start], limit - starts[start]).TrimEnd()) > width) break;
                    if (!PunctuationAllows(text, limit)) continue;
                    emergency = end;
                    if (limit == text.Length || !CodeCharacter(text[limit - 1]) || !CodeCharacter(text[limit]))
                        natural = end;
                }
                int next = natural > start ? natural : emergency;
                if (next == start)
                {
                    // Keep inseparable punctuation together even if a window is smaller than one token.
                    next = Math.Min(end, count);
                    while (next < count && !PunctuationAllows(text, starts[next])) next++;
                }
                int finish = next == count ? text.Length : starts[next];
                lines.Add(text.Substring(starts[start], finish - starts[start]).TrimEnd());
                start = next;
            }
            return lines;
        }

        private static bool PunctuationAllows(string text, int limit)
        {
            if (limit == text.Length) return true;
            int right = limit;
            while (right < text.Length && char.IsWhiteSpace(text[right])) right++;
            if (right == text.Length) return true;
            int left = limit - 1;
            while (left >= 0 && char.IsWhiteSpace(text[left])) left--;
            return left < 0 || (NoEnd.IndexOf(text[left]) < 0 && NoStart.IndexOf(text[right]) < 0);
        }

        private static bool CodeCharacter(char c) =>
            (c < 128 && (char.IsLetterOrDigit(c) || "_:#.,/@%+-\\'".IndexOf(c) >= 0)) || c == '·' || c == '−' || c == '\u00a0';
    }
}
