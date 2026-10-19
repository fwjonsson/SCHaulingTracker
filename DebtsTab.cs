using System.Globalization;

namespace HaulingTracker;

// The "Debts" tab: earnings per worker, balance with every other party member and the money transfers found in the logs.
public sealed partial class MainForm
{
    readonly ComboBox _debtMember = new();
    readonly DataGridView _balanceGrid = new();
    readonly DataGridView _earnGrid = new();
    readonly DataGridView _txGrid = new();
    readonly DataGridView _historyGrid = new();
    readonly Label _bestLine = new();
    string _debtSelected = "";
    bool _debtUpdating;

    /// <summary>A title in bold, with an optional short explanation after it in grey.</summary>
    static Control Caption(string title, string? hint = null)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        p.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Margin = new Padding(0, 8, 10, 0),
        });
        if (!string.IsNullOrEmpty(hint))
        {
            p.Controls.Add(new Label { Text = hint, AutoSize = true, ForeColor = Color.DimGray, Tag = "muted", Margin = new Padding(0, 11, 0, 0) });
        }
        return p;
    }

    Control BuildDebtsPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        bar.Controls.Add(new Label { Text = "Party Member:", AutoSize = true, Margin = new Padding(0, 10, 6, 0) });
        _debtMember.DropDownStyle = ComboBoxStyle.DropDownList;
        _debtMember.Width = 170;
        _debtMember.Margin = new Padding(0, 6, 24, 4);
        _debtMember.SelectedIndexChanged += (_, _) =>
        {
            if (_debtUpdating) return;
            _debtSelected = _debtMember.SelectedItem as string ?? "";
            RefreshDebts();
        };
        bar.Controls.Add(_debtMember);
        var partyBtn = new Button();
        StyleButton(partyBtn, "Session Party…");
        partyBtn.Click += (_, _) => EditSessionParty();
        bar.Controls.Add(partyBtn);
        var fileBtn = new Button();
        StyleButton(fileBtn, "Data File…");
        fileBtn.Click += (_, _) => ChooseDataFile();
        bar.Controls.Add(fileBtn);
        t.Controls.Add(bar, 0, 0);

        t.Controls.Add(Caption("Balances", "+ they owe the selected member, − the selected member owes them. Type over a number to correct it."), 0, 1);
        StyleGrid(_balanceGrid);
        _balanceGrid.Dock = DockStyle.Fill;
        _balanceGrid.AllowUserToAddRows = false;
        _balanceGrid.AllowUserToDeleteRows = false;
        _balanceGrid.Columns.Add(Col("Party Member", 200, true, DataGridViewContentAlignment.MiddleLeft));
        _balanceGrid.Columns.Add(Col("Balance", 160, false, DataGridViewContentAlignment.MiddleRight));
        _balanceGrid.Columns.Add(Col("Meaning", 400, true, DataGridViewContentAlignment.MiddleLeft, true));
        _balanceGrid.CellEndEdit += BalanceEdited;
        t.Controls.Add(_balanceGrid, 0, 2);

        t.Controls.Add(Caption("Earnings", "Value of the pickup locations each worker carried"), 0, 3);
        StyleGrid(_earnGrid);
        _earnGrid.Dock = DockStyle.Fill;
        _earnGrid.AllowUserToAddRows = false;
        _earnGrid.AllowUserToDeleteRows = false;
        _earnGrid.Columns.Add(Col("Worker", 200, true, DataGridViewContentAlignment.MiddleLeft));
        _earnGrid.Columns.Add(Col("This Session", 160, true, DataGridViewContentAlignment.MiddleRight));
        _earnGrid.Columns.Add(Col("All Sessions", 160, true, DataGridViewContentAlignment.MiddleRight));
        _earnGrid.Columns.Add(Col("Net Balance (+ Owed to Them)", 240, true, DataGridViewContentAlignment.MiddleRight, true));
        t.Controls.Add(_earnGrid, 0, 4);

        t.Controls.Add(Caption("Money Transfers", "Of the selected member. Tick Payment when a transfer pays off a debt."), 0, 5);
        StyleGrid(_txGrid);
        _txGrid.Dock = DockStyle.Fill;
        _txGrid.AllowUserToAddRows = false;
        _txGrid.AllowUserToDeleteRows = false;
        _txGrid.Columns.Add(Col("Time", 150, true));
        _txGrid.Columns.Add(Col("From", 160, true));
        _txGrid.Columns.Add(Col("To", 160, true));
        _txGrid.Columns.Add(Col("Amount", 170, true, DataGridViewContentAlignment.MiddleRight));
        _txGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Payment",
            Width = 100,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
        _txGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_txGrid.IsCurrentCellDirty) _txGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _txGrid.CellValueChanged += TxChanged;
        t.Controls.Add(_txGrid, 0, 6);

        t.Controls.Add(new Label
        {
            Text = "Mission rewards are split equally between the party of each session, but everybody is owed the value he carried. Use Session Party… if someone was in the party without carrying. Ticked payments reduce the debts.",
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
            Tag = "muted",
            AutoEllipsis = true,
        }, 0, 7);
        return t;
    }

    Control BuildHistoryPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 62));

        t.Controls.Add(Caption("All-Time Leaderboard", "Every saved session"), 0, 0);
        BuildStatColumns(_allTimeGrid, true);
        t.Controls.Add(_allTimeGrid, 0, 1);

        _bestLine.Dock = DockStyle.Fill;
        _bestLine.TextAlign = ContentAlignment.MiddleLeft;
        _bestLine.ForeColor = Color.DimGray;
        _bestLine.Tag = "muted";
        t.Controls.Add(_bestLine, 0, 2);

        t.Controls.Add(Caption("Saved Sessions", "They stay here also when their logs are gone"), 0, 3);
        StyleGrid(_historyGrid);
        _historyGrid.Dock = DockStyle.Fill;
        _historyGrid.AllowUserToAddRows = false;
        _historyGrid.AllowUserToDeleteRows = false;
        _historyGrid.Columns.Add(Col("Date", 130, true));
        _historyGrid.Columns.Add(Col("Time", 130, true));
        _historyGrid.Columns.Add(Col("Missions", 90, true, DataGridViewContentAlignment.MiddleRight));
        _historyGrid.Columns.Add(Col("Revenue", 140, true, DataGridViewContentAlignment.MiddleRight));
        _historyGrid.Columns.Add(Col("Carried by Worker", 400, true, DataGridViewContentAlignment.MiddleLeft, true));
        t.Controls.Add(_historyGrid, 0, 4);
        return t;
    }

    void FillHistory()
    {
        FillAllTime();
        _historyGrid.Rows.Clear();
        var saved = _data.Sessions.Values
            .Where(s => s.Carried.Count > 0 && s.StartUtc.HasValue)
            .OrderByDescending(s => s.StartUtc)
            .ToList();
        decimal revenue = 0;
        var missions = 0;
        foreach (var s in saved)
        {
            var start = s.StartUtc!.Value.ToLocalTime();
            var time = start.ToString("HH:mm", CultureInfo.InvariantCulture)
                + (s.EndUtc.HasValue ? " to " + s.EndUtc.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) : "");
            var carried = string.Join(",  ", s.Carried.OrderByDescending(kv => kv.Value)
                .Select(kv => kv.Key + " " + kv.Value.ToString("N0", CultureInfo.InvariantCulture)));
            _historyGrid.Rows.Add(start.ToString("d MMM yyyy", CultureInfo.CurrentCulture), time, s.MissionCount,
                s.Revenue.ToString("N0", CultureInfo.InvariantCulture), carried);
            revenue += s.Revenue;
            missions += s.MissionCount;
        }
        // best session: most revenue, and the best aUEC per hour
        var best = saved.OrderByDescending(x => x.Revenue).FirstOrDefault();
        var bestHour = saved.Where(x => x.StartUtc.HasValue && x.EndUtc.HasValue && x.EndUtc > x.StartUtc)
            .OrderByDescending(x => x.Revenue / (decimal)(x.EndUtc!.Value - x.StartUtc!.Value).TotalHours).FirstOrDefault();
        var parts = new List<string>();
        if (best != null)
            parts.Add("Best session: " + best.StartUtc!.Value.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture) + ", "
                + best.Revenue.ToString("N0", CultureInfo.InvariantCulture) + " aUEC");
        if (bestHour != null)
            parts.Add("Best aUEC/hour: " + bestHour.StartUtc!.Value.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture) + ", "
                + (bestHour.Revenue / (decimal)(bestHour.EndUtc!.Value - bestHour.StartUtc!.Value).TotalHours).ToString("N0", CultureInfo.InvariantCulture));
        _bestLine.Text = string.Join("      ", parts);

        var tr = _historyGrid.Rows.Add("Total", "", missions, revenue.ToString("N0", CultureInfo.InvariantCulture), "");
        _historyGrid.Rows[tr].DefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
    }

    // ------------------------------------------------------------------ saving what the logs show

    static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static decimal CarriedBy(Dictionary<string, decimal> carried, string name) =>
        carried.Where(kv => SameName(kv.Key, name)).Sum(kv => kv.Value);

    /// <summary>Every pickup stop of a completed mission with the worker who did it and its value (reward / stops).</summary>
    List<(string Worker, decimal Value)> CompletedStops(SessionGroup g, SessionInputs inp)
    {
        var result = new List<(string, decimal)>();
        if (_log == null) return result;
        var r2 = inp.Reward2 == 0 ? DefaultReward2 : inp.Reward2;
        var r3 = inp.Reward3 == 0 ? DefaultReward3 : inp.Reward3;
        foreach (var m in g.Missions)
        {
            if (!m.Completed || !m.EndedAt.HasValue) continue;
            var reward = m.Multi == 2 ? r2 : m.Multi == 3 ? r3 : 0m;
            if (reward <= 0) continue;
            foreach (var kv in m.ObjZone)
            {
                if (!kv.Key.StartsWith("pickup_") || !System.Text.RegularExpressions.Regex.IsMatch(kv.Key, @"_\d+$")) continue;
                if (!m.Events.TryGetValue(kv.Key, out var ev) || ev.Count == 0) continue;
                string? who = null;
                if (inp.Rows.TryGetValue(kv.Value, out var ri) && !string.IsNullOrWhiteSpace(ri.Worker)) who = ri.Worker.Trim();
                else who = WorkerTripBuilder.ActorAt(_log, true, ev.Min(e => e.Time));
                if (who == null) continue;
                result.Add((who, reward / m.Multi));
            }
        }
        return result;
    }

    /// <summary>What every worker carried: the value of the stops of completed missions. This is the real income that the debts and the all-time board use.</summary>
    Dictionary<string, decimal> ComputeCarried(SessionGroup g, SessionInputs inp)
    {
        var carried = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (who, value) in CompletedStops(g, inp))
        {
            carried.TryGetValue(who, out var old);
            carried[who] = old + value;
        }
        return carried;
    }

    /// <summary>Keeps what every session earned per worker, and the money transfers, so they survive closing the program.</summary>
    void UpdateSnapshots()
    {
        if (_log == null) return;
        var changed = false;

        foreach (var g in _sessions)
        {
            _data.Sessions.TryGetValue(g.Key, out var stored);
            var carried = ComputeCarried(g, stored ?? new SessionInputs());
            if (carried.Count == 0) continue;

            var inp = stored ?? GetInputs(g.Key);
            var ids = g.Missions.Select(m => m.Id).ToList();

            // a session that grew and got a new key must not be counted twice
            foreach (var kv in _data.Sessions)
            {
                if (kv.Key == g.Key || kv.Value.Carried.Count == 0) continue;
                if (kv.Value.MissionIds.Any(id => ids.Contains(id)))
                {
                    kv.Value.Carried = new Dictionary<string, decimal>();
                    changed = true;
                }
            }

            var members = new List<string>(inp.Party ?? new List<string>());
            foreach (var k in carried.Keys)
            {
                if (!members.Any(x => SameName(x, k))) members.Add(k);
            }

            var summary = SessionBuilder.Summarize(g);
            var r2 = inp.Reward2 == 0 ? DefaultReward2 : inp.Reward2;
            var r3 = inp.Reward3 == 0 ? DefaultReward3 : inp.Reward3;
            var revenue = g.Missions.Count(m => m.Multi == 2) * r2 + g.Missions.Count(m => m.Multi == 3) * r3;
            if (inp.StartUtc != g.Start || inp.EndUtc != summary.End || inp.MissionCount != g.Missions.Count || inp.Revenue != revenue)
            {
                inp.StartUtc = g.Start;
                inp.EndUtc = summary.End;
                inp.MissionCount = g.Missions.Count;
                inp.Revenue = revenue;
                changed = true;
            }

            var trips = WorkerTripBuilder.Build(g,
                z => inp.Rows.TryGetValue(z, out var r) && !string.IsNullOrWhiteSpace(r.Worker) ? r.Worker : "Unassigned",
                _log, inp.ManualEnds);
            var stats = ComputeStats(trips, inp);
            // batches delivered for the all-time board come from completed missions
            foreach (var w in stats.Values) w.Missions = 0;
            foreach (var (who, _) in CompletedStops(g, inp))
            {
                var key = stats.Keys.FirstOrDefault(k => SameName(k, who)) ?? who;
                if (!stats.TryGetValue(key, out var ws)) stats[key] = ws = new WorkerStat();
                ws.Missions++;
            }

            var same = SameStats(inp.Stats, stats)
                && inp.Carried.Count == carried.Count
                && carried.All(kv => inp.Carried.TryGetValue(kv.Key, out var v) && v == kv.Value)
                && inp.Members.Count == members.Count
                && members.All(m => inp.Members.Any(x => SameName(x, m)));
            if (!same || inp.MissionIds.Count != ids.Count)
            {
                inp.Carried = new Dictionary<string, decimal>(carried);
                inp.Stats = new Dictionary<string, WorkerStat>(stats);
                inp.Members = members;
                inp.MissionIds = ids;
                changed = true;
            }
        }

        foreach (var t in _log.Transfers)
        {
            var known = _data.Transfers.Any(x => x.Amount == t.Amount && SameName(x.From, t.From) && SameName(x.To, t.To)
                && Math.Abs((x.Time - t.Time).TotalSeconds) <= 10);
            if (known) continue;
            _data.Transfers.Add(new SavedTransfer { Time = t.Time, From = t.From, To = t.To, Amount = t.Amount });
            changed = true;
        }

        if (changed) Storage.Save(_data);
    }

    // ------------------------------------------------------------------ showing it

    List<string> DebtPartyNames()
    {
        var list = new List<string>(_data.Party);
        foreach (var s in _data.Sessions.Values)
        {
            foreach (var k in s.Carried.Keys)
            {
                if (!list.Any(x => SameName(x, k))) list.Add(k);
            }
        }
        return list;
    }

    static void SetItems(ComboBox box, List<string> items)
    {
        var same = box.Items.Count == items.Count;
        for (var i = 0; same && i < items.Count; i++) same = (box.Items[i] as string) == items[i];
        if (same) return;
        box.Items.Clear();
        foreach (var x in items) box.Items.Add(x);
    }

    static string Signed(decimal v) => v.ToString("+#,##0;-#,##0;0", CultureInfo.InvariantCulture);

    static string Meaning(string me, string other, decimal v)
    {
        if (Math.Abs(v) < 0.5m) return "even";
        var amount = Math.Abs(v).ToString("N0", CultureInfo.InvariantCulture);
        return v > 0 ? other + " owes " + me + " " + amount : me + " owes " + other + " " + amount;
    }

    void RefreshDebts()
    {
        if (_debtUpdating || _balanceGrid.IsCurrentCellInEditMode) return;
        _debtUpdating = true;
        try
        {
            var party = DebtPartyNames();

            SetItems(_debtMember, party);
            var idx = party.FindIndex(p => SameName(p, _debtSelected));
            if (idx < 0) idx = party.Count > 0 ? 0 : -1;
            _debtMember.SelectedIndex = idx;
            _debtSelected = idx >= 0 ? party[idx] : "";

            var nets = DebtCalc.Nets(_data.Sessions.Values, _data.Transfers);
            var settle = DebtCalc.Settle(nets);

            _balanceGrid.Rows.Clear();
            if (_debtSelected.Length > 0)
            {
                foreach (var o in party)
                {
                    if (SameName(o, _debtSelected)) continue;
                    var calc = DebtCalc.Between(settle, _debtSelected, o);
                    var shown = calc + GetAdj(_debtSelected, o);
                    var i = _balanceGrid.Rows.Add(o, Signed(shown), Meaning(_debtSelected, o, shown));
                    _balanceGrid.Rows[i].Tag = calc;
                }
            }

            _earnGrid.Rows.Clear();
            decimal sumNow = 0;
            decimal sumAll = 0;
            foreach (var p in party)
            {
                var now = _inputs == null ? 0m : CarriedBy(_inputs.Carried, p);
                var all = _data.Sessions.Values.Sum(s => CarriedBy(s.Carried, p));
                nets.TryGetValue(p, out var net);
                sumNow += now;
                sumAll += all;
                _earnGrid.Rows.Add(p, now.ToString("N0", CultureInfo.InvariantCulture), all.ToString("N0", CultureInfo.InvariantCulture), Signed(net));
            }
            var tr = _earnGrid.Rows.Add("Total", sumNow.ToString("N0", CultureInfo.InvariantCulture), sumAll.ToString("N0", CultureInfo.InvariantCulture), "");
            _earnGrid.Rows[tr].DefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);

            FillHistory();

            _txGrid.Rows.Clear();
            if (_debtSelected.Length > 0)
            {
                foreach (var t in _data.Transfers
                    .Where(x => SameName(x.From, _debtSelected) || SameName(x.To, _debtSelected))
                    .OrderByDescending(x => x.Time))
                {
                    var i = _txGrid.Rows.Add(
                        t.Time.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture),
                        t.From,
                        t.To,
                        t.Amount.ToString("N0", CultureInfo.InvariantCulture),
                        t.Payment);
                    _txGrid.Rows[i].Tag = t;
                }
            }
        }
        finally
        {
            _debtUpdating = false;
        }
    }

    // ------------------------------------------------------------------ data file

    bool _skipSave;

    /// <summary>Lets you keep the data file somewhere else, for example in a OneDrive folder, so it follows you between PCs.</summary>
    void ChooseDataFile()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Pick the folder for the data file, for example a folder in your OneDrive. Now in use: " + Storage.FilePath,
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var target = Path.Combine(dlg.SelectedPath, "HaulingTracker-data.json");
        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(Storage.FilePath), StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("That file is already in use.");
            return;
        }

        try
        {
            if (File.Exists(target))
            {
                var answer = MessageBox.Show(this,
                    "There is already a data file in that folder (for example from another PC).\n\n" +
                    "Yes = use that file (the tracker restarts, the data on this PC stays in its old file)\n" +
                    "No = replace it with the data from this PC\n" +
                    "Cancel = do nothing",
                    "Data File", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.Yes)
                {
                    Storage.SetLocation(target);
                    _skipSave = true;   // do not overwrite the file we are switching to
                    Application.Restart();
                    return;
                }
            }

            Storage.SetLocation(target);
            Storage.Save(_data);
            SetStatus("Data is now saved in " + target);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not use that folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ------------------------------------------------------------------ editing

    /// <summary>Who was in the party of the selected session. The reward is split equally between them.</summary>
    void EditSessionParty()
    {
        if (_inputs == null || _current == null)
        {
            SetStatus("Pick a session first.");
            return;
        }

        var names = DebtPartyNames();
        var now = _inputs.Party ?? _inputs.Members;
        using var f = new Form
        {
            Text = "Session Party",
            ClientSize = new Size(360, 320),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = Font,
        };
        var label = new Label
        {
            Text = "Everyone who was in the party this session, also someone who carried nothing. The reward is split equally between them.",
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(6, 6, 6, 0),
        };
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        foreach (var n in names) list.Items.Add(n, now.Any(x => SameName(x, n)));
        var ok = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        bar.Controls.Add(ok);
        f.Controls.Add(list);
        f.Controls.Add(label);
        f.Controls.Add(bar);
        f.AcceptButton = ok;
        ApplyTheme(f);
        if (f.ShowDialog(this) != DialogResult.OK) return;

        _inputs.Party = list.CheckedItems.Cast<object>().Select(x => x.ToString() ?? "").Where(x => x.Length > 0).ToList();
        Storage.Save(_data);
        UpdateSnapshots();
        RefreshDebts();
    }

    static string PairKey(string a, string b, out bool aFirst)
    {
        aFirst = string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0;
        var x = aFirst ? a : b;
        var y = aFirst ? b : a;
        return x.ToLowerInvariant() + "|" + y.ToLowerInvariant();
    }

    /// <summary>Your typed correction for the pair: positive = other owes me.</summary>
    decimal GetAdj(string me, string other)
    {
        var key = PairKey(me, other, out var meFirst);
        _data.PairAdjust.TryGetValue(key, out var v);
        return meFirst ? v : -v;
    }

    void SetAdj(string me, string other, decimal balance)
    {
        var key = PairKey(me, other, out var meFirst);
        var v = meFirst ? balance : -balance;
        if (Math.Abs(v) < 0.5m) _data.PairAdjust.Remove(key);
        else _data.PairAdjust[key] = v;
    }

    static decimal ParseSigned(object? value)
    {
        var s = Convert.ToString(value, CultureInfo.CurrentCulture) ?? "";
        var negative = s.Contains('-') || s.Contains('−');
        return (negative ? -1m : 1m) * ParseNumber(s);
    }

    void BalanceEdited(object? sender, DataGridViewCellEventArgs e)
    {
        if (_debtUpdating || e.RowIndex < 0 || e.ColumnIndex != 1 || _debtSelected.Length == 0) return;
        var row = _balanceGrid.Rows[e.RowIndex];
        if (row.Tag is not decimal calc) return;
        var other = Convert.ToString(row.Cells[0].Value) ?? "";
        SetAdj(_debtSelected, other, ParseSigned(row.Cells[1].Value) - calc);
        Storage.Save(_data);
        BeginInvoke(new Action(RefreshDebts));
    }

    void TxChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_debtUpdating || e.RowIndex < 0 || e.ColumnIndex != 4) return;
        var row = _txGrid.Rows[e.RowIndex];
        if (row.Tag is not SavedTransfer t) return;
        t.Payment = row.Cells[4].Value is true;
        Storage.Save(_data);
        BeginInvoke(new Action(RefreshDebts));
    }
}
