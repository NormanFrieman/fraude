using System.Runtime.CompilerServices;

namespace Fraude.Tools;

public static class FraudManager
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (bool, float) Detect(ReadOnlySpan<byte> labels, int length)
    {
        if (length == 0)
            return (true, 0f);

        var frauds = 0;
        for (var i = 0; i < length; i++)
        {
            if (labels[i] == 1)
                frauds++;
        }

        var fraudScore = (float)frauds / length;
        return (fraudScore < 0.6, fraudScore);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Add(long distance, byte label, Span<long> distances, Span<byte> labels, ref int length)
    {
        if (length == 5 && distance >= distances[4])
            return;

        var pos = 0;
        while (pos < length && pos < 5 && distance >= distances[pos])
            pos++;

        var end = length < 5 ? length : 4;
        for (var i = end; i > pos; i--)
        {
            distances[i] = distances[i - 1];
            labels[i] = labels[i - 1];
        }

        distances[pos] = distance;
        labels[pos] = label;
        if (length < 5)
            length++;
    }
}
