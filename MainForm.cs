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
    readonly ToolStripMenuItem _addBtn = new();
    readonly DataGridView _tripGrid = new();
    readonly DataGridView _planRunsGrid = new();   // a read-only copy of Cargo Runs under the Trip Planner
    List<WorkerTrip> _trips = new();
    readonly AppData _data = Storage.Load();
    List<SessionGroup> _sessions = new();
    SessionGroup? _current;
    SessionInputs? _inputs;
    SessionSummary? _summary;
    List<LocationRow> _rows = new();       // every pickup location of the session (the totals and the saved numbers use these)
    List<LocationRow> _openRows = new();   // the locations of the missions that are not completed yet (completed missions are on neither board)
    List<LocationRow> _pickupRows = new(); // of those, the ones with cargo still to deliver (the pickup table)
    List<LocationRow> _doneRows = new();   // of those, the ones where every batch is handed in (the finished stops table)
    readonly DataGridView _doneGrid = new();
    Label? _doneTitle;
    decimal _revenue;
    int _renderedVersion = -1;
    bool _updating;

    // live watching of the running game's Game.log
    bool _live;

    readonly ThemedCombo _sessionBox = new();
    readonly ToolStripMenuItem _openBtn = new();
    readonly ToolStripMenuItem _liveBtn = new();
    readonly ToolStripMenuItem _resetBtn = new();
    readonly ToolStripMenuItem _copyBtn = new();
    readonly Label _status = new();

    readonly TextBox _startBox = new();
    readonly TextBox _endBox = new();
    readonly Label _durationVal = new();
    readonly Label _missionsVal = new();
    readonly Label _revenueVal = new();
    readonly Label _hourlyVal = new();
    readonly Label _missionsDoneVal = new();
    readonly Label _missionsActiveVal = new();

    readonly DataGridView _rewards = new();
    readonly Label _rewardsTotalMissions = new();
    readonly Label _rewardsTotalRevenue = new();

    readonly DataGridView _logGrid = new();

    readonly System.Windows.Forms.Timer _timer = new();
    readonly System.Windows.Forms.Timer _clock = new();

    public MainForm()
    {
        Text = "Hauling Tracker";
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

        _clock.Interval = 1000;
        _clock.Tick += (_, _) =>
        {
            UpdateSessionClock();
            UpdateLiveDurations();
        };
        _clock.Start();
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

        InitSessionButtons();
        ClearView();
        InitWebView();
        SetStatus("Open a log file, or press \"Watch live Game.log\" while you play.");
    }

    // ------------------------------------------------------------------ layout

    void BuildUi()
    {
        var root = _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12, 3, 12, 12),   // the party table starts right under the line of the tabs
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 420));   // fitted to the boards by FitTopArea
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // the run board takes what is left and scrolls
        var tabs = new ThemedTabControl { Dock = DockStyle.Fill };
        var sessionPage = new TabPage("Session") { BackColor = Color.White };
        var debtsPage = new TabPage("Debts") { BackColor = Color.White };
        tabs.TabPages.Add(sessionPage);
        tabs.TabPages.Add(debtsPage);
        Controls.Add(tabs);
        BuildMenu();
        Controls.Add(_menu);
        MainMenuStrip = _menu;
        sessionPage.Controls.Add(root);
        debtsPage.Controls.Add(BuildDebtsPage());
        var historyPage = new TabPage("History") { BackColor = Color.White };
        tabs.TabPages.Add(historyPage);
        historyPage.Controls.Add(BuildHistoryPage());
        var plannerPage = new TabPage("Trip Planner") { BackColor = Color.White };
        tabs.TabPages.Insert(1, plannerPage);   // Session, Trip Planner, Debts, History: the same order as the web view
        plannerPage.Controls.Add(BuildPlannerPage());
        tabs.SelectedIndexChanged += (_, _) =>
        {
            _planBtn.Visible = _resetTripsBtn.Visible = tabs.SelectedTab == plannerPage;
            if (tabs.SelectedTab == plannerPage) RefreshPlanner();
            else if (tabs.SelectedTab != sessionPage) RefreshDebts();
        };

        // top area: the session bar and the pickup boards on the left, the party with the small times and contracts tables on the right,
        // the leaderboard under both
        var main = _main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SideWidth));   // the party, the contracts and the times
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 306));   // both rows are fitted to the boards by FitTopArea
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));

        var left = _left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = new Padding(0) };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));   // the title: Stops
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));    // the finished stops: shown when there are some
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        BuildLogGrid();
        BuildDoneGrid();
        BuildStatsGrid();
        FitColumns(_logGrid);     // these never scroll: the columns share the width instead
        FitColumns(_doneGrid);
        FitColumns(_statsGrid);
        _logGrid.Margin = new Padding(0, 0, 3, 3);     // the right gap keeps it clear of the summary tables
        _doneGrid.Margin = new Padding(0, 0, 3, 3);
        _statsGrid.Margin = new Padding(0, 3, 0, 3);   // runs to the right edge, like the run board
        foreach (var grid in new[] { _logGrid, _doneGrid, _statsGrid })
        {
            grid.RowsAdded += (_, _) => FitTopArea();
            grid.RowsRemoved += (_, _) => FitTopArea();
        }
        Shown += (_, _) => FitTopArea();
        left.Controls.Add(SectionTitle("Stops"), 0, 0);
        left.Controls.Add(_logGrid, 0, 1);
        _doneTitle = SectionTitle("Finished Stops");
        _doneTitle.Visible = false;
        _doneGrid.Visible = false;
        left.Controls.Add(_doneTitle, 0, 2);
        left.Controls.Add(_doneGrid, 0, 3);

        var board = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        board.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));
        board.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        board.Controls.Add(SectionTitle("Session Leaderboard"), 0, 0);
        board.Controls.Add(_statsGrid, 0, 1);

        // the party (with its name box and buttons) on top, the small tables with the contracts and the times under it
        var side = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0), Padding = new Padding(10, 0, 0, 0) };
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // as high as the party table with its members
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        side.Controls.Add(BuildPartyPanel(), 0, 0);
        side.Controls.Add(BuildHeader(), 0, 1);

        main.Controls.Add(left, 0, 0);
        main.Controls.Add(side, 1, 0);
        main.Controls.Add(board, 0, 1);
        main.SetColumnSpan(board, 2);

        // the run board with its title
        var runs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        runs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        runs.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));
        runs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        runs.Controls.Add(SectionTitle("Cargo Runs"), 0, 0);

        BuildTripGrid();
        root.Controls.Add(main, 0, 0);
        runs.Controls.Add(_tripGrid, 0, 1);
        root.Controls.Add(runs, 0, 1);
    }

    const int TitleHeight = 30;
    const int SideWidth = 420;     // the column on the right: the party, the contracts and the times
    const int ContractsHeight = 132;   // the contracts table: header and four rows
    const int SummaryHeight = 190;     // the times and totals table: seven rows of 25 with their borders
    TableLayoutPanel? _header;

    /// <summary>The height the right column needs: the party, then the contracts and the times with their titles (as drawn, also on a scaled screen).</summary>
    int SideNeed()
    {
        var h = _partyPanel?.PreferredSize.Height ?? _partyHeight;
        if (_header != null)
        {
            foreach (RowStyle rs in _header.RowStyles)
            {
                if (rs.SizeType == SizeType.Absolute) h += (int)Math.Ceiling(rs.Height);
            }
        }
        return h;
    }

    /// <summary>
    /// The grid never scrolls, neither sideways nor up and down (FitTopArea gives it the height of all its rows).
    /// So the columns share the width in the proportion of their normal widths, and shrink a little on a narrow window.
    /// </summary>
    static void FitColumns(DataGridView g)
    {
        g.ScrollBars = ScrollBars.None;
        foreach (DataGridViewColumn c in g.Columns)
        {
            if (c.Name == "num")
            {
                // the # column keeps its small width, the others share the rest
                c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                c.MinimumWidth = 30;
                c.Width = 36;
                continue;
            }
            c.FillWeight = c.Width;
            c.MinimumWidth = 40;
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }
    }

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
            var done = Need(_doneGrid);
            var hasDone = _doneGrid.Rows.Count > 0;
            var leaders = Need(_statsGrid);

            // set the heights, then check that every row really is shown and add what is missing
            for (var pass = 0; pass < 4; pass++)
            {
                var title = LogicalToDeviceUnits(TitleHeight);   // as drawn, also on a scaled screen
                _left.RowStyles[0].Height = title;
                _left.RowStyles[1].Height = pickups;
                if (_doneTitle != null) _doneTitle.Visible = hasDone;
                _doneGrid.Visible = hasDone;
                _left.RowStyles[2].Height = hasDone ? title : 0;
                _left.RowStyles[3].Height = hasDone ? done : 0;
                var row0 = Math.Max(title + pickups + (hasDone ? title + done : 0), SideNeed());   // the party and the small tables on the right need their own height too
                var row1 = title + leaders;
                _main.RowStyles[0].Height = row0;
                _main.RowStyles[1].Height = row1;
                _root.RowStyles[0].Height = row0 + row1;
                _root.PerformLayout();
                _main.PerformLayout();
                _left.PerformLayout();

                var missingPickups = _logGrid.Rows.Count - _logGrid.DisplayedRowCount(false);
                var missingLeaders = _statsGrid.Rows.Count - _statsGrid.DisplayedRowCount(false);
                var missingDone = hasDone ? _doneGrid.Rows.Count - _doneGrid.DisplayedRowCount(false) : 0;
                if (missingPickups <= 0 && missingLeaders <= 0 && missingDone <= 0) break;
                if (missingDone > 0) done += missingDone * _doneGrid.RowTemplate.Height;
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

    /// <summary>
    /// The top row of the window, next to the Options menu: the status text just right of Options, and on the far right the session
    /// picker with the Start / Stop Session button.
    /// </summary>
    void AddSessionControlsToMenu()
    {
        _sessionBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _sessionBox.Width = 260;
        _sessionBox.Margin = new Padding(0, 0, 8, 0);
        _sessionBox.SelectedIndexChanged += SessionChanged;

        _status.AutoSize = false;
        _status.AutoEllipsis = true;
        _status.Size = new Size(430, 18);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Color.DimGray;
        _status.Tag = "muted";
        _status.Margin = new Padding(0);
        _menu.Items.Add(new ToolStripControlHost(_status) { Margin = new Padding(14, 0, 0, 0) });

        // right aligned items are placed from the right edge in the order they are added: the button is the last one, the picker is left of it
        _sessionToggleBtn.Alignment = ToolStripItemAlignment.Right;
        _sessionToggleBtn.Margin = new Padding(6, 1, 6, 1);
        _menu.Items.Add(_sessionToggleBtn);
        _menu.Items.Add(new ToolStripControlHost(_sessionBox) { Alignment = ToolStripItemAlignment.Right, Margin = new Padding(0, 0, 0, 0) });

        // the Trip Planner buttons, left of the session picker (Plan Trips left of Reset Trips), only while that tab is shown
        _menu.ShowItemToolTips = true;
        _planBtn.Alignment = _resetTripsBtn.Alignment = ToolStripItemAlignment.Right;
        _planBtn.Margin = new Padding(6, 1, 0, 1);
        _resetTripsBtn.Margin = new Padding(6, 1, 6, 1);
        _planBtn.Click += (_, _) => PlanTrips();
        _resetTripsBtn.Click += (_, _) => ResetTrips();
        _menu.Items.Add(_resetTripsBtn);
        _menu.Items.Add(_planBtn);
    }

    readonly MenuStrip _menu = new();
    readonly List<ToolStripMenuItem> _channelItems = new();

    /// <summary>The Options menu in the top left of the window.</summary>
    void BuildMenu()
    {
        var options = new ToolStripMenuItem("Options");

        _openBtn.Text = "Open Log File(s)…";
        _openBtn.Click += (_, _) => OpenLogs();
        _liveBtn.Text = "Watch Live Game.log";
        _liveBtn.Click += (_, _) => ToggleLive();
        _addBtn.Text = "Add Live Source…";
        _addBtn.Click += (_, _) => AddSource();
        _sourcesBtn.Text = "Sources…";
        _sourcesBtn.Click += (_, _) => ShowSources();

        var channel = new ToolStripMenuItem("Log Channel");
        var current = _data.Channel is { Length: > 0 } ch && Channels.Contains(ch) ? ch : "Auto";
        foreach (var name in Channels)
        {
            var item = new ToolStripMenuItem(name) { Checked = name == current };
            item.Click += (_, _) => SetChannel(name);
            _channelItems.Add(item);
            channel.DropDownItems.Add(item);
        }

        _partyBtn.Text = "Party…";
        _partyBtn.Click += (_, _) => _partyGrid.Focus();   // the party is the table at the top right of the front page
        _resetBtn.Text = "Use Log Times";
        _resetBtn.Click += (_, _) => ResetTimes();
        _copyBtn.Text = "Copy Table for Google Sheets";
        _copyBtn.Click += (_, _) => CopyTable();
        var backupBtn = new ToolStripMenuItem("Open Backup Folder");
        backupBtn.Click += (_, _) => OpenBackupFolder();
        _themeBtn.Text = ThemeButtonText();
        _themeBtn.Click += (_, _) => ToggleDark();
        _webBtn.Text = WebButtonText();
        _webBtn.Click += (_, _) => ToggleWeb();

        options.DropDownItems.AddRange(new ToolStripItem[]
        {
            _openBtn, _liveBtn, _addBtn, _sourcesBtn, channel,
            new ToolStripSeparator(),
            _partyBtn,
            new ToolStripSeparator(),
            _resetBtn, _copyBtn, backupBtn,
            new ToolStripSeparator(),
            _themeBtn, _webBtn,
            new ToolStripSeparator(),
            _versionItem, _updateBtn,
        });
        InitUpdater();
        _menu.Items.Add(options);
        _menu.Dock = DockStyle.Top;
        AddSessionControlsToMenu();
    }

    /// <summary>Opens the folder with the daily copies of the data file.</summary>
    void OpenBackupFolder()
    {
        try
        {
            Directory.CreateDirectory(Storage.BackupDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + Storage.BackupDir + "\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus("Could not open the backup folder: " + ex.Message);
        }
    }

    static readonly string[] Channels = { "Auto", "LIVE", "HOTFIX", "PTU", "EPTU" };

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
        var p = _header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = new Padding(0),
        };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));      // Contracts
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, ContractsHeight));   // the contracts: small, they are not the main thing
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));      // Session
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, SummaryHeight));
        p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // empty, so the clean table keeps its small rows

        // start / end / duration / totals, neutral and clean
        var t = new BufferedTable
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ColumnCount = 2,
            RowCount = 7,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 7; i++) t.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));

        ConfigureTimeBox(_startBox);
        ConfigureTimeBox(_endBox);
        StyleValue(_durationVal);
        StyleValue(_missionsVal);
        StyleValue(_revenueVal);
        StyleValue(_hourlyVal);
        StyleValue(_missionsDoneVal);
        StyleValue(_missionsActiveVal);

        AddSummaryRow(t, 0, "Start Time", WrapInput(_startBox));
        AddSummaryRow(t, 1, "Session Time", _durationVal);
        AddSummaryRow(t, 2, "Batches Delivered", _missionsVal);
        AddSummaryRow(t, 3, "Active Missions", _missionsActiveVal);
        AddSummaryRow(t, 4, "Missions Completed", _missionsDoneVal);
        AddSummaryRow(t, 5, "Total aUEC Earned", _revenueVal);
        AddSummaryRow(t, 6, "aUEC/Hour", _hourlyVal);
        p.Controls.Add(SectionTitle("Session"), 0, 2);
        p.Controls.Add(t, 0, 3);

        // the contract table on top, in the blue style of the other boards
        _rewards.Name = "compact";   // StyleGrid gives it smaller rows (also every time the theme is applied again)
        StyleGrid(_rewards);
        _rewards.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
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
            if (_inputs != null && _rewards.Rows.Count >= 3)
            {
                _inputs.Reward2 = ParseNumber(_rewards.Rows[0].Cells[1].Value);
                _inputs.Reward3 = ParseNumber(_rewards.Rows[1].Cells[1].Value);
                _inputs.Reward4 = ParseNumber(_rewards.Rows[2].Cells[1].Value);
            }
            RecomputeRewards();
            Storage.Save(_data);
            UpdateSnapshots();
            RefreshDebts();
        };
        p.Controls.Add(SectionTitle("Contracts"), 0, 0);
        p.Controls.Add(_rewards, 0, 1);
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
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
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
        _logGrid.Columns.Add(Col("From Ruin", 90, true, DataGridViewContentAlignment.MiddleRight));
        _logGrid.Columns.Add(Col("Batches", 90, true));
        var workerCol = new DataGridViewComboBoxColumn
        {
            HeaderText = "Assigned Worker",
            Width = 150,
            FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        workerCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        workerCol.Visible = false;   // the runs get their workers from the logs and the Trip Planner now (the column stays so older sessions keep what was assigned)
        _logGrid.Columns.Add(workerCol);
        _logGrid.Columns.Add(Col("Value", 100, true, DataGridViewContentAlignment.MiddleRight));
        _logGrid.Columns.Add(Col("Notes", 120, false, DataGridViewContentAlignment.MiddleLeft, true));
        AddNumberColumn(_logGrid);
        _logGrid.CellEndEdit += LogGrid_CellEndEdit;
        _logGrid.DataError += (_, e) => e.ThrowException = false;
        _logGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_logGrid.IsCurrentCellDirty) _logGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
    }

    /// <summary>The finished stops: pickup locations whose missions are all completed. Nothing to type here.</summary>
    void BuildDoneGrid()
    {
        _doneGrid.Dock = DockStyle.Fill;
        StyleGrid(_doneGrid);
        _doneGrid.AllowUserToAddRows = false;
        _doneGrid.AllowUserToDeleteRows = false;
        _doneGrid.Columns.Add(Col("Pickup Location", 190, true, DataGridViewContentAlignment.MiddleLeft));
        _doneGrid.Columns.Add(Col("From Ruin", 90, true, DataGridViewContentAlignment.MiddleRight));
        _doneGrid.Columns.Add(Col("Delivered", 90, true));
        _doneGrid.Columns.Add(Col("Worker", 150, true));
        _doneGrid.Columns.Add(Col("Value", 100, true, DataGridViewContentAlignment.MiddleRight));
        _doneGrid.Columns.Add(Col("Handed In", 120, true, DataGridViewContentAlignment.MiddleCenter, true));
        AddNumberColumn(_doneGrid);
    }

    void BuildTripGrid()
    {
        _tripGrid.Dock = DockStyle.Fill;
        _tripGrid.Margin = new Padding(0, 3, 0, 3);   // no side gap, so it lines up with the boards above
        StyleGrid(_tripGrid);
        _tripGrid.AllowUserToAddRows = false;
        _tripGrid.AllowUserToDeleteRows = false;
        _tripGrid.Columns.Add(Col("Worker", 190, true));   // as wide as Pickup Location above
        var runCol = Col("#", 36, true);   // the run number, at the far left like the # of the boards above
        _tripGrid.Columns.Add(runCol);
        runCol.DisplayIndex = 0;
        _tripGrid.Columns.Add(Col("Pickup Location & Time", 420, true, DataGridViewContentAlignment.MiddleLeft, true));
        // the columns after Pickups add up to the width of the right panel, so Pickups ends where the boards above end
        _tripGrid.Columns.Add(Col("Batches", 90, true));
        _tripGrid.Columns.Add(Col("Value", 100, true, DataGridViewContentAlignment.MiddleRight));
        _tripGrid.Columns.Add(Col("Started", 76, true));
        _tripGrid.Columns.Add(Col("Completed", 108, true));
        _tripGrid.Columns.Add(Col("Run Time", 90, true));
        var lostCol = new DataGridViewComboBoxColumn
        {
            HeaderText = "Lost Cargo",
            Width = 116,
            FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        lostCol.Items.AddRange("None", "Little", "All");
        lostCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _tripGrid.Columns.Add(lostCol);
        _tripGrid.DataError += (_, e) => e.ThrowException = false;
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

            // All lost = the run failed: one that is still on the way ends here, and picking something else brings it back
            var restart = false;
            if (level == "All" && !trip.End.HasValue)
            {
                _inputs.ManualEnds.Add(new ManualEnd { Worker = trip.Worker, Time = DateTime.UtcNow });
                restart = true;
            }
            else if (level != "All" && trip.Manual && trip.End.HasValue)
            {
                var at = trip.End.Value;
                _inputs.ManualEnds.RemoveAll(x => SameName(x.Worker, trip.Worker) && x.Time == at);
                restart = true;
            }
            Storage.Save(_data);
            BeginInvoke(new Action(() =>
            {
                if (restart) FillTrips();
                else RecomputeTimes();
                UpdateSnapshots();
                RefreshDebts();
            }));
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

    /// <summary>Time from the start of the run to its end, or to now while the run is still on the way (when watching live).</summary>
    string RunDuration(WorkerTrip t)
    {
        if (t.End.HasValue) return LongDuration(t.End.Value - t.Start);
        var latest = _sessions.Count > 0 && _current != null && _current.Key == _sessions[^1].Key;
        if (!_live || !latest || !HasOpenContracts()) return "";   // no active contract: nothing is counting
        var d = DateTime.UtcNow - t.Start;
        return d > TimeSpan.Zero ? LongDuration(d) : "";
    }

    /// <summary>Keeps the duration of runs that are on the way ticking between log updates.</summary>
    void UpdateLiveDurations()
    {
        foreach (var grid in new[] { _tripGrid, _planRunsGrid })
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.Tag is not WorkerTrip t || t.End.HasValue) continue;
                var text = RunDuration(t);
                if (!Equals(row.Cells[7].Value, text)) row.Cells[7].Value = text;
            }
        }
    }

    /// <summary>The latest thing that happened in a run: its hand-in when it is completed, otherwise the last pickup. A run that gets completed jumps to the top.</summary>
    static DateTime RunLastActivity(WorkerTrip t)
    {
        var last = t.End ?? t.Start;
        if (t.Pickups.Count > 0) last = last > t.Pickups.Max(p => p.Time) ? last : t.Pickups.Max(p => p.Time);
        return last;
    }

    /// <summary>When a run really started: the first pickup. A run begins at the previous hand-in, but the cargo run itself starts when cargo is picked up. Runs without a pickup use their start time.</summary>
    static DateTime RunStartedAt(WorkerTrip t) => t.Pickups.Count > 0 ? t.Pickups.Min(p => p.Time) : t.Start;

    /// <summary>The runs of a session: who did them comes from the logs and the Trip Planner (see WorkerTripBuilder.Build).</summary>
    List<WorkerTrip> BuildRuns(SessionGroup g, SessionInputs inp)
    {
        _data.PlanCreditSince ??= DateTime.UtcNow;
        return WorkerTripBuilder.Build(g, (z, t) => WorkerTripBuilder.WorkerAt(inp, z, t), _log!, inp.ManualEnds, (_data.PlanCreditSince.Value, inp.PickupClaims));
    }

    void FillTrips()
    {
        var wasUpdating = _updating;
        _updating = true;
        try
        {
            _tripGrid.Rows.Clear();
            _planRunsGrid.Rows.Clear();
            _trips = new List<WorkerTrip>();
            if (_current == null || _inputs == null || _log == null) return;

            var inputs = _inputs;
            var sessionStart = _current.Start;
            _trips = BuildRuns(_current, inputs)
                .Where(t => !t.End.HasValue || t.End.Value >= sessionStart)   // runs that were handed in before the session started belong to an earlier session
                .OrderByDescending(RunLastActivity).ThenByDescending(RunStartedAt).ThenByDescending(t => t.Start).ThenBy(t => t.Worker, StringComparer.OrdinalIgnoreCase).ToList();   // latest activity on top

            foreach (var t in _trips)
            {
                var pickups = string.Join(",  ", t.Pickups.Select(p => LocationName(p.Zone) + " " + FormatTime(p.Time)));
                var level = LostLevel(t.Key);
                var values = new object[]
                {
                    t.Worker,
                    t.Number.ToString(CultureInfo.InvariantCulture),
                    pickups,
                    t.Manual || level == "All" ? "failed" : t.End.HasValue ? t.HandedIn.ToString() : "",
                    RunValueText(t),
                    FormatTime(t.Start),
                    t.End.HasValue ? FormatTime(t.End.Value) : "",
                    RunDuration(t),
                    level,
                };
                foreach (var grid in new[] { _tripGrid, _planRunsGrid })
                {
                    var row = grid.Rows[grid.Rows.Add(values)];
                    row.Tag = t;
                    row.DefaultCellStyle.BackColor = RunColor(t.Manual ? "All" : level, t.End.HasValue);
                }
            }

            // the finished stops show who carried from them
            foreach (DataGridViewRow row in _doneGrid.Rows)
            {
                if (row.Tag is not string zone) continue;
                row.Cells[3].Value = string.Join(", ", _trips.Where(t => t.Pickups.Any(p => p.Zone == zone)).Select(t => t.Worker).Distinct(StringComparer.OrdinalIgnoreCase));
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

    /// <summary>
    /// A "#" column at the far left that counts the rows 1, 2, 3... It is added last (so the other columns keep their numbers in the code)
    /// and moved to the left on screen. NumberRows fills it.
    /// </summary>
    static void AddNumberColumn(DataGridView g)
    {
        var c = Col("#", 36, true);
        c.Name = "num";
        g.Columns.Add(c);
        c.DisplayIndex = 0;
    }

    /// <summary>Numbers the first `count` rows 1, 2, 3... (the rest, like a Total row, stay empty).</summary>
    static void NumberRows(DataGridView g, int? count = null)
    {
        if (!g.Columns.Contains("num")) return;
        var n = count ?? g.Rows.Count;
        for (var i = 0; i < g.Rows.Count; i++) g.Rows[i].Cells["num"].Value = i < n ? (i + 1).ToString(CultureInfo.InvariantCulture) : "";
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataGridView, object> HookedGrids = new();

    /// <summary>A grid only shows a selected cell while you are working in it, so no first cell lights up blue by itself.</summary>
    static void HideIdleSelection(DataGridView g)
    {
        if (HookedGrids.TryGetValue(g, out _)) return;
        HookedGrids.Add(g, new object());
        var busy = false;
        void Clear()
        {
            if (busy || g.ContainsFocus || g.SelectedCells.Count == 0) return;
            busy = true;
            try { g.ClearSelection(); }
            finally { busy = false; }
        }
        g.SelectionChanged += (_, _) => Clear();
        g.RowsAdded += (_, _) => { if (g.IsHandleCreated) g.BeginInvoke(new Action(Clear)); };
        g.HandleCreated += (_, _) => g.BeginInvoke(new Action(Clear));
        g.Leave += (_, _) => Clear();
    }

    static void StyleGrid(DataGridView g)
    {
        HideIdleSelection(g);
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
        var wrap = g.Name == "wrapheader";   // the leaderboards have long column names that wrap onto two lines
        var compact = g.Name == "compact";   // the small contracts table
        g.ColumnHeadersHeight = wrap ? 46 : compact ? 26 : 28;
        g.ColumnHeadersDefaultCellStyle.WrapMode = wrap ? DataGridViewTriState.True : DataGridViewTriState.NotSet;
        g.RowHeadersVisible = false;
        g.RowTemplate.Height = compact ? 24 : 26;
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

    readonly ToolStripMenuItem _sourcesBtn = new();
    readonly ToolStripMenuItem _partyBtn = new();

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

    void SetChannel(string name)
    {
        foreach (var item in _channelItems) item.Checked = item.Text == name;
        _data.Channel = name;
        Storage.Save(_data);
        if (_live) PollLive();
    }

    readonly List<LiveSource> _sources = new();

    /// <summary>The sources that are switched on. A disabled source keeps its address but is not read.</summary>
    List<string> ActivePaths() => _data.WatchPaths.Where(p => !_data.DisabledWatchPaths.Contains(p)).ToList();

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
        _data.DisabledWatchPaths.Remove(dlg.SelectedPath);
        Storage.Save(_data);
        SetStatus("Added " + dlg.SelectedPath);
        ApplySources();
    }

    /// <summary>Shows every added live source with a tick to switch it on or off, and lets you remove one for good.</summary>
    void ShowSources()
    {
        using var f = new Form
        {
            Text = "Live Sources",
            ClientSize = new Size(820, 320),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = Font,
        };
        var label = new Label { Text = "Tick or untick Enabled: it works at once. Untick stops reading a source without losing its address.", Dock = DockStyle.Top, Height = 28, Padding = new Padding(6, 6, 6, 0) };
        var grid = new DataGridView();
        StyleGrid(grid);
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Enabled", Width = 70, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(Col("Source", 330, true, DataGridViewContentAlignment.MiddleLeft));
        grid.Columns.Add(Col("Reads", 400, true, DataGridViewContentAlignment.MiddleLeft, true));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        var remove = new Button { Text = "Remove Selected", AutoSize = true };
        bar.Controls.Add(close);
        bar.Controls.Add(remove);
        f.Controls.Add(grid);
        f.Controls.Add(label);
        f.Controls.Add(bar);
        f.AcceptButton = close;

        var filling = false;
        void Fill()
        {
            filling = true;
            grid.Rows.Clear();
            foreach (var path in _data.WatchPaths)
            {
                var on = !_data.DisabledWatchPaths.Contains(path);
                var row = grid.Rows[grid.Rows.Add(on, path, "")];
                row.Tag = path;
                RefreshStatus(row);
            }
            filling = false;
        }
        void RefreshStatus(DataGridViewRow row)
        {
            var path = (string)row.Tag!;
            if (row.Cells[0].Value is false) { row.Cells[2].Value = "switched off"; return; }
            row.Cells[2].Value = ResolveLog(path) ?? "cannot be reached right now";
        }
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellValueChanged += (_, e) =>
        {
            if (filling || e.ColumnIndex != 0 || e.RowIndex < 0) return;
            var row = grid.Rows[e.RowIndex];
            var path = (string)row.Tag!;
            if (row.Cells[0].Value is false)
            {
                if (!_data.DisabledWatchPaths.Contains(path)) _data.DisabledWatchPaths.Add(path);
            }
            else _data.DisabledWatchPaths.Remove(path);
            Storage.Save(_data);
            filling = true;   // RefreshStatus writes to a cell, that is not a click
            RefreshStatus(row);
            filling = false;
            ApplySources();   // a tick works at once, the dialog does not have to be closed
        };
        remove.Click += (_, _) =>
        {
            if (grid.CurrentRow?.Tag is not string path) return;
            _data.WatchPaths.Remove(path);
            _data.DisabledWatchPaths.Remove(path);
            Storage.Save(_data);
            Fill();
            ApplySources();
        };
        Fill();
        ApplyTheme(f);
        f.ShowDialog(this);
    }

    /// <summary>
    /// Makes the program read exactly the sources that are ticked, right now. With none ticked it stops and clears the screen;
    /// ticking one again starts watching again (unless you pressed Stop Watching yourself).
    /// </summary>
    void ApplySources()
    {
        if (ActivePaths().Count > 0)
        {
            if (_live || !_userStopped) StartLive();
            return;
        }
        StopLive();
        _logs.Clear();
        _sources.Clear();
        _renderedVersion = -1;
        RefreshFromLog();
        SetStatus("All sources are switched off. Tick one in Sources to watch again.");
    }

    bool _userStopped;   // Stop Watching was pressed: ticking a source does not start watching by itself

    void ToggleLive()
    {
        if (_live)
        {
            StopLive();
            _userStopped = true;
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
        if (ActivePaths().Count == 0)
        {
            SetStatus("All sources are switched off. Tick one in Sources to watch.");
            return;
        }
        StartLive();
    }

    void StartLive()
    {
        _userStopped = false;
        _sources.Clear();
        _logs.Clear();
        var active = ActivePaths();
        foreach (var path in active)
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
        SetStatus("Watching " + string.Join(", ", active));
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

        var keep = _forceKey ?? _current?.Key;
        _forceKey = null;
        _sessions = SessionBuilder.Build(_log, _data.ManualSessions);

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
            SetStatus("No session yet. Press Start Session to begin.");
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
        if (g.Manual)
        {
            var until = g.Stop.HasValue ? g.Stop.Value.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture) : "now";
            return "Session: " + start + " to " + until + (g.Stop.HasValue ? "" : " (running)") + "  (" + g.Missions.Count + " missions)";
        }
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

    /// <summary>The id of the station in the tables: found by the position the log gave (the same on every server), else the last digits of the location id.</summary>
    static string StationKey(string zone) => StationIds.Of(zone) ?? Tail(zone);

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

    /// <summary>The name the program knows for a location (from its position), or Unknown ...id.</summary>
    string AutoName(string zone) =>
        KnownNames.TryGetValue(StationKey(zone), out var known) ? known : "Unknown ..." + Tail(zone);

    string LocationName(string zone)
    {
        var auto = AutoName(zone);
        if (_data.LocationNames.TryGetValue(zone, out var n) && !string.IsNullOrWhiteSpace(n))
        {
            // a saved name that is only the name of another station, while this one is known now (it was saved by clicking into the cell), is not a name you chose
            if (!auto.StartsWith("Unknown ...") && n != auto && KnownNames.ContainsValue(n)) return auto;
            return n;
        }
        return auto;
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
            if (WorkerTripBuilder.ResetWorkersForNewBatch(_current, _inputs)) Storage.Save(_data);   // new missions: a clean slate for the assigned workers
            ClaimNewPickups();   // before the boards change: the plan still has the batches that were just picked up
            _summary = SessionBuilder.Summarize(_current);
            _rows = SessionBuilder.Locations(_current);
            // both boards only show the missions that are active (not completed). A location where every batch of those missions
            // is handed in moves from the pickup table to the finished stops table. A completed mission is on neither: the
            // Cargo Runs log below keeps everything since the session started.
            _openRows = SessionBuilder.Locations(_current, true);
            _pickupRows = _openRows.Where(r => !r.DeliveredTime.HasValue).ToList();
            _doneRows = _openRows.Where(r => r.DeliveredTime.HasValue).ToList();

            _startBox.Text = _inputs.Start ?? FormatTime(_summary.Start);
            _endBox.Text = _inputs.End ?? (_summary.End.HasValue ? FormatTime(_summary.End.Value) : "");

            // the mission counts come from the log (2, 3 and 4 stop contracts), you only type the payouts
            _rewards.Rows.Clear();
            if (_inputs.Reward2 == 0) _inputs.Reward2 = DefaultReward2;
            if (_inputs.Reward3 == 0) _inputs.Reward3 = DefaultReward3;
            var accepted = _current.Missions;   // every accepted mission of the session (abandoned ones are already left out)
            _rewards.Rows.Add("2 stops", _inputs.Reward2.ToString("N0"), accepted.Count(m => m.Multi == 2), "");
            _rewards.Rows.Add("3 stops", _inputs.Reward3.ToString("N0"), accepted.Count(m => m.Multi == 3), "");
            _rewards.Rows.Add("4 stops", _inputs.Reward4.ToString("N0"), accepted.Count(m => m.Multi == 4), "");   // Master rank: type the payout
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
        if (_inputs != null) GetPlan();   // keeps the plan and the locked Current trips up to date while the planner tab is closed
    }

    void ClearView()
    {
        _updating = true;
        try
        {
            _inputs = null;
            _summary = null;
            _rows = new List<LocationRow>();
            _openRows = new List<LocationRow>();
            _pickupRows = new List<LocationRow>();
            _doneRows = new List<LocationRow>();
            _doneGrid.Rows.Clear();
            _startBox.Text = "";
            _endBox.Text = "";
            _rewards.Rows.Clear();
            _logGrid.Rows.Clear();
            _tripGrid.Rows.Clear();
            _planRunsGrid.Rows.Clear();
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
        _missionsDoneVal.Text = "";
        _missionsActiveVal.Text = "";
        _rewardsTotalMissions.Text = "";
        _rewardsTotalRevenue.Text = "";
        RefreshPartyPanel();   // the party is not part of a session: it is shown also when no session is loaded
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
        // only the active party members can be picked; someone already assigned stays in the list so the cell can show him
        var choices = _data.Party.Where(n => !_data.InactiveParty.Any(x => SameName(x, n))).ToList();
        if (_inputs != null)
        {
            foreach (var r in _inputs.Rows.Values)
            {
                var w = r.Worker?.Trim() ?? "";
                if (w.Length > 0 && !choices.Any(x => SameName(x, w))) choices.Add(w);
            }
        }
        foreach (var name in choices) col.Items.Add(name);
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
                var who = WorkerTripBuilder.PickupActor(_current, r.Zone, _log, _inputs!.WorkersResetAt);
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

        // the most valuable location on top. Once new missions are accepted, completed ones are gone from here
        // (everything else, runs, leaderboards and totals, keeps counting them)
        foreach (var r in _pickupRows.OrderByDescending(StopValue))
        {
            _inputs!.Rows.TryGetValue(r.Zone, out var inp);
            var idx = _logGrid.Rows.Add(
                LocationName(r.Zone),
                DistanceText(r.Zone),
                MissionsText(r),
                inp?.Worker ?? "",
                StopValueText(r),
                inp?.Notes ?? "");

            var row = _logGrid.Rows[idx];
            row.Tag = r.Zone;
            ColorMissionsCell(row, r);
        }
        NumberRows(_logGrid);

        // the finished stops: latest hand-in on top
        _doneGrid.Rows.Clear();
        foreach (var r in _doneRows.OrderByDescending(x => x.DeliveredTime ?? x.LoadedTime ?? DateTime.MinValue))
        {
            _inputs!.Rows.TryGetValue(r.Zone, out var inp);
            var idx = _doneGrid.Rows.Add(
                LocationName(r.Zone),
                DistanceText(r.Zone),
                r.Delivered + "/" + r.Missions,
                inp?.Worker ?? "",
                StopValueText(r),
                DeliveredText(r));
            var row = _doneGrid.Rows[idx];
            row.Tag = r.Zone;
            row.Cells[2].Style.BackColor = DoneGreen;
        }
        NumberRows(_doneGrid);
        FitTopArea();
        RefreshPartyPanel();

        if (_planBody.Visible && _planBody.IsHandleCreated) RefreshPlanner();   // the plan follows the batches you pick up while the tab is open
    }

    /// <summary>
    /// Value of a pickup location: every mission pays its reward split over its stops,
    /// so a stop of a 2 stop mission is worth reward / 2 and a stop of a 3 stop mission reward / 3.
    /// </summary>
    decimal StopValue(LocationRow r)
    {
        if (_inputs == null) return 0;
        return r.Stops2 * _inputs.Reward2 / 2m + r.Stops3 * _inputs.Reward3 / 3m + r.Stops4 * _inputs.Reward4 / 4m;
    }

    /// <summary>Gm from Ruin Station to this pickup location, empty when the distance table does not know it.</summary>
    string DistanceText(string zone)
    {
        var id = StationOf(zone);
        return id == null ? "" : PyroDistances.Between(PyroDistances.Ruin, id).ToString("N1", CultureInfo.InvariantCulture) + " Gm";
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
            var info = _pickupRows.FirstOrDefault(x => x.Zone == zone);
            if (info != null) row.Cells[4].Value = StopValueText(info);
        }
        foreach (DataGridViewRow row in _doneGrid.Rows)
        {
            if (row.Tag is not string zone) continue;
            var info = _doneRows.FirstOrDefault(x => x.Zone == zone);
            if (info != null) row.Cells[4].Value = StopValueText(info);
        }
        foreach (var grid in new[] { _tripGrid, _planRunsGrid })
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.Tag is WorkerTrip t) row.Cells[4].Value = RunValueText(t);
            }
        }
    }

    void ColorMissionsCell(DataGridViewRow row, LocationRow r)
    {
        row.Cells[2].Style.BackColor = r.Loaded >= r.Missions ? DoneGreen : CalcBlue;
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
                if (text.Length == 0 || text.StartsWith("Unknown ...") || text == AutoName(zone))
                {
                    _data.LocationNames.Remove(zone);
                    row.Cells[0].Value = LocationName(zone);
                }
                else
                {
                    _data.LocationNames[zone] = text;
                }
                row.Cells[1].Value = DistanceText(zone);   // a new name can be one the distance table knows
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
                if (row.Index >= 3) continue;   // the Total row
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
        if (_rewards.Rows.Count >= 4)
        {
            _rewards.Rows[3].Cells[2].Value = missions;
            _rewards.Rows[3].Cells[3].Value = revenue == 0 ? "" : revenue.ToString("N0");
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

    /// <summary>The value of the batches a run picked up (reward / stops of each mission).</summary>
    decimal RunValue(WorkerTrip t) => _current == null || _inputs == null ? 0 : RunValues(t, _current, _inputs).Total;

    string RunValueText(WorkerTrip t)
    {
        var v = RunValue(t);
        return v == 0 ? "" : v.ToString("N0");
    }

    /// <summary>How long the session lasts: until now while you are watching it live, otherwise until its last activity.</summary>
    /// <summary>True while the session has a contract that is not completed (abandoned and failed ones are not part of the session). The live clocks only run then.</summary>
    bool HasOpenContracts() => _current != null && _current.Missions.Any(m => !m.Completed);

    TimeSpan? SessionElapsed()
    {
        if (_current == null || _summary == null) return null;
        DateTime start = _summary.Start;
        if (_inputs?.Start is { Length: > 0 } typed && TryTime(typed, out var st))
        {
            var local = DateTime.Now.Date + st;
            if (local > DateTime.Now) local = local.AddDays(-1);
            start = local.ToUniversalTime();
        }
        var latest = _sessions.Count > 0 && _current.Key == _sessions[^1].Key;
        if (_inputs?.Start is not { Length: > 0 })
        {
            // a session can go on for days: a stretch with no activity for an hour does not count (the clock just stops, the session goes on)
            var running = !_current.Stop.HasValue && _live && latest && HasOpenContracts();
            return SessionBuilder.ActiveTime(_current, running ? DateTime.UtcNow : null);
        }
        var active = _live && latest && HasOpenContracts() && (!_summary.End.HasValue || DateTime.UtcNow - _summary.End.Value < SessionBuilder.ClockIdleLimit);
        var end = active ? DateTime.UtcNow : _summary.End ?? start;
        var d = end - start;
        return d > TimeSpan.Zero ? d : TimeSpan.Zero;
    }

    static string LongDuration(TimeSpan d) => (int)d.TotalHours + ":" + d.Minutes.ToString("00") + ":" + d.Seconds.ToString("00");

    decimal _incomeNow;

    /// <summary>Ticks the session time and the hourly rate every second.</summary>
    void UpdateSessionClock()
    {
        var d = SessionElapsed();
        var time = d.HasValue ? LongDuration(d.Value) : "";
        var hourly = d.HasValue && d.Value.TotalHours > 0 && _incomeNow > 0
            ? (_incomeNow / (decimal)d.Value.TotalHours).ToString("N0")
            : "";
        if (_durationVal.Text != time) _durationVal.Text = time;   // only repaint what changed
        if (_hourlyVal.Text != hourly) _hourlyVal.Text = hourly;
    }

    void RecomputeTimes()
    {
        // total earned = what completed missions really paid
        decimal income = 0;
        if (_current != null && _inputs != null)
            income = _current.Missions.Where(m => m.Completed).Sum(m => RewardOf(m, _inputs));
        _incomeNow = income;
        _revenueVal.Text = income == 0 ? "" : income.ToString("N0");
        UpdateSessionClock();

        FillStats();

        // batches = pickup stops; delivered = the ones in finished runs
        var totalBatches = _rows.Sum(r => r.Missions);
        var doneBatches = _trips.Where(t => t.End.HasValue && _inputs != null && !RunFailed(t, _inputs)).Sum(t => t.Pickups.Sum(p => p.Missions));
        _missionsVal.Text = _summary == null || totalBatches == 0 ? "" : Math.Min(doneBatches, totalBatches) + "/" + totalBatches;
        _missionsDoneVal.Text = _summary == null ? "" : _summary.Completed.ToString(CultureInfo.InvariantCulture);
        _missionsActiveVal.Text = _summary == null ? "" : _summary.Active.ToString(CultureInfo.InvariantCulture);
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
        // the whole session, also the locations that are gone from the pickup table because their missions are completed
        foreach (var info in _inputs == null ? new List<LocationRow>() : _rows.OrderByDescending(StopValue).ToList())
        {
            static string C(string? v) =>
                (v ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

            _inputs!.Rows.TryGetValue(info.Zone, out var inp);
            var notes = C(inp?.Notes);
            var loaded = info.LoadedTime.HasValue ? FormatTime(info.LoadedTime.Value) : "";
            var delivered = DeliveredText(info);
            if (info.Loaded < info.Missions)
                notes = (notes + " (" + (info.Missions - info.Loaded) + " of " + info.Missions + " still waiting)").Trim();

            // Same order as the spreadsheet: Pickup Location, Missions, Assigned Worker, Loaded Time, Delivered Time, Notes
            sb.Append(string.Join("\t", C(LocationName(info.Zone)), info.Missions.ToString(), C(inp?.Worker), loaded, delivered, notes)).Append('\n');
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
            if (!_live && (ActivePaths().Count > 0 || (_data.WatchPaths.Count == 0 && Directory.Exists(DefaultLogPath)))) ToggleLive();
        };
    }
}

/// <summary>A table that paints without flicker (the session clock changes every second).</summary>
sealed class BufferedTable : TableLayoutPanel
{
    public BufferedTable()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }
}
