namespace Fraude;

public static class FraudManager
{
    private static readonly SortedSet<(double dist, string label)> TopRegisters = [];

    public static void CalcTopRegisters(VectorBase[] vectorsBase, float[] values)
    {
        TopRegisters.Clear();
        foreach (var (vector, label) in vectorsBase)
        {
            double sum = 0;
            for (var i = 0; i < 14; i++)
            {
                sum += Math.Pow(vector[i] - values[i], 2);
            }

            var dist = Math.Sqrt(sum);
            StoreTopRegisters(dist, label);
        }
    }

    private static void StoreTopRegisters(double dist, string label)
    {
        TopRegisters.Add((dist, label));
        
        if (TopRegisters.Count > 5)
            TopRegisters.Remove(TopRegisters.Max);
    }

    public static (bool, float) Detect()
    {
        float frauds = TopRegisters.Count(x => x.label.Equals("fraud"));
        return ((frauds / 5) < 0.6, (frauds / 5));
    }
}