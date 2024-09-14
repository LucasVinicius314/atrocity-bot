namespace atrocity_bot
{
  class ChainEntry
  {
    required public List<MatchCondition> conditions;
    required public Dictionary<string, int> next;
    required public string token;
    required public int total;

    public string PickNext()
    {
      if (total == 0)
      {
        return "";
      }

      var index = new Random().Next(total);

      foreach (var condition in conditions)
      {
        if (index >= condition.min && index < condition.max)
        {
          return condition.token;
        }
      }

      return "";
    }
  }
}
