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

/// <summary>A run that was ended by hand (all cargo lost), or (Left) one that stopped because the worker left the party.</summary>
public sealed class ManualEnd
{
    public string Worker { get; set; } = "";
    public DateTime Time { get; set; }
    public bool Left { get; set; }   // ended because he left the party: the run just stops, it is not a failed run
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

/// <summary>A session you started (and stopped) by hand: it reads the logs from the start to the stop, or to now while it runs. It can go on for days.</summary>
public sealed class ManualSession
{
    public DateTime StartUtc { get; set; }
    public DateTime? StopUtc { get; set; }   // null = still running
}

/// <summary>Someone joined (Active) or left the party at this time. Kept so mission payouts are split between who was there when they paid out.</summary>
public sealed class PartyChange
{
    public string Name { get; set; } = "";
    public DateTime TimeUtc { get; set; }
    public bool Active { get; set; }
}

/// <summary>One stop of a locked trip: the station (see PyroDistances) and the batches taken there.</summary>
public sealed class LockedStop
{
    public string Id { get; set; } = "";
    public int Batches { get; set; }
    public DateTime? Since { get; set; }   // set when the lock was made while the worker was flying a run: the time of his last pickup then (a pickup after it at another station ends the lock)
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
    public Dictionary<string, string> PickupClaims { get; set; } = new();   // "mission|objective" -> the worker the Trip Planner credited a pickup to (only where no log names who did it)
    public bool NextStopsLocked { get; set; }   // the "Next Stops: Locked" button of the Trip Planner: only while it is on is the next stop of everybody kept for him (off = the plan is free)
    public Dictionary<string, List<LockedStop>> LockedTrips { get; set; } = new();   // worker -> his Current trip: the Trip Planner leaves it as it is (set by Assign Workers to Stops or by assigning stops by hand)
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
    public Dictionary<string, decimal> Received { get; set; } = new();   // worker -> his share of the mission payouts (split between the party present when each mission paid out)
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

/// <summary>A completed mission and how its payout was shared. Kept so the Mission Payouts list of the Debts tab survives the logs.</summary>
public sealed class PayoutRecord
{
    public string MissionId { get; set; } = "";
    public DateTime Time { get; set; }          // UTC: when the mission completed (paid out)
    public string Contract { get; set; } = "";  // for example "Potassium, 3 stops"
    public decimal Payout { get; set; }
    public List<string> Members { get; set; } = new();   // who was active and shared it
    public decimal Each { get; set; }
}

/// <summary>Where a worker starts his next trip when he is not at Ruin Station (set on the Trip Planner). It ends when he picks up or hands in something.</summary>
public sealed class StartPoint
{
    public string Id { get; set; } = "";
    public DateTime Since { get; set; }   // UTC
}

public sealed class AppData
{
    public string? GameLogPath { get; set; }
    public List<string> Party { get; set; } = new() { "zzoega", "zzatvo", "bildog123" };
    public List<PartyChange> PartyChanges { get; set; } = new();   // when each member joined or left (Active / inactive)
    public DateTime? PartyTrackingSince { get; set; }   // payouts of missions that ended before this time are split the old way (the whole session party)
    public List<string> InactiveParty { get; set; } = new();   // party members that are not shown in the party list of the Session tab
    public DateTime? PlanCreditSince { get; set; }   // from this time on the runs get their workers from the logs and the Trip Planner, not from Assigned Worker (earlier pickups keep the old rule)
    public bool DarkMode { get; set; }   // the dark look
    public string? Channel { get; set; }   // Auto, LIVE, HOTFIX...
    public List<ManualSession> ManualSessions { get; set; } = new();   // started and stopped with the Start Session / Stop Session buttons
    public bool WebView { get; set; }      // the read-only live web view is switched on
    public int WebViewPort { get; set; } = 8765;   // the port of the live web view (on this computer only)
    public Dictionary<string, string> Ship { get; set; } = new();   // worker -> his ship (Ships name, like "C2 Hercules Starlifter"), for the SCU his ship holds
    public Dictionary<string, string> QuantumDrive { get; set; } = new();   // worker -> the quantum drive in his ship (QuantumDrives key, like "Eos S1"), for the travel time estimates
    public Dictionary<string, int> WorkerColors { get; set; } = new();   // worker -> colour (ARGB) on the Trip Planner Overview, picked in the Party table
    public Dictionary<string, int> ShipCapacity { get; set; } = new();   // worker -> batches his ship holds (the Trip Planner), 0 = not flying
    public List<string> WatchPaths { get; set; } = new();   // Game.log files to watch live, also on other computers
    public List<string> DisabledWatchPaths { get; set; } = new();   // sources that are saved but switched off (unticked in Sources)
    public Dictionary<string, string> LocationNames { get; set; } = new();   // location id -> your name
    public Dictionary<string, SessionInputs> Sessions { get; set; } = new();
    public List<SavedTransfer> Transfers { get; set; } = new();
    public List<PayoutRecord> Payouts { get; set; } = new();   // the completed missions and who shared their payout
    public Dictionary<string, Dictionary<string, bool>> MissionHolders { get; set; } = new();   // mission id -> member -> has the mission (ticked on the Contracts tab, wins over what the logs say)
    public Dictionary<string, decimal> PayoutOverrides { get; set; } = new();   // mission id -> what it really paid (typed in the Mission Payouts list), instead of the reward of its contract
    public Dictionary<string, DateTime> DroppedMissions { get; set; } = new();   // mission id -> when it was marked as gone on the Contracts tab (treated like an abandoned mission)
    public Dictionary<string, StartPoint> StartPoints { get; set; } = new();   // worker -> where he starts his next trip
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

    /// <summary>The Backups folder next to the data file.</summary>
    public static string BackupDir => Path.Combine(Path.GetDirectoryName(FilePath) ?? DefaultDir, "Backups");

    const int BackupsToKeep = 30;

    /// <summary>
    /// A dated copy of the data file (data-2026-10-20.json) made the first time the program starts each day, so it is the state
    /// from before you played today. The newest 30 copies are kept. A problem here never stops the program.
    /// </summary>
    static void Backup()
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length == 0) return;
            Directory.CreateDirectory(BackupDir);
            var target = Path.Combine(BackupDir, "data-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json");
            if (!File.Exists(target)) File.Copy(FilePath, target);
            foreach (var old in Directory.GetFiles(BackupDir, "data-*.json").OrderByDescending(f => f, StringComparer.Ordinal).Skip(BackupsToKeep))
                File.Delete(old);
        }
        catch (Exception)
        {
            // no backup today, the data itself is untouched
        }
    }

    public static AppData Load()
    {
        Backup();
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
