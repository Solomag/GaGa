// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

using GaGa.Controls;
using GaGa.NotifyIconPlayer;


namespace GaGa
{
    /// <summary>
    /// Sleep timer, track history and recent stations.
    /// </summary>
    internal partial class GaGa
    {
        private const Int32 HistoryLimit = 100;
        private const Int32 HistoryShown = 30;
        private const Int32 RecentLimit = 15;

        private ToolStripMenuItem recentMenuItem;
        private ToolStripMenuItem historyMenuItem;
        private ToolStripMenuItem sleepMenuItem;

        private System.Windows.Forms.Timer sleepTimer;
        private DateTime sleepAt;

        // newest first:
        private readonly List<HistoryEntry> history = new List<HistoryEntry>();
        private readonly List<PlayerStream> recent = new List<PlayerStream>();

        private class HistoryEntry
        {
            public DateTime Time;
            public String Station;
            public String Title;
        }

        /// <summary>
        /// Create the menus and load history and recent stations from the settings.
        /// </summary>
        private void ExtrasInit()
        {
            recentMenuItem = new ToolStripMenuItem("Recent stations");
            historyMenuItem = new ToolStripMenuItem("Track history");
            sleepMenuItem = new ToolStripMenuItem("Sleep timer");

            // a placeholder, so that the arrow shows; the real items come on opening:
            recentMenuItem.DropDownItems.Add("(none)");
            historyMenuItem.DropDownItems.Add("(none)");
            sleepMenuItem.DropDownItems.Add("(none)");

            sleepTimer = new System.Windows.Forms.Timer(container);
            sleepTimer.Interval = 1000;
            sleepTimer.Tick += OnSleepTimerTick;

            if (settings.RecentStations != null)
            {
                foreach (String line in settings.RecentStations)
                {
                    String[] parts = (line ?? "").Split('\t');
                    Uri uri;
                    if (parts.Length == 2 && Uri.TryCreate(parts[1], UriKind.Absolute, out uri))
                        recent.Add(new PlayerStream(parts[0], uri));
                }
            }

            if (settings.TrackHistory != null)
            {
                foreach (String line in settings.TrackHistory)
                {
                    String[] parts = (line ?? "").Split('\t');
                    DateTime time;
                    if (parts.Length == 3 && DateTime.TryParse(parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out time))
                        history.Add(new HistoryEntry { Time = time, Station = parts[1], Title = parts[2] });
                }
            }
        }

        /// <summary>
        /// Store history and recent stations in the settings (tab-separated text,
        /// which any GaGa version can read back).
        /// </summary>
        private void ExtrasSave()
        {
            List<String> lines = new List<String>();
            foreach (PlayerStream stream in recent)
                lines.Add(Clean(stream.Name) + "\t" + stream.Uri.AbsoluteUri);
            settings.RecentStations = lines.ToArray();

            lines = new List<String>();
            foreach (HistoryEntry entry in history)
                lines.Add(entry.Time.ToString("o") + "\t" + Clean(entry.Station) + "\t" + Clean(entry.Title));
            settings.TrackHistory = lines.ToArray();
        }

        private static String Clean(String text)
        {
            return (text ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        /// <summary>
        /// Menu text: no mnemonics, not too long.
        /// </summary>
        private static String MenuText(String text)
        {
            text = text ?? "";
            if (text.Length > 70)
                text = text.Substring(0, 67) + "...";
            return text.Replace("&", "&&");
        }

        /// <summary>
        /// Rebuild the submenus, called when the menu is about to open.
        /// </summary>
        private void ExtrasMenuUpdate()
        {
            RecentMenuUpdate();
            HistoryMenuUpdate();
            SleepMenuUpdate();
        }

        private static void DisposeDropDown(ToolStripMenuItem menu)
        {
            List<ToolStripItem> items = new List<ToolStripItem>();
            foreach (ToolStripItem item in menu.DropDownItems)
                items.Add(item);

            menu.DropDownItems.Clear();
            foreach (ToolStripItem item in items)
                item.Dispose();
        }

        ///
        /// Recent stations
        ///

        /// <summary>
        /// A station was started: put it on top of the recent ones.
        /// </summary>
        private void RecentAdd(PlayerStream stream)
        {
            if (stream == null)
                return;

            recent.RemoveAll(s => s.Uri == stream.Uri);
            recent.Insert(0, stream);

            if (recent.Count > RecentLimit)
                recent.RemoveRange(RecentLimit, recent.Count - RecentLimit);
        }

        private void RecentMenuUpdate()
        {
            DisposeDropDown(recentMenuItem);

            if (recent.Count == 0)
            {
                ToolStripMenuItem none = new ToolStripMenuItem("No stations played yet");
                none.Enabled = false;
                recentMenuItem.DropDownItems.Add(none);
                return;
            }

            PlayerStream current = player.Source;

            foreach (PlayerStream stream in recent)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(MenuText(stream.Name));
                item.Tag = stream;
                item.Checked = current != null && current.Uri == stream.Uri;
                item.Click += OnRecentItemClick;
                recentMenuItem.DropDownItems.Add(item);
            }

            recentMenuItem.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem clear = new ToolStripMenuItem("Clear recent stations");
            clear.Click += (s, e) => recent.Clear();
            recentMenuItem.DropDownItems.Add(clear);
        }

        private void OnRecentItemClick(Object sender, EventArgs e)
        {
            PlayerStream stream = (PlayerStream) ((ToolStripMenuItem) sender).Tag;
            RecentAdd(stream);
            player.Play(stream);
        }

        ///
        /// Track history
        ///

        /// <summary>
        /// Whether a stream title is an actual track and not the station
        /// talking about itself ("Station name", "Live", "Buffering"...).
        /// </summary>
        internal static Boolean IsTrackTitle(String title, String station)
        {
            String t = (title ?? "").Trim();
            if (t.Length < 3)
                return false;

            String lower = t.ToLowerInvariant();
            String[] junk = { "live", "on air", "buffering", "unknown", "advertisement", "commercial", "-", "n/a", "null" };
            foreach (String word in junk)
            {
                if (lower == word)
                    return false;
            }

            // slogans, ads and addresses that stations send between tracks:
            String[] phrases = { "commercial-free", "commercial free", "listener-supported", "listener supported",
                                 "advertis", "www.", "http://", "https://", "station id", "jingle" };
            foreach (String phrase in phrases)
            {
                if (lower.Contains(phrase))
                    return false;
            }

            String name = (station ?? "").Trim();
            if (name.Length > 0 && String.Equals(t, name, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        /// <summary>
        /// The stream announced a new track: remember it.
        /// </summary>
        private void HistoryAdd(String title, PlayerStream stream)
        {
            String station = (stream != null) ? stream.Name : "";
            if (!IsTrackTitle(title, station))
                return;

            title = title.Trim();
            if (history.Count > 0 && history[0].Title == title && history[0].Station == station)
                return;

            history.Insert(0, new HistoryEntry { Time = DateTime.Now, Station = station, Title = title });

            if (history.Count > HistoryLimit)
                history.RemoveRange(HistoryLimit, history.Count - HistoryLimit);
        }

        private void HistoryMenuUpdate()
        {
            DisposeDropDown(historyMenuItem);

            if (history.Count == 0)
            {
                ToolStripMenuItem none = new ToolStripMenuItem("No tracks yet");
                none.Enabled = false;
                historyMenuItem.DropDownItems.Add(none);
                return;
            }

            for (Int32 i = 0; i < history.Count && i < HistoryShown; i++)
            {
                HistoryEntry entry = history[i];

                ToolStripMenuItem item = new ToolStripMenuItem(entry.Time.ToString("HH:mm") + "   " + MenuText(entry.Title));
                item.ToolTipText = entry.Station + ", " + entry.Time.ToString("g");

                ToolStripMenuItem copy = new ToolStripMenuItem("Copy");
                copy.Click += (s, e) => HistoryCopy(entry.Title);

                ToolStripMenuItem search = new ToolStripMenuItem("Search on the web");
                search.Click += (s, e) => HistorySearch(entry.Title);

                item.DropDownItems.Add(copy);
                item.DropDownItems.Add(search);
                historyMenuItem.DropDownItems.Add(item);
            }

            historyMenuItem.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem copyAll = new ToolStripMenuItem("Copy the whole history");
            copyAll.Click += (s, e) => HistoryCopyAll();
            historyMenuItem.DropDownItems.Add(copyAll);

            ToolStripMenuItem clear = new ToolStripMenuItem("Clear history");
            clear.Click += (s, e) => history.Clear();
            historyMenuItem.DropDownItems.Add(clear);
        }

        private static void HistoryCopy(String text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception)
            {
                // clipboard busy, not worth an error message
            }
        }

        private void HistoryCopyAll()
        {
            List<String> lines = new List<String>();
            foreach (HistoryEntry entry in history)
                lines.Add(entry.Time.ToString("yyyy-MM-dd HH:mm") + "  " + entry.Title + "  (" + entry.Station + ")");

            HistoryCopy(String.Join(Environment.NewLine, lines.ToArray()));
        }

        private static void HistorySearch(String title)
        {
            try
            {
                Process.Start("https://duckduckgo.com/?q=" + Uri.EscapeDataString(title));
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Unable to open the browser");
            }
        }

        ///
        /// Sleep timer
        ///

        private static readonly Int32[] SleepPresets = { 15, 30, 60, 120 };

        private Boolean SleepActive
        {
            get { return sleepTimer.Enabled; }
        }

        private Int32 SleepMinutesLeft
        {
            get { return Math.Max(0, (Int32) Math.Ceiling((sleepAt - DateTime.Now).TotalMinutes)); }
        }

        private void SleepMenuUpdate()
        {
            DisposeDropDown(sleepMenuItem);

            sleepMenuItem.Checked = SleepActive;
            sleepMenuItem.Text = SleepActive
                ? ("Sleep timer (" + ((SleepMinutesLeft > 120) ? ("until " + sleepAt.ToString("HH:mm")) : (SleepMinutesLeft + " min left")) + ")")
                : "Sleep timer";

            foreach (Int32 minutes in SleepPresets)
            {
                Int32 m = minutes;
                ToolStripMenuItem item = new ToolStripMenuItem((m < 60) ? (m + " minutes") : ((m / 60) + ((m == 60) ? " hour" : " hours")));
                item.Click += (s, e) => SleepStart(m);
                sleepMenuItem.DropDownItems.Add(item);
            }

            ToolStripMenuItem custom = new ToolStripMenuItem("Custom...");
            custom.Click += (s, e) => SleepAskCustom();
            sleepMenuItem.DropDownItems.Add(custom);

            sleepMenuItem.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem off = new ToolStripMenuItem("Off");
            off.Checked = !SleepActive;
            off.Click += (s, e) => SleepStop();
            sleepMenuItem.DropDownItems.Add(off);
        }

        private void SleepStart(Int32 minutes)
        {
            SleepStartAt(DateTime.Now.AddMinutes(minutes));
        }

        private void SleepStartAt(DateTime at)
        {
            sleepAt = at;
            sleepTimer.Start();
        }

        /// <summary>
        /// Parse what the user typed for a custom sleep timer:
        /// "45" or "45m" (minutes), "1h30", "1:30h"... (duration),
        /// or "23:30" (a clock time, tomorrow when it's already past).
        /// Null when it can't be understood.
        /// </summary>
        internal static DateTime? SleepParse(String text, DateTime now)
        {
            String t = (text ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            if (t.Length == 0)
                return null;

            System.Text.RegularExpressions.Match m;

            // plain minutes:
            m = System.Text.RegularExpressions.Regex.Match(t, @"^(\d{1,4})(m|min|мин|м)?$");
            if (m.Success)
            {
                Int32 minutes = Int32.Parse(m.Groups[1].Value);
                return (minutes > 0) ? now.AddMinutes(minutes) : (DateTime?) null;
            }

            // hours and minutes: "1h", "1h30", "1h30m", "1ч30":
            m = System.Text.RegularExpressions.Regex.Match(t, @"^(\d{1,2})(h|ч)(\d{1,2})?(m|min|мин|м)?$");
            if (m.Success)
            {
                Int32 minutes = Int32.Parse(m.Groups[1].Value) * 60
                              + (m.Groups[3].Success ? Int32.Parse(m.Groups[3].Value) : 0);
                return (minutes > 0) ? now.AddMinutes(minutes) : (DateTime?) null;
            }

            // a clock time: "23:30", "7.15":
            m = System.Text.RegularExpressions.Regex.Match(t, @"^(\d{1,2})[:.](\d{2})$");
            if (m.Success)
            {
                Int32 hours = Int32.Parse(m.Groups[1].Value);
                Int32 minutes = Int32.Parse(m.Groups[2].Value);
                if (hours > 23 || minutes > 59)
                    return null;

                DateTime at = now.Date.AddHours(hours).AddMinutes(minutes);
                if (at <= now)
                    at = at.AddDays(1);
                return at;
            }

            return null;
        }

        private void SleepAskCustom()
        {
            using (Form form = new Form())
            {
                form.Text = "GaGa - Sleep timer";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ShowInTaskbar = true;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.AutoScaleMode = AutoScaleMode.Font;
                form.ClientSize = new System.Drawing.Size(320, 118);
                form.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);

                Label label = new Label();
                label.Text = "Stop playing after (minutes, like 45 or 1h30)\nor at a time (like 23:30):";
                label.SetBounds(12, 10, 296, 34);

                TextBox box = new TextBox();
                box.SetBounds(12, 48, 296, 23);
                box.Text = SleepActive ? sleepAt.ToString("HH:mm") : "";

                Button ok = new Button();
                ok.Text = "Start";
                ok.SetBounds(152, 82, 75, 26);
                ok.DialogResult = DialogResult.OK;

                Button cancel = new Button();
                cancel.Text = "Cancel";
                cancel.SetBounds(233, 82, 75, 26);
                cancel.DialogResult = DialogResult.Cancel;

                form.Controls.AddRange(new Control[] { label, box, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                // a time that can't be understood keeps the window open:
                form.FormClosing += (s, e) =>
                {
                    if (form.DialogResult == DialogResult.OK && SleepParse(box.Text, DateTime.Now) == null)
                    {
                        e.Cancel = true;
                        box.Focus();
                        box.SelectAll();
                        System.Media.SystemSounds.Beep.Play();
                    }
                };

                Theme.Apply(form);
                form.HandleCreated += (s, e) => Theme.ApplyTitleBar(form);

                if (form.ShowDialog() == DialogResult.OK)
                {
                    DateTime? at = SleepParse(box.Text, DateTime.Now);
                    if (at.HasValue)
                        SleepStartAt(at.Value);
                }
            }
        }

        private void SleepStop()
        {
            sleepTimer.Stop();
        }

        private void OnSleepTimerTick(Object sender, EventArgs e)
        {
            if (DateTime.Now < sleepAt)
                return;

            sleepTimer.Stop();
            player.Stop();
        }
    }
}
