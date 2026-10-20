using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HaulingTracker;

/// <summary>
/// A tiny read-only web server for the live view. It only answers GET for the page and the data, listens on this computer only
/// (localhost) and has no way to change anything. To let somebody else watch, a tunnel (Cloudflare) points a public link at it.
/// </summary>
sealed class LiveWebServer : IDisposable
{
    readonly TcpListener _listener;
    volatile string _json = "{}";
    volatile bool _stop;

    public int Port { get; }

    /// <summary>The newest picture of the tracker, as JSON.</summary>
    public string Json
    {
        set => _json = value;
    }

    public LiveWebServer(int port)
    {
        Port = port;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    public void Dispose()
    {
        _stop = true;
        try { _listener.Stop(); }
        catch (Exception) { }
    }

    async Task AcceptLoop()
    {
        while (!_stop)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync();
            }
            catch (Exception)
            {
                if (_stop) return;
                await Task.Delay(200);
                continue;
            }
            _ = Task.Run(() => Serve(client));
        }
    }

    async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var stream = client.GetStream();

                // read the request head (only the first line matters)
                var buffer = new byte[8192];
                var length = 0;
                while (length < buffer.Length)
                {
                    var n = await stream.ReadAsync(buffer.AsMemory(length), cts.Token);
                    if (n == 0) break;
                    length += n;
                    if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n")) break;
                }
                var head = Encoding.ASCII.GetString(buffer, 0, length);
                var parts = head.Split("\r\n")[0].Split(' ');
                var method = parts.Length > 0 ? parts[0] : "";
                var target = parts.Length > 1 ? parts[1] : "/";
                var path = target.Split('?')[0];

                var status = 200;
                var reason = "OK";
                var type = "text/html; charset=utf-8";
                var cache = "no-store";
                byte[] body;
                if (method != "GET" && method != "HEAD")
                {
                    status = 405;
                    reason = "Method Not Allowed";
                    type = "text/plain; charset=utf-8";
                    body = Encoding.UTF8.GetBytes("Read only.");
                }
                else if (path == "/" || path == "/index.html")
                {
                    body = Encoding.UTF8.GetBytes(Page);
                }
                else if (path == "/data.json")
                {
                    type = "application/json; charset=utf-8";
                    body = Encoding.UTF8.GetBytes(_json);
                }
                else if (path == "/favicon.ico" && Icon() is { } icon)
                {
                    type = "image/x-icon";
                    body = icon;
                    cache = "public, max-age=86400";
                }
                else
                {
                    status = 404;
                    reason = "Not Found";
                    type = "text/plain; charset=utf-8";
                    body = Encoding.UTF8.GetBytes("Not found.");
                }

                var header = "HTTP/1.1 " + status + " " + reason + "\r\n"
                    + "Content-Type: " + type + "\r\n"
                    + "Content-Length: " + body.Length + "\r\n"
                    + "Cache-Control: " + cache + "\r\n"
                    + "X-Content-Type-Options: nosniff\r\n"
                    + "Content-Security-Policy: default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; img-src 'self'\r\n"
                    + "Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cts.Token);
                if (method != "HEAD") await stream.WriteAsync(body, cts.Token);
                await stream.FlushAsync(cts.Token);
                client.Client.Shutdown(SocketShutdown.Send);
            }
            catch (Exception)
            {
                // a viewer that went away, or was too slow: nothing to do
            }
        }
    }

    static byte[]? _icon;
    static bool _iconRead;

    /// <summary>The program icon (built into the exe), shown as the icon of the page and of the bookmark. Null when it cannot be read.</summary>
    static byte[]? Icon()
    {
        if (_iconRead) return _icon;
        _iconRead = true;
        try
        {
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("icon.ico");
            if (stream == null) return null;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            _icon = memory.ToArray();
        }
        catch (Exception)
        {
            _icon = null;
        }
        return _icon;
    }

    const string Page = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Hauling Tracker (live)</title>
<link rel="icon" href="/favicon.ico" type="image/x-icon">
<link rel="shortcut icon" href="/favicon.ico">
<style>
  :root { --bg:#1e1e1e; --fg:#e6e6e6; --header:#1f3a68; --line:#3a3a3a; --cell:#2b2b2b; }
  * { box-sizing: border-box; }
  body { margin:0; padding:14px 18px 40px; background:var(--bg); color:var(--fg); font:14px "Segoe UI", system-ui, sans-serif; }
  h1 { font-size:20px; margin:0 0 2px; }
  h2 { font-size:16px; margin:22px 0 6px; }
  h3 { font-size:14px; margin:14px 0 4px; }
  .bar { display:flex; gap:14px; align-items:baseline; flex-wrap:wrap; margin-bottom:6px; opacity:.85; }
  .live { font-weight:600; }
  .ok { color:#5fd35f; } .bad { color:#ff6b6b; }
  .top { display:flex; gap:18px; flex-wrap:wrap; align-items:flex-start; }
  .top > div { min-width:300px; flex:1 1 360px; }
  .scroll { overflow-x:auto; }
  .bal table { table-layout:fixed; }
  .bal th:nth-child(1) { width:24%; }
  .bal th:nth-child(2) { width:18%; }
  table { border-collapse:collapse; width:100%; }
  th, td { border:1px solid var(--line); padding:4px 8px; white-space:nowrap; }
  th { background:var(--header); color:#fff; font-weight:600; text-align:center; white-space:normal; }
  .kv td:first-child { font-weight:600; width:48%; }
  .empty { opacity:.6; padding:6px 0; }
  .nav, .sub { display:flex; gap:8px; margin:8px 0 4px; flex-wrap:wrap; }
  .nav button, .sub button { background:var(--cell); color:var(--fg); border:1px solid var(--line); padding:6px 16px; font:inherit; font-weight:600; cursor:pointer; border-radius:4px; }
  .nav button.on, .sub button.on { background:var(--header); color:#fff; }
  .hidden { display:none; }
  #planSummary { font-weight:600; margin:8px 0 2px; }
  .planwrap { display:flex; gap:16px; flex-wrap:wrap; align-items:flex-start; margin-top:6px; }
  .mapbox { flex:0 0 520px; max-width:100%; background:var(--cell); border:1px solid var(--line); }
  .plantables { flex:1 1 520px; min-width:0; }
  .plantables h2:first-child { margin-top:0; }
</style>
</head>
<body>
<h1>Hauling Tracker</h1>
<div class="bar"><span id="session"></span><span id="live" class="live"></span><span id="updated"></span></div>
<div class="nav"><button id="tabS" class="on">Session</button><button id="tabP">Trip Planner</button><button id="tabD">Debts</button><button id="tabH">History</button></div>

<div id="viewS">
<div class="top">
  <div><h2>Stops</h2><div class="scroll" id="pickups"></div>
    <div id="finishedBox"><h2>Finished stops</h2><div class="scroll" id="finished"></div></div></div>
  <div>
    <h2>Party</h2><div class="scroll" id="party"></div>
    <h2>Contracts</h2><div class="scroll" id="rewards"></div>
    <h2>Session</h2><div class="scroll" id="summary"></div>
  </div>
</div>
<h2>Session leaderboard</h2><div class="scroll" id="leaders"></div>
<h2>Cargo runs</h2><div class="scroll" id="runs"></div>
</div>

<div id="viewP" class="hidden">
  <div id="planSummary"></div><div id="planNote" class="empty"></div>
  <div class="sub" id="planNav"></div>
  <div id="planBody"></div>
</div>

<div id="viewD" class="hidden"></div>
<div id="viewH" class="hidden"></div>

<script>
var last = null;
var planSel = 'Overview';

function table(el, g) {
  el.textContent = '';
  if (!g || !g.rows || g.rows.length === 0) {
    const d = document.createElement('div'); d.className = 'empty'; d.textContent = 'Nothing yet.'; el.appendChild(d); return;
  }
  const t = document.createElement('table');
  const hr = t.createTHead().insertRow();
  g.headers.forEach(function (h) { const th = document.createElement('th'); th.textContent = h.text; hr.appendChild(th); });
  const tb = t.createTBody();
  g.rows.forEach(function (r) {
    const tr = tb.insertRow();
    if (r.bold) tr.style.fontWeight = '700';
    r.cells.forEach(function (v, i) {
      const td = tr.insertCell();
      td.textContent = v;
      td.style.textAlign = g.headers[i].align;
      if (r.back[i]) td.style.background = r.back[i];
      if (r.fore[i]) td.style.color = r.fore[i];
    });
  });
  el.appendChild(t);
}

// Clocks that run (the session time and the run time of runs on the way) count up on the page every second between the updates.
let tickers = [];
let fetchedAt = Date.now();
function parseHms(s) { const m = /^(\d+):(\d\d):(\d\d)$/.exec(s || ''); return m ? (+m[1]) * 3600 + (+m[2]) * 60 + (+m[3]) : null; }
function fmtHms(n) { const h = Math.floor(n / 3600), m = Math.floor(n % 3600 / 60), s = n % 60; return h + ':' + String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0'); }
function addTicker(td) { const b = parseHms(td.textContent); if (b !== null) tickers.push({ td: td, base: b }); }
setInterval(function () {
  const dt = Math.floor((Date.now() - fetchedAt) / 1000);
  tickers.forEach(function (t) { t.td.textContent = fmtHms(t.base + dt); });
}, 1000);

function summary(el, items) {
  el.textContent = '';
  const t = document.createElement('table'); t.className = 'kv';
  const tb = t.createTBody();
  items.forEach(function (it) {
    const tr = tb.insertRow();
    tr.insertCell().textContent = it.k;
    const td = tr.insertCell(); td.textContent = it.v; td.style.textAlign = 'right';
    if (it.live) addTicker(td);
  });
  el.appendChild(t);
}

function section(parent, tag, title, g) {
  const h = document.createElement(tag); h.textContent = title; parent.appendChild(h);
  const box = document.createElement('div'); box.className = 'scroll'; parent.appendChild(box);
  table(box, g);
}

// green when somebody owes the member (+), red when he owes them (-), like in the program
function signColors(box, col, d, rows) {
  // the sign is read from the second column (balance) of every row, the colour goes to column col
  (rows || box.querySelectorAll('tbody tr')).forEach(function (tr) {
    const td = tr.cells[col];
    const sign = (rows ? tr.cells[1] : td);
    if (!td || !sign) return;
    if (sign.textContent.charAt(0) === '+') td.style.background = d.pos;
    else if (sign.textContent.charAt(0) === '-') td.style.background = d.neg;
  });
}

function mapSvg(el, stations, routes, bg) {
  el.textContent = '';
  const NS = 'http://www.w3.org/2000/svg';
  const W = 520, H = 560, L = 40, R = 150, B = 24;
  const legend = routes.filter(function (r) { return r.label; });
  const cols = Math.max(1, Math.floor(W / 170));
  const T = 14 + Math.ceil(legend.length / cols) * 16;
  let minX = 1e9, maxX = -1e9, minY = 1e9, maxY = -1e9;
  stations.forEach(function (s) { minX = Math.min(minX, s.x); maxX = Math.max(maxX, s.x); minY = Math.min(minY, s.y); maxY = Math.max(maxY, s.y); });
  const w = W - L - R, h = H - T - B;
  const sc = Math.min(w / (maxX - minX), h / (maxY - minY));
  const ox = L + (w - (maxX - minX) * sc) / 2, oy = T + (h - (maxY - minY) * sc) / 2;
  const pos = {};
  stations.forEach(function (s) { pos[s.id] = [ox + (s.x - minX) * sc, oy + (s.y - minY) * sc]; });
  const svg = document.createElementNS(NS, 'svg');
  svg.setAttribute('viewBox', '0 0 ' + W + ' ' + H);
  svg.setAttribute('width', '100%');
  function add(tag, attrs, text) {
    const e = document.createElementNS(NS, tag);
    for (const k in attrs) e.setAttribute(k, attrs[k]);
    if (text !== undefined) e.textContent = text;
    svg.appendChild(e);
    return e;
  }
  add('rect', { x: 0, y: 0, width: W, height: H, fill: '#08080c' });
  const sx = ox + (bg.sun[0] - minX) * sc, sy = oy + (bg.sun[1] - minY) * sc;
  bg.orbits.forEach(function (r) { add('circle', { cx: sx, cy: sy, r: r * sc, fill: 'none', stroke: '#ebebf5', 'stroke-opacity': 0.37, 'stroke-width': 1 }); });
  bg.planets.forEach(function (q) { add('circle', { cx: ox + (q[0] - minX) * sc, cy: oy + (q[1] - minY) * sc, r: Math.max(3.5, 10 * sc), fill: '#8c5514' }); });
  const sr = Math.max(7, bg.sunRadius * sc);
  add('circle', { cx: sx, cy: sy, r: sr * 1.9, fill: '#ff461e', 'fill-opacity': 0.22 });
  add('circle', { cx: sx, cy: sy, r: sr, fill: '#ff3a18' });
  const n = routes.length;
  [false, true].forEach(function (boldPass) {
    routes.forEach(function (r, i) {
      if (!!r.bold !== boldPass) return;
      const ids = [r.from || 'Ruin'].concat(r.stops, ['Ruin']);
      const shift = (i - (n - 1) / 2) * 3.5;
      for (let s = 0; s + 1 < ids.length; s++) {
        if (ids[s] === ids[s + 1]) continue;
        let a = pos[ids[s]], b = pos[ids[s + 1]];
        if (!a || !b) continue;
        const dx = b[0] - a[0], dy = b[1] - a[1], len = Math.sqrt(dx * dx + dy * dy);
        if (len < 1) continue;
        const ux = dx / len, uy = dy / len, nx = -uy, ny = ux;
        a = [a[0] + nx * shift, a[1] + ny * shift];
        b = [b[0] + nx * shift, b[1] + ny * shift];
        if (r.dashed) add('line', { x1: a[0], y1: a[1], x2: b[0], y2: b[1], stroke: r.color, 'stroke-width': r.bold ? 3.5 : 2.5, 'stroke-opacity': 0.92, 'stroke-dasharray': '1 5', 'stroke-linecap': 'round' });
        else add('line', { x1: a[0], y1: a[1], x2: b[0], y2: b[1], stroke: r.color, 'stroke-width': r.bold ? 3.5 : 2, 'stroke-opacity': r.bold ? 1 : 0.67 });
        const cx = a[0] + (b[0] - a[0]) * 0.62, cy = a[1] + (b[1] - a[1]) * 0.62, z = r.bold ? 8 : 6;
        const pts = [
          [cx + ux * z, cy + uy * z],
          [cx - ux * z * 0.7 + nx * z * 0.6, cy - uy * z * 0.7 + ny * z * 0.6],
          [cx - ux * z * 0.7 - nx * z * 0.6, cy - uy * z * 0.7 - ny * z * 0.6]
        ].map(function (p) { return p[0] + ',' + p[1]; }).join(' ');
        add('polygon', { points: pts, fill: r.color });
      }
    });
  });
  const used = {};
  routes.forEach(function (r) { r.stops.forEach(function (id) { used[id] = true; }); });
  stations.forEach(function (s) {
    const p = pos[s.id];
    if (s.id === 'Ruin') {
      add('rect', { x: p[0] - 6, y: p[1] - 6, width: 12, height: 12, style: 'fill:#ecece8' });
      add('text', { x: p[0] + 9, y: p[1] + 4, style: 'fill:#ecece8;font-size:12px;font-weight:700' }, s.name);
      return;
    }
    const on = !!used[s.id];
    const st = on ? 'fill:#ecece8' : 'fill:#ecece8;opacity:.55';
    add('circle', { cx: p[0], cy: p[1], r: on ? 5 : 3.5, style: st });
    if (s.id === '73041') add('text', { x: p[0] - 8, y: p[1] + 4, 'text-anchor': 'end', style: st + ';font-size:11px' }, s.name);
    else add('text', { x: p[0] + 8, y: p[1] + 4, style: st + ';font-size:11px' }, s.name);
  });
  legend.forEach(function (r, i) {
    const x = 8 + (i % cols) * 170, y = 6 + Math.floor(i / cols) * 16;
    add('line', r.dashed ? { x1: x, y1: y + 8, x2: x + 16, y2: y + 8, stroke: r.color, 'stroke-width': 3, 'stroke-dasharray': '1 5', 'stroke-linecap': 'round' } : { x1: x, y1: y + 8, x2: x + 16, y2: y + 8, stroke: r.color, 'stroke-width': 3 });
    add('text', { x: x + 20, y: y + 12, style: 'fill:#ecece8;font-size:11px' }, r.label);
  });
  el.appendChild(svg);
}

function renderPlanner() {
  const p = last.planner;
  document.getElementById('planSummary').textContent = p.summary || '';
  document.getElementById('planNote').textContent = p.note || '';
  const names = ['Overview'].concat(p.workers.map(function (w) { return w.name; }));
  if (names.indexOf(planSel) < 0) planSel = 'Overview';
  const nav = document.getElementById('planNav');
  nav.textContent = '';
  names.forEach(function (n) {
    const b = document.createElement('button');
    b.textContent = n;
    if (n === planSel) b.className = 'on';
    b.onclick = function () { planSel = n; renderPlanner(); };
    nav.appendChild(b);
  });
  const body = document.getElementById('planBody');
  body.textContent = '';
  const wrap = document.createElement('div'); wrap.className = 'planwrap';
  const mapBox = document.createElement('div'); mapBox.className = 'mapbox';
  const tables = document.createElement('div'); tables.className = 'plantables';
  wrap.appendChild(mapBox); wrap.appendChild(tables); body.appendChild(wrap);
  let shown = p.workers;
  const routes = [];
  if (planSel === 'Overview') {
    p.workers.forEach(function (w) {
      w.routes.forEach(function (r, i) { routes.push({ label: i === 0 ? w.name : '', color: w.color, stops: r.stops, from: r.from, bold: !!r.solid, dashed: !r.solid }); });
    });
  } else {
    shown = p.workers.filter(function (w) { return w.name === planSel; });
    shown.forEach(function (w) {
      w.routes.forEach(function (r, i) { routes.push({ label: r.label, color: w.color, stops: r.stops, from: r.from, bold: !!r.solid, dashed: !r.solid }); });
    });
  }
  mapSvg(mapBox, p.stations, routes, p.backdrop);
  shown.forEach(function (w) { section(tables, 'h2', w.title, planSel === 'Overview' ? w.grid : w.personal); });
  // a copy of Cargo Runs under the planner
  section(body, 'h2', 'Cargo Runs', last.runs);
  if (last.runsLive) {
    const rows = body.lastChild.querySelectorAll('tbody tr');
    last.runsLive.forEach(function (live, i) { if (live && rows[i] && rows[i].cells[7]) addTicker(rows[i].cells[7]); });
  }
}

function renderDebts() {
  const el = document.getElementById('viewD');
  el.textContent = '';
  const d = last.debts;
  section(el, 'h2', 'Earnings', d.earnings);
  signColors(el.lastChild, 3, d);
  const h = document.createElement('h2'); h.textContent = 'Balances'; el.appendChild(h);
  d.balances.forEach(function (b) {
    section(el, 'h3', b.name, b.grid);
    el.lastChild.classList.add('bal');   // the same column widths in every table
    signColors(el.lastChild, 1, d);
    signColors(el.lastChild, 2, d, el.lastChild.querySelectorAll('tbody tr'));   // the meaning has the colour of its balance
  });
  section(el, 'h2', 'Money transfers', d.transfers);
}

function renderHistory() {
  const el = document.getElementById('viewH');
  el.textContent = '';
  const d = last.history;
  section(el, 'h2', 'All-time leaderboard', d.allTime);
  const best = document.createElement('div'); best.className = 'empty'; best.textContent = d.best || ''; el.appendChild(best);
  section(el, 'h2', 'Saved sessions', d.saved);
}

function show(which) {
  ['S', 'P', 'D', 'H'].forEach(function (k) {
    document.getElementById('view' + k).classList.toggle('hidden', k !== which);
    document.getElementById('tab' + k).classList.toggle('on', k === which);
  });
  try { history.replaceState(null, '', '#' + { S: 'session', P: 'plan', D: 'debts', H: 'history' }[which]); } catch (e) { }
}
['S', 'P', 'D', 'H'].forEach(function (k) { document.getElementById('tab' + k).onclick = function () { show(k); }; });
show({ '#plan': 'P', '#debts': 'D', '#history': 'H' }[location.hash] || 'S');

async function refresh() {
  const live = document.getElementById('live');
  try {
    const res = await fetch('/data.json', { cache: 'no-store' });
    if (!res.ok) throw new Error('HTTP ' + res.status);
    const d = await res.json();
    last = d;
    tickers = [];
    fetchedAt = Date.now();
    const root = document.documentElement.style;
    if (d.theme) { root.setProperty('--bg', d.theme.bg); root.setProperty('--fg', d.theme.fg); root.setProperty('--header', d.theme.header); root.setProperty('--line', d.theme.line); root.setProperty('--cell', d.theme.cell); }
    document.getElementById('session').textContent = d.session || '';
    document.getElementById('updated').textContent = 'updated ' + d.updated;
    live.textContent = '● live'; live.className = 'live ok';
    table(document.getElementById('pickups'), d.pickups);
    document.getElementById('finishedBox').classList.toggle('hidden', !d.finished || !d.finished.rows || d.finished.rows.length === 0);
    table(document.getElementById('finished'), d.finished);
    table(document.getElementById('rewards'), d.rewards);
    summary(document.getElementById('summary'), d.summary || []);
    table(document.getElementById('party'), d.party);
    table(document.getElementById('leaders'), d.leaders);
    table(document.getElementById('runs'), d.runs);
    if (d.runsLive) {
      const runRows = document.querySelectorAll('#runs tbody tr');
      d.runsLive.forEach(function (live, i) { if (live && runRows[i] && runRows[i].cells[7]) addTicker(runRows[i].cells[7]); });
    }
    if (d.planner) renderPlanner();
    if (d.debts) renderDebts();
    if (d.history) renderHistory();
  } catch (e) {
    live.textContent = '● connection lost, trying again'; live.className = 'live bad';
  }
}
refresh();
setInterval(refresh, 3000);
</script>
</body>
</html>
""";
}

public sealed partial class MainForm
{
    LiveWebServer? _web;
    readonly System.Windows.Forms.Timer _webTimer = new();
    readonly ToolStripMenuItem _webBtn = new();
    int _webTick;
    string _webLastDuration = "";
    bool _webClockRunning;

    string WebButtonText() => _web != null ? "Live Web View: On (port " + _web.Port + ")" : "Live Web View: Off";

    void InitWebView()
    {
        _webTimer.Interval = 2000;
        _webTimer.Tick += (_, _) => PublishWeb();
        FormClosing += (_, _) => StopWeb();
        if (_data.WebView) StartWeb(false);
    }

    void ToggleWeb()
    {
        if (_web != null)
        {
            StopWeb();
            _data.WebView = false;
            Storage.Save(_data);
            SetStatus("The live web view is off.");
            return;
        }
        if (StartWeb(true))
        {
            _data.WebView = true;
            Storage.Save(_data);
        }
    }

    bool StartWeb(bool announce)
    {
        var port = _data.WebViewPort is >= 1024 and <= 65535 ? _data.WebViewPort : 8765;
        try
        {
            var web = new LiveWebServer(port);
            web.Start();
            _web = web;
        }
        catch (Exception ex)
        {
            _web = null;
            _webBtn.Text = WebButtonText();
            SetStatus("Could not start the live web view on port " + port + ": " + ex.Message);
            return false;
        }
        _webBtn.Text = WebButtonText();
        _webTick = 0;
        _webTimer.Start();
        PublishWeb();
        SetStatus("Live web view on http://localhost:" + port + " (read only).");
        return true;
    }

    void StopWeb()
    {
        _webTimer.Stop();
        _web?.Dispose();
        _web = null;
        _webBtn.Text = WebButtonText();
    }

    void PublishWeb()
    {
        if (_web == null || IsDisposed) return;
        try
        {
            // the Debts and History tables are filled when you open those tabs: keep them current, without touching them while you look at them
            if (_webTick++ % 5 == 0 && !_balanceGrid.Visible && !_balanceGrid.IsCurrentCellInEditMode) RefreshDebts();
            // the session clock is running when its text changed since the last update
            var duration = _durationVal.Text;
            _webClockRunning = duration.Length > 0 && duration != _webLastDuration;
            _webLastDuration = duration;
            _web.Json = BuildWebSnapshot();
        }
        catch (Exception ex)
        {
            SetStatus("Live web view: " + ex.Message);
        }
    }

    /// <summary>The tabs as data: the same tables, with the colours they have on the screen.</summary>
    string BuildWebSnapshot()
    {
        var snapshot = new
        {
            updated = DateTime.Now.ToString("HH:mm:ss"),
            session = _sessionBox.Text,
            theme = new
            {
                bg = WebColor(Theme.Window),
                fg = WebColor(Theme.Text),
                header = WebColor(Theme.Header),
                line = WebColor(Theme.Lines),
                cell = WebColor(Theme.Cell),
            },
            summary = new[]
            {
                new { k = "Start Time", v = _startBox.Text, live = false },
                new { k = "Session Time", v = _durationVal.Text, live = _webClockRunning },   // the page counts it up between updates
                new { k = "Batches Delivered", v = _missionsVal.Text, live = false },
                new { k = "Active Missions", v = _missionsActiveVal.Text, live = false },
                new { k = "Missions Completed", v = _missionsDoneVal.Text, live = false },
                new { k = "Total aUEC Earned", v = _revenueVal.Text, live = false },
                new { k = "aUEC/Hour", v = _hourlyVal.Text, live = false },
            },
            rewards = WebGrid(_rewards),
            party = WebSimple(new[] { ("Member", "left"), ("Active", "center"), ("Batches", "center"), ("QT Drive", "left") },
                _data.Party.Select(n => new[]
                {
                    n,
                    IsActiveAt(n, DateTime.UtcNow) ? "Yes" : "No",
                    Math.Max(1, ShipCapacityOf(n)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    DriveOf(n)?.Label ?? "",
                })),
            pickups = WebGrid(_logGrid),
            finished = WebGrid(_doneGrid),
            leaders = WebGrid(_statsGrid),
            runs = WebGrid(_tripGrid),
            runsLive = _tripGrid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow)
                .Select(r => r.Tag is WorkerTrip t && !t.End.HasValue && (Convert.ToString(r.Cells[7].Value) ?? "").Length > 0).ToArray(),   // runs on the way: the page counts their Run Time up
            planner = WebPlan(GetPlan()),
            debts = WebDebts(),
            history = new { allTime = WebGrid(_allTimeGrid), best = _bestLine.Text, saved = WebGrid(_historyGrid) },
        };
        return JsonSerializer.Serialize(snapshot);
    }

    /// <summary>The Debts tab for everybody: earnings, the balance of every member with the others, and all money transfers.</summary>
    object WebDebts()
    {
        var party = DebtPartyNames();
        var nets = DebtCalc.Nets(_data.Sessions.Values, _data.Transfers);
        var settle = DebtCalc.Settle(nets);
        var headers = new[] { ("Party Member", "left"), ("Balance", "right"), ("Meaning", "left") };
        var balances = party.Select(me => new
        {
            name = me,
            grid = WebSimple(headers, party.Where(o => !SameName(o, me)).Select(o =>
            {
                var shown = DebtCalc.Between(settle, me, o) + GetAdj(me, o);
                return new[] { o, Signed(shown), Meaning(me, o, shown) };
            })),
        }).ToList();
        var transfers = WebSimple(
            new[] { ("Time", "center"), ("From", "center"), ("To", "center"), ("Amount", "right"), ("Payment", "center") },
            _data.Transfers.OrderByDescending(t => t.Time).Select(t => new[]
            {
                t.Time.ToLocalTime().ToString("d MMM HH:mm", System.Globalization.CultureInfo.CurrentCulture),
                t.From,
                t.To,
                t.Amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                t.Payment ? "yes" : "",
            }));
        return new { earnings = WebGrid(_earnGrid), balances, transfers, pos = WebColor(DoneGreen), neg = WebColor(LostRed) };
    }

    /// <summary>A plain table made from text: no special colours.</summary>
    static object WebSimple((string Text, string Align)[] headers, IEnumerable<string[]> rows)
    {
        var cell = WebColor(Theme.Cell);
        var text = WebColor(Theme.Text);
        return new
        {
            headers = headers.Select(h => new { text = h.Text, align = h.Align }).ToList(),
            rows = rows.Select(r => new
            {
                cells = r,
                back = r.Select(_ => cell).ToArray(),
                fore = r.Select(_ => text).ToArray(),
                bold = false,
            }).ToList(),
        };
    }

    /// <summary>The Trip Planner tab as data: the stations for the map and, for every crew member, his table and his planned routes.</summary>
    static object WebPlan(PlanView plan)
    {
        var headers = new[]
        {
            new { text = "Trip", align = "center" },
            new { text = "Route (Ruin, then these, then back to Ruin)", align = "left" },
            new { text = "Batches", align = "center" },
            new { text = "Distance", align = "right" },
            new { text = "Time", align = "right" },
            new { text = "Gm / Batch", align = "right" },
        };
        var cell = WebColor(Theme.Cell);
        var text = WebColor(Theme.Text);

        // the same table twice: on the Overview the whole Current row has the worker's colour, on his own page the Trip cell has the colour of its line on the map
        object Grid(PlanWorkerView w, bool personal)
        {
            var planned = w.Planned;
            return new
            {
                headers,
                rows = w.Rows.Where(r => !r.Hidden).Select(r =>
                {
                    var back = RowColor(r) is { } c ? WebColor(c) : cell;
                    var backs = new[] { back, back, back, back, back, back };
                    var fores = new[] { text, text, text, text, text, text };
                    if (r.First)
                    {
                        for (var i = 0; i < backs.Length; i++)
                        {
                            backs[i] = WebColor(w.Color);
                            fores[i] = WebColor(TextOn(w.Color));
                        }
                    }
                    else if (personal && r.IsPlan)
                    {
                        var tint = r.Solid ? w.Color : Faded(w.Color);   // the later trips are faded, like their dotted line on the map
                        backs[0] = WebColor(tint);
                        fores[0] = WebColor(TextOn(tint));
                    }
                    return new
                    {
                        cells = new[]
                        {
                            RowLabel(r),
                            r.Route,
                            r.Batches,
                            r.Gm > 0 ? Gm(r.Gm) : "",
                            TimeText(r),
                            r.Gm > 0 && r.BatchCount > 0 ? (r.Gm / r.BatchCount).ToString("N1", System.Globalization.CultureInfo.InvariantCulture) : "",
                        },
                        back = backs,
                        fore = fores,
                        bold = r.First,
                    };
                }).ToList(),
            };
        }

        var workers = plan.Workers.Select(w => new
        {
            name = w.Name,
            color = WebColor(w.Color),
            title = PlanTitle(w),
            grid = Grid(w, false),
            personal = Grid(w, true),
            routes = w.Planned.Select(r => new { label = r.Label + "  " + Gm(r.Gm), stops = r.Stops, from = r.From, solid = r.Solid }).ToList(),
        }).ToList();
        return new
        {
            summary = plan.Summary,
            note = plan.Note,
            palette = MapPalette.Select(WebColor).ToArray(),
            stations = PyroDistances.Points.Select(p => new { id = p.Id, name = p.Name, x = p.X, y = p.Y }).ToList(),
            backdrop = new
            {
                sun = new[] { PyroDistances.Sun.X, PyroDistances.Sun.Y },
                sunRadius = PyroDistances.SunRadius,
                orbits = PyroDistances.OrbitRadii,
                planets = PyroDistances.Planets.Select(q => new[] { q.X, q.Y }).ToList(),
            },
            workers,
        };
    }

    static string WebColor(Color c) => "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2");

    static string WebAlign(DataGridViewContentAlignment a)
    {
        var name = a.ToString();
        return name.Contains("Right") ? "right" : name.Contains("Center") ? "center" : "left";
    }

    static Color WebBack(DataGridViewCell c, DataGridView g)
    {
        foreach (var style in new[] { c.Style, c.OwningRow.DefaultCellStyle, c.OwningColumn.DefaultCellStyle, g.DefaultCellStyle })
        {
            if (!style.BackColor.IsEmpty) return style.BackColor;
        }
        return Theme.Cell;
    }

    static Color WebFore(DataGridViewCell c, DataGridView g)
    {
        foreach (var style in new[] { c.Style, c.OwningRow.DefaultCellStyle, c.OwningColumn.DefaultCellStyle, g.DefaultCellStyle })
        {
            if (!style.ForeColor.IsEmpty) return style.ForeColor;
        }
        return Theme.Text;
    }

    static object WebGrid(DataGridView g)
    {
        var columns = g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();   // as on screen: the # column is first
        var headers = columns.Select(c => new { text = c.HeaderText, align = WebAlign(c.DefaultCellStyle.Alignment) }).ToList();
        var rows = new List<object>();
        foreach (DataGridViewRow r in g.Rows)
        {
            if (r.IsNewRow) continue;
            var cells = columns.Select(c => r.Cells[c.Index]).ToList();
            rows.Add(new
            {
                cells = cells.Select(c => Convert.ToString(c.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "").ToArray(),
                back = cells.Select(c => WebColor(WebBack(c, g))).ToArray(),
                fore = cells.Select(c => WebColor(WebFore(c, g))).ToArray(),
                bold = (r.DefaultCellStyle.Font ?? g.DefaultCellStyle.Font)?.Bold == true,
            });
        }
        return new { headers, rows };
    }
}
