// ReSharper disable TailRecursiveCall
namespace Fraude.Tools;

public class TopScores
{
    private (double, bool)[] _scores = new(double, bool) [5];
    private int _length = 0;
    
    public void Add(double score, bool isFraud)
    {
        if (_length == 4 && score > _scores[4].Item1)
            return;

        _length = AddOrder(score, isFraud, 0);
    }

    public float GetFraudScore()
    {
        float frauds = 0;
        for (var i = 0; i < 5; i++)
        {
            if (_scores[i].Item2)
                frauds++;
        }

        return frauds / 5;
    }
    
    private int AddOrder(double score, bool isFraud, int i)
    {
        if (i > 4)
            return 4;
        
        if (i > _length)
        {
            _scores[i] = (score, isFraud);
            return i;
        }

        var (scoreItem, isFraudItem) = _scores[i];
        if (score >= scoreItem)
            return AddOrder(score, isFraud, i + 1);
        
        _scores[i] = (score, isFraud);
        return AddOrder(scoreItem, isFraudItem, i+1);
    }
}