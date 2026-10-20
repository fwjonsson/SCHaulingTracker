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
        public bool Hidden;              // a planned trip that is shown inside the Current row (the continuation of the run in progress), not as a row of its own
        public bool Estimate;            // the time is an estimate (~) although this is a run
        public bool Solid;               // a planned trip that is flown first (solid line on the map); the later ones are dotted
        public string? From;             // a trip that continues a run in progress starts at this station
        public bool IsPlan => Level == "plan";
    }

    sealed class PlanWorkerView
    {
        public string Name = "";
        public int Capacity;
        public string Drive = "";        // the quantum drive, or that none is set
        public Color Color;
        public List<PlanRow> Rows = new();
        public List<PlanRow> Planned => Rows.Where(r => r.IsPlan).ToList();
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

    readonly Button _planBtn = new();
    readonly Button _resetTripsBtn = new();
    readonly Label _planSummary = new();
    readonly Label _planNote = new();
    readonly ThemedTabControl _planTabs = new();   // Overview and one page per crew member
    string _planShown = "";                        // what the pages show now, so they are only rebuilt when it changes
    PlanCore? _planCore;

    Control BuildPlannerPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));    // title
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));    // hint
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));    // button
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));    // summary
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));    // note
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 62));     // the pages
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));   // Cargo Runs
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 38));     // the copy of Cargo Runs

        t.Controls.Add(Caption("Trip Planner", "Divides the open batches over the crew, so everybody flies as little as possible and you finish together"), 0, 0);

        var hint = new Label
        {
            Text = "Who flies, how many batches each ship holds and which quantum drive each has is set in the Party table on the front page. The runs below get their worker from the logs, and from these trips for members without a log. A run shows the time it really took; a planned trip shows ~ the estimated flying time (spool time + distance / drive speed for every jump, no loading). Green = done, orange = some cargo lost, red = all lost. The run you are flying is your Current row, in your colour. Plan Trips locks the next stop of everybody: planning again (for example when someone joins) leaves those alone and plans the rest around them. Reset Trips takes the locks away. Both ask first.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
            Tag = "muted",
        };
        t.Controls.Add(hint, 0, 1);

        StyleButton(_planBtn, "Plan Trips");
        _planBtn.Margin = new Padding(0, 5, 0, 5);
        _planBtn.Click += (_, _) => PlanTrips();
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        StyleButton(_resetTripsBtn, "Reset Trips");
        _resetTripsBtn.Margin = new Padding(8, 5, 0, 5);
        _resetTripsBtn.Click += (_, _) => ResetTrips();
        bar.Controls.Add(_planBtn);
        bar.Controls.Add(_resetTripsBtn);
        t.Controls.Add(bar, 0, 2);

        _planSummary.Dock = DockStyle.Fill;
        _planSummary.TextAlign = ContentAlignment.MiddleLeft;
        _planSummary.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        t.Controls.Add(_planSummary, 0, 3);
        _planNote.Dock = DockStyle.Fill;
        _planNote.TextAlign = ContentAlignment.MiddleLeft;
        _planNote.ForeColor = Color.DimGray;
        _planNote.Tag = "muted";
        t.Controls.Add(_planNote, 0, 4);

        _planTabs.Dock = DockStyle.Fill;
        _planTabs.Margin = new Padding(0);
        t.Controls.Add(_planTabs, 0, 5);
        t.Controls.Add(SectionTitle("Cargo Runs"), 0, 6);
        BuildPlanRunsGrid();
        t.Controls.Add(_planRunsGrid, 0, 7);
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
    /// A pickup that no log names (a member without a log) is credited to the worker whose planned trip needed a batch at that station, in the order
    /// of the plan. It runs before the boards change, so the plan still holds the batches that were just picked up. What the logs show always wins.
    /// </summary>
    void ClaimNewPickups()
    {
        if (_current == null || _inputs == null || _log == null) return;
        var since = _data.PlanCreditSince ??= DateTime.UtcNow;
        var core = _planCore;
        if (core == null || core.Trips.Count == 0) return;

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

        foreach (var group in fresh.GroupBy(f => f.Station))
        {
            var queue = new List<string>();   // one entry for every batch the plan expects at this station
            foreach (var trip in core.Trips)
            {
                foreach (var (id, n) in trip.Stops)
                {
                    if (id != group.Key) continue;
                    for (var i = 0; i < n; i++) queue.Add(trip.Worker);
                }
            }
            var next = 0;
            foreach (var f in group.OrderBy(x => x.Time))
            {
                if (next >= queue.Count) break;
                _inputs.PickupClaims[f.Key] = queue[next++];
            }
        }
        Storage.Save(_data);
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
        var text = w.Name + "  –  ship holds " + w.Capacity + (w.Capacity == 1 ? " batch" : " batches") + "  –  " + w.Drive + "  –  "
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
                Route = string.Join("  →  ", parts.Select(x => x.Name + " ×" + x.N)),
                Batches = level == "All" ? "failed" : t.End.HasValue ? t.HandedIn.ToString(CultureInfo.InvariantCulture) : picked.ToString(CultureInfo.InvariantCulture),
                BatchCount = picked,
                Gm = stops.Count > 0 ? PyroDistances.Run(stops) : 0,
                Seconds = t.End.HasValue ? Math.Max(0, (t.End.Value - t.Start).TotalSeconds) : 0,
                Level = level == "All" ? "all" : level == "Little" ? "little" : t.End.HasValue ? "done" : "active",
                Stops = stops,
            });
        }
        return rows;
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

            var note = "Planning every open batch, whoever is assigned to the location now.";
            if (starts.Any(t => t.FirstId != null)) note += " A locked stop is the next stop of its worker; the rest of his trip is planned.";
            if (starts.Any(t => t.Loaded > 0)) note += " A run in progress counts in the ship: the plan continues it.";
            if (unknown.Count > 0) note += " Not in the distance table, so left out: " + string.Join(", ", unknown) + ".";
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
        if (_inputs != null)
        {
            foreach (var r in _openRows)
            {
                var left = r.Missions - r.Loaded;   // batches not picked up yet
                if (left <= 0) continue;
                var id = StationOf(r.Zone);
                if (id == null)
                {
                    unknown.Add(LocationName(r.Zone) + " (" + left + ")");
                    continue;
                }
                remaining[id] = remaining.GetValueOrDefault(id) + left;
            }
        }

        var core = CoreFor(crew, remaining, unknown);

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
                    Gm = mine[k].Distance,
                    Seconds = mine[k].Seconds,
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
                runRow.Batches = runRow.BatchCount + "/" + c.Capacity;
                runRow.Stops = runRow.Stops.Concat(cont.Stops.Select(s => s.Id)).ToList();
                runRow.Gm = PyroDistances.Run(runRow.Stops);
                runRow.Seconds = cont.Seconds;   // what is left of the flight, from where he is
                runRow.Estimate = true;
                w.Rows.First(r => r.Trip == cont).Hidden = true;
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
            w.Rows.Select(r => w.Name + "|" + w.Capacity + "|" + r.Label + "|" + r.Hidden + "|" + r.Locked + "|" + r.First + "|" + r.Solid + "|" + r.Route + "|" + r.Batches + "|" + r.Level + "|" + r.Gm.ToString("F1", CultureInfo.InvariantCulture) + "|" + r.Seconds.ToString("F0", CultureInfo.InvariantCulture) + "|" + w.Drive)));
        return plan;
    }

    /// <summary>Shows the plan on its pages. It only runs while the Trip Planner tab is on screen.</summary>
    void RefreshPlanner()
    {
        var plan = GetPlan();
        _planSummary.Text = plan.Summary;
        _planNote.Text = plan.Note;
        if (plan.Signature == _planShown) return;   // nothing changed, so nothing flickers
        _planShown = plan.Signature;

        var selected = _planTabs.SelectedTab?.Text ?? "Overview";
        _planTabs.SuspendLayout();
        foreach (var old in _planTabs.TabPages.Cast<TabPage>().ToList())
        {
            _planTabs.TabPages.Remove(old);
            old.Dispose();
        }
        _planTabs.TabPages.Add(PlanPage("Overview", plan, null));
        foreach (var w in plan.Workers) _planTabs.TabPages.Add(PlanPage(w.Name, plan, w));
        var keep = _planTabs.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Text == selected);
        if (keep != null) _planTabs.SelectedTab = keep;
        _planTabs.ResumeLayout(true);
        _planTabs.RefreshStyle();
    }

    /// <summary>A page: the map on the left, the tables on the right. only = null is the overview with everybody.</summary>
    TabPage PlanPage(string title, PlanView plan, PlanWorkerView? only)
    {
        var page = new TabPage(title) { BackColor = Theme.Window };
        // the map on the left, square and as large as the page allows, the tables on the right
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(8) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var map = new PyroMapView { Dock = DockStyle.Top, Margin = new Padding(0, 0, 12, 0) };
        var host = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Margin = new Padding(0) };
        host.Resize += (_, _) => FitPlanBlocks(host);

        var workers = only == null ? plan.Workers : new List<PlanWorkerView> { only };
        if (only == null)
        {
            // everybody: one colour per worker, his next trip in bold
            foreach (var w in workers)
            {
                var k = 0;
                foreach (var r in w.Planned)
                {
                    map.Routes.Add(new MapRoute { Label = k == 0 ? w.Name : "", Color = w.Color, Stops = r.Stops, From = r.From, Bold = r.Solid, Dashed = !r.Solid, Tag = r });
                    k++;
                }
            }
        }
        else
        {
            // one worker: his colour, the next trip solid and bold, the later ones dotted (click a planned trip in the table to bold that one)
            var k = 0;
            foreach (var r in only.Planned)
            {
                map.Routes.Add(new MapRoute { Label = r.Label + "  " + Gm(r.Gm), Color = only.Color, Stops = r.Stops, From = r.From, Bold = r.Solid, Dashed = !r.Solid, Tag = r });
                k++;
            }
        }

        Action<PlanRow>? onSelect = null;
        if (only != null)
        {
            onSelect = row =>
            {
                foreach (var route in map.Routes) route.Bold = ReferenceEquals(route.Tag, row);
                map.Invalidate();
            };
        }
        foreach (var w in workers) host.Controls.Add(PlanBlock(w, onSelect));
        FitPlanBlocks(host);

        // the map is a square: as high as the page, but at most 55% of its width (the tables keep the rest)
        void FitMap()
        {
            var room = Math.Max(320, t.ClientSize.Height - t.Padding.Vertical);
            var side = Math.Max(320, Math.Min(room, (int)((t.ClientSize.Width - t.Padding.Horizontal) * 0.5)));
            t.ColumnStyles[0].Width = side + map.Margin.Horizontal;
            map.Height = side;
        }
        t.Resize += (_, _) => FitMap();
        FitMap();
        t.Controls.Add(map, 0, 0);
        t.Controls.Add(host, 1, 0);
        page.Controls.Add(t);
        return page;
    }

    static void FitPlanBlocks(FlowLayoutPanel host)
    {
        var width = Math.Max(200, host.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
        foreach (Control c in host.Controls) c.Width = width;
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
            grid.Columns.Add(Col("Distance", 110, true, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(Col("Time", 90, true, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(Col("Gm / Batch", 100, true, DataGridViewContentAlignment.MiddleRight));
            FitColumns(grid);   // the columns share the width, so nothing is cut off or has to scroll sideways
            var boldFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            foreach (var r in w.Rows.Where(x => !x.Hidden))
            {
                var idx = grid.Rows.Add(
                    RowLabel(r),
                    r.Route,
                    r.Batches,
                    r.Gm > 0 ? Gm(r.Gm) : "",
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
    static string TimeText(PlanRow r) => r.Seconds <= 0 ? "" : (r.IsPlan || r.Estimate ? "~" : "") + FlightSpan(r.Seconds);
}
