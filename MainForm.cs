using System.Globalization;
using System.Text;

namespace HaulingTracker;

public sealed partial class MainForm : Form
{
    static Color HeaderBlue => Theme.Header;
    static Color CalcBlue => Theme.Calc;
    const decimal DefaultReward2 = 1268250m;   // payout of a 2 stop mission
    const decimal DefaultReward3 = 1691000m;   // payout of a 3 stop mission
    static Color LostRed => Theme.Red;
    static Color DoneGreen => Theme.Green;
    static Color LostOrange => Theme.Orange;
    static Color PartialYellow => Theme.Yellow;

    const string DefaultLogPath = @"C:\Program Files\Roberts Space Industries\StarCitizen";

    readonly List<HaulingLog> _logs = new();
    HaulingLog? _log;
    readonly Button _addBtn = new();
    readonly DataGridView _tripGrid = new();
    List<WorkerTrip> _trips = new();
    readonly AppData _data = Storage.Load();
    List<SessionGroup> _sessions = new();
    SessionGroup? _current;
    SessionInputs? _inputs;
    SessionSummary? _summary;
    List<LocationRow> _rows = new();
    decimal _revenue;
    int _renderedVersion = -1;
    bool _updating;

    // live watching of the running game's Game.log
    bool _live;

    readonly ComboBox _sessionBox = new();
    readonly Button _openBtn = new();
    readonly Button _liveBtn = new();
    readonly Button _resetBtn = new();
    readonly Button _copyBtn = new();
    readonly Label _status = new();

    readonly TextBox _startBox = new();
    readonly TextBox _endBox = new();
    readonly Label _durationVal = new();
    readonly Label _missionsVal = new();
    readonly Label _revenueVal = new();
    readonly Label _hourlyVal = new();

    readonly DataGridView _rewards = new();
    readonly Label _rewardsTotalMissions = new();
    readonly Label _rewardsTotalRevenue = new();

    readonly DataGridView _logGrid = new();

    readonly System.Windows.Forms.Timer _timer = new();

    public MainForm()
    {
        Text = "Hauling Session Tracker";
        Font = new Font("Segoe UI", 10f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1500, 860);
        MinimumSize = new Size(1250, 700);
        try
        {
            // the window and taskbar icon is the one built into the exe
            if (Environment.ProcessPath is { } exe) Icon = Icon.ExtractAssociatedIcon(exe);
        }
        catch
        {
        }
        Theme.Dark = _data.DarkMode;
        BackColor = Theme.Window;

        BuildUi();
        ApplyTheme(this);

        _timer.Interval = 1500;
        _timer.Tick += (_, _) => PollLive();
        FormClosing += (_, _) =>
        {
            if (!_skipSave) Storage.Save(_data);
        };
        Storage.SaveFailed += msg =>
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => SetStatus("Could not save the data file: " + msg)));
        };

        ClearView();
        SetStatus("Open a log file, or press \"Watch live Game.log\" while you play.");
    }

    // ------------------------------------------------------------------ layout

    void BuildUi()
    {
        var root = _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 420));   // fitted to the boards by FitTopArea
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // the run board takes what is left and scrolls
        var tabs = new ThemedTabControl { Dock = DockStyle.Fill };
        var sessionPage = new TabPage("Session") { BackColor = Color.White };
        var debtsPage = new TabPage("Debts") { BackColor = Color.White };
        tabs.TabPages.Add(sessionPage);
        tabs.TabPages.Add(debtsPage);
        Controls.Add(tabs);
        sessionPage.Controls.Add(root);
        debtsPage.Controls.Add(BuildDebtsPage());
        var historyPage = new TabPage("History") { BackColor = Color.White };
        tabs.TabPages.Add(historyPage);
        historyPage.Controls.Add(BuildHistoryPage());
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (tabs.SelectedIndex >= 1) RefreshDebts();
        };

        // top area: the pickup board and the session summary side by side, the leaderboard under both
        var main = _main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 450));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 306));   // both rows are fitted to the boards by FitTopArea
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));

        var left = _left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        BuildLogGrid();
        BuildStatsGrid();
        _logGrid.Margin = new Padding(0, 3, 3, 3);     // the right gap keeps it clear of the summary tables
        _statsGrid.Margin = new Padding(0, 3, 0, 3);   // runs to the right edge, like the run board
        foreach (var grid in new[] { _logGrid, _statsGrid })
        {
            grid.RowsAdded += (_, _) => FitTopArea();
            grid.RowsRemoved += (_, _) => FitTopArea();
        }
        Shown += (_, _) => FitTopArea();
        left.Controls.Add(SectionTitle("Hauling Tracker"), 0, 0);
        left.Controls.Add(_logGrid, 0, 1);

        var board = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        board.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));
        board.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        board.Controls.Add(SectionTitle("Session Leaderboard"), 0, 0);
        board.Controls.Add(_statsGrid, 0, 1);

        main.Controls.Add(left, 0, 0);
        main.Controls.Add(BuildHeader(), 1, 0);
        main.Controls.Add(board, 0, 1);
        main.SetColumnSpan(board, 2);

        // the run board with its title
        var runs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        runs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        runs.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));
        runs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        runs.Controls.Add(SectionTitle("Cargo Runs"), 0, 0);

        BuildTripGrid();
        root.Controls.Add(BuildToolbar(), 0, 0);
        root.Controls.Add(main, 0, 1);
        runs.Controls.Add(_tripGrid, 0, 1);
        root.Controls.Add(runs, 0, 2);
    }

    const int TitleHeight = 30;

    TableLayoutPanel? _root, _left, _main;
    bool _fitPending;

    /// <summary>
    /// The pickup board and the leaderboard are always shown in full, the run board gets what is left (and scrolls).
    /// Called whenever their rows change or the window is resized.
    /// </summary>
    void FitTopArea()
    {
        if (_fitPending || !IsHandleCreated || _root == null || _left == null || _main == null) return;
        _fitPending = true;
        BeginInvoke(new Action(() =>
        {
            _fitPending = false;
            if (_root == null || _left == null || _main == null) return;
            int Need(DataGridView g)
            {
                var rows = 0;
                foreach (DataGridViewRow r in g.Rows) rows += r.Height + r.DividerHeight;
                return g.ColumnHeadersHeight + Math.Max(rows, g.RowTemplate.Height) + 4 + g.Margin.Vertical;
            }
            var pickups = Need(_logGrid);
            var leaders = Need(_statsGrid);

            // set the heights, then check that every row really is shown and add what is missing
            for (var pass = 0; pass < 4; pass++)
            {
                var title = _left.GetRowHeights().FirstOrDefault();   // as drawn, also on a scaled screen
                if (title <= 0) title = TitleHeight;
                _left.RowStyles[1].Height = pickups;
                var row0 = Math.Max(title + pickups, TitleHeight + 112 + 14 + 170 + 6);   // the summary on the right needs its own height too
                var row1 = title + leaders;
                _main.RowStyles[0].Height = row0;
                _main.RowStyles[1].Height = row1;
                _root.RowStyles[1].Height = row0 + row1;
                _root.PerformLayout();
                _main.PerformLayout();
                _left.PerformLayout();

                var missingPickups = _logGrid.Rows.Count - _logGrid.DisplayedRowCount(false);
                var missingLeaders = _statsGrid.Rows.Count - _statsGrid.DisplayedRowCount(false);
                if (missingPickups <= 0 && missingLeaders <= 0) break;
                if (missingPickups > 0) pickups += missingPickups * _logGrid.RowTemplate.Height;
                if (missingLeaders > 0) leaders += missingLeaders * _statsGrid.RowTemplate.Height;
            }
        }));
    }

    static Label SectionTitle(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.BottomLeft,
        Font = new Font("Segoe UI", 12f, FontStyle.Bold),
        Margin = new Padding(0),
        Padding = new Padding(2, 0, 0, 3),
    };

    Control BuildToolbar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
        };

        StyleButton(_openBtn, "Open Log File(s)…");
        _openBtn.Click += (_, _) => OpenLogs();

        StyleButton(_liveBtn, "Watch Live Game.log");
        _liveBtn.Click += (_, _) => ToggleLive();

        StyleButton(_addBtn, "Add Live Source…");
        _addBtn.Click += (_, _) => AddSource();

        StyleButton(_partyBtn, "Party…");
        _partyBtn.Click += (_, _) => EditParty();

        StyleButton(_sourcesBtn, "Sources…");
        _sourcesBtn.Click += (_, _) => ShowSources();

        _channelBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _channelBox.Width = 90;
        _channelBox.Margin = new Padding(0, 6, 10, 4);
        _channelBox.Items.AddRange(new object[] { "Auto", "LIVE", "HOTFIX", "PTU", "EPTU" });
        _channelBox.SelectedItem = _data.Channel is { Length: > 0 } ch && _channelBox.Items.Contains(ch) ? ch : "Auto";
        _channelBox.SelectedIndexChanged += ChannelChanged;


        _sessionBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _sessionBox.Width = 280;
        _sessionBox.Margin = new Padding(0, 6, 10, 4);
        _sessionBox.SelectedIndexChanged += SessionChanged;

        StyleButton(_resetBtn, "Use Log Times");
        _resetBtn.Click += (_, _) => ResetTimes();

        StyleButton(_copyBtn, "Copy Table for Google Sheets");
        _copyBtn.Click += (_, _) => CopyTable();

        _status.AutoSize = true;
        _status.ForeColor = Color.DimGray;
        _status.Tag = "muted";
        _status.Margin = new Padding(6, 10, 0, 0);

        StyleButton(_themeBtn, ThemeButtonText());
        _themeBtn.Click += (_, _) => ToggleDark();

        bar.Controls.AddRange(new Control[] { _openBtn, _liveBtn, _addBtn, _sourcesBtn, _partyBtn, _channelBox, _sessionBox, _resetBtn, _copyBtn, _themeBtn, _status });
        return bar;
    }

    static void StyleButton(Button b, string text)
    {
        b.Text = text;
        b.AutoSize = true;
        b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        b.Height = 32;
        b.Margin = new Padding(0, 4, 10, 4);
        b.Padding = new Padding(8, 0, 8, 0);
    }

    /// <summary>The session summary on the right: the blue contract table on top, the clean times and totals table below it.</summary>
    Control BuildHeader()
    {
        var p = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10, TitleHeight, 0, 0),   // lines up with the boards on the left, which have a title above them
        };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // empty, so the clean table keeps its small rows

        // start / end / duration / totals, neutral and clean
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ColumnCount = 2,
            RowCount = 6,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 6; i++) t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        ConfigureTimeBox(_startBox);
        ConfigureTimeBox(_endBox);
        StyleValue(_durationVal);
        StyleValue(_missionsVal);
        StyleValue(_revenueVal);
        StyleValue(_hourlyVal);

        AddSummaryRow(t, 0, "Start Time", WrapInput(_startBox));
        AddSummaryRow(t, 1, "End Time", WrapInput(_endBox));
        AddSummaryRow(t, 2, "Duration", _durationVal);
        AddSummaryRow(t, 3, "Total Batches", _missionsVal);
        AddSummaryRow(t, 4, "Total Revenue", _revenueVal);
        AddSummaryRow(t, 5, "Hourly Rate", _hourlyVal);
        p.Controls.Add(t, 0, 2);

        // the contract table on top, in the blue style of the other boards
        StyleGrid(_rewards);
        _rewards.Dock = DockStyle.Fill;
        _rewards.Margin = new Padding(0);
        _rewards.AllowUserToAddRows = false;
        _rewards.AllowUserToDeleteRows = false;
        _rewards.ScrollBars = ScrollBars.None;
        _rewards.Columns.Add(Col("Contract", 90, true, DataGridViewContentAlignment.MiddleLeft));
        _rewards.Columns.Add(Col("Mission Reward", 120, false, DataGridViewContentAlignment.MiddleRight));
        _rewards.Columns.Add(Col("# Missions", 90, true, DataGridViewContentAlignment.MiddleRight));
        _rewards.Columns.Add(Col("Total", 100, true, DataGridViewContentAlignment.MiddleRight, true));
        _rewards.CellEndEdit += (_, _) =>
        {
            if (_inputs != null && _rewards.Rows.Count >= 2)
            {
                _inputs.Reward2 = ParseNumber(_rewards.Rows[0].Cells[1].Value);
                _inputs.Reward3 = ParseNumber(_rewards.Rows[1].Cells[1].Value);
            }
            RecomputeRewards();
            Storage.Save(_data);
            UpdateSnapshots();
            RefreshDebts();
        };
        p.Controls.Add(_rewards, 0, 0);
        return p;
    }

    static void ConfigureTimeBox(TextBox tb)
    {
        tb.BorderStyle = BorderStyle.None;
        tb.TextAlign = HorizontalAlignment.Right;
        tb.MaxLength = 5;
        tb.PlaceholderText = "00:00";
        tb.Dock = DockStyle.Fill;
    }

    static Control WrapInput(TextBox tb)
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Input, Tag = "input", Padding = new Padding(0, 3, 6, 0), Margin = new Padding(0) };
        p.Controls.Add(tb);
        return p;
    }

    static void StyleValue(Label l)
    {
        l.Dock = DockStyle.Fill;
        l.TextAlign = ContentAlignment.MiddleRight;
        l.BackColor = CalcBlue;
        l.Tag = "calc";
        l.Margin = new Padding(0);
        l.Padding = new Padding(0, 0, 6, 0);
    }

    static void AddSummaryRow(TableLayoutPanel t, int row, string label, Control? value)
    {
        t.Controls.Add(new Label
        {
            Text = label,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
        }, 0, row);

        if (value != null)
        {
            value.Margin = new Padding(0);
            t.Controls.Add(value, 1, row);
        }
    }

    void BuildLogGrid()
    {
        _logGrid.Dock = DockStyle.Fill;
        StyleGrid(_logGrid);
        _logGrid.AllowUserToAddRows = false;
        _logGrid.AllowUserToDeleteRows = false;

        // Columns you type in are white. Columns filled in from the log are tinted.
        _logGrid.Columns.Add(Col("Pickup Location", 190, false, DataGridViewContentAlignment.MiddleLeft));
        _logGrid.Columns.Add(Col("Batches", 90, true));
        _logGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Partial Pickup",
            Width = 110,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
        var workerCol = new DataGridViewComboBoxColumn
        {
            HeaderText = "Assigned Worker",
            Width = 150,
            FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        workerCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _logGrid.Columns.Add(workerCol);
        _logGrid.Columns.Add(Col("Value", 100, true, DataGridViewContentAlignment.MiddleRight));
        _logGrid.Columns.Add(Col("Notes", 120, false, DataGridViewContentAlignment.MiddleLeft, true));
        _logGrid.CellEndEdit += LogGrid_CellEndEdit;
        _logGrid.DataError += (_, e) => e.ThrowException = false;
        _logGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_logGrid.IsCurrentCellDirty) _logGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _logGrid.CellValueChanged += LogGrid_CellValueChanged;
    }

    void BuildTripGrid()
    {
        _tripGrid.Dock = DockStyle.Fill;
        _tripGrid.Margin = new Padding(0, 3, 0, 3);   // no side gap, so it lines up with the boards above
        StyleGrid(_tripGrid);
        _tripGrid.AllowUserToAddRows = false;
        _tripGrid.AllowUserToDeleteRows = false;
        _tripGrid.Columns.Add(Col("Worker", 190, true));   // as wide as Pickup Location above
        _tripGrid.Columns.Add(Col("Run", 60, true));
        _tripGrid.Columns.Add(Col("Pickup Location & Time", 420, true, DataGridViewContentAlignment.MiddleLeft, true));
        // the columns after Pickups add up to the width of the right panel, so Pickups ends where the boards above end
        _tripGrid.Columns.Add(Col("Batches", 56, true));
        _tripGrid.Columns.Add(Col("Value", 66, true, DataGridViewContentAlignment.MiddleRight));
        _tripGrid.Columns.Add(Col("Started", 52, true));
        _tripGrid.Columns.Add(Col("Completed", 66, true));
        _tripGrid.Columns.Add(Col("Duration", 56, true));
        var lostCol = new DataGridViewComboBoxColumn
        {
            HeaderText = "Lost Cargo",
            Width = 84,
            FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        lostCol.Items.AddRange("None", "Little", "All");
        lostCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _tripGrid.Columns.Add(lostCol);
        _tripGrid.Columns.Add(new DataGridViewButtonColumn
        {
            HeaderText = "End Run",
            Width = 73,
            FlatStyle = FlatStyle.Standard,   // a real button
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
        _tripGrid.DataError += (_, e) => e.ThrowException = false;
        _tripGrid.CellContentClick += TripGrid_CellContentClick;
        _tripGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_tripGrid.IsCurrentCellDirty) _tripGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _tripGrid.CellValueChanged += (_, e) =>
        {
            if (_updating || _inputs == null || e.RowIndex < 0 || e.ColumnIndex != 8) return;
            var row = _tripGrid.Rows[e.RowIndex];
            if (row.Tag is not WorkerTrip trip) return;
            var level = Convert.ToString(row.Cells[8].Value) ?? "None";
            _inputs.LostRuns.Remove(trip.Key);
            if (level == "None") _inputs.LostCargo.Remove(trip.Key);
            else _inputs.LostCargo[trip.Key] = level;
            row.DefaultCellStyle.BackColor = RunColor(level, trip.End.HasValue);
            Storage.Save(_data);
            FillStats();
            UpdateSnapshots();
        };
    }

    /// <summary>Red = all cargo lost, orange = some lost, green = completed with everything delivered, yellow = still on the way.</summary>
    static Color RunColor(string level, bool ended) =>
        level == "All" ? LostRed : level == "Little" ? LostOrange : ended ? DoneGreen : PartialYellow;   // yellow = on the way

    string LostLevel(string key) => _inputs == null ? "None" : LostLevelOf(_inputs, key);

    static string LostLevelOf(SessionInputs inp, string key)
    {
        if (inp.LostCargo.TryGetValue(key, out var level)) return level;
        return inp.LostRuns.Contains(key) ? "All" : "None";
    }

    void TripGrid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (_updating || _inputs == null || e.RowIndex < 0 || e.ColumnIndex != 9) return;
        if (_tripGrid.Rows[e.RowIndex].Tag is not WorkerTrip trip) return;

        if (!trip.End.HasValue)
        {
            var answer = MessageBox.Show(this,
                "End the run of " + trip.Worker + " now?\n\nUse this when the cargo was lost and never handed in. You can undo it afterwards.",
                "End Run", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            _inputs.ManualEnds.Add(new ManualEnd { Worker = trip.Worker, Time = DateTime.UtcNow });
        }
        else if (trip.Manual)
        {
            var at = trip.End.Value;
            _inputs.ManualEnds.RemoveAll(x => SameName(x.Worker, trip.Worker) && x.Time == at);
        }
        else
        {
            return;
        }
        Storage.Save(_data);
        BeginInvoke(new Action(() =>
        {
            FillTrips();
            UpdateSnapshots();
        }));
    }

    /// <summary>Time from the start of the run to its end, or to now while the run is still on the way (when watching live).</summary>
    string RunDuration(WorkerTrip t)
    {
        if (t.End.HasValue) return ShortDuration(t.End.Value - t.Start);
        var latest = _sessions.Count > 0 && _current != null && _current.Key == _sessions[^1].Key;
        if (!_live || !latest) return "";
        var d = DateTime.UtcNow - t.Start;
        return d > TimeSpan.Zero ? ShortDuration(d) : "";
    }

    /// <summary>Keeps the duration of runs that are on the way ticking between log updates.</summary>
    void UpdateLiveDurations()
    {
        foreach (DataGridViewRow row in _tripGrid.Rows)
        {
            if (row.Tag is not WorkerTrip t || t.End.HasValue) continue;
            var text = RunDuration(t);
            if (!Equals(row.Cells[7].Value, text)) row.Cells[7].Value = text;
        }
    }

    string WorkerOfZone(string zone)
    {
        if (_inputs != null && _inputs.Rows.TryGetValue(zone, out var r) && !string.IsNullOrWhiteSpace(r.Worker))
            return r.Worker;
        return "Unassigned";
    }

    void FillTrips()
    {
        var wasUpdating = _updating;
        _updating = true;
        try
        {
            _tripGrid.Rows.Clear();
            _trips = new List<WorkerTrip>();
            if (_current == null || _inputs == null || _log == null) return;

            _trips = WorkerTripBuilder.Build(_current, WorkerOfZone, _log, _inputs.ManualEnds)
                .OrderByDescending(t => t.Start).ThenBy(t => t.Worker, StringComparer.OrdinalIgnoreCase).ToList();   // newest run on top

            foreach (var t in _trips)
            {
                var pickups = string.Join(",  ", t.Pickups.Select(p => LocationName(p.Zone) + " " + FormatTime(p.Time)));
                var level = LostLevel(t.Key);
                var idx = _tripGrid.Rows.Add(
                    t.Worker,
                    "#" + t.Number,
                    pickups,
                    t.Manual ? "ended by hand" : t.End.HasValue ? t.HandedIn.ToString() : "",
                    RunValueText(t),
                    FormatTime(t.Start),
                    t.End.HasValue ? FormatTime(t.End.Value) : "",
                    RunDuration(t),
                    level,
                    t.Manual ? "Undo End" : "End Run");
                var row = _tripGrid.Rows[idx];
                // a finished run cannot be ended any more: its button is greyed out
                row.Cells[9] = new RunButtonCell { Value = t.Manual ? "Undo End" : "End Run", Disabled = t.End.HasValue && !t.Manual };
                row.Tag = t;
                row.DefaultCellStyle.BackColor = RunColor(level, t.End.HasValue);
            }
        }
        finally
        {
            _updating = wasUpdating;
            RecomputeTimes();   // also refreshes the leaderboard
        }
    }

    Control BuildFooter()
    {
        var f = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        f.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        f.RowStyles.Add(new RowStyle(SizeType.Percent, 100));


        f.Controls.Add(new Label
        {
            Text = "Rename a location once and it is remembered. \"Partially\" means some missions still have cargo waiting there. SCU is typed by you.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
            Tag = "muted",
            AutoEllipsis = true,
        }, 1, 0);
        return f;
    }

    static DataGridViewTextBoxColumn Col(
        string header, int width, bool readOnly,
        DataGridViewContentAlignment align = DataGridViewContentAlignment.MiddleCenter,
        bool fill = false)
    {
        var c = new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            Width = width,
            ReadOnly = readOnly,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        c.DefaultCellStyle.Alignment = align;
        if (readOnly) c.DefaultCellStyle.BackColor = CalcBlue;
        if (fill)
        {
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            c.MinimumWidth = 90;
        }
        return c;
    }

    static void StyleGrid(DataGridView g)
    {
        g.BackgroundColor = Theme.Window;
        g.BorderStyle = BorderStyle.None;
        g.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        g.GridColor = Theme.Lines;
        g.DefaultCellStyle.BackColor = Theme.Cell;
        g.DefaultCellStyle.ForeColor = Theme.Text;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.ColumnHeadersDefaultCellStyle.BackColor = Theme.Header;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Header;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;
        g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        g.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersHeight = 28;
        g.RowHeadersVisible = false;
        g.RowTemplate.Height = 26;
        g.AllowUserToResizeRows = false;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        g.DefaultCellStyle.SelectionBackColor = Theme.Selection;
        g.DefaultCellStyle.SelectionForeColor = Theme.SelectionText;
    }

    // ------------------------------------------------------------------ loading logs

    sealed class LiveSource
    {
        public string Path = "";
        public long Pos;
        public Decoder Dec = Encoding.UTF8.GetDecoder();
        public string Carry = "";
        public HaulingLog Log = new();
        public string? Current;     // the Game.log file that is being read right now
    }

    readonly ComboBox _channelBox = new();
    readonly Button _sourcesBtn = new();
    readonly Button _partyBtn = new();

    /// <summary>A watch path is a Game.log file, a folder with Game.log, or the StarCitizen folder with LIVE, HOTFIX... inside.</summary>
    string? ResolveLog(string path)
    {
        try
        {
            if (File.Exists(path)) return path;
            if (!Directory.Exists(path)) return null;

            var direct = Path.Combine(path, "Game.log");
            if (File.Exists(direct)) return direct;

            var found = Directory.GetDirectories(path)
                .Select(d => (Name: Path.GetFileName(d), File: Path.Combine(d, "Game.log")))
                .Where(x => File.Exists(x.File))
                .ToList();

            var channel = _data.Channel;
            if (!string.IsNullOrEmpty(channel) && channel != "Auto")
            {
                var pick = found.FirstOrDefault(x => string.Equals(x.Name, channel, StringComparison.OrdinalIgnoreCase));
                return pick.File;   // null when that channel has no log
            }
            return found.OrderByDescending(x => File.GetLastWriteTimeUtc(x.File)).Select(x => x.File).FirstOrDefault();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    void ChannelChanged(object? sender, EventArgs e)
    {
        if (_updating) return;
        _data.Channel = _channelBox.SelectedItem as string ?? "Auto";
        Storage.Save(_data);
        if (_live) PollLive();
    }

    readonly List<LiveSource> _sources = new();

    void OpenLogs()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose one or more Game.log files (from any of your computers)",
            Filter = "Star Citizen logs (*.log)|*.log|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        StopLive();
        try
        {
            Cursor = Cursors.WaitCursor;
            var files = dlg.FileNames
                .OrderBy(f => HaulingLog.FirstTimestamp(f) ?? DateTime.MaxValue)
                .ToList();

            // one log per player, so logs from two accounts stay apart
            _logs.Clear();
            var byPlayer = new Dictionary<string, HaulingLog>();
            foreach (var f in files)
            {
                var player = HaulingLog.FirstPlayer(f) ?? "Unknown player";
                if (!byPlayer.TryGetValue(player, out var log))
                {
                    log = new HaulingLog();
                    byPlayer[player] = log;
                    _logs.Add(log);
                }
                log.ProcessFile(f);
            }

            _renderedVersion = -1;
            _current = null;
            _log = null;
            RefreshFromLog();
            SetStatus($"Loaded {files.Count} log file(s) for {_logs.Count} player(s), {_sessions.Count} session(s) found.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not read the log", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    /// <summary>Add a Game.log to watch live. Can be a network path to another computer, like the Gauntlet Tracker.</summary>
    void AddSource()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Pick the StarCitizen folder (the one that holds LIVE and HOTFIX). It can be on another computer, for example \\\\PCNAME\\StarCitizen.",
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (!_data.WatchPaths.Contains(dlg.SelectedPath)) _data.WatchPaths.Add(dlg.SelectedPath);
        Storage.Save(_data);
        SetStatus("Added " + dlg.SelectedPath);
        if (_live) StartLive();
    }

    /// <summary>Shows every added live source, whether it can be reached right now, and lets you remove one.</summary>
    void ShowSources()
    {
        using var f = new Form
        {
            Text = "Live Sources",
            ClientSize = new Size(760, 320),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = Font,
        };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        var remove = new Button { Text = "Remove Selected", AutoSize = true };
        bar.Controls.Add(close);
        bar.Controls.Add(remove);
        f.Controls.Add(list);
        f.Controls.Add(bar);
        f.AcceptButton = close;

        var changed = false;
        void Fill()
        {
            list.Items.Clear();
            if (_data.WatchPaths.Count == 0) list.Items.Add("(no live sources added yet, use \"Add Live Source…\")");
            foreach (var path in _data.WatchPaths)
            {
                var file = ResolveLog(path);
                list.Items.Add(path + "    →    " + (file ?? "cannot be reached right now"));
            }
        }
        remove.Click += (_, _) =>
        {
            var i = list.SelectedIndex;
            if (i < 0 || i >= _data.WatchPaths.Count) return;
            _data.WatchPaths.RemoveAt(i);
            Storage.Save(_data);
            changed = true;
            Fill();
        };
        Fill();
        ApplyTheme(f);
        f.ShowDialog(this);

        if (changed && _live) StartLive();
    }

    void ToggleLive()
    {
        if (_live)
        {
            StopLive();
            SetStatus("Stopped watching.");
            return;
        }

        if (_data.WatchPaths.Count == 0)
        {
            if (Directory.Exists(DefaultLogPath)) _data.WatchPaths.Add(DefaultLogPath);
            else
            {
                AddSource();
                if (_data.WatchPaths.Count == 0) return;
            }
        }
        StartLive();
    }

    void StartLive()
    {
        _sources.Clear();
        _logs.Clear();
        foreach (var path in _data.WatchPaths)
        {
            var src = new LiveSource { Path = path };
            _sources.Add(src);
            _logs.Add(src.Log);
        }
        _renderedVersion = -1;
        _current = null;
        _log = null;
        _live = true;
        _liveBtn.Text = "Stop Watching";
        _timer.Start();
        PollLive();
        SetStatus("Watching " + string.Join(", ", _data.WatchPaths));
    }

    void StopLive()
    {
        _timer.Stop();
        _live = false;
        _liveBtn.Text = "Watch Live Game.log";
    }

    void PollLive()
    {
        if (!_live) return;
        foreach (var src in _sources)
        {
            try
            {
                var file = ResolveLog(src.Path);
                if (file == null) continue;
                if (file != src.Current)
                {
                    // after a restart of this program or of the game: read the recent backup logs first,
                    // so a hauling session that was already going on is not lost
                    Backfill(src, file);

                    // switched channel (LIVE / HOTFIX) or a new location: read the new file from the top
                    src.Current = file;
                    src.Pos = 0;
                    src.Carry = "";
                    src.Dec = Encoding.UTF8.GetDecoder();
                }
                var info = new FileInfo(file);
                if (!info.Exists) continue;

                // A new Game.log starts empty. Keep the missions we already know and read it from the top.
                if (info.Length < src.Pos)
                {
                    src.Pos = 0;
                    src.Carry = "";
                    src.Dec = Encoding.UTF8.GetDecoder();
                }
                if (info.Length <= src.Pos) continue;

                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                fs.Seek(src.Pos, SeekOrigin.Begin);
                var buffer = new byte[1 << 16];
                var chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
                int read;
                while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    src.Pos += read;
                    var n = src.Dec.GetChars(buffer, 0, read, chars, 0);
                    Feed(src, new string(chars, 0, n));
                }
            }
            catch (IOException)
            {
                // The game may be writing at the same moment, or the other computer is offline. Try again next tick.
            }
        }
        RefreshFromLog();
        UpdateLiveDurations();
    }

    static readonly TimeSpan BackupAge = TimeSpan.FromHours(24);

    /// <summary>Reads the logs in the logbackups folder next to Game.log that are less than 24 hours old.</summary>
    static void Backfill(LiveSource src, string liveFile)
    {
        try
        {
            var dir = Path.Combine(Path.GetDirectoryName(liveFile) ?? "", "logbackups");
            if (!Directory.Exists(dir)) return;

            var cutoff = DateTime.UtcNow - BackupAge;
            var files = Directory.GetFiles(dir, "*.log")
                .Where(f => File.GetLastWriteTimeUtc(f) >= cutoff)
                .OrderBy(f => HaulingLog.FirstTimestamp(f) ?? DateTime.MaxValue)
                .ToList();

            foreach (var f in files)
            {
                try { src.Log.ProcessFile(f); }
                catch (IOException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static void Feed(LiveSource src, string text)
    {
        var data = src.Carry + text;
        var start = 0;
        int nl;
        while ((nl = data.IndexOf('\n', start)) >= 0)
        {
            src.Log.ProcessLine(data.Substring(start, nl - start).TrimEnd('\r'));
            start = nl + 1;
        }
        src.Carry = data.Substring(start);
    }

    // ------------------------------------------------------------------ showing a session

    bool IsEditing() =>
        _logGrid.IsCurrentCellInEditMode || _rewards.IsCurrentCellInEditMode || _balanceGrid.IsCurrentCellInEditMode || _startBox.Focused || _endBox.Focused;

    void RefreshFromLog()
    {
        var version = _logs.Sum(l => l.Version) + _logs.Count * 1000003;
        if (version == _renderedVersion) return;
        if (IsEditing()) return;   // do not disturb typing, we try again on the next tick
        _renderedVersion = version;

        if (_logs.Count == 0) { ClearView(); return; }

        // the logs of all accounts are merged, a mission shared in the party appears once
        _log = HaulingLog.Merge(_logs);

        var keep = _current?.Key;
        _sessions = SessionBuilder.Build(_log);

        _updating = true;
        try
        {
            _sessionBox.Items.Clear();
            foreach (var g in _sessions) _sessionBox.Items.Add(DescribeSession(g));

            var idx = keep == null ? -1 : _sessions.FindIndex(g => g.Key == keep);
            if (idx < 0) idx = _sessions.Count - 1;
            if (idx >= 0) _sessionBox.SelectedIndex = idx;
        }
        finally
        {
            _updating = false;
        }

        _current = _sessionBox.SelectedIndex >= 0 ? _sessions[_sessionBox.SelectedIndex] : null;
        if (_current == null)
        {
            ClearView();
            UpdateSnapshots();
            RefreshDebts();
            if (!_live) SetStatus("No Station to Ruin hauling missions were found in this log.");
            return;
        }
        ShowSession();
        UpdateSnapshots();
        RefreshDebts();
    }

    static string DescribeSession(SessionGroup g)
    {
        var s = SessionBuilder.Summarize(g);
        var start = s.Start.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
        var end = s.End.HasValue ? s.End.Value.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) : "ongoing";
        return start + " to " + end + "  (" + g.Missions.Count + " missions)";
    }

    void SessionChanged(object? sender, EventArgs e)
    {
        if (_updating) return;
        var i = _sessionBox.SelectedIndex;
        if (i < 0 || i >= _sessions.Count) return;
        _current = _sessions[i];
        ShowSession();
    }

    SessionInputs GetInputs(string key)
    {
        if (!_data.Sessions.TryGetValue(key, out var inputs))
        {
            inputs = new SessionInputs();
            _data.Sessions[key] = inputs;
        }
        return inputs;
    }

    RowInputs GetRow(string zone)
    {
        var rows = _inputs!.Rows;
        if (!rows.TryGetValue(zone, out var r))
        {
            r = new RowInputs();
            rows[zone] = r;
        }
        return r;
    }

    static string FormatTime(DateTime utc) =>
        utc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    static string Tail(string zone) => zone.Length > 5 ? zone.Substring(zone.Length - 5) : zone;

    // Names found so far, keyed by the last digits of the location id. Your own names in the grid win.
    static readonly Dictionary<string, string> KnownNames = new()
    {
        ["73791"] = "Rat's Nest",
        ["70269"] = "Megumi Refueling",
        ["70120"] = "Patch City",
        ["69936"] = "Gaslight",
        ["72785"] = "Dudley & Daughters",
        ["73041"] = "Orbituary",
        ["72645"] = "Rod's Fuel 'N Supplies",
        ["70053"] = "Starlight Service Station",
        ["72952"] = "Checkmate",
        ["74320"] = "Nyx Gateway",
        ["74325"] = "Stanton Gateway",
        ["70273"] = "Endgame",
    };

    string LocationName(string zone)
    {
        if (_data.LocationNames.TryGetValue(zone, out var n) && !string.IsNullOrWhiteSpace(n)) return n;
        if (KnownNames.TryGetValue(Tail(zone), out var known)) return known;
        return "Unknown ..." + Tail(zone);
    }

    void ShowSession()
    {
        if (_current == null)
        {
            ClearView();
            return;
        }

        _updating = true;
        try
        {
            _inputs = GetInputs(_current.Key);
            _summary = SessionBuilder.Summarize(_current);
            _rows = SessionBuilder.Locations(_current);

            _startBox.Text = _inputs.Start ?? FormatTime(_summary.Start);
            _endBox.Text = _inputs.End ?? (_summary.End.HasValue ? FormatTime(_summary.End.Value) : "");

            // the mission counts come from the log (2 stop and 3 stop contracts), you only type the payouts
            _rewards.Rows.Clear();
            if (_inputs.Reward2 == 0) _inputs.Reward2 = DefaultReward2;
            if (_inputs.Reward3 == 0) _inputs.Reward3 = DefaultReward3;
            var accepted = _current.Missions;   // every accepted mission of the session (abandoned ones are already left out)
            _rewards.Rows.Add("2 stops", _inputs.Reward2.ToString("N0"), accepted.Count(m => m.Multi == 2), "");
            _rewards.Rows.Add("3 stops", _inputs.Reward3.ToString("N0"), accepted.Count(m => m.Multi == 3), "");
            var totalIdx = _rewards.Rows.Add("Total", "", 0, "");
            _rewards.Rows[totalIdx].ReadOnly = true;
            _rewards.Rows[totalIdx].DefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);

            FillLog();
            FillTrips();
        }
        finally
        {
            _updating = false;
        }

        RecomputeRewards();
    }

    void ClearView()
    {
        _updating = true;
        try
        {
            _inputs = null;
            _summary = null;
            _rows = new List<LocationRow>();
            _startBox.Text = "";
            _endBox.Text = "";
            _rewards.Rows.Clear();
            _logGrid.Rows.Clear();
            _tripGrid.Rows.Clear();
            _statsGrid.Rows.Clear();
        }
        finally
        {
            _updating = false;
        }
        _revenue = 0;
        _durationVal.Text = "";
        _missionsVal.Text = "";
        _revenueVal.Text = "";
        _hourlyVal.Text = "";
        _rewardsTotalMissions.Text = "";
        _rewardsTotalRevenue.Text = "";
    }

    /// <summary>Missions still waiting at this location / missions that have cargo there. 5 missions start as 5/5.</summary>
    static string MissionsText(LocationRow r) => (r.Missions - r.Loaded) + "/" + r.Missions;

    static string DeliveredText(LocationRow r)
    {
        if (r.DeliveredTime.HasValue) return FormatTime(r.DeliveredTime.Value);
        if (r.Delivered > 0) return "Partial " + r.Delivered + "/" + r.Missions;
        return "";
    }

    void RefreshWorkerChoices()
    {
        var col = (DataGridViewComboBoxColumn)_logGrid.Columns[3];
        col.Items.Clear();
        col.Items.Add("");
        foreach (var name in _data.Party) col.Items.Add(name);
    }

    void FillLog()
    {
        _logGrid.Rows.Clear();

        // a location nobody is assigned to gets the player the logs show picking up there (you can change it afterwards)
        var assignedNow = false;
        if (_log != null && _current != null)
        {
            foreach (var r in _rows)
            {
                var input = GetRow(r.Zone);
                if (!string.IsNullOrWhiteSpace(input.Worker)) continue;
                var who = WorkerTripBuilder.PickupActor(_current, r.Zone, _log);
                if (who == null) continue;
                input.Worker = who;
                assignedNow = true;
            }
        }
        if (assignedNow) Storage.Save(_data);

        // a worker that is not in the party yet (typed earlier) is added to it
        foreach (var r in _rows)
        {
            if (_inputs!.Rows.TryGetValue(r.Zone, out var w) && !string.IsNullOrWhiteSpace(w.Worker)
                && !_data.Party.Contains(w.Worker))
                _data.Party.Add(w.Worker);
        }
        RefreshWorkerChoices();

        foreach (var r in _rows)
        {
            _inputs!.Rows.TryGetValue(r.Zone, out var inp);
            var idx = _logGrid.Rows.Add(
                LocationName(r.Zone),
                MissionsText(r),
                inp?.PartialPickup ?? false,
                inp?.Worker ?? "",
                StopValueText(r),
                inp?.Notes ?? "");

            var row = _logGrid.Rows[idx];
            row.Tag = r.Zone;
            ColorMissionsCell(row, r);
        }
    }

    /// <summary>
    /// Value of a pickup location: every mission pays its reward split over its stops,
    /// so a stop of a 2 stop mission is worth reward / 2 and a stop of a 3 stop mission reward / 3.
    /// </summary>
    decimal StopValue(LocationRow r)
    {
        if (_inputs == null) return 0;
        return r.Stops2 * _inputs.Reward2 / 2m + r.Stops3 * _inputs.Reward3 / 3m;
    }

    string StopValueText(LocationRow r)
    {
        var v = StopValue(r);
        return v == 0 ? "" : v.ToString("N0");
    }

    void RefreshStopValues()
    {
        foreach (DataGridViewRow row in _logGrid.Rows)
        {
            if (row.Tag is not string zone) continue;
            var info = _rows.FirstOrDefault(x => x.Zone == zone);
            if (info != null) row.Cells[4].Value = StopValueText(info);
        }
        foreach (DataGridViewRow row in _tripGrid.Rows)
        {
            if (row.Tag is WorkerTrip t) row.Cells[4].Value = RunValueText(t);
        }
    }

    void ColorMissionsCell(DataGridViewRow row, LocationRow r)
    {
        var partial = row.Cells[2].Value is true;
        row.Cells[1].Style.BackColor = partial ? PartialYellow : r.Loaded >= r.Missions ? DoneGreen : CalcBlue;
    }

    void LogGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_updating || _inputs == null || e.RowIndex < 0) return;
        var row = _logGrid.Rows[e.RowIndex];
        if (row.Tag is not string zone) return;

        if (e.ColumnIndex == 2)
        {
            GetRow(zone).PartialPickup = row.Cells[2].Value is true;
            var info = _rows.FirstOrDefault(x => x.Zone == zone);
            if (info != null) ColorMissionsCell(row, info);
            Storage.Save(_data);
        }
        else if (e.ColumnIndex == 3)
        {
            GetRow(zone).Worker = (Convert.ToString(row.Cells[3].Value) ?? "").Trim();
            Storage.Save(_data);
            FillTrips();   // the run board follows the assigned workers
            UpdateSnapshots();
            RefreshDebts();
        }
    }

    void EditParty()
    {
        using var f = new Form
        {
            Text = "Party",
            ClientSize = new Size(360, 300),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = Font,
        };
        var label = new Label { Text = "One name per line. These are the choices in Assigned Worker.", Dock = DockStyle.Top, Height = 30, Padding = new Padding(6, 6, 6, 0) };
        var box = new TextBox { Multiline = true, Dock = DockStyle.Fill, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Text = string.Join(Environment.NewLine, _data.Party) };
        var ok = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        bar.Controls.Add(ok);
        f.Controls.Add(box);
        f.Controls.Add(label);
        f.Controls.Add(bar);
        f.AcceptButton = ok;
        ApplyTheme(f);
        if (f.ShowDialog(this) != DialogResult.OK) return;

        _data.Party = box.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Storage.Save(_data);

        _renderedVersion = -1;
        RefreshFromLog();
    }

    // ------------------------------------------------------------------ editing

    void LogGrid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_updating || _inputs == null || e.RowIndex < 0) return;
        var row = _logGrid.Rows[e.RowIndex];
        if (row.Tag is not string zone) return;
        var text = (Convert.ToString(row.Cells[e.ColumnIndex].Value) ?? "").Trim();

        switch (e.ColumnIndex)
        {
            case 0:
                if (text.Length == 0 || text.StartsWith("Unknown ..."))
                {
                    _data.LocationNames.Remove(zone);
                    row.Cells[0].Value = LocationName(zone);
                }
                else
                {
                    _data.LocationNames[zone] = text;
                }
                break;
            case 5:
                GetRow(zone).Notes = text;
                break;
        }
        Storage.Save(_data);
    }

    void RecomputeRewards()
    {
        if (_inputs == null) return;

        decimal revenue = 0;
        var missions = 0;

        var wasUpdating = _updating;
        _updating = true;
        try
        {
            foreach (DataGridViewRow row in _rewards.Rows)
            {
                if (row.Index >= 2) continue;   // the Total row
                var reward = ParseNumber(row.Cells[1].Value);
                var count = (int)ParseNumber(row.Cells[2].Value);
                var total = reward * count;
                row.Cells[3].Value = total == 0 ? "" : total.ToString("N0");
                revenue += total;
                missions += count;
            }
        }
        finally
        {
            _updating = wasUpdating;
        }

        _revenue = revenue;
        _rewardsTotalMissions.Text = missions.ToString("N0");
        _rewardsTotalRevenue.Text = revenue.ToString("N0");
        if (_rewards.Rows.Count >= 3)
        {
            _rewards.Rows[2].Cells[2].Value = missions;
            _rewards.Rows[2].Cells[3].Value = revenue == 0 ? "" : revenue.ToString("N0");
        }
        RefreshStopValues();
        RecomputeTimes();
    }

    void TimeBoxChanged(object? sender, EventArgs e)
    {
        if (_updating || _inputs == null) return;
        var start = _startBox.Text.Trim();
        var end = _endBox.Text.Trim();
        _inputs.Start = start.Length == 0 ? null : start;
        _inputs.End = end.Length == 0 ? null : end;
        RecomputeTimes();
    }

    void ResetTimes()
    {
        if (_inputs == null) return;
        _inputs.Start = null;
        _inputs.End = null;
        Storage.Save(_data);
        ShowSession();
    }

    /// <summary>What the finished runs are worth: every pickup of a run counts its share of the value of that location.</summary>
    decimal CompletedRevenue()
    {
        if (_inputs == null || _current == null) return 0;
        var r2 = _inputs.Reward2 == 0 ? DefaultReward2 : _inputs.Reward2;
        var r3 = _inputs.Reward3 == 0 ? DefaultReward3 : _inputs.Reward3;
        return _current.Missions.Where(m => m.Completed && m.EndedAt.HasValue)
            .Sum(m => m.Multi == 2 ? r2 : m.Multi == 3 ? r3 : 0m);
    }

    /// <summary>The value of the locations a run picked up at, combined.</summary>
    decimal RunValue(WorkerTrip t)
    {
        if (_inputs == null) return 0;
        decimal sum = 0;
        foreach (var p in t.Pickups)
        {
            var loc = _rows.FirstOrDefault(x => x.Zone == p.Zone);
            if (loc == null || loc.Missions <= 0) continue;
            sum += StopValue(loc) * Math.Min(p.Missions, loc.Missions) / loc.Missions;
        }
        return sum;
    }

    string RunValueText(WorkerTrip t)
    {
        var v = RunValue(t);
        return v == 0 ? "" : v.ToString("N0");
    }

    void RecomputeTimes()
    {
        TimeSpan? duration = null;
        if (TryTime(_startBox.Text, out var s) && TryTime(_endBox.Text, out var e))
        {
            var d = e - s;
            if (d < TimeSpan.Zero) d += TimeSpan.FromDays(1);
            duration = d;
        }

        _durationVal.Text = duration.HasValue
            ? (int)duration.Value.TotalHours + ":" + duration.Value.Minutes.ToString("00")
            : "";

        // revenue = the real income: the rewards of the missions that are completed
        var income = CompletedRevenue();
        _revenueVal.Text = _inputs == null || income == 0 ? "" : income.ToString("N0");

        _hourlyVal.Text = duration.HasValue && duration.Value.TotalHours > 0 && income > 0
            ? (income / (decimal)duration.Value.TotalHours).ToString("N0")
            : "";

        FillStats();

        // batches = pickup stops; done = the ones in finished runs
        var totalBatches = _rows.Sum(r => r.Missions);
        var doneBatches = _trips.Where(t => t.End.HasValue && !t.Manual).Sum(t => t.Pickups.Sum(p => p.Missions));
        if (_summary == null || totalBatches == 0)
            _missionsVal.Text = "";
        else if (doneBatches >= totalBatches)
            _missionsVal.Text = totalBatches.ToString();
        else
            _missionsVal.Text = doneBatches + " of " + totalBatches + " done";
    }

    static bool TryTime(string text, out TimeSpan value) =>
        TimeSpan.TryParseExact(text.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out value);

    static decimal ParseNumber(object? v)
    {
        var s = Convert.ToString(v, CultureInfo.CurrentCulture) ?? "";
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsDigit(ch) || ch == '.') sb.Append(ch);   // drops spaces, commas and non-breaking spaces
        }
        return decimal.TryParse(sb.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    // ------------------------------------------------------------------ copy to Google Sheets

    void CopyTable()
    {
        var sb = new StringBuilder();
        foreach (DataGridViewRow row in _logGrid.Rows)
        {
            string C(int i) =>
                (Convert.ToString(row.Cells[i].Value) ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

            var notes = C(5);
            var info = _rows.FirstOrDefault(x => x.Zone == (row.Tag as string));
            var loaded = "";
            var delivered = "";
            var total = C(1);
            if (info != null)
            {
                total = info.Missions.ToString();
                loaded = info.LoadedTime.HasValue ? FormatTime(info.LoadedTime.Value) : "";
                delivered = DeliveredText(info);
                if (info.Loaded < info.Missions)
                    notes = (notes + " (" + (info.Missions - info.Loaded) + " of " + info.Missions + " still waiting)").Trim();
                if (row.Cells[2].Value is true)
                    notes = (notes + " (partial pickup)").Trim();
            }

            // Same order as the spreadsheet: Pickup Location, Missions, Assigned Worker, Loaded Time, Delivered Time, Notes
            sb.Append(string.Join("\t", C(0), total, C(3), loaded, delivered, notes)).Append('\n');
        }

        if (sb.Length == 0)
        {
            SetStatus("Nothing to copy yet.");
            return;
        }
        Clipboard.SetText(sb.ToString());
        SetStatus("Copied. Click the first Pickup Location cell in the sheet and paste.");
    }

    void SetStatus(string text) => _status.Text = text;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _startBox.TextChanged += TimeBoxChanged;
        _endBox.TextChanged += TimeBoxChanged;
        _startBox.Leave += (_, _) => Storage.Save(_data);
        _endBox.Leave += (_, _) => Storage.Save(_data);

        // start watching by itself: this also reads the backup logs of the last 24 hours
        Shown += (_, _) =>
        {
            if (!_live && (_data.WatchPaths.Count > 0 || Directory.Exists(DefaultLogPath))) ToggleLive();
        };
    }
}

/// <summary>The button of a run. Greyed out when it cannot be pressed, and drawn in the dark look when dark mode is on.</summary>
sealed class RunButtonCell : DataGridViewButtonCell
{
    public bool Disabled { get; set; }

    public override object Clone()
    {
        var c = (RunButtonCell)base.Clone();
        c.Disabled = Disabled;
        return c;
    }

    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex,
        DataGridViewElementStates cellState, object? value, object? formattedValue, string? errorText,
        DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
    {
        if (!Disabled && !Theme.Dark)
        {
            base.Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);
            return;
        }

        using (var back = new SolidBrush(cellStyle.BackColor)) graphics.FillRectangle(back, cellBounds);
        var button = Rectangle.Inflate(cellBounds, -5, -3);
        var text = formattedValue?.ToString() ?? "";
        if (Theme.Dark)
        {
            using (var fill = new SolidBrush(Disabled ? Theme.ButtonOff : Theme.Button)) graphics.FillRectangle(fill, button);
            using (var pen = new Pen(Theme.Lines)) graphics.DrawRectangle(pen, button.X, button.Y, button.Width - 1, button.Height - 1);
            TextRenderer.DrawText(graphics, text, cellStyle.Font, button, Disabled ? Theme.Muted : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        else
        {
            System.Windows.Forms.ButtonRenderer.DrawButton(graphics, button, text, cellStyle.Font, false,
                System.Windows.Forms.VisualStyles.PushButtonState.Disabled);
        }
        PaintBorder(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle);
    }
}
