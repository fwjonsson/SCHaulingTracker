using System.Globalization;

namespace HaulingTracker;

// The Trip Planner tab: divides the open batches over the whole crew, each with a ship that holds a few batches, so that
// everybody flies as little as possible and the crew finishes at about the same time.
public sealed partial class MainForm
{
    const int DefaultShipCapacity = 3;
    const int MaxShipCapacity = 24;

    readonly DataGridView _crewGrid = new();
    readonly Button _planBtn = new();
    readonly Label _planSummary = new();
    readonly Label _planNote = new();
    readonly DataGridView _planGrid = new();
    bool _planFilling;   // the crew table is being filled by the program, not by you

    Control BuildPlannerPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));    // title
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));    // hint
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));   // crew
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));    // button
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));    // summary
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));    // note
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // the plan

        t.Controls.Add(Caption("Trip Planner", "Divides the open batches over the crew, so everybody flies as little as possible and you finish together"), 0, 0);

        var hint = new Label
        {
            Text = "The active party members (Options → Party) are the crew. Type how many batches each ship holds, 0 if somebody is not flying.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
            Tag = "muted",
        };
        t.Controls.Add(hint, 0, 1);

        StyleGrid(_crewGrid);
        _crewGrid.Dock = DockStyle.Fill;
        _crewGrid.AllowUserToAddRows = false;
        _crewGrid.AllowUserToDeleteRows = false;
        _crewGrid.Columns.Add(Col("Crew", 260, true, DataGridViewContentAlignment.MiddleLeft));
        _crewGrid.Columns.Add(Col("Ship holds (batches)", 170, false, DataGridViewContentAlignment.MiddleCenter, true));
        _crewGrid.CellEndEdit += CrewGrid_CellEndEdit;
        t.Controls.Add(_crewGrid, 0, 2);

        StyleButton(_planBtn, "Plan Trips");
        _planBtn.Margin = new Padding(0, 5, 0, 5);
        _planBtn.Click += (_, _) => RefreshPlanner();
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        bar.Controls.Add(_planBtn);
        t.Controls.Add(bar, 0, 3);

        _planSummary.Dock = DockStyle.Fill;
        _planSummary.TextAlign = ContentAlignment.MiddleLeft;
        _planSummary.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        t.Controls.Add(_planSummary, 0, 4);
        _planNote.Dock = DockStyle.Fill;
        _planNote.TextAlign = ContentAlignment.MiddleLeft;
        _planNote.ForeColor = Color.DimGray;
        _planNote.Tag = "muted";
        t.Controls.Add(_planNote, 0, 5);

        StyleGrid(_planGrid);
        _planGrid.Dock = DockStyle.Fill;
        _planGrid.AllowUserToAddRows = false;
        _planGrid.AllowUserToDeleteRows = false;
        _planGrid.Columns.Add(Col("Worker", 120, true));
        _planGrid.Columns.Add(Col("Trip", 55, true));
        _planGrid.Columns.Add(Col("Route (Ruin, then these, then back to Ruin)", 480, true, DataGridViewContentAlignment.MiddleLeft, true));
        _planGrid.Columns.Add(Col("Batches", 90, true));
        _planGrid.Columns.Add(Col("Distance", 110, true, DataGridViewContentAlignment.MiddleRight));
        _planGrid.Columns.Add(Col("Gm / Batch", 100, true, DataGridViewContentAlignment.MiddleRight));
        t.Controls.Add(_planGrid, 0, 6);
        return t;
    }

    int ShipCapacityOf(string name) =>
        _data.ShipCapacity.TryGetValue(name, out var c) ? Math.Clamp(c, 0, MaxShipCapacity) : DefaultShipCapacity;

    List<string> ActiveCrew() => _data.Party.Where(n => !_data.InactiveParty.Any(x => SameName(x, n))).ToList();

    void FillCrew()
    {
        var crew = ActiveCrew();
        var same = _crewGrid.Rows.Count == crew.Count;
        for (var i = 0; same && i < crew.Count; i++)
        {
            same = (string?)_crewGrid.Rows[i].Cells[0].Value == crew[i]
                && Convert.ToString(_crewGrid.Rows[i].Cells[1].Value) == ShipCapacityOf(crew[i]).ToString(CultureInfo.InvariantCulture);
        }
        if (same || _crewGrid.IsCurrentCellInEditMode) return;   // nothing changed, or you are typing

        _planFilling = true;
        try
        {
            _crewGrid.Rows.Clear();
            foreach (var name in crew) _crewGrid.Rows.Add(name, ShipCapacityOf(name));
        }
        finally
        {
            _planFilling = false;
        }
    }

    void CrewGrid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_planFilling || e.RowIndex < 0 || e.ColumnIndex != 1) return;
        var row = _crewGrid.Rows[e.RowIndex];
        var name = Convert.ToString(row.Cells[0].Value) ?? "";
        if (name.Length == 0) return;

        var text = (Convert.ToString(row.Cells[1].Value) ?? "").Trim();
        var capacity = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var typed) ? Math.Clamp(typed, 0, MaxShipCapacity) : ShipCapacityOf(name);
        _data.ShipCapacity[name] = capacity;
        Storage.Save(_data);
        row.Cells[1].Value = capacity;
        RefreshPlanner();
    }

    /// <summary>Plans the trips for the open batches of the selected session. It only runs while the Trip Planner tab is on screen.</summary>
    void RefreshPlanner()
    {
        FillCrew();
        _planGrid.Rows.Clear();
        _planSummary.Text = "";
        _planNote.Text = "";

        var crew = ActiveCrew().Select(n => (Name: n, Capacity: ShipCapacityOf(n))).ToList();
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

        var flying = crew.Where(c => c.Capacity > 0).ToList();
        var batches = remaining.Values.Sum();
        var trips = TripPlanner.Plan(remaining, crew);
        if (flying.Count == 0)
        {
            _planSummary.Text = "Nobody is flying. Type how many batches a ship holds for at least one crew member.";
        }
        else if (trips.Count == 0)
        {
            _planSummary.Text = "No open batches to pick up.";
        }
        else
        {
            var totals = flying.Select(c => (c.Name, Gm: trips.Where(t => t.Worker == c.Name).Sum(t => t.Distance))).ToList();
            var longest = totals.OrderByDescending(x => x.Gm).First();
            _planSummary.Text = trips.Count + " trips, " + Gm(totals.Sum(x => x.Gm)) + " in total for " + batches + " batches. Longest: "
                + longest.Name + " with " + Gm(longest.Gm) + ".";
        }

        var note = "Planning every open batch, whoever is assigned to the location now.";
        if (unknown.Count > 0) note += " Not in the distance table, so left out: " + string.Join(", ", unknown) + ".";
        _planNote.Text = note;

        foreach (var c in flying)
        {
            var mine = trips.Where(t => t.Worker == c.Name).ToList();
            var number = 1;
            foreach (var trip in mine)
            {
                var route = string.Join("  →  ", trip.Stops.Select(s => PyroDistances.NameOf(s.Id) + " ×" + s.Batches));
                var idx = _planGrid.Rows.Add(
                    c.Name,
                    number++,
                    route,
                    trip.Batches + "/" + c.Capacity,
                    Gm(trip.Distance),
                    (trip.Distance / trip.Batches).ToString("N1", CultureInfo.InvariantCulture));
                _planGrid.Rows[idx].Cells[3].Style.BackColor = trip.Batches >= c.Capacity ? DoneGreen : PartialYellow;   // green = full ship
            }

            var sumBatches = mine.Sum(t => t.Batches);
            var sumGm = mine.Sum(t => t.Distance);
            var sub = _planGrid.Rows.Add(
                c.Name,
                "",
                mine.Count == 0 ? "No trips" : "Total: " + mine.Count + " trips",
                sumBatches == 0 ? "" : sumBatches.ToString(CultureInfo.InvariantCulture),
                sumGm == 0 ? "" : Gm(sumGm),
                sumBatches == 0 ? "" : (sumGm / sumBatches).ToString("N1", CultureInfo.InvariantCulture));
            var subRow = _planGrid.Rows[sub];
            subRow.DefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            subRow.DefaultCellStyle.BackColor = CalcBlue;
        }
    }

    static string Gm(double v) => v.ToString("N1", CultureInfo.InvariantCulture) + " Gm";
}
