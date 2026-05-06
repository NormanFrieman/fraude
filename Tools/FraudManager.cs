using System.Numerics;
using System.Runtime.CompilerServices;

namespace Fraude.Tools;
public static class FraudManager
{
    public static float Euclidean(ReadOnlySpan<float> vector, ReadOnlySpan<float> values)
    {
        var i = 0;
        var simdLen = Vector<float>.Count;

        float sum = 0;
        for (; i < vector.Length - simdLen; i += simdLen)
        {
            var va = Vector.Create(vector[i..]);
            var vb = Vector.Create(values[i..]);
            var sub = va - vb;
            sum += Vector.Dot(sub, sub);
        }

        for (; i < vector.Length; i++)
        {
            var sub = vector[i] - values[i];
            sum += sub * sub;
        }

        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (bool, float) Detect(Span<(float, bool)> scores)
    {
        var fraudScore = GetFraudScore(scores);
        return (fraudScore < 0.6, fraudScore);
    }

    public static void Add(float score, bool isFraud, Span<(float, bool)> scores, ref int length)
    {
        if (length == 4 && score > scores[4].Item1)
            return;

        length = AddOrder(score, isFraud, 0, scores, length);
    }

    private static float GetFraudScore(Span<(float, bool)> scores)
    {
        float frauds = 0;
        for (var i = 0; i < 5; i++)
        {
            if (scores[i].Item2)
                frauds++;
        }

        return frauds / 5;
    }

    private static int AddOrder(float score, bool isFraud, int i, Span<(float, bool)> scores, int length)
    {
        while (true)
        {
            if (i > 4)
                return 4;

            if (i > length)
            {
                scores[i] = (score, isFraud);
                return i;
            }

            var (scoreItem, isFraudItem) = scores[i];
            if (score >= scoreItem)
            {
                i += 1;
                continue;
            }

            scores[i] = (score, isFraud);
            score = scoreItem;
            isFraud = isFraudItem;
            i += 1;
        }
    }
}