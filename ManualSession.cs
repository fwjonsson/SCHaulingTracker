using System.Globalization;

namespace HaulingTracker;

// Start Session / Stop Session: a session you choose yourself. It reads the logs from the moment you press Start until you press Stop,
// also when that is days later, and the start is remembered when you close the program.
public sealed partial class MainForm
{
    readonly Button _sessionToggleBtn = new();
    string? _forceKey;   // the session to show after the next refresh (the one you just started)

    ManualSession? OpenManualSession => _data.ManualSessions.LastOrDefault(s => !s.StopUtc.HasValue);

    void InitSessionButtons()
    {
        StyleButton(_sessionToggleBtn, "Start Session");
        _sessionToggleBtn.AutoSize = true;
        _sessionToggleBtn.Click += (_, _) => ToggleSession();
        UpdateSessionButtons();
    }

    /// <summary>One button: it starts a session, and once one is running it stops it.</summary>
    void UpdateSessionButtons()
    {
        _sessionToggleBtn.Text = OpenManualSession != null ? "Stop Session" : "Start Session";
    }

    void ToggleSession()
    {
        var open = OpenManualSession;
        if (open == null)
        {
            var answer = MessageBox.Show(this,
                "Start a new session now?\n\nOnly what happens from this moment is read, until you press Stop Session.",
                "Start Session", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            var now = DateTime.UtcNow;
            _data.ManualSessions.Add(new ManualSession { StartUtc = now });
            Storage.Save(_data);
            _forceKey = "manual-" + now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            SetStatus("Session started. Only what happens from now on is read, until you press Stop Session.");
        }
        else
        {
            var since = open.StartUtc.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
            var answer = MessageBox.Show(this,
                "Stop the session that started " + since + "?\n\nThe logs are not read for this session after this moment.",
                "Stop Session", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            open.StopUtc = DateTime.UtcNow;
            Storage.Save(_data);
            SetStatus("Session stopped.");
        }

        UpdateSessionButtons();
        _renderedVersion = -1;
        RefreshFromLog();
    }
}
