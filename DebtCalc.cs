namespace HaulingTracker;

/// <summary>Who owes whom. net &gt; 0 means others owe that person, net &lt; 0 means he owes others.</summary>
public static class DebtCalc
{
    /// <summary>
    /// Every saved session: the rewards are split equally between everyone in the party, but every worker is only
    /// entitled to the value he carried. The difference is what he is owed (or owes).
    /// Payments (ticked transfers) move money from the sender to the receiver.
    /// </summary>
    public static Dictionary<string, decimal> Nets(IEnumerable<SessionInputs> sessions, IEnumerable<SavedTransfer> transfers)
    {
        var net = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, decimal value)
        {
            net.TryGetValue(name, out var old);
            net[name] = old + value;
        }

        foreach (var s in sessions)
        {
            if (s.Carried.Count == 0) continue;
            if (s.Received.Count > 0)
            {
                // every payout was split between the party present at that moment: you are owed what you carried and you owe what you received
                foreach (var k in s.Carried.Keys.Concat(s.Received.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var carried = s.Carried.Where(kv => Same(kv.Key, k)).Sum(kv => kv.Value);
                    var got = s.Received.Where(kv => Same(kv.Key, k)).Sum(kv => kv.Value);
                    Add(k, carried - got);
                }
                continue;
            }
            var members = new List<string>(s.Members);
            foreach (var k in s.Carried.Keys)
            {
                if (!members.Any(x => Same(x, k))) members.Add(k);
            }
            var total = s.Carried.Values.Sum();
            var share = total / members.Count;
            foreach (var m in members)
            {
                var carried = s.Carried.Where(kv => Same(kv.Key, m)).Sum(kv => kv.Value);
                Add(m, carried - share);
            }
        }

        foreach (var t in transfers)
        {
            if (!t.Payment) continue;
            Add(t.From, t.Amount);    // the sender has paid, so he is owed more (or owes less)
            Add(t.To, -t.Amount);
        }
        return net;
    }

    /// <summary>Fewest payments that settle everything: the biggest debtor pays the biggest creditor first.</summary>
    public static List<(string Debtor, string Creditor, decimal Amount)> Settle(Dictionary<string, decimal> nets)
    {
        var debtors = nets.Where(kv => kv.Value < -0.5m)
            .Select(kv => (Name: kv.Key, Left: -kv.Value))
            .OrderByDescending(x => x.Left).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var creditors = nets.Where(kv => kv.Value > 0.5m)
            .Select(kv => (Name: kv.Key, Left: kv.Value))
            .OrderByDescending(x => x.Left).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var res = new List<(string Debtor, string Creditor, decimal Amount)>();
        var i = 0;
        var j = 0;
        while (i < debtors.Count && j < creditors.Count)
        {
            var d = debtors[i];
            var c = creditors[j];
            var amount = Math.Min(d.Left, c.Left);
            res.Add((d.Name, c.Name, amount));
            debtors[i] = (d.Name, d.Left - amount);
            creditors[j] = (c.Name, c.Left - amount);
            if (debtors[i].Left <= 0.5m) i++;
            if (creditors[j].Left <= 0.5m) j++;
        }
        return res;
    }

    /// <summary>Balance of "me" with "other": positive = other owes me, negative = I owe other.</summary>
    public static decimal Between(List<(string Debtor, string Creditor, decimal Amount)> settle, string me, string other)
    {
        decimal v = 0;
        foreach (var s in settle)
        {
            if (Same(s.Creditor, me) && Same(s.Debtor, other)) v += s.Amount;
            else if (Same(s.Debtor, me) && Same(s.Creditor, other)) v -= s.Amount;
        }
        return v;
    }

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
