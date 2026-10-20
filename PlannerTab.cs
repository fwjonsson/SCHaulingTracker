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
        public int BatchCount;           // batches picked up or planned, for Gm per batch
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
        public string? From;             // a trip that continues a run in progress starts at this station
        public bool IsPlan => Level == "plan";
    }

    sealed class PlanWorkerView
    {
        public string Name = "";
        public int Capacity;
        public string DriveNote = "";    // only when no drive is set
        public string Drive = "";        // the quantum drive, or that none is set
        public Color Color;
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
    }

    // the two buttons sit in the top bar of the window, level with Options and Stop Session, while the Trip Planner tab is shown
    readonly ToolStripButton _planBtn = new() { DisplayStyle = ToolStripItemDisplayStyle.Text, Text = "Plan Trips", Padding = new Padding(8, 0, 8, 0), Visible = false };
    readonly ToolStripButton _resetTripsBtn = new() { DisplayStyle = ToolStripItemDisplayStyle.Text, Text = "Reset Trips", Padding = new Padding(8, 0, 8, 0), Visible = false };
    readonly Label _planNote = new();
    readonly Panel _planBody = new();              // the page: the map and the tables (rebuilt when the plan changes)
    string _planShown = "";                        // what the pages show now, so they are only rebuilt when it changes
    PlanCore? _planCore;

    Control BuildPlannerPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // a warning, only when there is one
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 62));     // the map and the tables
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));   // Cargo Runs
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 38));     // the copy of Cargo Runs

        // Plan Trips and Reset Trips are in the top bar of the window (AddSessionControlsToMenu); hold the mouse over them for the explanation
        _planBtn.ToolTipText = "Divides the open batches over the crew, so everybody flies as little as possible and you finish together.\n\n"
            + "Who flies, how many batches each ship holds and which quantum drive each has is set in the Party table on the front page. The runs below get their worker from the logs, and from these trips for members without a log.\n\n"
            + "Flight time is the estimated flying time of that whole route with the drive of the worker (~, no loading); Time is how long a run really took. Green = done, orange = some cargo lost, red = all lost. The run you are flying is your Current row, in your colour.\n\n"
            + "Plan Trips locks the next stop of everybody: planning again (for example when someone joins) leaves those alone and plans the rest around them, and a run in progress counts in the ship. Reset Trips takes the locks away. Both ask first.";
        _resetTripsBtn.ToolTipText = "Takes the locked stops away, so the trips can be planned from scratch. Asks first.";

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

    /// <summary>
    /// Plan Trips: asks first, then works the plan out again and locks every worker's Current trip to him. Locked trips are never changed by
    /// planning again (someone joining, more contracts), only the trips after them are.
    /// </summary>
    void PlanTrips()
    {
        if (_inputs == null)
        {
            SetStatus("There is no session to plan.");
            return;
        }
        var answer = MessageBox.Show(this,
            "Plan the trips now?\n\nThe next stop of everyone is locked to him from now on. Planning again later only changes the other trips, so someone who joins gets his share of the rest.",
            "Plan Trips", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        _planCore = null;
        _planShown = "";
        GetPlan();
        var core = _planCore;
        if (core == null || core.Trips.Count == 0)
        {
            SetStatus("Nothing is planned: no open batches, or nobody is flying.");
            RefreshPlanner();
            return;
        }
        var locked = 0;
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
            if (trip.Stops.Count == 0) continue;
            _inputs.LockedTrips[LockKey(trip.Worker) ?? trip.Worker] = new List<LockedStop> { new() { Id = trip.Stops[0].Id, Batches = trip.Stops[0].Batches } };   // only his next stop
            locked++;
        }
        Storage.Save(_data);
        SetStatus("Planned the trips and locked the next stop of " + locked + (locked == 1 ? " worker." : " workers."));
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

    /// <summary>Clears all locked Current trips (asks first). The plan is free again until you press Plan Trips.</summary>
    void ResetTrips()
    {
        if (_inputs == null) return;
        var answer = MessageBox.Show(this,
            "Reset the trips?\n\nThe locked Current trips are cleared, so nobody has a trip of his own until you press Plan Trips again. Runs that are done or on the way are not touched.",
            "Reset Trips", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        _inputs.LockedTrips.Clear();
        Storage.Save(_data);
        SetStatus("Reset the trips: no Current trip is locked.");
        _planCore = null;
        _planShown = "";
        RefreshPlanner();
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
        g.Columns.Add(Col("Run Time", 90, true));
        g.Columns.Add(Col("Lost Cargo", 116, true));
        FitColumns(g);
        g.ScrollBars = ScrollBars.Vertical;
    }

    int ShipCapacityOf(string name) =>
        _data.ShipCapacity.TryGetValue(name, out var c) ? Math.Clamp(c, 0, MaxShipCapacity) : DefaultShipCapacity;

    List<string> ActiveCrew() => _data.Party.Where(n => !_data.InactiveParty.Any(x => SameName(x, n))).ToList();

    static string RouteText(PlannedTrip trip) =>
        (trip.From != null ? "From " + PyroDistances.NameOf(trip.From) + "  →  " : "") + StopsText(trip);

    static string StopsText(PlannedTrip trip) =>
        string.Join("  →  ", trip.Stops.Select((s, i) => PyroDistances.NameOf(s.Id) + " ×" + s.Batches + (i == 0 && trip.Locked ? " (locked)" : "")));   // the locked stop says so

    static string PlanTitle(PlanWorkerView w)
    {
        var planned = w.Planned;
        var done = w.Rows.Count - planned.Count;
        var text = w.Name + "  –  ship holds " + w.Capacity + (w.Capacity == 1 ? " batch" : " batches") + "  –  " + (w.DriveNote.Length > 0 ? w.DriveNote + "  –  " : "")
            + done + (done == 1 ? " run so far" : " runs so far") + "  –  ";
        text += planned.Count == 0
            ? "nothing planned"
            : planned.Count + (planned.Count == 1 ? " trip planned (" : " trips planned (") + planned.Sum(r => r.BatchCount) + " batches, " + Gm(planned.Sum(r => r.Gm)) + ", about " + FlightSpan(planned.Sum(r => r.Seconds)) + " of flying)";
        return text;
    }

    /// <summary>Red = all cargo lost, orange = some lost, green = done, yellow = on the way. Planned trips have no colour.</summary>
    static Color? RowColor(PlanRow r) => r.Level switch
    {
        "all" => LostRed,
        "little" => LostOrange,
        "done" => DoneGreen,
        "active" => PartialYellow,
        _ => null,
    };

    /// <summary>The runs of one worker in the selected session, oldest first, as lines for his table.</summary>
    List<PlanRow> RunRows(string name)
    {
        var rows = new List<PlanRow>();
        foreach (var t in _trips.Where(x => SameName(x.Worker, name)).OrderBy(RunStartedAt))
        {
            var level = t.Manual ? "All" : LostLevel(t.Key);
            var stops = new List<string>();
            var parts = new List<(string Name, int N)>();
            var picked = 0;
            foreach (var p in t.Pickups.OrderBy(x => x.Time))
            {
                var n = p.MissionIds.Count > 0 ? p.MissionIds.Count : p.Missions;
                picked += n;
                var shown = LocationName(p.Zone);
                if (parts.Count > 0 && parts[^1].Name == shown) parts[^1] = (shown, parts[^1].N + n);
                else parts.Add((shown, n));
                var id = StationOf(p.Zone);
                if (id != null) stops.Add(id);
            }
            rows.Add(new PlanRow
            {
                Label = "Run #" + t.Number,
                Route = string.Join("  →  ", parts.Select(x => x.Name + " ×" + x.N + (t.End.HasValue ? "" : " (picked up)"))),   // the run in progress: what is already on board says so
                Batches = level == "All" ? "failed" : t.End.HasValue ? t.HandedIn.ToString(CultureInfo.InvariantCulture) : picked.ToString(CultureInfo.InvariantCulture),
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

    /// <summary>The run the worker is flying (picked up, not handed in yet): the batches on board and the station of the last pickup, or null.</summary>
    (int Loaded, string? From, DateTime Last)? ActiveRunOf(string name)
    {
        var loaded = 0;
        string? from = null;
        DateTime? latest = null;
        var any = false;
        foreach (var t in _trips.Where(t => SameName(t.Worker, name) && !t.End.HasValue))
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
    PlanCore CoreFor(List<(string Name, int Capacity, QtDrive Drive)> crew, Dictionary<string, int> open, List<string> unknown)
    {
        for (var pass = 0; ; pass++)
        {
            var remaining = new Dictionary<string, int>(open);
            var starts = new List<WorkerStart>();
            var waiting = new List<string>();   // on his way to nothing locked: the first stop of his next planned trip gets locked once it is planned
            if (_inputs != null)
            {
                var changed = false;
                // somebody who left the party (or does not fly) has no locked stop: his batches go back to the others
                foreach (var gone in _inputs.LockedTrips.Keys.Where(k => !crew.Any(c => c.Capacity > 0 && SameName(c.Name, k))).ToList())
                {
                    _inputs.LockedTrips.Remove(gone);
                    changed = true;
                }
                foreach (var c in crew.Where(x => x.Capacity > 0))
                {
                    var run = ActiveRunOf(c.Name);
                    var start = new WorkerStart { Worker = c.Name, Loaded = run?.Loaded ?? 0, From = run?.From };
                    var lockKey = LockKey(c.Name);
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
                    if (start.FirstId != null || start.Loaded > 0) starts.Add(start);
                }
                if (changed) Storage.Save(_data);
            }

            var key = string.Join("|", crew.Select(c => c.Name + ":" + c.Capacity + ":" + c.Drive.Key))
                + "#" + string.Join("|", remaining.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + ":" + kv.Value))
                + "#" + string.Join("|", unknown)
                + "#" + string.Join("|", starts.Select(t => t.Worker + "=" + t.FirstId + "x" + t.FirstBatches + "/" + t.Loaded + "@" + t.From));
            if (waiting.Count == 0 && _planCore != null && _planCore.Key == key) return _planCore;

            var note = unknown.Count > 0 ? "Not in the distance table, so left out: " + string.Join(", ", unknown) + "." : "";
            var core = new PlanCore
            {
                Key = key,
                Trips = TripPlanner.Plan(remaining, crew, starts),
                Batches = remaining.Where(kv => kv.Value > 0).Sum(kv => kv.Value) + starts.Sum(t => t.FirstBatches),
                Note = note,
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
                if (next == null || next.Stops.Count == 0)
                {
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
            }
        }

        var core = CoreFor(crew, remaining, unknown);
        decimal PerBatch(string id) => worth.TryGetValue(id, out var x) && x.Batches > 0 ? x.Value / x.Batches : 0;   // the average batch at that station

        var plan = new PlanView { Note = core.Note };
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
            w.Rows.AddRange(RunRows(c.Name));
            // the run in progress is his Current trip (a locked stop is marked in the Route cell of a planned trip)
            var runRow = w.Rows.LastOrDefault(r => r.Level == "active");
            if (runRow != null)
            {
                runRow.Label = "Current";
                runRow.First = true;
            }
            var mine = core.Trips.Where(t => t.Worker == c.Name).ToList();
            for (var k = 0; k < mine.Count; k++)
            {
                w.Rows.Add(new PlanRow
                {
                    Label = k > 0 ? "Plan " + (k + 1) : runRow == null ? "Current" : mine[k].Special ? "Continue" : "Next",
                    First = k == 0 && runRow == null,
                    Solid = k == 0 && (runRow == null || mine[k].Special),
                    Locked = mine[k].Locked,
                    Route = RouteText(mine[k]),
                    Batches = mine[k].Batches + "/" + (mine[k].Room > 0 ? mine[k].Room : c.Capacity),
                    BatchCount = mine[k].Batches,
                    Value = mine[k].Stops.Sum(s => s.Batches * PerBatch(s.Id)),
                    Gm = mine[k].Distance,
                    Seconds = mine[k].Seconds,
                    Flight = mine[k].Seconds,
                    Level = "plan",
                    Stops = mine[k].Stops.Select(s => s.Id).ToList(),
                    From = mine[k].From,
                    Trip = mine[k],
                });
            }
            // what the plan adds to the run in progress is part of his Current row: the stops already picked up, then the ones still to pick up
            if (runRow != null && mine.Count > 0 && mine[0].Special && mine[0].Stops.Count > 0)
            {
                var cont = mine[0];
                runRow.Route += "  →  " + StopsText(cont);
                runRow.BatchCount += cont.Batches;
                runRow.Value += cont.Stops.Sum(s => s.Batches * PerBatch(s.Id));
                runRow.Batches = runRow.BatchCount + "/" + c.Capacity;
                runRow.Stops = runRow.Stops.Concat(cont.Stops.Select(s => s.Id)).ToList();
                runRow.Gm = PyroDistances.Run(runRow.Stops);
                runRow.Flight = FlightOf(c.Name, runRow.Stops);   // the whole run, Ruin to Ruin
                w.Rows.First(r => r.Trip == cont).Hidden = true;
            }
            else if (runRow != null && runRow.Stops.Count > 0)
            {
                runRow.OnMap = true;   // nothing is added to it (full ship): the run itself is his solid line
                runRow.Solid = true;
            }
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

        plan.Signature = plan.Summary + "¦" + (Theme.Dark ? "dark" : "light") + "¦" + string.Join("¦", plan.Workers.SelectMany(w =>
            w.Rows.Select(r => w.Name + "|" + w.Capacity + "|" + r.Label + "|" + r.Hidden + "|" + r.Locked + "|" + r.First + "|" + r.Solid + "|" + r.Route + "|" + r.Batches + "|" + r.Value.ToString("F0", CultureInfo.InvariantCulture) + "|" + r.Level + "|" + r.Gm.ToString("F1", CultureInfo.InvariantCulture) + "|" + r.Flight.ToString("F0", CultureInfo.InvariantCulture) + "|" + r.Seconds.ToString("F0", CultureInfo.InvariantCulture) + "|" + w.Drive)));
        return plan;
    }

    /// <summary>Shows the plan: the map on the left, the tables on the right. It only runs while the Trip Planner tab is on screen.</summary>
    void RefreshPlanner()
    {
        var plan = GetPlan();
        _planNote.Text = plan.Note;
        _planNote.Visible = plan.Note.Length > 0;
        if (plan.Signature == _planShown) return;   // nothing changed, so nothing flickers
        _planShown = plan.Signature;

        _planBody.SuspendLayout();
        foreach (var old in _planBody.Controls.Cast<Control>().ToList())
        {
            _planBody.Controls.Remove(old);
            old.Dispose();
        }
        _planBody.Controls.Add(PlanPage(plan));
        _planBody.ResumeLayout(true);
    }

    /// <summary>The map on the left, square and as large as the page allows, the tables of everybody on the right.</summary>
    Control PlanPage(PlanView plan)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 4, 0, 0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var map = new PyroMapView { Dock = DockStyle.Top, Margin = new Padding(0, 0, 12, 0) };
        var host = new PlanHost { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Margin = new Padding(0) };
        host.Resize += (_, _) => FitPlanBlocks(host);

        // one colour per worker, his current trip in bold and solid, the later ones dotted
        foreach (var w in plan.Workers)
        {
            var k = 0;
            foreach (var r in w.MapRows)
            {
                map.Routes.Add(new MapRoute { Label = k == 0 ? w.Name : "", Color = w.Color, Stops = r.Stops, From = r.From, Bold = r.Solid, Dashed = !r.Solid, Tag = r });
                k++;
            }
        }
        foreach (var w in plan.Workers) host.Controls.Add(PlanBlock(w, null));
        FitPlanBlocks(host);

        // the map is a square: as high as the page, but at most half of its width (the tables keep the rest)
        void FitMap()
        {
            var room = Math.Max(320, t.ClientSize.Height - t.Padding.Vertical);
            var side = Math.Max(320, Math.Min(room, (int)(t.ClientSize.Width * 0.4)));
            t.ColumnStyles[0].Width = side + map.Margin.Horizontal;
            map.Height = side;
        }
        t.Resize += (_, _) => FitMap();
        FitMap();
        t.Controls.Add(map, 0, 0);
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

    /// <summary>A title and the table of one crew member: the runs so far, then the planned trips.</summary>
    Control PlanBlock(PlanWorkerView w, Action<PlanRow>? onSelect)
    {
        var block = new Panel { Margin = new Padding(0, 0, 0, 14) };
        var label = new Label
        {
            Text = PlanTitle(w),
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.BottomLeft,
        };
        var height = label.Height;

        if (w.Rows.Count > 0)
        {
            var grid = new DataGridView();
            StyleGrid(grid);
            grid.Dock = DockStyle.Top;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.ScrollBars = ScrollBars.None;
            grid.Columns.Add(Col("Trip", 110, true));
            grid.Columns.Add(Col("Route (Ruin, then these, then back to Ruin)", 430, true, DataGridViewContentAlignment.MiddleLeft, true));
            grid.Columns.Add(Col("Batches", 80, true));
            grid.Columns.Add(Col("Value", 100, true, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(Col("Distance", 110, true, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(Col("Flight time", 100, true, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(Col("Time", 90, true, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(Col("Gm / Batch", 100, true, DataGridViewContentAlignment.MiddleRight));
            FitColumns(grid);   // the columns share the width, so nothing is cut off or has to scroll sideways
            // only the Route column is long (it takes what is left); the others keep a narrow width of their own: Trip, Batches, Value, Distance, Flight time, Time, Gm / Batch
            var narrow = new[] { 90, 0, 66, 96, 82, 90, 76, 82 };
            for (var i = 0; i < grid.Columns.Count && i < narrow.Length; i++)
            {
                if (i == 1)
                {
                    grid.Columns[i].MinimumWidth = 220;
                    continue;
                }
                grid.Columns[i].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                grid.Columns[i].MinimumWidth = 40;
                grid.Columns[i].Width = narrow[i];
            }
            var boldFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            foreach (var r in w.Rows.Where(x => !x.Hidden))
            {
                var idx = grid.Rows.Add(
                    RowLabel(r),
                    r.Route,
                    r.Batches,
                    ValueText(r.Value),
                    r.Gm > 0 ? Gm(r.Gm) : "",
                    FlightText(r),
                    TimeText(r),
                    r.Gm > 0 && r.BatchCount > 0 ? (r.Gm / r.BatchCount).ToString("N1", CultureInfo.InvariantCulture) : "");
                var row = grid.Rows[idx];
                row.Tag = r;
                if (RowColor(r) is { } color) row.DefaultCellStyle.BackColor = color;
                if (r.First) row.DefaultCellStyle.Font = boldFont;
                if (r.First)
                {
                    // the whole Current row of a worker has his colour (his run in progress, or his first planned trip), on the Overview and on his own page
                    var st = row.DefaultCellStyle;
                    st.BackColor = w.Color;
                    st.ForeColor = TextOn(w.Color);
                    st.SelectionBackColor = w.Color;
                    st.SelectionForeColor = TextOn(w.Color);
                }
                else if (r.IsPlan && onSelect != null)   // a worker's own page (the Overview keeps one colour per worker)
                {
                    // the Trip cell has the colour of the worker, the later trips faded like their dotted line on the map
                    var c = r.Solid ? w.Color : Faded(w.Color);
                    var cell = row.Cells[0].Style;
                    cell.BackColor = c;
                    cell.ForeColor = TextOn(c);
                    cell.SelectionBackColor = c;
                    cell.SelectionForeColor = TextOn(c);
                }
            }
            if (onSelect != null)
            {
                grid.SelectionChanged += (_, _) =>
                {
                    if (grid.CurrentRow?.Tag is PlanRow picked && picked.IsPlan) onSelect(picked);
                };
            }
            grid.Height = grid.ColumnHeadersHeight + w.Rows.Count(x => !x.Hidden) * grid.RowTemplate.Height + 3;
            height += grid.Height;
            block.Controls.Add(grid);   // added first, so the title sits above it
        }

        block.Controls.Add(label);
        block.Height = height;
        return block;
    }

    /// <summary>The text of the Trip cell (a locked stop is marked in the Route cell, next to the stop).</summary>
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

    /// <summary>A run: the time it really took. A planned trip: the estimated flight time, with a ~ in front.</summary>
    static string ValueText(decimal v) => v <= 0 ? "" : Math.Round(v).ToString("N0");

    /// <summary>How long a run really took (a planned trip has not been flown yet).</summary>
    static string TimeText(PlanRow r) => r.IsPlan || r.Seconds <= 0 ? "" : FlightSpan(r.Seconds);

    /// <summary>The estimated flying time of the route, spool time and jumps with the drive of the worker, no loading.</summary>
    static string FlightText(PlanRow r) => r.Flight <= 0 ? "" : "~" + FlightSpan(r.Flight);
}
