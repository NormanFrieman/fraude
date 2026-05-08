using System.Runtime.CompilerServices;

namespace Fraude.Tools;

public static class FraudManager
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (bool, float) Detect(ReadOnlySpan<(float, bool)> scores, int length)
    {
        var frauds = 0;
        for (var i = 0; i < length; i++)
        {
            if (scores[i].Item2)
                frauds++;
        }

        var fraudScore = (float)frauds / length;
        return (fraudScore < 0.6, fraudScore);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Add(float score, bool isFraud, Span<(float, bool)> scores, ref int length)
    {
        if (length == 5 && score >= scores[4].Item1)
            return;

        var pos = 0;
        while (pos < length && pos < 5 && score >= scores[pos].Item1)
            pos++;

        var end = length < 5 ? length : 4;
        for (var i = end; i > pos; i--)
            scores[i] = scores[i - 1];

        scores[pos] = (score, isFraud);
        if (length < 5)
            length++;
    }
}