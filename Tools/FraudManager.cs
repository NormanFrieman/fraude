using System.Collections.Immutable;
using System.Numerics;
using Fraude.Models;

namespace Fraude.Tools;
public class FraudManager
{
    private readonly TopScores _topScores = new();

    public void CalcTopRegisters(ImmutableArray<VectorBase> vectorsBase, float[] values)
    {
        foreach (var (vector, label) in vectorsBase)
        {
            var dist = Euclidiana(vector, values);
            _topScores.Add(dist, label);
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

    public (bool, float) Detect()
    {
        // foreach (var register in _topRegisters)
        // {
        //     Console.WriteLine(register.Key);
        // }
        var scores = _topScores.Get();
        float frauds = scores
            .Select(x => x.Item2)
            .Count(x => x.Equals("fraud"));
        
        _topScores.Clear();
        return ((frauds / 5) < 0.6, (frauds / 5));
    }
}