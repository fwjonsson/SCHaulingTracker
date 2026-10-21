using System.Globalization;

namespace HaulingTracker;

// The Party table on the front page (top right): who is in the party, who is active, and since when.
// Every change of Active is saved with its time, so someone can join or leave in the middle of a session and the
// Debts tab still knows who was there when a mission paid out.
public sealed partial class MainForm
{
    readonly DataGridView _partyGrid = new();
    readonly TextBox _partyName = new();
    readonly Button _partyAddBtn = new();
    readonly Button _partyRemoveBtn = new();
    bool _partyUpdating;

    // ------------------------------------------------------------------ who was in the party when

    /// <summary>All names that are or were in the party.</summary>
    List<string> PartyNamesAll()
    {
        var list = new List<string>(_data.Party);
        foreach (var c in _data.PartyChanges)
        {
            if (!list.Any(x => SameName(x, c.Name))) list.Add(c.Name);
        }
        return list;
    }

    /// <summary>
    /// Was this person active in the party at this time? Without any saved change that is simply the Active tick now.
    /// Before his first saved change he was the opposite of what that change says (a first "joined" means he was not there before).
    /// </summary>
    bool IsActiveAt(string name, DateTime utc)
    {
        var changes = _data.PartyChanges.Where(c => SameName(c.Name, name)).OrderBy(c => c.TimeUtc).ToList();
        if (changes.Count == 0) return !_data.InactiveParty.Any(x => SameName(x, name));
        PartyChange? last = null;
        foreach (var c in changes)
        {
            if (c.TimeUtc <= utc) last = c;
            else break;
        }
        return last != null ? last.Active : !changes[0].Active;
    }

    List<string> PartyAt(DateTime utc) => PartyNamesAll().Where(n => IsActiveAt(n, utc)).ToList();

    /// <summary>From this time on the payouts are split by who was in the party at that moment. Earlier missions keep the old split.</summary>
    DateTime PartyTrackingStart()
    {
        var t = _data.PartyTrackingSince ?? DateTime.UtcNow;
        foreach (var c in _data.PartyChanges)
        {
            if (c.TimeUtc < t) t = c.TimeUtc;
        }
        return t;
    }

    void SetMemberActive(string name, bool active, DateTime? at = null)
    {
        _data.PartyTrackingSince ??= DateTime.UtcNow;
        _data.PartyChanges.Add(new PartyChange { Name = name, TimeUtc = at ?? DateTime.UtcNow, Active = active });
        _data.InactiveParty.RemoveAll(x => SameName(x, name));
        if (!active) _data.InactiveParty.Add(name);
    }

    // ------------------------------------------------------------------ ships

    /// <summary>The ship set for this worker, or null.</summary>
    Ship? ShipOf(string name)
    {
        var key = _data.Ship.FirstOrDefault(kv => SameName(kv.Key, name)).Value;
        return Ships.Find(key);
    }

    // ------------------------------------------------------------------ quantum drives

    /// <summary>The quantum drive set for this worker, or null.</summary>
    QtDrive? DriveOf(string name)
    {
        var key = _data.QuantumDrive.FirstOrDefault(kv => SameName(kv.Key, name)).Value;
        return QuantumDrives.Find(key);
    }

    /// <summary>The drive for the travel time estimates. Without a drive set, a common one is assumed.</summary>
    QtDrive QtOf(string name) => DriveOf(name) ?? QuantumDrives.Assumed;

    // ------------------------------------------------------------------ the panel

    Control BuildPartyPanel()
    {
        _data.PartyTrackingSince ??= DateTime.UtcNow;

        var p = _partyPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
        };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));   // the title: Party
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));   // the table, as high as its members (RefreshPartyPanel)
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, PartyRowHeight));   // the entry row: it looks like the next row of the table

        StyleGrid(_partyGrid);
        _partyGrid.Dock = DockStyle.Fill;
        _partyGrid.Margin = new Padding(0, 0, 0, 0);
        _partyGrid.AllowUserToAddRows = false;
        _partyGrid.AllowUserToDeleteRows = false;
        _partyGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _partyGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _partyGrid.Columns.Add(Col("Member", PartyColMember, true, DataGridViewContentAlignment.MiddleLeft));
        _partyGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Active", Width = PartyColActive, SortMode = DataGridViewColumnSortMode.NotSortable });
        _partyGrid.Columns.Add(Col("Batches", PartyColBatches, false));
        var shipCol = new DataGridViewComboBoxColumn { HeaderText = "Ship", Width = PartyColShip, SortMode = DataGridViewColumnSortMode.NotSortable, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing };
        shipCol.Items.Add("");
        foreach (var s in Ships.All) shipCol.Items.Add(s.Label);
        _partyGrid.Columns.Add(shipCol);
        var driveCol = new DataGridViewComboBoxColumn { HeaderText = "QT Drive", Width = PartyColDrive, SortMode = DataGridViewColumnSortMode.NotSortable, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing };
        driveCol.Items.Add("");
        foreach (var d in QuantumDrives.All.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Size)) driveCol.Items.Add(d.Label);
        _partyGrid.Columns.Add(driveCol);
        _partyGrid.Columns.Add(Col("Color", PartyColColor, true));   // click it to pick the colour of this worker on the Trip Planner Overview
        foreach (DataGridViewColumn c in _partyGrid.Columns)
        {
            c.FillWeight = c.Width;
            c.MinimumWidth = 40;
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }
        _partyGrid.ScrollBars = ScrollBars.Vertical;
        _partyGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_partyGrid.IsCurrentCellDirty && _partyGrid.CurrentCell is DataGridViewCheckBoxCell or DataGridViewComboBoxCell)
                _partyGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _partyGrid.CellValueChanged += PartyGrid_CellValueChanged;
        _partyGrid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 5) return;
            var name = Convert.ToString(_partyGrid.Rows[e.RowIndex].Cells[0].Value) ?? "";
            if (name.Length == 0) return;
            using var dlg = new ColorDialog { Color = WorkerColor(name), FullOpen = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            foreach (var k in _data.WorkerColors.Keys.Where(k => SameName(k, name)).ToList()) _data.WorkerColors.Remove(k);
            _data.WorkerColors[name] = dlg.Color.ToArgb();
            PartyChanged();
        };
        _partyGrid.DataError += (_, e) => e.ThrowException = false;

        // The entry row: the name box is a cell under Member / Active, Add sits under Batches and Remove under Ship, QT Drive and Color
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, PartyColMember + PartyColActive));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, PartyColBatches));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, PartyColShip + PartyColDrive + PartyColColor));
        bar.RowStyles.Add(new RowStyle(SizeType.Absolute, PartyRowHeight));
        _partyName.BorderStyle = BorderStyle.None;
        _partyName.Dock = DockStyle.Fill;
        _partyName.PlaceholderText = "New member";
        _partyName.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            AddPartyMember();
        };
        foreach (var (b, text) in new[] { (_partyAddBtn, "Add"), (_partyRemoveBtn, "Remove") })
        {
            b.Text = text;
            b.AutoSize = false;
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(0);
            b.Padding = new Padding(0);
        }
        _partyAddBtn.Click += (_, _) => AddPartyMember();
        _partyRemoveBtn.Click += (_, _) => RemovePartyMember();

        // the name box looks like a cell of the table: a bordered cell with the text a little in from the edge
        var cell = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(6, 4, 4, 0), Tag = "input" };
        cell.Controls.Add(_partyName);
        cell.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Lines);
            e.Graphics.DrawRectangle(pen, 0, 0, cell.Width - 1, cell.Height - 1);
        };
        bar.Controls.Add(cell, 0, 0);
        bar.Controls.Add(_partyAddBtn, 1, 0);
        bar.Controls.Add(_partyRemoveBtn, 2, 0);

        p.Controls.Add(SectionTitle("Party"), 0, 0);
        p.Controls.Add(_partyGrid, 0, 1);
        p.Controls.Add(bar, 0, 2);
        return p;
    }

    // the party table: one row per member (up to PartyMaxRows, then it scrolls) with the entry row right under the last one
    const int PartyRowHeight = 26;
    const int PartyMaxRows = 6;
    // proportions of the columns (the entry row under the table uses the same ones)
    const int PartyColMember = 90, PartyColActive = 62, PartyColBatches = 78, PartyColShip = 150, PartyColDrive = 116, PartyColColor = 64;
    TableLayoutPanel? _partyPanel;
    int _partyHeight = PartyMaxHeight;   // as drawn now, the party table and its entry row

    /// <summary>The height of the party table with all its rows, and of its entry row.</summary>
    const int PartyMaxHeight = TitleHeight + 28 + PartyMaxRows * PartyRowHeight + 2 + PartyRowHeight;   // with its title

    void FitPartyGrid()
    {
        if (_partyPanel == null) return;
        var rows = Math.Clamp(_partyGrid.Rows.Count, 1, PartyMaxRows);
        var grid = _partyGrid.ColumnHeadersHeight + rows * _partyGrid.RowTemplate.Height + 2;
        _partyPanel.RowStyles[0].Height = TitleHeight;
        _partyPanel.RowStyles[1].Height = grid;
        _partyPanel.RowStyles[2].Height = PartyRowHeight;
        _partyHeight = TitleHeight + grid + PartyRowHeight;
    }

    void RefreshPartyPanel()
    {
        if (_partyGrid.IsCurrentCellInEditMode) return;
        _partyUpdating = true;
        try
        {
            var selected = _partyGrid.CurrentRow?.Cells[0].Value as string;
            _partyGrid.Rows.Clear();
            foreach (var name in _data.Party)
            {
                var active = IsActiveAt(name, DateTime.UtcNow);
                var idx = _partyGrid.Rows.Add(name, active, Math.Max(1, ShipCapacityOf(name)), ShipOf(name)?.Label ?? "", DriveOf(name)?.Label ?? "", "");
                if (!active) _partyGrid.Rows[idx].DefaultCellStyle.ForeColor = Theme.Muted;
                var swatch = _partyGrid.Rows[idx].Cells[5].Style;
                swatch.BackColor = swatch.SelectionBackColor = WorkerColor(name);
            }
            if (selected != null)
            {
                foreach (DataGridViewRow r in _partyGrid.Rows)
                {
                    if (SameName(Convert.ToString(r.Cells[0].Value) ?? "", selected)) r.Selected = true;
                }
            }
        }
        finally
        {
            _partyUpdating = false;
        }
        FitPartyGrid();
        FitTopArea();
    }

    void PartyChanged()
    {
        Storage.Save(_data);
        BeginInvoke(new Action(() =>
        {
            _renderedVersion = -1;
            RefreshFromLog();
            RefreshPartyPanel();
            RefreshDebts();
            if (_planBody.Visible && _planBody.IsHandleCreated) RefreshPlanner();
        }));
    }

    void PartyGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_partyUpdating || e.RowIndex < 0) return;
        var row = _partyGrid.Rows[e.RowIndex];
        var name = Convert.ToString(row.Cells[0].Value) ?? "";
        if (name.Length == 0) return;

        if (e.ColumnIndex == 1)
        {
            var active = row.Cells[1].Value is true;
            if (active == IsActiveAt(name, DateTime.UtcNow)) return;
            SetMemberActive(name, active);
            SetStatus(name + (active ? " joined the party." : " left the party.") + " Mission payouts from now on are split between the active members.");
        }
        else if (e.ColumnIndex == 2)
        {
            var typed = (Convert.ToString(row.Cells[2].Value) ?? "").Trim();
            _data.ShipCapacity[name] = int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? Math.Clamp(n, 1, MaxShipCapacity)
                : DefaultShipCapacity;
        }
        else if (e.ColumnIndex == 3)
        {
            var ship = Ships.Find(Convert.ToString(row.Cells[3].Value));
            foreach (var k in _data.Ship.Keys.Where(k => SameName(k, name)).ToList()) _data.Ship.Remove(k);
            if (ship != null) _data.Ship[name] = ship.Name;
        }
        else if (e.ColumnIndex == 4)
        {
            var picked = QuantumDrives.Find(Convert.ToString(row.Cells[4].Value));
            foreach (var k in _data.QuantumDrive.Keys.Where(k => SameName(k, name)).ToList()) _data.QuantumDrive.Remove(k);
            if (picked != null) _data.QuantumDrive[name] = picked.Key;
        }
        PartyChanged();
    }

    void AddPartyMember()
    {
        var name = _partyName.Text.Trim();
        if (name.Length == 0) return;
        var existing = _data.Party.FirstOrDefault(x => SameName(x, name));
        if (existing != null)
        {
            if (IsActiveAt(existing, DateTime.UtcNow))
            {
                SetStatus(existing + " is already in the party.");
                _partyName.Clear();
                return;
            }
            SetMemberActive(existing, true);
        }
        else
        {
            _data.Party.Add(name);
            if (!_data.ShipCapacity.ContainsKey(name)) _data.ShipCapacity[name] = DefaultShipCapacity;
            SetMemberActive(name, true);
        }
        _partyName.Clear();
        SetStatus(name + " joined the party.");
        PartyChanged();
    }

    void RemovePartyMember()
    {
        var name = _partyGrid.CurrentRow?.Cells[0].Value as string;
        if (string.IsNullOrEmpty(name))
        {
            SetStatus("Click a name in the party table first.");
            return;
        }
        var answer = MessageBox.Show(this,
            "Remove " + name + " from the party?\n\nHis earlier work and debts stay in the Debts tab, he just stops getting a share of new payouts.",
            "Remove from Party", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        if (IsActiveAt(name, DateTime.UtcNow)) SetMemberActive(name, false);
        _data.Party.RemoveAll(x => SameName(x, name));
        _data.InactiveParty.RemoveAll(x => SameName(x, name));
        SetStatus(name + " was removed from the party.");
        PartyChanged();
    }
}
