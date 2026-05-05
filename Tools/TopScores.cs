// ReSharper disable TailRecursiveCall
namespace Fraude.Tools;

public class TopScores
{
    private (double, string)[] _scores = new (double, string)[5];
    private int _length = 0;
    
    public void Add(double score, string label)
    {
        if (_length == 4 && score > _scores[4].Item1)
            return;

        _length = AddOrder(score, label, 0);
    }

    public (double, string)[] Get()
    {
        return _scores;
    }

    public void Clear()
    {
        _scores = new (double, string)[5];
        _length = 0;
    }
    
    private int AddOrder(double score, string label, int i)
    {
        if (i > 4)
            return 4;
        
        var (scoreItem, labelItem) = _scores[i];
        if (scoreItem == 0 && string.IsNullOrEmpty(labelItem))
        {
            _scores[i] = (score, label);
            return i;
        }

        if (score >= scoreItem)
            return AddOrder(score, label, i + 1);
        
        _scores[i] = (score, label);
        return AddOrder(scoreItem, labelItem, i+1);
    }
}