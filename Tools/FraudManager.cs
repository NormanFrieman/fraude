using System.Collections.Immutable;
using System.Numerics;
using Fraude.Models;

namespace Fraude.Tools;
public static class FraudManager
{
    public static void CalcTopRegisters(ImmutableArray<VectorBase> vectorsBase, float[] values, TopScores topScores)
    {
        foreach (var (vector, isFraud) in vectorsBase)
        {
            var dist = Euclidiana(vector, values);
            topScores.Add(dist, isFraud);
        }
    }

    private static double Euclidiana(float[] vector, float[] values)
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

        return Math.Sqrt(sum);
    }

    public static (bool, float) Detect(TopScores topScores)
    {
        var fraudScore = topScores.GetFraudScore();
        return (fraudScore < 0.6, fraudScore);
    }
}