using Acme.Jieba;
using ToolGood.Words.Pinyin;

namespace SimulatedTyping;

internal enum UnitKind { Hanzi, Direct, Newline }

internal sealed record TypingUnit(string Text, string Pinyin, UnitKind Kind);

internal sealed record TypingOptions(
    bool UseSpeedRange, int FixedSpeed, int MinSpeed, int MaxSpeed,
    int ErrorRate, bool NativePinyin, int NewlineMode,
    int SentencePauseRate, int ParagraphPauseRate, int SaveWaitSeconds);

internal static class TypingPlan
{
    public static IReadOnlyList<TypingUnit> Build(string source)
    {
        var normalized = source.Replace("\r\n", "\n").Replace('\r', '\n');
        var result = new List<TypingUnit>();
        for (var i = 0; i < normalized.Length;)
        {
            if (normalized[i] == '\n')
            {
                result.Add(new TypingUnit("\n", "", UnitKind.Newline));
                i++;
                continue;
            }
            if (IsHan(normalized[i]))
            {
                var start = i;
                while (i < normalized.Length && IsHan(normalized[i])) i++;
                var run = normalized[start..i];
                var words = JiebaSegmenter.Default.Cut(run).ToArray();
                if (string.Concat(words) != run) words = [run];
                foreach (var word in words)
                {
                    var pinyin = WordsHelper.GetPinyin(word, ",").ToLowerInvariant();
                    var syllables = pinyin.Split(',');
                    var supported = syllables.Length == word.Length &&
                                    syllables.All(s => s.Length > 0 && s.All(c => c is >= 'a' and <= 'z'));
                    result.Add(new TypingUnit(word, supported ? pinyin : "", supported ? UnitKind.Hanzi : UnitKind.Direct));
                }
                continue;
            }
            // Keep punctuation and Latin characters separate so their delays remain visible.
            result.Add(new TypingUnit(normalized[i].ToString(), "", UnitKind.Direct));
            i++;
        }
        return result;
    }

    private static bool IsHan(char c) => c is >= '\u3400' and <= '\u9fff';
}
