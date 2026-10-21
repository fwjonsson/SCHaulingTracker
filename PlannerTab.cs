using System.Globalization;

namespace HaulingTracker;

// The Trip Planner tab. It divides the open batches over the crew (the active party, each with a Batch Capacity) and shows
// the trips on two kinds of pages: an Overview with everybody, and a page for every crew member. A page has a map of Pyro with
// the planned trips and a table: the runs so far (coloured like Cargo Runs) followed by the planned trips.
public sealed partial class MainForm
{
    const int DefaultShipCapacity = 3;
    const int MaxShipCapacity = 24;

    /// <summary>The colour of a worker on the Overview: the one picked in the Party table, else zzoega orange, zzatvo green, the others one of the palette.</summary>
    Color WorkerColor(string name)
    {
        var picked = _data.WorkerColors.FirstOrDefault(kv => SameName(kv.Key, name));
        if (picked.Key != null) return Color.FromArgb(picked.Value);
        if (SameName(name, "zzoega")) return Color.FromArgb(255, 150, 40);
        if (SameName(name, "zzatvo")) return Color.FromArgb(60, 205, 95);
        var index = Math.Max(0, _data.Party.FindIndex(x => SameName(x, name)));
        return MapPalette[index % MapPalette.Length];
    }

    /// <summary>A trip that is not the next one: the worker's colour, half faded into the table colour.</summary>
    static Color Faded(Color c) => Color.FromArgb(
        (c.R * 55 + Theme.Cell.R * 45) / 100, (c.G * 55 + Theme.Cell.G * 45) / 100, (c.B * 55 + Theme.Cell.B * 45) / 100);
    // routes on the map: colours that cannot be mistaken for the run colours (green, orange, red, yellow)
    static readonly Color[] MapPalette =
    {
        Color.FromArgb(70, 130, 255), Color.FromArgb(210, 70, 180), Color.FromArgb(0, 170, 160),
        Color.FromArgb(140, 100, 220), Color.FromArgb(205, 160, 100), Color.FromArgb(90, 190, 230),
    };

    /// <summary>One line of a table: a run that is done (or on the way), or a planned trip.</summary>
    sealed class PlanRow
    {
        public string Label = "";
        public string Route = "";
        public string Batches = "";
        public int BatchCount;           // batches picked up or planned
        public double Gm;
        public double Seconds;           // a run: the time it really took; a planned trip: the estimated flight time
        public string Level = "plan";    // done, little, all, active, plan
        public List<string> Stops = new();   // station ids in flying order
        public PlannedTrip? Trip;
        public bool Locked;              // a planned trip that is locked to its worker (his Current)
        public bool First;               // the trip the worker is flying right now (his run in progress, or his first planned trip when he has none): the whole row has his colour
        public decimal Value;            // a run: what its batches are worth; a planned trip: the batches at each stop times the average value of a batch there
        public bool OnMap;               // a run in progress that is drawn on the map by itself (Ruin, its pickups, back to Ruin)
        public bool Hidden;              // a planned trip that is shown inside the Current row (the continuation of the run in progress), not as a row of its own
        public double Flight;            // the estimated flying time of the whole route (Ruin, the stops, back to Ruin) with the drive of the worker
        public bool Solid;               // a planned trip that is flown first (solid line on the map); the later ones are dotted
        public int Passed;               // a run that is drawn on the map by itself: how many of its stops are done (the waves run on the leg after them)
        public string? From;             // a trip that continues a run in progress starts at this station
        public List<StopLine> Lines = new();   // the route one stop at a time (the Current Route table)
        public int Here = -1;            // the line where the worker is now (his Current location): the worker colour follows it
        public bool IsPlan => Level == "plan";
    }

    /// <summary>One line of the Current Route table: a stop of the route, what is done there, and the leg that leads to it.</summary>
    sealed class StopLine
    {
        public string Id = "";           // station id ("" = a place that is not in the distance table)
        public string Name = "";
        public List<string> Actions = new();   // Collect ×2, Drop Waste, Collected ×1 ✓ ... (the Drop/Collect column)
        public bool Stop = true;         // false: the start or the end at Ruin (or where he starts from): not counted as a stop
        public bool Done;                // collected already (it is on board)
        public bool Busy;                // done here, but he is still at it (Collecting, Dropping: he has not set his next destination yet)
        public int Batches;              // batches collected, or still to collect, here
        public decimal Value;
        public double Gm;                // the leg from the line before it
        public double Seconds;           // the estimated flying time of that leg
        public List<string> Loads = new();   // the Cargo column: a load that goes out from Ruin or is dropped here, with the containers it is cut into (152 SCU: 4×32 + 24)
        public List<string> Unknown = new();   // drop-offs on this line whose SCU nobody knows ("mission id|objective id"): clicking the Cargo cell lets you type it
        public double Scu;               // SCU picked up here: a guess, the log only knows the SCU of a whole mission, so every stop of it counts as an equal share
        public string ActionText => string.Join(" | ", Actions);
        public string CargoText => string.Join(" | ", ShowPickupScu && Scu > 0 ? Loads.Append(ScuGuess(Scu)) : Loads);
    }

    /// <summary>What was done at one line of a run in progress (for the text of its Drop/Collect column).</summary>
    sealed class DoneAt
    {
        public int Taken;                  // batches of waste taken out at Ruin
        public string? TakenCargo;
        public int Delivered;              // batches of waste delivered here
        public List<int> DeliveredScu = new();   // the SCU of every one of them (0 = not known)
        public string? DeliveredCargo;
        public int Picked;                 // batches picked up here
        public DateTime Last;              // when the latest of these was done (UTC)
    }

    sealed class PlanWorkerView
    {
        public string Name = "";
        public int Capacity;
        public string DriveNote = "";    // only when no drive is set
        public string Drive = "";        // the quantum drive, or that none is set
        public Color Color;
        public int RunsSoFar;            // all his runs of the session, also the finished ones that are no longer listed in his table
        public string LocationId = PyroDistances.Ruin;   // his Current location (station id): where he is now
        public bool LocationByHand;      // set in the drop-down (the logs take over again at his next pickup, delivery or hand-in)
        public string AutoLocationId = PyroDistances.Ruin;   // where the logs say he is
        public List<PlanRow> Rows = new();
        public List<PlanRow> Planned => Rows.Where(r => r.IsPlan).ToList();
        public List<PlanRow> MapRows => Rows.Where(r => r.IsPlan || r.OnMap).ToList();   // the lines on the map: the planned trips, and the run in progress when the plan adds nothing to it
    }

    sealed class PlanView
    {
        public string Signature = "";
        public string Summary = "";
        public string Note = "";
        public List<PlanWorkerView> Workers = new();
    }

    sealed class PlanCore
    {
        public string Key = "";
        public List<PlannedTrip> Trips = new();
        public int Batches;
        public string Note = "";
        public HashSet<string> Settled = new();   // workers whose next trip has no pickup stop to lock (drop-off first, or nothing to do): no reason to plan again just for them
    }

    // the button sits in the top bar of the window, level with Options and Stop Session, while the Trip Planner tab is shown
    readonly ToolStripButton _lockBtn = new() { DisplayStyle = ToolStripItemDisplayStyle.Text, Text = "Next Stops: Unlocked", Padding = new Padding(8, 0, 8, 0), Visible = false };
    readonly Label _planNote = new();
    TableLayoutPanel? _planPage;                   // the whole Trip Planner page (the row of the map and the tables is sized to fit everybody)
    Action? _fitPlanPage;                          // sizes that row for the page shown now
    readonly Panel _planBody = new();              // the page: the map and the tables (rebuilt when the plan changes)
    string _planShown = "";                        // what the pages show now, so they are only rebuilt when it changes
    PlanCore? _planCore;

    Control BuildPlannerPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12), AutoScroll = true };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // a warning, only when there is one
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 500));   // the map and the tables: as high as everybody's tables need (PlanPage sets it)
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));   // Cargo Runs
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // the copy of Cargo Runs: the room that is left (the page scrolls when there is too little)
        _planPage = t;
        t.Resize += (_, _) => _fitPlanPage?.Invoke();   // a taller or lower window changes how big the map can be

        // Next Stops: Locked / Unlocked is in the top bar of the window (AddSessionControlsToMenu); hold the mouse over it for the explanation
        _lockBtn.ToolTipText = "Unlocked: the plan is free, and is worked out again whenever something changes (someone joins, more contracts, a pickup).\n\n"
            + "Locked: the next stop of everybody is kept for him. Planning again leaves those alone and plans the rest around them, and a run in progress counts in the ship. "
            + "Press it again to unlock. Nothing is locked until you press it.\n\n"
            + "Who flies, how many batches each ship holds and which quantum drive each has is set in the Party table on the front page. The runs below get their worker from the logs, and from these trips for members without a log.\n\n"
            + "Flight time is the estimated flying time of that whole route with the drive of the worker (~, no loading); Time is how long a run really took. Green = done, orange = some cargo lost, red = all lost. The run you are flying is your Current row, in your colour.";

        _planNote.Dock = DockStyle.Fill;
        _planNote.AutoSize = true;
        _planNote.TextAlign = ContentAlignment.MiddleLeft;
        _planNote.ForeColor = Color.DimGray;
        _planNote.Tag = "muted";
        _planNote.Visible = false;
        t.Controls.Add(_planNote, 0, 0);

        _planBody.Dock = DockStyle.Fill;
        _planBody.Margin = new Padding(0);
        t.Controls.Add(_planBody, 0, 1);
        t.Controls.Add(SectionTitle("Cargo Runs"), 0, 2);
        BuildPlanRunsGrid();
        t.Controls.Add(_planRunsGrid, 0, 3);
        return t;
    }

    /// <summary>The first stop of a trip that only has a drop-off there (or nothing to pick up) is not a pickup, so there is nothing to lock.</summary>
    static bool NothingToLock(PlannedTrip t) => t.Stops.Count == 0 || (t.Drops.Count > 0 && t.Stops[0].Id != t.Drops[0].Id);

    /// <summary>The text of the button follows the session: Unlocked (the plan is free) or Locked (the next stop of everybody is kept for him).</summary>
    void UpdateLockButton()
    {
        var on = _inputs?.NextStopsLocked == true;
        _lockBtn.Text = on ? "Next Stops: Locked" : "Next Stops: Unlocked";
        if (_lockBtn.Font.Bold != on) _lockBtn.Font = new Font(_lockBtn.Font, on ? FontStyle.Bold : FontStyle.Regular);
    }

    /// <summary>
    /// Next Stops: Locked / Unlocked. Unlocked (the start) the plan is free. Pressing it works the plan out and locks the next stop of everybody to him:
    /// planning again later only changes the other trips, so someone who joins gets his share of the rest. Pressing it again takes the locks away.
    /// </summary>
    void ToggleLocks()
    {
        if (_inputs == null)
        {
            SetStatus("There is no session to plan.");
            return;
        }
        var lockedNow = _inputs.NextStopsLocked;
        var answer = MessageBox.Show(this,
            lockedNow
                ? "Unlock the next stops?\n\nThe plan is free again, so it can change while people are on their way to a stop."
                : "Lock the next stops?\n\nThe plan is worked out now and the next stop of everybody is locked to him. Planning again later only changes the other trips.",
            "Next Stops", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        _planCore = null;
        _planShown = "";
        if (lockedNow)
        {
            _inputs.NextStopsLocked = false;
            _inputs.LockedTrips.Clear();
            Storage.Save(_data);
            SetStatus("The next stops are unlocked: the plan is free.");
            UpdateLockButton();
            RefreshPlanner();
            return;
        }

        _inputs.LockedTrips.Clear();
        GetPlan();   // nothing is locked yet, so this is the free plan
        var core = _planCore;
        _inputs.NextStopsLocked = true;
        var locked = 0;
        if (core != null)
        {
            var seen = new HashSet<string>();
            foreach (var trip in core.Trips)
            {
                if (!seen.Add(trip.Worker)) continue;   // the first trip of a worker is his Current
                if (HasActiveRun(trip.Worker))
                {
                    // he is flying a run now: the next stop of its continuation gets locked as soon as the plan is shown (see CoreFor)
                    if (LockKey(trip.Worker) == null) _inputs.LockedTrips[trip.Worker] = new List<LockedStop>();
                    continue;
                }
                if (NothingToLock(trip)) continue;
                _inputs.LockedTrips[LockKey(trip.Worker) ?? trip.Worker] = new List<LockedStop> { new() { Id = trip.Stops[0].Id, Batches = trip.Stops[0].Batches } };   // only his next stop
                locked++;
            }
        }
        _planCore = null;
        Storage.Save(_data);
        SetStatus("The next stops are locked" + (locked > 0 ? " (" + locked + (locked == 1 ? " worker)." : " workers).") : "."));
        UpdateLockButton();
        RefreshPlanner();
    }

    // ------------------------------------------------------------------ locked trips and who gets the credit

    /// <summary>The name a worker has in the locked trips of this session, or null when he has none.</summary>
    string? LockKey(string name) => _inputs?.LockedTrips.Keys.FirstOrDefault(k => SameName(k, name));

    /// <summary>
    /// A pickup that no log names (a member without a log) is credited to somebody once, and the credit is saved, so it never changes again (not when
    /// the program restarts either). Who gets it: the worker whose locked stop it is, then the worker whose planned trip needed a batch at that station
    /// (in the order of the plan, from before the batch was picked up), and else the only member without a log (a member with a log would have shown
    /// up in it). Nobody gets more batches than his ship holds. What the logs show always wins.
    /// </summary>
    void ClaimNewPickups()
    {
        if (_current == null || _inputs == null || _log == null) return;
        var since = _data.PlanCreditSince ??= DateTime.UtcNow;

        var fresh = new List<(string Key, string Station, DateTime Time)>();
        foreach (var m in _current.Missions)
        {
            foreach (var kv in m.ObjZone)
            {
                if (!WorkerTripBuilder.IsPickupStop(kv.Key)) continue;
                if (!m.Events.TryGetValue(kv.Key, out var ev) || ev.Count == 0) continue;
                var first = ev.Min(e => e.Time);
                if (first < since) continue;
                var key = m.Id + "|" + kv.Key;
                if (_inputs.PickupClaims.ContainsKey(key)) continue;
                if (WorkerTripBuilder.ActorAt(_log, true, first) != null) continue;   // a log shows who it was
                var station = StationOf(kv.Value);
                if (station == null) continue;
                fresh.Add((key, station, first));
            }
        }
        if (fresh.Count == 0) return;

        var crew = ActiveCrew().Where(n => ShipCapacityOf(n) > 0).ToList();
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);   // claimed just now
        bool HasRoom(string n) => ShipCapacityOf(n) - (ActiveRunOf(n)?.Loaded ?? 0) - used.GetValueOrDefault(n) > 0;
        var logged = new HashSet<string>(_log.UnstowByPlayer.Keys.Concat(_log.PlatformByPlayer.Keys), StringComparer.OrdinalIgnoreCase);
        var core = _planCore;
        var changed = false;

        foreach (var group in fresh.GroupBy(f => f.Station))
        {
            // 1. whose locked stop this is (one entry for every batch of the lock)
            var locked = new List<string>();
            foreach (var n in crew)
            {
                var lk = LockKey(n);
                if (lk == null) continue;
                var stored = _inputs.LockedTrips[lk];
                if (stored.Count == 0 || stored[0].Id != group.Key) continue;
                for (var i = 0; i < stored[0].Batches; i++) locked.Add(n);
            }
            // 2. whose planned trip expects a batch here, in the order of the plan
            var queue = new List<string>();
            if (core != null)
            {
                foreach (var trip in core.Trips)
                    foreach (var (id, n) in trip.Stops)
                    {
                        if (id != group.Key) continue;
                        for (var i = 0; i < n; i++) queue.Add(trip.Worker);
                    }
            }
            // 3. members without a log: his pickups are the ones no log names
            var noLog = crew.Where(n => !logged.Any(l => SameName(l, n))).ToList();

            foreach (var f in group.OrderBy(x => x.Time))
            {
                string? who = null;
                foreach (var list in new[] { locked, queue })
                {
                    var i = list.FindIndex(n => HasRoom(n));
                    if (i < 0) continue;
                    who = list[i];
                    list.RemoveAt(i);
                    break;
                }
                if (who == null)
                {
                    var able = noLog.Where(HasRoom).ToList();
                    if (able.Count == 1) who = able[0];
                }
                if (who == null) continue;
                _inputs.PickupClaims[f.Key] = who;
                used[who] = used.GetValueOrDefault(who) + 1;
                changed = true;
            }
        }
        if (changed) Storage.Save(_data);
    }

    /// <summary>A copy of Cargo Runs for the Trip Planner tab, so the runs are in view while you plan. It cannot be edited (do that on the Session tab).</summary>
    void BuildPlanRunsGrid()
    {
        var g = _planRunsGrid;
        g.Dock = DockStyle.Fill;
        g.Margin = new Padding(0, 3, 0, 0);
        StyleGrid(g);
        g.AllowUserToAddRows = false;
        g.AllowUserToDeleteRows = false;
        g.ReadOnly = true;
        g.Columns.Add(Col("Worker", 190, true));
        var run = Col("#", 36, true);
        run.Name = "num";
        g.Columns.Add(run);
        run.DisplayIndex = 0;
        g.Columns.Add(Col("Pickup Location & Time", 420, true, DataGridViewContentAlignment.MiddleLeft, true));
        g.Columns.Add(Col("Batches", 90, true));
        g.Columns.Add(Col("Value", 100, true, DataGridViewContentAlignment.MiddleRight));
        g.Columns.Add(Col("Started", 76, true));
        g.Columns.Add(Col("Completed", 108, true));
        g.Columns.Add(Col("Time", 90, true));
        g.Columns.Add(Col("Flight time", 90, true));
        g.Columns.Add(Col("Lost Cargo", 116, true));
        FitColumns(g);
        g.ScrollBars = ScrollBars.Vertical;
    }

    int ShipCapacityOf(string name) =>
        _data.ShipCapacity.TryGetValue(name, out var c) ? Math.Clamp(c, 0, MaxShipCapacity) : DefaultShipCapacity;

    List<string> ActiveCrew() => _data.Party.Where(n => !_data.InactiveParty.Any(x => SameName(x, n))).ToList();

    /// <summary>
    /// The route of a planned trip as a short tag for the Queued routes table: just the stations, with Ruin as the first and the last stop
    /// (<c>Ruin → Rod's Fuel → Endgame → Ruin</c>). A trip that continues from a station (<c>From</c>) starts there. What is done at every stop is in the Current Route table.
    /// </summary>
    static string RouteText(PlannedTrip trip)
    {
        var parts = new List<string>();
        var hasDrop = trip.Drops.Count > 0;
        var drop = hasDrop ? trip.Drops[0] : default;
        var firstStop = trip.Stops.Count > 0 ? trip.Stops[0].Id : null;
        if (trip.From == null) parts.Add("Ruin");
        else if (firstStop != trip.From) parts.Add(PyroDistances.NameOf(trip.From));   // where he is now
        if (hasDrop && firstStop != drop.Id) parts.Add(PyroDistances.NameOf(drop.Id));   // a stop with only the drop-off
        foreach (var (id, _) in trip.Stops) parts.Add(PyroDistances.NameOf(id));
        parts.Add("Ruin");
        return string.Join("  \u2192  ", parts);
    }

    /// <summary>
    /// The stops of a planned trip one by one, for the Current Route table: where it starts (Ruin, collecting the load from Ruin if there is one, or the station he is at),
    /// what is done at every stop, and the way back to Ruin. Every line has the leg that leads to it, so the legs add up to the whole route.
    /// </summary>
    List<StopLine> LinesOf(PlannedTrip trip, string worker, Func<string, decimal> perBatch, Func<string, double> scuPerBatch, string? at = null)
    {
        var drive = QtOf(worker);
        var lines = new List<StopLine>();
        var prev = trip.From ?? at ?? PyroDistances.Ruin;
        var hasDrop = trip.Drops.Count > 0;
        var drop = hasDrop ? trip.Drops[0] : default;
        var cargo = trip.DropCargo ?? "cargo";
        var firstStop = trip.Stops.Count > 0 ? trip.Stops[0].Id : null;
        var unknownKeys = trip.DropKeys.Where((k, i) => i < trip.DropParts.Count && trip.DropParts[i] <= 0).ToList();   // drop-offs the game did not tell the SCU of

        StopLine Add(string id, string name, bool stop, IEnumerable<string> actions, int batches = 0, decimal value = 0, double scu = 0)
        {
            var gm = PyroDistances.Between(prev, id);
            var line = new StopLine { Id = id, Name = name, Stop = stop, Batches = batches, Value = value, Scu = scu, Gm = gm, Seconds = gm > 0 ? drive.Seconds(gm) : 0 };
            line.Actions.AddRange(actions);
            lines.Add(line);
            prev = id;
            return line;
        }

        if (trip.From == null)
        {
            // his first trip starts at Ruin but he is somewhere else: first the flight back to Ruin
            if (at != null && at != PyroDistances.Ruin) Add(at, PyroDistances.NameOf(at), false, Array.Empty<string>());
            // the load for the first stop is collected at Ruin
            var collect = hasDrop
                ? new[] { "Collect " + cargo + (drop.Batches > 1 ? " \u00d7" + drop.Batches : "") }
                : Array.Empty<string>();
            var ruin = Add(PyroDistances.Ruin, "Ruin", false, collect);
            if (hasDrop)
            {
                ruin.Loads.Add(LoadText(trip.DropParts));   // the SCU of the load and the containers it is cut into
                ruin.Unknown.AddRange(unknownKeys);
            }
        }
        else if (firstStop != trip.From) Add(trip.From, PyroDistances.NameOf(trip.From), false, Array.Empty<string>());   // where he is now

        var dropShown = !hasDrop;
        if (hasDrop && firstStop != drop.Id)
        {
            var only = Add(drop.Id, PyroDistances.NameOf(drop.Id), true, new[] { "Drop " + cargo });   // a stop with only the drop-off
            only.Loads.Add(LoadText(trip.DropParts));
            only.Unknown.AddRange(unknownKeys);
            dropShown = true;
        }
        foreach (var (id, batches) in trip.Stops)
        {
            var actions = new List<string>();
            var unloads = false;
            if (!dropShown && id == drop.Id)
            {
                actions.Add("Drop " + cargo);
                dropShown = true;
                unloads = true;
            }
            if (batches > 0) actions.Add("Collect \u00d7" + batches);
            var stopLine = Add(id, PyroDistances.NameOf(id), true, actions, batches, batches * perBatch(id), batches * scuPerBatch(id));
            if (unloads)
            {
                stopLine.Loads.Add(LoadText(trip.DropParts));
                stopLine.Unknown.AddRange(unknownKeys);
            }
        }
        var home = Add(PyroDistances.Ruin, "Ruin", false, trip.Batches > 0 ? new[] { "Drop \u00d7" + trip.Batches } : Array.Empty<string>());
        var handedIn = lines.Sum(l => l.Scu);
        if (ShowPickupScu && handedIn > 0) home.Loads.Add(ScuGuess(handedIn));   // everything picked up is handed in here
        return lines;
    }

    /// <summary>Puts what the plan still collects at a station after the lines of a run in progress (at the station where the last pickup was it joins that line).</summary>
    void AddCollectLine(List<StopLine> lines, string worker, string id, int batches, decimal perBatch, double scuPerBatch)
    {
        var last = lines.Count > 0 ? lines[^1] : null;
        if (last != null && last.Id == id)
        {
            last.Actions.Add("Collect \u00d7" + batches);
            last.Batches += batches;
            last.Value += batches * perBatch;
            last.Scu += batches * scuPerBatch;
            return;
        }
        var prev = lines.LastOrDefault(l => l.Id.Length > 0)?.Id ?? PyroDistances.Ruin;
        var gm = PyroDistances.Between(prev, id);
        var line = new StopLine { Id = id, Name = PyroDistances.NameOf(id), Batches = batches, Value = batches * perBatch, Scu = batches * scuPerBatch, Gm = gm, Seconds = gm > 0 ? QtOf(worker).Seconds(gm) : 0 };
        line.Actions.Add("Collect \u00d7" + batches);
        lines.Add(line);
    }

    /// <summary>The last line of a run in progress: back at Ruin, where everything on board is handed in.</summary>
    void AddRuinLine(List<StopLine> lines, string worker, int batches)
    {
        var prev = lines.LastOrDefault(l => l.Id.Length > 0)?.Id ?? PyroDistances.Ruin;
        var gm = PyroDistances.Between(prev, PyroDistances.Ruin);
        var line = new StopLine { Id = PyroDistances.Ruin, Name = "Ruin", Stop = false, Gm = gm, Seconds = gm > 0 ? QtOf(worker).Seconds(gm) : 0 };
        if (batches > 0) line.Actions.Add("Drop \u00d7" + batches);
        var handedIn = lines.Sum(l => l.Scu);
        if (ShowPickupScu && handedIn > 0) line.Loads.Add(ScuGuess(handedIn));   // everything picked up is handed in here
        lines.Add(line);
    }

    /// <summary>
    /// The line of the Current Route that has the worker colour: the step he is doing. A run in progress: the stop he is still busy at (Collecting), else the
    /// stop after the last one that is done (he is on his way there), or the stop he set by hand; a trip that has not started: the line it starts from.
    /// </summary>
    static int HereLine(List<StopLine> lines, string location, bool byHand)
    {
        if (lines.Count == 0) return -1;
        var running = lines.Any(l => l.Done);
        if (byHand)
        {
            var at = running ? lines.FindLastIndex(l => l.Id == location) : lines.FindIndex(l => l.Id == location);
            if (at >= 0) return at;
        }
        // what is done at the last stop he got to is finished (Collected, Delivered Waste): his colour moves on to the next step, the one he is on his way to;
        // while he is still at it (Collecting, Dropping) it stays on that stop
        var lastDone = lines.FindLastIndex(l => l.Done);
        if (lastDone < 0) return 0;
        return !lines[lastDone].Busy && lastDone + 1 < lines.Count ? lastDone + 1 : lastDone;
    }

    /// <summary>What the batches of one pickup are worth: every mission pays its reward split over its stops (the same sum as the value of a run).</summary>
    decimal PickupValue(TripPickup p)
    {
        if (_current == null || _inputs == null) return 0;
        decimal total = 0;
        foreach (var id in p.MissionIds)
        {
            var m = _current.Missions.FirstOrDefault(x => x.Id == id) ?? _current.Failed.FirstOrDefault(x => x.Id == id);
            if (m == null || m.Multi == 0) continue;
            total += RewardOf(m, _inputs) / m.Multi;
        }
        return total;
    }

    /// <summary>
    /// One stop's share of the SCU of a hauling mission: the SCU of the whole mission (the "Deliver 0/490 SCU of Carbon to Ruin Station" popup) divided by its pickup stops.
    /// The log does not say how much is loaded at each stop, so this is a guess; 0 when the SCU of the mission is not known.
    /// </summary>
    static double StopScu(Mission m)
    {
        var all = m.ObjScu.Values.Sum();
        return all > 0 && m.Multi > 0 ? all / (double)m.Multi : 0;
    }

    /// <summary>The SCU of the batches of one pickup (a guess, see StopScu).</summary>
    double PickupScu(TripPickup p)
    {
        if (_current == null) return 0;
        double total = 0;
        foreach (var id in p.MissionIds)
        {
            var m = _current.Missions.FirstOrDefault(x => x.Id == id) ?? _current.Failed.FirstOrDefault(x => x.Id == id);
            if (m != null) total += StopScu(m);
        }
        return total;
    }

    /// <summary>
    /// Whether the Cargo column also guesses the SCU (as containers, "~5×32 + 2 + 1") of the cargo that is picked up for Ruin. Off: every pickup goes to the same place, so the
    /// number of batches at a station is all that matters; the code stays for a later version.
    /// </summary>
    const bool ShowPickupScu = false;

    /// <summary>A guessed amount of SCU as containers, like "~12×32 + 8".</summary>
    static string ScuGuess(double scu) => Math.Round(scu) < 1 ? "? SCU" : "~" + ContainersText((int)Math.Round(scu));

    /// <summary>The latest time the logs show the worker picking up, handing in or delivering a load from Ruin (the minimum time = nothing yet).</summary>
    DateTime LastMoveOf(string name)
    {
        var last = DateTime.MinValue;
        foreach (var t in _trips.Where(t => SameName(t.Worker, name)))
        {
            if (t.End.HasValue && t.End.Value > last) last = t.End.Value;
            foreach (var p in t.Pickups)
            {
                if (p.Time > last) last = p.Time;
            }
        }
        var drop = LastDropOf(name);
        return drop != null && drop.Value.Time > last ? drop.Value.Time : last;
    }

    /// <summary>
    /// Where the logs say the worker is: at the station of his last pickup (a run in progress), or of the last load of a from-Ruin mission he delivered, whichever
    /// is later; else at Ruin Station (nothing yet, or his run is handed in).
    /// </summary>
    string AutoLocationOf(string name)
    {
        var run = ActiveRunOf(name);
        var drop = DropStartOf(name);
        if (run?.From != null && (drop == null || run.Value.Last >= drop.Value.Time)) return run.Value.From!;
        return drop?.Station ?? PyroDistances.Ruin;
    }

    /// <summary>
    /// The Current location of a worker: where he is now, and where the plan starts his next trip. The logs keep it up to date (AutoLocationOf: every pickup, every
    /// delivery from Ruin, every hand-in). A station set by hand (for example at the start of a session, or when the logs miss something) holds until the logs
    /// show him doing something after that, then the logs take over again.
    /// </summary>
    (string Id, bool ByHand) LocationOf(string name)
    {
        var kv = _data.StartPoints.FirstOrDefault(k => SameName(k.Key, name));
        if (kv.Value != null)
        {
            if (LastMoveOf(name) > kv.Value.Since)
            {
                _data.StartPoints.Remove(kv.Key);   // he has picked up, delivered or handed in since: the logs know better
                Storage.Save(_data);
            }
            else if (PyroDistances.Points.Any(s => s.Id == kv.Value.Id))
            {
                return (kv.Value.Id, true);
            }
        }
        return (AutoLocationOf(name), false);
    }

    /// <summary>Sets (or, with null, clears) where a worker is by hand. His locked next stop is released, so the plan is free to start with a stop near him.</summary>
    void SetLocation(string name, string? stationId)
    {
        foreach (var k in _data.StartPoints.Keys.Where(k => SameName(k, name)).ToList()) _data.StartPoints.Remove(k);
        // the same station as the logs say is no choice at all; Ruin Station by hand is one (the station of his last delivery from Ruin does not take over then)
        if (stationId != null && stationId != AutoLocationOf(name)) _data.StartPoints[name] = new StartPoint { Id = stationId, Since = DateTime.UtcNow };
        var lockKey = LockKey(name);
        if (lockKey != null) _inputs!.LockedTrips.Remove(lockKey);
        Storage.Save(_data);
        BeginInvoke(new Action(() =>
        {
            _planCore = null;
            _planShown = "";
            GetPlan();
            RefreshPlanner();
        }));
    }

    /// <summary>The title of a worker's table: just his name.</summary>
    static string PlanTitle(PlanWorkerView w) => w.Name;

    /// <summary>The Total line of the Queued routes table: the number of stops, batches, value, distance and flight time of the trips above it.</summary>
    static string[] PlanTotals(List<PlanRow> rows)
    {
        var stops = rows.Sum(r => r.Stops.Count);
        var gm = rows.Sum(r => r.Gm);
        var flight = rows.Sum(r => r.Flight);
        return new[]
        {
            "\u03a3",
            stops.ToString(CultureInfo.InvariantCulture),
            rows.Sum(r => r.BatchCount).ToString(CultureInfo.InvariantCulture),
            ValueText(rows.Sum(r => r.Value)),
            gm > 0 ? Gm(gm) : "",
            flight > 0 ? "~" + FlightSpan(flight) : "",
        };
    }

    /// <summary>The cells of the Current Route table: the lines one by one (every line has a number, the Ruin lines too), the stop, what is done there, and the leg that leads to it.</summary>
    static List<string[]> LineCells(List<StopLine> lines)
    {
        var cells = new List<string[]>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            cells.Add(new[]
            {
                (i + 1).ToString(CultureInfo.InvariantCulture),
                l.Name,
                l.ActionText,
                l.CargoText,
                ValueText(l.Value),
                l.Gm > 0 ? Gm(l.Gm) : "",
                l.Seconds > 0 ? "~" + FlightSpan(l.Seconds) : "",
            });
        }
        return cells;
    }

    /// <summary>The Total line of the Current Route table: the number of stops, the batches that are collected, the value, the distance and the flight time.</summary>
    static string[] LineTotals(List<StopLine> lines)
    {
        var stops = lines.Count(l => l.Stop);
        var batches = lines.Sum(l => l.Batches);
        var scu = lines.Sum(l => l.Scu);
        var gm = lines.Sum(l => l.Gm);
        var flight = lines.Sum(l => l.Seconds);
        return new[]
        {
            "",
            "Total: " + stops + (stops == 1 ? " stop" : " stops"),
            batches > 0 ? "Collect \u00d7" + batches : "",
            ShowPickupScu && scu > 0 ? ScuGuess(scu) : "",
            ValueText(lines.Sum(l => l.Value)),
            gm > 0 ? Gm(gm) : "",
            flight > 0 ? "~" + FlightSpan(flight) : "",
        };
    }

    /// <summary>The cells of a trip in the Queued routes table: its number, the route, the batches, the value, the distance and the flight time.</summary>
    static string[] QueuedCells(PlanRow r) => new[]
    {
        RowLabel(r),
        r.Stops.Count.ToString(CultureInfo.InvariantCulture),
        r.BatchCount.ToString(CultureInfo.InvariantCulture),
        ValueText(r.Value),
        r.Gm > 0 ? Gm(r.Gm) : "",
        FlightText(r),
    };

    /// <summary>Red = all cargo lost, orange = some lost, green = done, yellow = on the way. Planned trips have no colour.</summary>
    static Color? RowColor(PlanRow r) => r.Level switch
    {
        "all" => LostRed,
        "little" => LostOrange,
        "done" => DoneGreen,
        "active" => PartialYellow,
        _ => null,
    };

    /// <summary>A run that has only waste taken out at Ruin or delivered in it (nothing picked up) is not shown as the worker's Current route once it is this old: nobody is flying it any more.</summary>
    static readonly TimeSpan StaleLeg = TimeSpan.FromHours(3);

    /// <summary>
    /// The runs of one worker in the selected session, oldest first, as lines for his table. The lines are everything that was done in the run so far, in the order it
    /// happened, and stay until the run is handed in: Ruin (with the waste taken out there), the stations where waste was delivered, and the pickups.
    /// </summary>
    List<PlanRow> RunRows(string name)
    {
        var rows = new List<PlanRow>();
        foreach (var t in _trips.Where(x => SameName(x.Worker, name) && !x.End.HasValue).OrderBy(RunStartedAt))   // finished runs are only on Cargo Runs
        {
            // a run with only waste in it: taking it out at Ruin is not a route yet (the plan still collects it), and a delivery from long ago is no run on the way
            if (t.Pickups.Count == 0 && (t.Drops.Count == 0 || _log == null || _log.LastTime - RunLastActivity(t) > StaleLeg)) continue;

            var level = t.Manual ? "All" : LostLevel(t.Key);
            var stops = new List<string>();
            var lines = new List<StopLine>();   // what is done, stop by stop (things done at the same place in a row are one line)
            var done = new Dictionary<StopLine, DoneAt>();
            var picked = 0;
            var drive = QtOf(name);
            var from = PyroDistances.Ruin;

            // the run starts at Ruin: the waste taken out there is on this line
            var home = new StopLine { Id = PyroDistances.Ruin, Name = "Ruin", Stop = false, Done = true };
            lines.Add(home);
            done[home] = new DoneAt();

            StopLine LineFor(string? id, string shown, bool atRuin)
            {
                if (lines[^1].Name == shown) return lines[^1];   // the same place again: one line
                var gm = id != null ? PyroDistances.Between(from, id) : 0;
                if (id != null) from = id;
                var line = new StopLine { Id = id ?? "", Name = shown, Stop = !atRuin, Done = true, Gm = gm, Seconds = gm > 0 ? drive.Seconds(gm) : 0 };
                lines.Add(line);
                done[line] = new DoneAt();
                return line;
            }

            var steps = new List<(DateTime Time, Action Do)>();
            foreach (var cluster in Clusters(t.Collects, x => x.Time, x => x.Cargo))
            {
                var batch = cluster;
                steps.Add((batch[0].Time, () =>
                {
                    var ruinLine = LineFor(PyroDistances.Ruin, "Ruin", true);
                    var d = done[ruinLine];
                    d.Taken += 1;   // the game completes every pickup of the contract at once, however much is taken out: one take-out, how much is not known
                    d.TakenCargo = batch[0].Cargo;
                    d.Last = batch.Max(x => x.Time);
                }));
            }
            foreach (var cluster in Clusters(t.Drops, x => x.Time, x => x.Cargo + "|" + x.Zone))
            {
                var batch = cluster;
                steps.Add((batch[0].Time, () =>
                {
                    var id = StationOf(batch[0].Zone);
                    var line = LineFor(id, LocationName(batch[0].Zone), false);
                    if (id != null && (stops.Count == 0 || stops[^1] != id)) stops.Add(id);
                    var d = done[line];
                    d.Delivered += batch.Count;
                    d.DeliveredScu.AddRange(batch.Select(x => x.Scu));
                    line.Unknown.AddRange(batch.Where(x => x.Scu <= 0).Select(x => x.MissionId + "|" + x.Objective));
                    d.DeliveredCargo = batch[0].Cargo;
                    d.Last = batch.Max(x => x.Time);
                }));
            }
            foreach (var p in t.Pickups)
            {
                var pick = p;
                steps.Add((pick.Time, () =>
                {
                    var n = pick.MissionIds.Count > 0 ? pick.MissionIds.Count : pick.Missions;
                    picked += n;
                    var id = StationOf(pick.Zone);
                    var line = LineFor(id, LocationName(pick.Zone), false);
                    if (id != null && (stops.Count == 0 || stops[^1] != id)) stops.Add(id);
                    line.Batches += n;
                    line.Value += PickupValue(pick);
                    line.Scu += PickupScu(pick);
                    done[line].Picked += n;
                    done[line].Last = pick.Time;
                }));
            }
            foreach (var step in steps.OrderBy(x => x.Time)) step.Do();

            // the run in progress: what is already done says so. At the line where he is, it says "Collecting" until he selects the next destination (only when his log shows that)
            foreach (var line in lines)
            {
                var d = done[line];
                // waste sent down the elevator is delivered at once; only what still has to go into the ship (taken out at Ruin, picked up) keeps him busy
                var busy = ReferenceEquals(line, lines[^1]) && (d.Taken > 0 || d.Picked > 0) && StillLoading(name, d.Last);
                var tick = busy ? "" : " \u2713";
                line.Busy = busy;
                if (d.Taken > 0)
                {
                    line.Actions.Add((busy ? "Collecting " : "Collected ") + d.TakenCargo + tick);
                    // what was taken out is only known from what was delivered since: those loads, with their containers
                    if (t.Drops.Count > 0)
                    {
                        line.Loads.Add(LoadText(t.Drops.Select(x => x.Scu)));
                        line.Unknown.AddRange(t.Drops.Where(x => x.Scu <= 0).Select(x => x.MissionId + "|" + x.Objective));
                    }
                }
                if (d.Delivered > 0)
                {
                    line.Actions.Add("Delivered " + d.DeliveredCargo + (d.Delivered > 1 ? " \u00d7" + d.Delivered : "") + " \u2713");
                    line.Loads.Add(LoadText(d.DeliveredScu));
                }
                if (d.Picked > 0) line.Actions.Add((busy ? "Collecting \u00d7" : "Collected \u00d7") + d.Picked + tick);
            }
            rows.Add(new PlanRow
            {
                Label = "Run #" + t.Number,
                Route = string.Join("  \u2192  ", lines.Select(x => x.Name)),
                Lines = lines,
                Batches = level == "All" ? "failed" : t.Left ? "left" : t.End.HasValue ? t.HandedIn.ToString(CultureInfo.InvariantCulture) : picked.ToString(CultureInfo.InvariantCulture),
                BatchCount = picked,
                Gm = stops.Count > 0 ? PyroDistances.Run(stops) : 0,
                Seconds = t.End.HasValue ? Math.Max(0, (t.End.Value - t.Start).TotalSeconds) : 0,
                Level = level == "All" ? "all" : level == "Little" ? "little" : t.End.HasValue ? "done" : "active",
                Stops = stops,
                Value = RunValue(t),
                Flight = FlightOf(name, stops),
            });
        }
        return rows;
    }

    /// <summary>The estimated flying time of Ruin, these stations and back to Ruin with the drive of the worker.</summary>
    double FlightOf(string name, IEnumerable<string> stops)
    {
        var drive = QtOf(name);
        var from = PyroDistances.Ruin;
        double seconds = 0;
        foreach (var id in stops)
        {
            seconds += drive.Seconds(PyroDistances.Between(from, id));
            from = id;
        }
        return seconds + drive.Seconds(PyroDistances.Between(from, PyroDistances.Ruin));
    }

    /// <summary>The estimated flying time of a run on the Cargo Runs board: Ruin, the stations it went to (waste delivered, pickups) in order, and back to Ruin.</summary>
    string RunFlightText(WorkerTrip t)
    {
        var stops = t.VisitedZones().Select(z => StationOf(z)).Where(id => id != null).Select(id => id!).ToList();
        var seconds = stops.Count > 0 ? FlightOf(t.Worker, stops) : 0;
        return seconds <= 0 ? "" : "~" + FlightSpan(seconds);
    }

    /// <summary>The run the worker is flying (picked up, not handed in yet): the batches on board and the station of the last pickup, or null. A run with only waste in it has no cargo of the plan on board.</summary>
    (int Loaded, string? From, DateTime Last)? ActiveRunOf(string name)
    {
        var loaded = 0;
        string? from = null;
        DateTime? latest = null;
        var any = false;
        foreach (var t in _trips.Where(t => SameName(t.Worker, name) && !t.End.HasValue && t.Pickups.Count > 0))
        {
            any = true;
            foreach (var p in t.Pickups)
            {
                loaded += p.Missions;
                if (latest == null || p.Time > latest)
                {
                    latest = p.Time;
                    from = StationOf(p.Zone);
                }
            }
        }
        return any ? (loaded, from, latest ?? DateTime.MinValue) : null;
    }

    /// <summary>True when the worker has a run that has pickups but is not handed in yet.</summary>
    bool HasActiveRun(string name) => ActiveRunOf(name) != null;

    /// <summary>False when he has picked up somewhere else since the lock was set (he did not go where the lock said); true while he is still on his way, or after he picked up at the locked stop.</summary>
    bool FollowsLock(string name, string lockedId, DateTime? since)
    {
        // only the pickups after the lock was set count: a lock made while he was already flying a run is for the next pickup
        var picked = false;
        foreach (var t in _trips.Where(t => SameName(t.Worker, name) && !t.End.HasValue))
        {
            foreach (var p in t.Pickups.Where(p => since == null || p.Time > since))
            {
                picked = true;
                if (StationOf(p.Zone) == lockedId) return true;
            }
        }
        return !picked;   // no pickup since the lock: he is still on his way
    }

    /// <summary>
    /// The trips for the open batches. Only the next stop of a worker is locked, and only while he is on his way to it: its batches are taken
    /// out first and his first trip goes there first, but the rest of that trip is planned freely, so a better stop can still be put after it.
    /// A worker who is flying a run has it counted in his ship (room left, and the trip continues from where he is) and the plan adds to it;
    /// while there is room, the next stop of that continuation is locked too (it is where he is heading). A lock ends when he picks up somewhere else, when its batches are picked up, and when he leaves the party; when his run
    /// is handed in, the first stop of his next planned trip is locked. Only worked out again when the batches, the crew or the locks change.
    /// </summary>
    PlanCore CoreFor(List<(string Name, int Capacity, QtDrive Drive)> crew, Dictionary<string, int> open, List<string> unknown, Dictionary<string, List<(string[] Workers, int Batches)>> limits, IReadOnlyList<DropChunk> chunks)
    {
        var settled = new HashSet<string>();
        for (var pass = 0; ; pass++)
        {
            var remaining = new Dictionary<string, int>(open);
            var starts = new List<WorkerStart>();
            var waiting = new List<string>();   // on his way to nothing locked: the first stop of his next planned trip gets locked once it is planned
            if (_inputs != null)
            {
                var changed = false;
                var locking = _inputs.NextStopsLocked;
                if (!locking && _inputs.LockedTrips.Count > 0)
                {
                    _inputs.LockedTrips.Clear();   // the button says Unlocked: the plan is free
                    changed = true;
                }
                // somebody who left the party (or does not fly) has no locked stop: his batches go back to the others
                foreach (var gone in _inputs.LockedTrips.Keys.Where(k => !crew.Any(c => c.Capacity > 0 && SameName(c.Name, k))).ToList())
                {
                    _inputs.LockedTrips.Remove(gone);
                    changed = true;
                }
                foreach (var c in crew.Where(x => x.Capacity > 0))
                {
                    var run = ActiveRunOf(c.Name);
                    var loc = LocationOf(c.Name).Id;   // where he is: the station of his last pickup or delivery (the logs), or the one set by hand
                    var start = new WorkerStart { Worker = c.Name, Loaded = run?.Loaded ?? 0, From = loc == PyroDistances.Ruin ? null : loc };
                    var lockKey = LockKey(c.Name);
                    if (lockKey == null && locking)
                    {
                        // with the button on Locked everybody who flies has his next stop kept for him: the first stop of his planned trip is where he is heading
                        _inputs.LockedTrips[c.Name] = new List<LockedStop>();
                        lockKey = c.Name;
                        changed = true;
                    }
                    if (lockKey != null)
                    {
                        var stored = _inputs.LockedTrips[lockKey];
                        if (stored.Count > 0 && run != null && !FollowsLock(c.Name, stored[0].Id, stored[0].Since))
                        {
                            _inputs.LockedTrips[lockKey] = stored = new List<LockedStop>();   // he picked up somewhere else: his trips are free again
                            changed = true;
                        }
                        if (stored.Count > 0)
                        {
                            var take = Math.Min(Math.Min(stored[0].Batches, remaining.GetValueOrDefault(stored[0].Id)), Math.Max(0, c.Capacity - start.Loaded));
                            if (take > 0)
                            {
                                start.FirstId = stored[0].Id;
                                start.FirstBatches = take;
                                remaining[stored[0].Id] -= take;
                            }
                            else
                            {
                                _inputs.LockedTrips[lockKey] = new List<LockedStop>();   // picked up completely
                                changed = true;
                            }
                        }
                        if (start.FirstId == null && (run == null || run.Value.Loaded < c.Capacity)) waiting.Add(c.Name);   // a run with room left locks the next stop of its continuation too
                    }
                    // his own log shows the station he is flying to (or has arrived at) since his last pickup or delivery: what he can take there stays his,
                    // locked or not, so planning again for somebody else never takes it away from him while he is on his way or about to load
                    var heading = start.FirstId == null ? HeadingFor(c.Name) : null;
                    if (heading != null && remaining.GetValueOrDefault(heading) > 0)
                    {
                        var notHis = limits.TryGetValue(heading, out var hl) ? hl.Where(x => !x.Workers.Contains(c.Name)).Sum(x => x.Batches) : 0;
                        var take = Math.Min(remaining[heading] - notHis, Math.Max(0, c.Capacity - start.Loaded));
                        if (take > 0)
                        {
                            start.FirstId = heading;
                            start.FirstBatches = take;
                            remaining[heading] -= take;
                        }
                    }
                    if (start.FirstId == null && run == null && start.From != null && remaining.GetValueOrDefault(start.From) > 0)
                    {
                        // he said where he is: what is left at that station is right there, so his first trip takes it first (what only the others have is left to them)
                        var here = start.From;
                        var onlyOthers = limits.TryGetValue(here, out var gl) ? gl.Where(x => !x.Workers.Contains(c.Name)).Sum(x => x.Batches) : 0;
                        var take = Math.Min(Math.Min(remaining[here] - onlyOthers, remaining[here]), c.Capacity);
                        if (take > 0)
                        {
                            start.FirstId = here;
                            start.FirstBatches = take;
                            remaining[here] -= take;
                        }
                    }
                    if (start.FirstId != null || start.Loaded > 0 || start.From != null) starts.Add(start);
                }
                if (changed) Storage.Save(_data);
            }

            var key = string.Join("|", crew.Select(c => c.Name + ":" + c.Capacity + ":" + c.Drive.Key))
                + "#" + string.Join("|", remaining.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + ":" + kv.Value))
                + "#" + string.Join("|", unknown)
                + "#" + string.Join("|", chunks.Select(x => x.Station + ":" + x.Batches + ":" + x.Scu + ":" + x.Cargo + ":" + string.Join(",", x.Allowed)))
                + "#" + string.Join("|", limits.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + string.Join(";", kv.Value.Select(g => string.Join(",", g.Workers) + "x" + g.Batches))))
                + "#" + string.Join("|", starts.Select(t => t.Worker + "=" + t.FirstId + "x" + t.FirstBatches + "/" + t.Loaded + "@" + t.From));
            if (_planCore != null && _planCore.Key == key && waiting.All(n => _planCore.Settled.Contains(n))) return _planCore;

            var note = unknown.Count > 0 ? "Not in the distance table, so left out: " + string.Join(", ", unknown) + "." : "";
            var only = limits.SelectMany(kv => kv.Value.Select(g => PyroDistances.NameOf(kv.Key) + " ×" + g.Batches + " only " + string.Join(" / ", g.Workers))).ToList();
            if (only.Count > 0) note = (note.Length > 0 ? note + "  " : "") + "Not everybody has every mission: " + string.Join(", ", only) + ".";
            var core = new PlanCore
            {
                Key = key,
                Trips = TripPlanner.Plan(remaining, crew, starts, limits, chunks),
                Batches = remaining.Where(kv => kv.Value > 0).Sum(kv => kv.Value) + starts.Sum(t => t.FirstBatches),
                Note = note,
                Settled = settled,
            };
            _planCore = core;
            if (waiting.Count == 0 || pass > 0 || _inputs == null) return core;

            // his run is handed in (or his lock was used up): the first stop of the trip planned for him is where he is going, so lock it (nothing planned = nothing to lock)
            foreach (var name in waiting)
            {
                var lockKey = LockKey(name);
                if (lockKey == null) continue;
                var next = core.Trips.FirstOrDefault(t => t.Worker == name);
                var flying = ActiveRunOf(name);
                if (next == null || NothingToLock(next))
                {
                    settled.Add(name);
                    if (flying == null) _inputs.LockedTrips.Remove(lockKey);   // with a run in progress the marker stays: his next trip is locked once the run is handed in
                }
                else _inputs.LockedTrips[lockKey] = new List<LockedStop> { new() { Id = next.Stops[0].Id, Batches = next.Stops[0].Batches, Since = flying?.Last } };
            }
            Storage.Save(_data);
        }
    }

    /// <summary>The plan for the open batches of the selected session, with the runs so far. The trips are only worked out again when the batches or the crew change.</summary>
    PlanView GetPlan()
    {
        var crew = ActiveCrew().Select(n => (Name: n, Capacity: ShipCapacityOf(n), Drive: QtOf(n))).ToList();
        var remaining = new Dictionary<string, int>();
        var unknown = new List<string>();
        var worth = new Dictionary<string, (decimal Value, int Batches)>();   // station -> what its batches are worth together (the value of a stop is reward / stops of its mission)
        var volume = new Dictionary<string, (double Scu, int Stops)>();   // station -> the SCU of its open stops together (a guess, see StopScu) and how many stops that is
        var limits = new Dictionary<string, List<(string[] Workers, int Batches)>>();   // station -> batches that only some of the crew can take (they have those missions running, the others do not)
        var flyers = crew.Where(c => c.Capacity > 0).Select(c => c.Name).ToList();
        if (_inputs != null)
        {
            foreach (var r in _openRows)
            {
                var left = r.Missions - r.Loaded;   // batches not picked up yet
                if (left <= 0) continue;
                var id = StationOf(r.Zone);
                if (id != null && r.Missions > 0)
                {
                    var was = worth.GetValueOrDefault(id);
                    worth[id] = (was.Value + StopValue(r), was.Batches + r.Missions);
                }
                if (id == null)
                {
                    unknown.Add(LocationName(r.Zone) + " (" + left + ")");
                    continue;
                }
                remaining[id] = remaining.GetValueOrDefault(id) + left;
                foreach (var open in r.OpenMissions)
                {
                    var share = StopScu(open);
                    if (share <= 0) continue;
                    var had = volume.GetValueOrDefault(id);
                    volume[id] = (had.Scu + share, had.Stops + 1);
                }
                foreach (var m in r.OpenMissions)
                {
                    var can = flyers.Where(n => HasMission(m, n)).ToArray();   // the Contracts tab: who has this mission
                    if (can.Length == 0 || can.Length == flyers.Count) continue;
                    if (!limits.TryGetValue(id, out var gl)) limits[id] = gl = new List<(string[] Workers, int Batches)>();
                    var same = gl.FindIndex(x => x.Workers.SequenceEqual(can));
                    if (same >= 0) gl[same] = (can, gl[same].Batches + 1);
                    else gl.Add((can, 1));
                }
            }
        }

        // cargo that goes out from Ruin (from-Ruin contracts): the planner gives every worker the loads of his missions, each in a trip of its own that unloads at its first stop
        var drops = OpenDrops();
        var chunks = new List<DropChunk>();
        var dropNote = "";
        if (drops.Count > 0)
        {
            var cap = flyers.Count == 0 ? 0 : crew.Where(c => c.Capacity > 0).Min(c => c.Capacity);
            chunks = DropChunks(drops, flyers, cap);
            var lost = drops.Count(d => d.Station == null);
            if (lost > 0) dropNote = "From Ruin: " + lost + (lost == 1 ? " drop-off is" : " drop-offs are") + " to a place that is not in the distance table, so left out.";
        }
        var core = CoreFor(crew, remaining, unknown, limits, chunks);
        var dropTrips = core.Trips;
        // the waste only rides along on the pickup trips: a load that none of them can take along waits (the note says so)
        var carried = dropTrips.SelectMany(pt => pt.Drops.Select(dd => dd.Id)).ToList();
        var waits = new List<string>();
        foreach (var load in chunks)
        {
            var at = carried.IndexOf(load.Station);
            if (at >= 0) carried.RemoveAt(at);
            else waits.Add(load.Cargo + " for " + PyroDistances.NameOf(load.Station));
        }
        if (waits.Count > 0) dropNote = (dropNote + "  Waiting to go along on a pickup trip: " + string.Join(", ", waits) + ".").Trim();
        decimal PerBatch(string id) => worth.TryGetValue(id, out var x) && x.Batches > 0 ? x.Value / x.Batches : 0;   // the average batch at that station
        double ScuPerBatch(string id) => volume.TryGetValue(id, out var y) && y.Stops > 0 ? y.Scu / y.Stops : 0;   // the average stop at that station, in SCU

        var plan = new PlanView { Note = (core.Note + "  " + dropNote).Trim() };
        var flying = crew.Where(c => c.Capacity > 0).ToList();
        for (var i = 0; i < flying.Count; i++)
        {
            var c = flying[i];
            var drive = DriveOf(c.Name);
            var w = new PlanWorkerView
            {
                Name = c.Name,
                Capacity = c.Capacity,
                DriveNote = drive == null ? "no QT drive set (assuming " + QuantumDrives.Assumed.Key + ")" : "",
                Drive = drive == null ? "no QT drive set (assuming " + QuantumDrives.Assumed.Key + ")" : drive.Key + ", " + (drive.Speed / 1e6).ToString("0", CultureInfo.InvariantCulture) + " Mm/s",
                Color = WorkerColor(c.Name),
            };
            w.RunsSoFar = _trips.Count(x => SameName(x.Worker, c.Name));
            var standing = LocationOf(c.Name);
            w.LocationId = standing.Id;
            w.LocationByHand = standing.ByHand;
            w.AutoLocationId = AutoLocationOf(c.Name);
            w.Rows.AddRange(RunRows(c.Name));
            // the run in progress is his Current trip (a locked stop is marked in the Route cell of a planned trip)
            var runRow = w.Rows.LastOrDefault(r => r.Level == "active");
            if (runRow != null)
            {
                runRow.Label = "Current";
                runRow.First = true;
            }
            var mine = dropTrips.Where(t => t.Worker == c.Name).ToList();
            for (var k = 0; k < mine.Count; k++)
            {
                w.Rows.Add(new PlanRow
                {
                    Label = k > 0 ? "Plan " + (k + 1) : runRow == null ? "Current" : mine[k].Special ? "Continue" : "Next",
                    First = k == 0 && runRow == null,
                    Solid = k == 0 && (runRow == null || mine[k].Special),
                    Locked = mine[k].Locked,
                    Route = RouteText(mine[k]),
                    Batches = mine[k].Batches.ToString(CultureInfo.InvariantCulture),
                    BatchCount = mine[k].Batches,
                    Value = mine[k].Stops.Sum(s => s.Batches * PerBatch(s.Id)),
                    Gm = mine[k].Distance,
                    Seconds = mine[k].Seconds,
                    Flight = mine[k].Seconds,
                    Level = "plan",
                    Stops = mine[k].Drops.Select(d => d.Id).Concat(mine[k].Stops.Select(s => s.Id)).Aggregate(new List<string>(), (l, id) => { if (l.Count == 0 || l[^1] != id) l.Add(id); return l; }),
                    From = mine[k].From,
                    Trip = mine[k],
                    Lines = LinesOf(mine[k], c.Name, PerBatch, ScuPerBatch, k == 0 && runRow == null ? w.LocationId : null),
                });
            }
            // what the plan adds to the run in progress is part of his Current row: the stops already picked up, then the ones still to pick up
            if (runRow != null && mine.Count > 0 && mine[0].Special && mine[0].Stops.Count > 0)
            {
                var cont = mine[0];
                runRow.BatchCount += cont.Batches;
                runRow.Value += cont.Stops.Sum(s => s.Batches * PerBatch(s.Id));
                runRow.Batches = runRow.BatchCount.ToString(CultureInfo.InvariantCulture);
                runRow.Stops = runRow.Stops.Concat(cont.Stops.Select(s => s.Id)).ToList();
                runRow.Gm = PyroDistances.Run(runRow.Stops);
                runRow.Flight = FlightOf(c.Name, runRow.Stops);   // the whole run, Ruin to Ruin
                foreach (var (stopId, stopBatches) in cont.Stops) AddCollectLine(runRow.Lines, c.Name, stopId, stopBatches, PerBatch(stopId), ScuPerBatch(stopId));
                w.Rows.First(r => r.Trip == cont).Hidden = true;
            }
            else if (runRow != null && runRow.Stops.Count > 0)
            {
                runRow.OnMap = true;   // nothing is added to it (full ship): the run itself is his solid line
                runRow.Solid = true;
                runRow.Passed = runRow.Stops.Count;   // everything on it is picked up: he is on his way home
            }
            if (runRow != null && runRow.Lines.Count > 0)
            {
                AddRuinLine(runRow.Lines, c.Name, runRow.BatchCount);   // the run ends at Ruin
                runRow.Route = string.Join("  \u2192  ", runRow.Lines.Select(l => l.Name));
            }
            // he has taken the waste out at Ruin already (his run has nothing else in it yet): the Ruin line of his first trip says so. "Loading Waste" while he is
            // still at it (he has not set his next quantum destination, only known from his own log), then "Collected Waste ✓" and his colour moves on to the next stop
            if (runRow == null && mine.Count > 0 && mine[0].From == null && mine[0].Drops.Count > 0)
            {
                var takenRun = _trips.Where(x => SameName(x.Worker, c.Name) && !x.End.HasValue && x.Pickups.Count == 0 && x.Drops.Count == 0 && x.Collects.Count > 0)
                    .OrderBy(x => x.Start).LastOrDefault();
                var firstRow = w.Rows.FirstOrDefault(r => r.Trip == mine[0]);
                var startLine = firstRow?.Lines.FirstOrDefault(l => l.Id == PyroDistances.Ruin && !l.Stop);
                var loadedAt = takenRun?.Collects.Max(x => x.Time) ?? DateTime.MinValue;
                if (takenRun != null && startLine != null && _log != null && _log.LastTime - loadedAt <= StaleLeg)
                {
                    // the game completes every pickup of the contract at once, however much is taken out, so the load of this trip is taken to be what he took
                    // (its SCU and containers stay as planned)
                    var stillLoading = StillLoading(c.Name, loadedAt);
                    var loads = mine[0].Drops[0].Batches;
                    startLine.Actions.Clear();
                    startLine.Actions.Add((stillLoading ? "Loading " : "Collected ") + (mine[0].DropCargo ?? takenRun.Collects[0].Cargo) + (loads > 1 ? " \u00d7" + loads : "") + (stillLoading ? "" : " \u2713"));
                    startLine.Done = true;
                    startLine.Busy = stillLoading;
                }
            }
            // the first line is his Current trip (the Current Route table), the others are his Queued routes, numbered 1, 2, ...
            var number = 0;
            foreach (var r in w.Rows.Where(x => !x.Hidden))
            {
                r.Label = number == 0 ? "Current" : number.ToString(CultureInfo.InvariantCulture);
                number++;
            }
            // the worker colour is on the line of the Current Route where he is
            var currentRow = w.Rows.FirstOrDefault(x => !x.Hidden);
            if (currentRow != null) currentRow.Here = HereLine(currentRow.Lines, w.LocationId, w.LocationByHand);
            plan.Workers.Add(w);
        }

        var planned = plan.Workers.SelectMany(w => w.Planned).ToList();
        if (flying.Count == 0)
        {
            plan.Summary = "Nobody is flying. Tick Active and set a Batch Capacity for the crew in Options → Party…";
        }
        else if (planned.Count == 0)
        {
            plan.Summary = "No open batches to pick up.";
        }
        else
        {
            var longest = plan.Workers.OrderByDescending(w => w.Planned.Sum(r => r.Seconds)).First();
            plan.Summary = planned.Count + " trips planned, " + Gm(planned.Sum(r => r.Gm)) + " in total for " + core.Batches + " batches. Longest: "
                + longest.Name + " with " + Gm(longest.Planned.Sum(r => r.Gm)) + ", about " + FlightSpan(longest.Planned.Sum(r => r.Seconds)) + " of flying.";
        }

        plan.Signature = plan.Summary + "¦" + (Theme.Dark ? "dark" : "light") + "¦" + string.Join("|", plan.Workers.Select(x => x.Name + "@" + x.LocationId + (x.LocationByHand ? "!" : "") + ">" + x.AutoLocationId)) + "¦" + string.Join("¦", plan.Workers.SelectMany(w =>
            w.Rows.Select(r => w.Name + "|" + w.Capacity + "|" + r.Label + "|" + r.Hidden + "|" + r.Locked + "|" + r.First + "|" + r.Solid + "|" + r.Route + "|" + r.Batches + "|" + r.Value.ToString("F0", CultureInfo.InvariantCulture) + "|" + r.Level + "|" + r.Gm.ToString("F1", CultureInfo.InvariantCulture) + "|" + r.Flight.ToString("F0", CultureInfo.InvariantCulture) + "|" + r.Seconds.ToString("F0", CultureInfo.InvariantCulture) + "|" + r.Here + "|" + string.Join(";", r.Lines.Select(l => l.Name + "~" + l.ActionText + "~" + l.CargoText + "~" + l.Batches + "~" + l.Value.ToString("F0", CultureInfo.InvariantCulture) + "~" + l.Gm.ToString("F1", CultureInfo.InvariantCulture) + "~" + l.Seconds.ToString("F0", CultureInfo.InvariantCulture))) + "|" + w.Drive + "|" + w.RunsSoFar)));
        return plan;
    }

    /// <summary>Shows the plan: the map on the left, the tables on the right. It only runs while the Trip Planner tab is on screen.</summary>
    void RefreshPlanner()
    {
        UpdateLockButton();
        PlanView plan;
        try
        {
            plan = GetPlan();
        }
        catch (Exception ex)
        {
            // the plan could not be worked out: say so (and try again on the next update), instead of leaving an old or empty page
            _planNote.Text = "The plan could not be worked out: " + ex.Message;
            _planNote.Visible = true;
            _planShown = "";
            _planCore = null;
            return;
        }
        _planNote.Text = plan.Note;
        _planNote.Visible = plan.Note.Length > 0;
        if (plan.Signature == _planShown) return;   // nothing changed, so nothing flickers

        _planBody.SuspendLayout();
        try
        {
            var page = PlanPage(plan);
            foreach (var old in _planBody.Controls.Cast<Control>().ToList())
            {
                _planBody.Controls.Remove(old);
                old.Dispose();
            }
            _planBody.Controls.Add(page);
            _planShown = plan.Signature;   // only when the page really got built, so a failed build is tried again
        }
        catch (Exception ex)
        {
            _planNote.Text = "The plan could not be shown: " + ex.Message;
            _planNote.Visible = true;
            _planShown = "";
        }
        finally
        {
            _planBody.ResumeLayout(true);
        }
        if (IsHandleCreated) BeginInvoke(new Action(RefitPlanGrids));   // the page is on screen now: the rows are measured at the widths they really have
    }

    /// <summary>Measures the rows of the tables of the Trip Planner again (a row with a long route is as high as its text needs at the width the table has).</summary>
    void RefitPlanGrids()
    {
        var stack = new Stack<Control>(_planBody.Controls.Cast<Control>());
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            foreach (Control child in c.Controls) stack.Push(child);
            if (c is PlanGridView grid) grid.FitHeight();
        }
    }

    /// <summary>The map on the left, square and as large as the page allows, the tables of everybody on the right.</summary>
    Control PlanPage(PlanView plan)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 4, 0, 0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var map = new PyroMapView { Dock = DockStyle.Top };
        // under the map: the small team status line (a text that changes every few seconds)
        var status = new StatusLine { Dock = DockStyle.Top };
        status.ShowText(_statusText, false);
        _statusLine = status;
        status.Disposed += (_, _) =>
        {
            if (ReferenceEquals(_statusLine, status)) _statusLine = null;
        };
        var left = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0, 0, 12, 0) };
        left.Controls.Add(status);   // added first, so it sits under the map
        left.Controls.Add(map);
        var host = new PlanHost { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false, Margin = new Padding(0) };   // never scrolls: the row is as high as everybody's tables
        host.Resize += (_, _) => FitPlanBlocks(host);

        // one colour per worker, his current trip in bold and solid, the later ones dotted
        foreach (var w in plan.Workers)
        {
            var k = 0;
            foreach (var r in w.MapRows)
            {
                map.Routes.Add(new MapRoute { Label = k == 0 ? w.Name : "", Color = w.Color, Stops = r.Stops, From = r.From, Passed = r.Passed, Bold = r.Solid, Dashed = !r.Solid, Tag = r });
                k++;
            }
        }
        var widths = PlanTextWidths(plan);   // the same for every worker, so his tables line up with the others
        foreach (var w in plan.Workers) host.Controls.Add(PlanBlock(w, widths));
        FitPlanBlocks(host);

        // The map is a square, 40% of the width (and no higher than the page leaves above Cargo Runs). The row of the map and the tables is as high as everybody's
        // tables need (or the map, when that is higher), so the Current Route of every worker is on the page: Cargo Runs moves down, and the page scrolls when the
        // window is too low for both.
        var fitting = false;
        void FitPage()
        {
            if (fitting) return;
            fitting = true;
            try
            {
                var page = _planPage;
                var note = _planNote.Visible ? _planNote.Height : 0;
                var above = page != null ? page.ClientSize.Height - page.Padding.Vertical - TitleHeight - PlanRunsMin - note : t.ClientSize.Height;
                var side = Math.Max(320, Math.Min((int)(t.ClientSize.Width * 0.4), above - status.Height - t.Padding.Vertical));
                if ((int)t.ColumnStyles[0].Width != side + left.Padding.Horizontal) t.ColumnStyles[0].Width = side + left.Padding.Horizontal;
                if (map.Height != side) map.Height = side;
                var blocks = host.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical);
                var need = Math.Max(side + status.Height, blocks) + t.Padding.Vertical + 4;
                if (page != null && Math.Abs(page.RowStyles[1].Height - need) > 0.5f)
                {
                    page.RowStyles[1].Height = need;
                    page.AutoScrollMinSize = new Size(0, need + TitleHeight + PlanRunsMin + page.Padding.Vertical + note);
                }
            }
            finally
            {
                fitting = false;
            }
        }
        _fitPlanPage = FitPage;
        t.Resize += (_, _) => FitPage();
        foreach (Control b in host.Controls) b.SizeChanged += (_, _) => FitPage();
        FitPage();
        t.Controls.Add(left, 0, 0);
        t.Controls.Add(host, 1, 0);
        return t;
    }

    static void FitPlanBlocks(FlowLayoutPanel host)
    {
        var width = Math.Max(200, host.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
        foreach (Control c in host.Controls) c.Width = width;
        // after the window got bigger again nothing may stay scrolled to the side
        if (host.AutoScrollPosition.X != 0) host.AutoScrollPosition = new Point(0, -host.AutoScrollPosition.Y);
    }

    /// <summary>The panel that holds the tables of the Trip Planner: it scrolls up and down only, never sideways (the tables fit its width).</summary>
    sealed class PlanHost : FlowLayoutPanel
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ShowScrollBar(IntPtr hWnd, int bar, bool show);

        protected override void WndProc(ref Message m)
        {
            if (IsHandleCreated) ShowScrollBar(Handle, 0, false);   // 0 = the horizontal bar
            base.WndProc(ref m);
        }
    }

    /// <summary>A title and the two tables of one crew member: the Current Route, and right of it the narrow Queued routes (under it when the window is too narrow for both).</summary>
    Control PlanBlock(PlanWorkerView w, (int Current, int Queued, int Cargo) widths)
    {
        var block = new Panel { Margin = new Padding(0, 0, 0, 14) };
        // the title: his name, and right of it where he is now (his Current location: the logs keep it up to date, and here it can be set by hand)
        var label = new Panel { Dock = DockStyle.Top, Height = 34 };
        var titleFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        var nameWidth = TextRenderer.MeasureText(PlanTitle(w), titleFont).Width;
        label.Controls.Add(new Label { Text = PlanTitle(w), AutoSize = true, Font = titleFont, ForeColor = Theme.Text, Location = new Point(0, 6) });
        var hintFont = new Font("Segoe UI", 9.5f);
        var hintText = "Current location:";
        var hintX = nameWidth + 30;
        label.Controls.Add(new Label { Text = hintText, AutoSize = true, Font = hintFont, ForeColor = Theme.Muted, Location = new Point(hintX, 9) });
        var comboX = hintX + TextRenderer.MeasureText(hintText, hintFont).Width + 12;
        var combo = new ThemedCombo { Width = 250, Location = new Point(comboX, 5), Font = hintFont };
        var stations = PyroDistances.Points.OrderBy(s => s.Id == PyroDistances.Ruin ? 0 : 1).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();   // Ruin Station first
        combo.Items.Add("Automatic: " + PyroDistances.NameOf(w.AutoLocationId));   // the logs: his last pickup, delivery or hand-in
        foreach (var s in stations) combo.Items.Add(s.Name);
        combo.SelectedIndex = w.LocationByHand ? Math.Max(0, stations.FindIndex(s => s.Id == w.LocationId) + 1) : 0;
        combo.SelectedIndexChanged += (_, _) =>
            SetLocation(w.Name, combo.SelectedIndex <= 0 ? null : stations[combo.SelectedIndex - 1].Id);
        label.Controls.Add(combo);
        label.Controls.Add(new Label
        {
            Text = w.LocationByHand ? "set by you, until his next pickup, delivery or hand-in" : "follows his pickups, deliveries and hand-ins",
            AutoSize = true, Font = new Font("Segoe UI", 9f), ForeColor = Theme.Muted, Location = new Point(comboX + combo.Width + 12, 10),
        });
        var height = label.Height;

        var visible = w.Rows.Where(x => !x.Hidden).ToList();
        if (visible.Count > 0)
        {
            var current = visible[0];
            var queued = visible.Skip(1).ToList();
            var currentGrid = CurrentRouteGrid(w, current, widths.Current, widths.Cargo);
            var queuedGrid = QueuedRoutesGrid(queued, widths.Queued);

            // each table with its small title above it
            Panel Boxed(string title, PlanGridView grid)
            {
                var box = new Panel { Margin = new Padding(0) };
                box.Controls.Add(grid);   // added first, so the title sits above it
                box.Controls.Add(PlanTableTitle(title));
                return box;
            }
            var currentBox = Boxed("Current Route", currentGrid);
            var queuedBox = Boxed("Queued routes", queuedGrid);
            var pair = new Panel { Dock = DockStyle.Top };
            pair.Controls.Add(currentBox);
            pair.Controls.Add(queuedBox);

            // The Current Route takes the room, and the Queued routes (a narrow table: just the numbers of every trip) sit right of it; when the window is too
            // narrow for both, the Queued routes go under the Current Route.
            var fitting = false;
            void FitPair()
            {
                if (fitting) return;   // setting the sizes below changes the heights of the tables, which comes back here
                fitting = true;
                try
                {
                    var minCurrent = PlanCurrentFixed(widths.Current) + PlanMinText;
                    var room = Math.Max(minCurrent, pair.ClientSize.Width);
                    var side = room >= minCurrent + PlanGap + PlanQueuedWidth;
                    currentBox.Width = side ? room - PlanGap - PlanQueuedWidth : room;
                    queuedBox.Width = PlanQueuedWidth;
                    // the tables are as high as their rows (a long text takes two lines), the boxes follow them, and the block follows the boxes
                    currentBox.Height = PlanTitleHeight + currentGrid.Height;
                    queuedBox.Height = PlanTitleHeight + queuedGrid.Height;
                    currentBox.Location = new Point(0, 0);
                    queuedBox.Location = side ? new Point(currentBox.Width + PlanGap, 0) : new Point(0, currentBox.Height + PlanGap);
                    var tall = side ? Math.Max(currentBox.Height, queuedBox.Height) : queuedBox.Bottom;
                    if (pair.Height != tall) pair.Height = tall;
                    if (block.Height != label.Height + tall) block.Height = label.Height + tall;
                }
                finally
                {
                    fitting = false;
                }
            }
            currentGrid.SizeChanged += (_, _) => FitPair();
            queuedGrid.SizeChanged += (_, _) => FitPair();
            pair.Resize += (_, _) => FitPair();
            FitPair();
            height += pair.Height;
            block.Controls.Add(pair);   // added first, so the title sits above it
        }

        block.Controls.Add(label);
        block.Height = height;
        return block;
    }

    const int PlanTitleHeight = 26;
    const int PlanMinText = 110;                            // the text column of a table (Cargo, Route) is never narrower than this
    const int PlanQueuedWidth = 36 + 64 + 64 + 80 + 74 + 68 + 3;   // the Queued routes: #, Stops, Batches, Value, Distance, In flight (and the border)
    const int PlanGap = 12;                                 // between the Current Route and the Queued routes
    const int PlanRunsMin = 160;                            // Cargo Runs under the planner is never lower than this (the page scrolls instead)

    /// <summary>The Current Route without its Cargo column (the one that takes the room that is left): #, Stops, Drop/Collect, Value, Distance, In flight.</summary>
    static int PlanCurrentFixed(int action) => 36 + 120 + action + 80 + 74 + 68;

    static readonly Font PlanBoldFont = new("Segoe UI", 10f, FontStyle.Bold);

    /// <summary>
    /// The width that the Drop/Collect column of the Current Route (what is done at a stop) needs for its longest line, the width of its Cargo column, and the width that the
    /// route of the Queued routes needs, over all the workers: every worker gets the same tables, so they line up, and a row stays one line high when there is room.
    /// </summary>
    static (int Current, int Queued, int Cargo) PlanTextWidths(PlanView plan)
    {
        int Measure(string text) => TextRenderer.MeasureText(text, PlanBoldFont).Width;   // bold: the widest it gets
        var current = 0;
        var queued = 0;
        var cargo = 0;
        foreach (var w in plan.Workers)
        {
            var visible = w.Rows.Where(r => !r.Hidden).ToList();
            if (visible.Count == 0) continue;
            foreach (var l in visible[0].Lines)
            {
                current = Math.Max(current, Measure(l.ActionText));
                cargo = Math.Max(cargo, Measure(l.CargoText));
            }
            foreach (var r in visible.Skip(1)) queued = Math.Max(queued, Measure(r.Route));
        }
        return (Math.Clamp(current + 20, 150, 420), Math.Clamp(queued + 20, 150, 700), Math.Clamp(cargo + 20, 110, 420));
    }

    /// <summary>The small title above one of the two tables of a worker.</summary>
    static Label PlanTableTitle(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        Height = PlanTitleHeight,
        AutoSize = false,
        TextAlign = ContentAlignment.BottomLeft,
        Padding = new Padding(0, 0, 0, 2),
        Font = PlanBoldFont,
        ForeColor = Theme.Text,
    };

    /// <summary>
    /// A table of the Trip Planner. Current Route: #, Stops (just the station), Drop/Collect (what is done there), Cargo (the SCU, and the containers of a load; this column
    /// takes the room that is left), Value, Distance, In flight. Queued routes: #, Route (the stations; takes the room that is left), Batches, Value, Distance, In flight.
    /// A text column wraps onto a second line only when it has to.
    /// </summary>
    PlanGridView NewPlanGrid(bool current, int textWidth, int cargoWidth)
    {
        var grid = new PlanGridView();
        StyleGrid(grid);
        grid.Dock = DockStyle.Top;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ScrollBars = ScrollBars.None;
        grid.ShowCellToolTips = false;   // a pop-up with the value of a cell that is a bit tight is only in the way
        grid.Columns.Add(Col("#", 36, true));
        DataGridViewColumn text;          // the column that takes the room that is left
        DataGridViewColumn? action = null;
        if (current)
        {
            grid.Columns.Add(Col("Stops", 120, true, DataGridViewContentAlignment.MiddleLeft));
            action = Col("Drop/Collect", textWidth, true, DataGridViewContentAlignment.MiddleLeft);
            grid.Columns.Add(action);
            text = Col("Cargo", cargoWidth, true, DataGridViewContentAlignment.MiddleLeft);
            grid.Columns.Add(text);
        }
        else
        {
            text = Col("Stops", 64, true);   // how many stops the trip has (the route is in the tooltip and on the map)
            grid.Columns.Add(text);
            grid.Columns.Add(Col("Batches", 64, true));
        }
        grid.Columns.Add(Col("Value", 80, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col("Distance", 74, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col("In flight", 68, true, DataGridViewContentAlignment.MiddleRight));
        foreach (DataGridViewColumn c in grid.Columns)
        {
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            c.MinimumWidth = current && c == text ? PlanMinText : 30;
        }
        text.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        if (action != null) action.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.TextColumn = current ? text : null;   // the Queued routes have no column that stretches
        grid.Width = current ? PlanCurrentFixed(textWidth) + cargoWidth : PlanQueuedWidth;   // the width it is expected to get, so the first measure of its rows is about right
        return grid;
    }

    /// <summary>The Current Route of a worker: the run he is flying (or the trip he starts with), one stop per line. The worker colour is on the line where he is.</summary>
    PlanGridView CurrentRouteGrid(PlanWorkerView w, PlanRow current, int textWidth, int cargoWidth)
    {
        var grid = NewPlanGrid(true, textWidth, cargoWidth);
        var cells = LineCells(current.Lines);
        for (var i = 0; i < cells.Count; i++)
        {
            var row = grid.Rows[grid.Rows.Add(cells[i])];
            if (i != current.Here) continue;
            var st = row.DefaultCellStyle;
            st.Font = PlanBoldFont;
            st.BackColor = w.Color;
            st.ForeColor = TextOn(w.Color);
            st.SelectionBackColor = w.Color;
            st.SelectionForeColor = TextOn(w.Color);
        }
        AddTotalRow(grid, LineTotals(current.Lines));
        // a load the game did not tell the SCU of: click its Cargo cell to type it (the program works out the containers)
        bool Typable(int row, int col) => col == 3 && row >= 0 && row < current.Lines.Count && current.Lines[row].Unknown.Count > 0;
        for (var i = 0; i < current.Lines.Count; i++)
        {
            if (Typable(i, 3)) grid.Rows[i].Cells[3].Value = current.Lines[i].CargoText + "  (click to type)";
        }
        grid.CellMouseEnter += (_, e) => grid.Cursor = Typable(e.RowIndex, e.ColumnIndex) ? Cursors.Hand : Cursors.Default;
        grid.CellClick += (_, e) =>
        {
            if (Typable(e.RowIndex, e.ColumnIndex)) AskScu(current.Lines[e.RowIndex].Unknown.Distinct().ToList());
        };
        grid.FitHeight();
        return grid;
    }

    /// <summary>The Queued routes of a worker: the trips after his current one, each with a short route tag (just the stations).</summary>
    PlanGridView QueuedRoutesGrid(List<PlanRow> queued, int textWidth)
    {
        var grid = NewPlanGrid(false, textWidth, 0);
        grid.ShowCellToolTips = true;   // the route of a trip is in the tooltip of its Stops
        if (queued.Count == 0)
        {
            var none = grid.Rows[grid.Rows.Add("", "none", "", "", "", "")];
            none.DefaultCellStyle.ForeColor = Theme.Muted;
        }
        foreach (var r in queued)
        {
            var row = grid.Rows[grid.Rows.Add(QueuedCells(r))];
            foreach (DataGridViewCell cell in row.Cells) cell.ToolTipText = r.Route;
            if (RowColor(r) is { } color) row.DefaultCellStyle.BackColor = color;
        }
        if (queued.Count > 0) AddTotalRow(grid, PlanTotals(queued));
        grid.FitHeight();
        return grid;
    }

    /// <summary>The Total line at the bottom of a table.</summary>
    static void AddTotalRow(DataGridView grid, string[] cells)
    {
        var total = grid.Rows[grid.Rows.Add(cells)];
        total.DefaultCellStyle.Font = PlanBoldFont;
        total.DefaultCellStyle.BackColor = Theme.Calc;
        total.DefaultCellStyle.ForeColor = Theme.Text;
        total.DefaultCellStyle.SelectionBackColor = Theme.Calc;
        total.DefaultCellStyle.SelectionForeColor = Theme.Text;
    }

    /// <summary>
    /// A table that never scrolls: its text column takes the width that the narrow columns leave, and it is as high as its rows (a row with a long text that has to wrap
    /// is as high as that text needs at the width the column has).
    /// </summary>
    sealed class PlanGridView : DataGridView
    {
        bool _fitting;

        /// <summary>The column that takes the room that is left (the others keep their width).</summary>
        public DataGridViewColumn? TextColumn;

        public void FitHeight()
        {
            if (_fitting) return;
            _fitting = true;
            try
            {
                if (TextColumn != null)
                {
                    var narrow = 0;
                    foreach (DataGridViewColumn c in Columns)
                    {
                        if (c != TextColumn && c.Visible) narrow += c.Width;
                    }
                    var wide = Math.Max(TextColumn.MinimumWidth, ClientSize.Width - narrow);
                    if (TextColumn.Width != wide) TextColumn.Width = wide;
                }
                foreach (DataGridViewRow row in Rows)
                {
                    var h = RowTemplate.Height;
                    try
                    {
                        h = Math.Max(h, row.GetPreferredHeight(row.Index, DataGridViewAutoSizeRowMode.AllCells, true));
                    }
                    catch (Exception)
                    {
                        // the text cannot be measured yet: one line
                    }
                    if (row.Height != h) row.Height = h;
                }
                var total = ColumnHeadersHeight + Rows.GetRowsHeight(DataGridViewElementStates.Visible) + 3;
                if (Height != total) Height = total;
            }
            catch (InvalidOperationException)
            {
                // the table is busy changing its own layout, when no column or row may change its size: the next resize, or the refit after the page is shown, does it
            }
            finally
            {
                _fitting = false;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitHeight();
        }
    }

    /// <summary>The text of the # cell of a queued trip: its number.</summary>
    static string RowLabel(PlanRow r) => r.Label;

    /// <summary>Black or white, whichever reads better on this colour.</summary>
    static Color TextOn(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 140 ? Color.Black : Color.White;

    static string Gm(double v) => v.ToString("N1", CultureInfo.InvariantCulture) + " Gm";

    /// <summary>m:ss or h:mm:ss.</summary>
    static string FlightSpan(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return t.TotalHours >= 1 ? (int)t.TotalHours + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : t.Minutes + ":" + t.Seconds.ToString("00");
    }

    /// <summary>The value with spaces between the thousands, or nothing when there is none.</summary>
    static string ValueText(decimal v) => v <= 0 ? "" : Math.Round(v).ToString("N0");

    /// <summary>The estimated flying time of the route, spool time and jumps with the drive of the worker, no loading.</summary>
    static string FlightText(PlanRow r) => r.Flight <= 0 ? "" : "~" + FlightSpan(r.Flight);
}
