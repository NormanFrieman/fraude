using Fraude.Models;

namespace Fraude.Tools;

public class FraudManager
{
    private readonly SortedList<double, string> _topRegisters = [];

    public void CalcTopRegisters(VectorBase[] vectorsBase, float[] values)
    {
        foreach (var (vector, label) in vectorsBase)
        {
            double sum = 0;
            for (var i = 0; i < 14; i++)
            {
                var sub = vector[i] - values[i];
                var dot = sub * sub;
                sum += dot;
            }
            var dist = Math.Sqrt(sum);
            StoreTopRegisters(dist, label);
        }
    }

    private void StoreTopRegisters(double dist, string label)
    {
        _topRegisters.Add(dist, label);

        if (_topRegisters.Count > 5)
            _topRegisters.RemoveAt(4);
    }

    public (bool, float) Detect()
    {
        float frauds = _topRegisters.Count(x => x.Value.Equals("fraud"));
        return ((frauds / 5) < 0.6, (frauds / 5));
    }
}