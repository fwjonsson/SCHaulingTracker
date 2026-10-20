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

    // ------------------------------------------------------------------ the panel

    Control BuildPartyPanel()
    {
        _data.PartyTrackingSince ??= DateTime.UtcNow;

        var p = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(10, 0, 0, 0),
        };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));
        p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        StyleGrid(_partyGrid);
        _partyGrid.Dock = DockStyle.Fill;
        _partyGrid.Margin = new Padding(0, 3, 0, 3);
        _partyGrid.AllowUserToAddRows = false;
        _partyGrid.AllowUserToDeleteRows = false;
        _partyGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _partyGrid.Columns.Add(Col("Member", 100, true, DataGridViewContentAlignment.MiddleLeft));
        _partyGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Active", Width = 52, SortMode = DataGridViewColumnSortMode.NotSortable });
        _partyGrid.Columns.Add(Col("Batches", 56, false));
        _partyGrid.Columns.Add(Col("Since", 100, false));
        foreach (DataGridViewColumn c in _partyGrid.Columns)
        {
            c.FillWeight = c.Width;
            c.MinimumWidth = 40;
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }
        _partyGrid.ScrollBars = ScrollBars.Vertical;
        _partyGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_partyGrid.IsCurrentCellDirty && _partyGrid.CurrentCell is DataGridViewCheckBoxCell)
                _partyGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _partyGrid.CellValueChanged += PartyGrid_CellValueChanged;
        _partyGrid.DataError += (_, e) => e.ThrowException = false;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 4, 0, 0) };
        _partyName.Width = 100;
        _partyName.Margin = new Padding(0, 3, 6, 0);
        _partyName.PlaceholderText = "Type a name";
        _partyName.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            AddPartyMember();
        };
        StyleButton(_partyAddBtn, "Add");
        StyleButton(_partyRemoveBtn, "Remove");
        _partyAddBtn.AutoSize = true;
        _partyRemoveBtn.AutoSize = true;
        _partyAddBtn.Click += (_, _) => AddPartyMember();
        _partyRemoveBtn.Click += (_, _) => RemovePartyMember();
        bar.Controls.Add(_partyName);
        bar.Controls.Add(_partyAddBtn);
        bar.Controls.Add(_partyRemoveBtn);

        p.Controls.Add(SectionTitle("Party"), 0, 0);
        p.Controls.Add(_partyGrid, 0, 1);
        p.Controls.Add(bar, 0, 2);
        return p;
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
                var last = _data.PartyChanges.Where(c => SameName(c.Name, name)).OrderBy(c => c.TimeUtc).LastOrDefault();
                var since = last == null ? "" : last.TimeUtc.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
                var idx = _partyGrid.Rows.Add(name, active, Math.Max(1, ShipCapacityOf(name)), since);
                if (!active) _partyGrid.Rows[idx].DefaultCellStyle.ForeColor = Theme.Muted;
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
    }

    /// <summary>Saves and updates everything that depends on the party (debts, planner, worker choices).</summary>
    void PartyChanged()
    {
        Storage.Save(_data);
        BeginInvoke(new Action(() =>
        {
            _renderedVersion = -1;
            RefreshFromLog();
            RefreshPartyPanel();
            RefreshDebts();
            if (_planTabs.Visible && _planTabs.IsHandleCreated) RefreshPlanner();
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
            var typed = (Convert.ToString(row.Cells[3].Value) ?? "").Trim();
            if (typed.Length > 0 && !TryParseSince(typed, out var when))
            {
                SetStatus("Could not read that time. Type it like 12:30 or 19 Oct 22:15.");
            }
            else if (typed.Length > 0)
            {
                var last = _data.PartyChanges.Where(c => SameName(c.Name, name)).OrderBy(c => c.TimeUtc).LastOrDefault();
                if (last != null) last.TimeUtc = when;
                else SetMemberActive(name, IsActiveAt(name, DateTime.UtcNow), when);   // "since" for someone with no saved change: from then on he is in his current state
                _data.PartyTrackingSince ??= DateTime.UtcNow;
            }
        }
        PartyChanged();
    }

    static readonly string[] SinceFormats = { "HH:mm", "H:mm", "d MMM HH:mm", "d MMM H:mm", "yyyy-MM-dd HH:mm", "d/M HH:mm", "d/M H:mm" };

    /// <summary>12:30 (today, or yesterday when that is still ahead), 19 Oct 22:15 or 2026-10-19 22:15, in your own time.</summary>
    static bool TryParseSince(string text, out DateTime utc)
    {
        utc = default;
        text = text.Trim();
        foreach (var culture in new[] { CultureInfo.CurrentCulture, CultureInfo.InvariantCulture })
        {
            if (!DateTime.TryParseExact(text, SinceFormats, culture, DateTimeStyles.AllowWhiteSpaces, out var d)) continue;
            var timeOnly = !text.Contains(' ') && !text.Contains('-') && !text.Contains('/');
            var now = DateTime.Now;
            if (d > now.AddMinutes(1)) d = timeOnly ? d.AddDays(-1) : d.AddYears(-1);
            utc = DateTime.SpecifyKind(d, DateTimeKind.Local).ToUniversalTime();
            return true;
        }
        return false;
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
