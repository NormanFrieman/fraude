// ReSharper disable TailRecursiveCall
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fraude.Models;

namespace Fraude.Tools;
public static class FraudManager
{
    public static void CalcTopRegisters(ImmutableArray<VectorBase> vectorsBase, float[] values, Span<(float, bool)> scores, ref int length)
    {
        foreach (var (vector, isFraud) in vectorsBase)
        {
            var dist = Euclidiana(vector, values);
            Add(dist, isFraud, scores, ref length);
        }
    }

    private static float Euclidiana(float[] vector, float[] values)
    {
        var i = 0;
        var simdLen = Vector<float>.Count;

        float sum = 0;
        for (; i < vector.Length - simdLen; i += simdLen)
        {
            var va = new Vector<float>(vector, i);
            var vb = new Vector<float>(values, i);
            var sub = Vector.Subtract(va, vb);
            var res = Vector.Multiply(sub, sub);

            sum += Vector.Sum(res);
        }

        for (; i < vector.Length; i++)
        {
            var sub = vector[i] - values[i];
            var dot = sub * sub;
            sum += dot;
        }

        return (float)Math.Sqrt(sum);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (bool, float) Detect(Span<(float, bool)> scores)
    {
        var fraudScore = GetFraudScore(scores);
        return (fraudScore < 0.6, fraudScore);
    }

    private static void Add(float score, bool isFraud, Span<(float, bool)> scores, ref int length)
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
        if (i > 4)
            return 4;
        
        if (i > length)
        {
            scores[i] = (score, isFraud);
            return i;
        }

        var (scoreItem, isFraudItem) = scores[i];
        if (score >= scoreItem)
            return AddOrder(score, isFraud, i + 1, scores, length);
        
        scores[i] = (score, isFraud);
        return AddOrder(scoreItem, isFraudItem, i+1, scores, length);
    }
}