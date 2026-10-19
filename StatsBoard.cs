using System.Globalization;

namespace HaulingTracker;

// The session leaderboard (between the pickup board and the run board) and the all-time leaderboard on the History tab.
public sealed partial class MainForm
{
    readonly DataGridView _statsGrid = new();
    readonly DataGridView _allTimeGrid = new();
    static Color TopGold => Theme.Gold;

    /// <summary>One row of a leaderboard before it is turned into text.</summary>
    sealed class StatLine
    {
        public string Name = "";
        public int Sessions;
        public WorkerStat S = new();
        public decimal Earned;
        public double Seconds;      // hours worked, for aUEC/hour
    }

    void BuildStatColumns(DataGridView grid, bool withSessions)
    {
        StyleGrid(grid);
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.Columns.Add(Col("Worker", 190, true));   // as wide as Pickup Location above
        if (withSessions) grid.Columns.Add(Col("Sessions", 80, true));
        grid.Columns.Add(Col("Completed Runs", 120, true));
        grid.Columns.Add(Col("Perfect Runs", 110, true));
        grid.Columns.Add(Col("Batches Delivered", 130, true));
        grid.Columns.Add(Col("Avg Batches/Run", 130, true));
        grid.Columns.Add(Col("Avg Run Time", 100, true));
        grid.Columns.Add(Col(withSessions ? "Earned aUEC" : "Total Run Value", 120, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col("Share of Value", 110, true));
        grid.Columns.Add(Col(withSessions ? "aUEC/Hour" : "Run Value/Hour", 120, true, DataGridViewContentAlignment.MiddleRight, true));
    }

    void BuildStatsGrid() => BuildStatColumns(_statsGrid, false);

    static string ShortDuration(TimeSpan d) => (int)d.TotalHours + ":" + d.Minutes.ToString("00");

    static string Num(decimal v) => v > 0 ? v.ToString("N0", CultureInfo.InvariantCulture) : "";

    static string PerHour(decimal earned, double seconds) =>
        earned > 0 && seconds > 0 ? Num(earned / (decimal)(seconds / 3600.0)) : "";

    /// <summary>Runs and missions per worker. Completed runs are the ones ended by a hand-in (not by hand), perfect runs are completed runs with lost cargo None.</summary>
    static Dictionary<string, WorkerStat> ComputeStats(IEnumerable<WorkerTrip> trips, SessionInputs inp)
    {
        var result = new Dictionary<string, WorkerStat>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in trips)
        {
            if (t.Worker == "Unassigned") continue;
            if (!result.TryGetValue(t.Worker, out var s)) result[t.Worker] = s = new WorkerStat();
            var missions = t.Pickups.Sum(p => p.Missions);
            if (!t.End.HasValue || t.Manual) continue;   // nothing counts until the run is done
            s.Missions += missions;
            s.Runs++;
            if (LostLevelOf(inp, t.Key) == "None") s.Perfect++;
            s.RunMissions += missions;
            s.RunSeconds += (t.End.Value - t.Start).TotalSeconds;
        }
        return result;
    }

    static bool SameStats(Dictionary<string, WorkerStat> a, Dictionary<string, WorkerStat> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var o) && o == kv.Value);

    /// <summary>The leaderboard: the highest earner on top, the total row last.</summary>
    static void WriteBoard(DataGridView grid, List<StatLine> lines, bool withSessions, int totalSessions, double totalSeconds)
    {
        grid.Rows.Clear();
        var totalValue = lines.Sum(l => l.Earned);
        var sum = new StatLine();
        var first = true;
        foreach (var l in lines.OrderByDescending(l => l.Earned).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
        {
            var avgTime = "";
            var avgMissions = "";
            if (l.S.Runs > 0)
            {
                avgTime = ShortDuration(TimeSpan.FromSeconds(l.S.RunSeconds / l.S.Runs));
                avgMissions = ((double)l.S.RunMissions / l.S.Runs).ToString("0.0", CultureInfo.InvariantCulture);
            }
            var share = totalValue > 0 && l.Earned > 0
                ? (l.Earned / totalValue * 100m).ToString("0", CultureInfo.InvariantCulture) + "%"
                : "";

            var cells = new List<object> { l.Name };
            if (withSessions) cells.Add(l.Sessions);
            cells.AddRange(new object[] { l.S.Runs, l.S.Perfect, l.S.Missions, avgMissions, avgTime, Num(l.Earned), share, PerHour(l.Earned, l.Seconds) });
            var idx = grid.Rows.Add(cells.ToArray());
            if (first && l.Earned > 0)
            {
                // the top earner: a star and a gold row
                grid.Rows[idx].Cells[0].Value = "★ " + l.Name + " ★";
                grid.Rows[idx].DefaultCellStyle.BackColor = TopGold;
                first = false;
            }

            sum.S.Runs += l.S.Runs;
            sum.S.Perfect += l.S.Perfect;
            sum.S.Missions += l.S.Missions;
            sum.S.RunMissions += l.S.RunMissions;
            sum.S.RunSeconds += l.S.RunSeconds;
            sum.Earned += l.Earned;
        }

        var total = new List<object> { "Total" };
        if (withSessions) total.Add(totalSessions);
        total.AddRange(new object[]
        {
            sum.S.Runs, sum.S.Perfect, sum.S.Missions, "", "", Num(sum.Earned), sum.Earned > 0 ? "100%" : "", PerHour(sum.Earned, totalSeconds),
        });
        var tr = grid.Rows.Add(total.ToArray());
        grid.Rows[tr].DefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
    }

    /// <summary>The leaderboard of the selected session.</summary>
    void FillStats()
    {
        _statsGrid.Rows.Clear();
        if (_inputs == null || _current == null) return;

        // the value of the finished runs per worker (the real income is the Total Revenue above)
        var carried = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in _trips.Where(t => t.End.HasValue && !t.Manual && t.Worker != "Unassigned"))
        {
            carried.TryGetValue(t.Worker, out var old);
            carried[t.Worker] = old + RunValue(t);
        }

        double seconds = 0;
        if (TryTime(_startBox.Text, out var s) && TryTime(_endBox.Text, out var e))
        {
            var d = e - s;
            if (d < TimeSpan.Zero) d += TimeSpan.FromDays(1);
            seconds = d.TotalSeconds;
        }

        var stats = ComputeStats(_trips, _inputs);
        // only workers who started a cargo run this session
        var names = new List<string>();
        foreach (var t in _trips)
        {
            if (t.Worker != "Unassigned" && !names.Any(x => SameName(x, t.Worker))) names.Add(t.Worker);
        }

        var lines = names.Select(n => new StatLine
        {
            Name = n,
            Sessions = 1,
            S = stats.FirstOrDefault(kv => SameName(kv.Key, n)).Value ?? new WorkerStat(),
            Earned = CarriedBy(carried, n),
            Seconds = seconds,
        }).ToList();
        WriteBoard(_statsGrid, lines, false, 1, seconds);
    }

    /// <summary>The leaderboard over every saved session (History tab).</summary>
    void FillAllTime()
    {
        var lines = new Dictionary<string, StatLine>(StringComparer.OrdinalIgnoreCase);
        StatLine Line(string name)
        {
            if (!lines.TryGetValue(name, out var l)) lines[name] = l = new StatLine { Name = name };
            return l;
        }
        foreach (var p in _data.Party) Line(p);

        var sessions = 0;
        double totalSeconds = 0;
        foreach (var s in _data.Sessions.Values.Where(x => x.Carried.Count > 0))
        {
            sessions++;
            var seconds = s.StartUtc.HasValue && s.EndUtc.HasValue ? Math.Max(0, (s.EndUtc.Value - s.StartUtc.Value).TotalSeconds) : 0;
            totalSeconds += seconds;

            foreach (var n in s.Members.Concat(s.Carried.Keys).Concat(s.Stats.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var l = Line(n);
                l.Sessions++;
                l.Seconds += seconds;
            }
            foreach (var kv in s.Carried) Line(kv.Key).Earned += kv.Value;
            foreach (var kv in s.Stats)
            {
                var l = Line(kv.Key);
                l.S.Runs += kv.Value.Runs;
                l.S.Perfect += kv.Value.Perfect;
                l.S.Missions += kv.Value.Missions;
                l.S.RunMissions += kv.Value.RunMissions;
                l.S.RunSeconds += kv.Value.RunSeconds;
            }
        }
        WriteBoard(_allTimeGrid, lines.Values.ToList(), true, sessions, totalSeconds);
    }
}
