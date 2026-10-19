using System.Text.Json;

namespace HaulingTracker;

public sealed class RewardRow
{
    public decimal Reward { get; set; }
    public int Missions { get; set; }
}

public sealed class RowInputs
{
    public string Worker { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Scu { get; set; } = "";
    public bool PartialPickup { get; set; }
}

/// <summary>The workers assigned to the locations before a new set of missions was accepted. Kept so the old runs keep their workers.</summary>
public sealed class WorkerEra
{
    public DateTime Until { get; set; }   // UTC: pickups before this time belong to the old assignments
    public Dictionary<string, string> Workers { get; set; } = new();   // location id -> worker
}

/// <summary>A run you ended by hand (for example when all cargo was lost).</summary>
public sealed class ManualEnd
{
    public string Worker { get; set; } = "";
    public DateTime Time { get; set; }
}

/// <summary>What one worker did in one session, saved so the all-time leaderboard works without the logs.</summary>
public sealed record WorkerStat
{
    public int Runs { get; set; }            // completed runs (ended by a hand-in)
    public int Perfect { get; set; }         // completed runs with no lost cargo
    public int Missions { get; set; }        // missions carried, also in a run still on the way
    public int RunMissions { get; set; }     // missions in the completed runs
    public double RunSeconds { get; set; }   // total time of the completed runs
    public double Distance { get; set; }     // distance travelled in the completed runs, in Gm (Ruin, the pickups, back to Ruin)
}

/// <summary>What you typed yourself for one session.</summary>
public sealed class SessionInputs
{
    public string? Start { get; set; }     // null = use the time from the log
    public string? End { get; set; }       // null = use the time from the log
    public List<RewardRow> Rewards { get; set; } = new();   // old format, no longer used
    public decimal Reward2 { get; set; }   // payout of a 2 stop contract
    public decimal Reward3 { get; set; }   // payout of a 3 stop contract
    public decimal Reward4 { get; set; }   // payout of a 4 stop contract (Master rank)
    public List<string> LostRuns { get; set; } = new();   // old format: runs ticked as "lost cargo" (counts as All)
    public Dictionary<string, string> LostCargo { get; set; } = new();   // run key -> Little or All
    public List<ManualEnd> ManualEnds { get; set; } = new();
    public Dictionary<string, RowInputs> Rows { get; set; } = new();   // key = pickup location id
    public List<WorkerEra> PastWorkers { get; set; } = new();   // assigned workers of earlier sets of missions (frozen when the table was reset)
    public DateTime? WorkersResetAt { get; set; }               // when the last set of missions began: the assigned workers were cleared then
    public Dictionary<string, decimal> Carried { get; set; } = new();   // worker -> value of the locations he carried (saved for the Debts tab)
    public Dictionary<string, decimal> RunValues { get; set; } = new();   // worker -> value of his finished runs (Carried = the part whose missions are completed)
    public Dictionary<string, decimal> FailedValues { get; set; } = new();   // worker -> part of the run value that is known to be lost (failed missions, runs marked as failed)
    public List<string> MissionIds { get; set; } = new();
    public List<string>? Party { get; set; }   // who was in the party this session (null = everyone who carried something), set with "Session party…"
    public DateTime? StartUtc { get; set; }   // saved with the session so the History tab works without the logs
    public DateTime? EndUtc { get; set; }
    public int MissionCount { get; set; }
    public decimal Revenue { get; set; }
    public Dictionary<string, WorkerStat> Stats { get; set; } = new();   // worker -> runs and missions of the session (for the History leaderboard)
    public List<string> Members { get; set; } = new();   // the party of the session as used for the split (saved with Carried)   // missions of the session, to avoid counting a session twice
}

/// <summary>A money transfer found in a log. Payment = ticked by you as paying off a debt.</summary>
public sealed class SavedTransfer
{
    public DateTime Time { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public decimal Amount { get; set; }
    public bool Payment { get; set; }
}

public sealed class AppData
{
    public string? GameLogPath { get; set; }
    public List<string> Party { get; set; } = new() { "zzoega", "zzatvo", "bildog123" };
    public List<string> InactiveParty { get; set; } = new();   // party members that are not shown in the party list of the Session tab
    public bool DarkMode { get; set; }   // the dark look
    public string? Channel { get; set; }   // Auto, LIVE, HOTFIX...
    public Dictionary<string, int> ShipCapacity { get; set; } = new();   // worker -> batches his ship holds (the Trip Planner), 0 = not flying
    public List<string> WatchPaths { get; set; } = new();   // Game.log files to watch live, also on other computers
    public List<string> DisabledWatchPaths { get; set; } = new();   // sources that are saved but switched off (unticked in Sources)
    public Dictionary<string, string> LocationNames { get; set; } = new();   // location id -> your name
    public Dictionary<string, SessionInputs> Sessions { get; set; } = new();
    public List<SavedTransfer> Transfers { get; set; } = new();
    public Dictionary<string, decimal> PairAdjust { get; set; } = new();   // "a|b" (a before b) -> amount b owes a, typed by you
}

public static class Storage
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HaulingTracker");

    // A small file next to the program's settings says where the data file is (for example a folder in OneDrive).
    static string PointerFile => Path.Combine(DefaultDir, "location.txt");

    static string? _path;

    /// <summary>Full path of the data file in use.</summary>
    public static string FilePath
    {
        get
        {
            if (_path != null) return _path;
            try
            {
                Directory.CreateDirectory(DefaultDir);
                if (File.Exists(PointerFile))
                {
                    var p = File.ReadAllText(PointerFile).Trim();
                    if (p.Length > 0)
                    {
                        _path = p;
                        return _path;
                    }
                }
            }
            catch (Exception)
            {
                // fall back to the normal place
            }
            _path = Path.Combine(DefaultDir, "data.json");
            return _path;
        }
    }

    /// <summary>Use another data file from now on. It is remembered the next time the program starts.</summary>
    public static void SetLocation(string path)
    {
        Directory.CreateDirectory(DefaultDir);
        File.WriteAllText(PointerFile, path);
        _path = path;
    }

    /// <summary>Raised with a short text when the data file could not be saved (for example a OneDrive folder that is not available).</summary>
    public static event Action<string>? SaveFailed;

    public static AppData Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(FilePath), Options);
                if (data != null) return data;
            }
        }
        catch (Exception)
        {
            // A damaged settings file should never stop the program from starting. Keep a copy so nothing is lost.
            try { File.Copy(FilePath, FilePath + ".broken", true); } catch (Exception) { }
        }
        return new AppData();
    }

    public static void Save(AppData data)
    {
        try
        {
            var path = FilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // write to a temporary file first, so a sync client never sees a half written file
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
            File.Move(tmp, path, true);
        }
        catch (Exception ex)
        {
            SaveFailed?.Invoke(ex.Message);
        }
    }
}
