
// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

using GaGa.NotifyIconPlayer;
using GaGa.StreamsFile;


namespace GaGa.Search
{
    /// <summary>
    /// Search the radio-browser.info directory, listen to stations
    /// and add them to the streams file.
    /// </summary>
    internal class SearchDialog : Form
    {
        /// <summary>
        /// Section of the streams file that found stations are added to.
        /// </summary>
        public const String StreamsFileSection = "Found stations";

        private readonly String streamsFilepath;

        private readonly TextBox queryTextBox;
        private readonly ComboBox fieldComboBox;
        private readonly Button searchButton;
        private readonly ListView resultsListView;
        private readonly Label statusLabel;
        private readonly Button playButton;
        private readonly Button addButton;
        private readonly Button closeButton;
        private readonly Label nowLabel;

        // what the selected station plays, looked up after a short pause
        // on the selection (one station at a time, a few seconds at most):
        private readonly System.Windows.Forms.Timer nowTimer;
        private Int32 nowNumber;
        private Boolean refitting;

        /// <summary>
        /// Window size, kept between openings (and saved in the settings by GaGa).
        /// </summary>
        public static Size LastSize = Size.Empty;

        // results of an older search that finish late are ignored:
        private Int32 searchNumber;

        /// <summary>
        /// Raised when the user wants to listen to a station.
        /// </summary>
        public event Action<String, Uri> PlayRequested;

        public SearchDialog(String streamsFilepath)
        {
            this.streamsFilepath = streamsFilepath;

            Text = "GaGa - Search stations";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(760, 480);
            MinimumSize = new Size(520, 320);
            if (!LastSize.IsEmpty)
                Size = LastSize;
            ShowInTaskbar = true;
            KeyPreview = true;

            // search row: [query.............] [Name v] [Search]
            TableLayoutPanel top = new TableLayoutPanel();
            top.Dock = DockStyle.Top;
            top.AutoSize = true;
            top.ColumnCount = 3;
            top.RowCount = 1;
            top.Padding = new Padding(8, 8, 8, 4);
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            queryTextBox = new TextBox();
            queryTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            queryTextBox.Margin = new Padding(0, 0, 6, 0);

            fieldComboBox = new ComboBox();
            fieldComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            fieldComboBox.Items.AddRange(new Object[] { "by name", "by genre", "by country" });
            fieldComboBox.SelectedIndex = 0;
            fieldComboBox.Width = 120;
            fieldComboBox.Anchor = AnchorStyles.Left;
            fieldComboBox.Margin = new Padding(0, 0, 6, 0);

            searchButton = new Button();
            searchButton.Text = "&Search";
            searchButton.Size = new Size(96, 27);
            searchButton.Anchor = AnchorStyles.Left;
            searchButton.Margin = new Padding(0);

            top.Controls.Add(queryTextBox, 0, 0);
            top.Controls.Add(fieldComboBox, 1, 0);
            top.Controls.Add(searchButton, 2, 0);

            // results:
            resultsListView = new ListView();
            resultsListView.Dock = DockStyle.Fill;
            resultsListView.View = View.Details;
            resultsListView.FullRowSelect = true;
            resultsListView.HideSelection = false;
            resultsListView.MultiSelect = false;
            resultsListView.Columns.Add("Station", 280);
            resultsListView.Columns.Add("Country", 110);
            resultsListView.Columns.Add("Format", 90);
            resultsListView.Columns.Add("Genres", 240);
            resultsListView.ClientSizeChanged += (sender, e) => ColumnsFit();

            // the list sits in a panel, for even margins around it:
            Panel middle = new Panel();
            middle.Dock = DockStyle.Fill;
            middle.Padding = new Padding(8, 4, 8, 0);
            middle.Controls.Add(resultsListView);

            // what the selected station is playing:
            nowLabel = new Label();
            nowLabel.Dock = DockStyle.Bottom;
            nowLabel.Height = 26;
            nowLabel.Padding = new Padding(10, 0, 10, 0);
            nowLabel.TextAlign = ContentAlignment.MiddleLeft;
            nowLabel.AutoEllipsis = true;
            nowLabel.UseMnemonic = false;

            nowTimer = new System.Windows.Forms.Timer();
            nowTimer.Interval = 700;
            nowTimer.Tick += (sender, e) => { nowTimer.Stop(); NowLookUp(); };

            // bottom row: status ............ [Play] [Add to streams file] [Close]
            TableLayoutPanel bottom = new TableLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.AutoSize = true;
            bottom.ColumnCount = 4;
            bottom.RowCount = 1;
            bottom.Padding = new Padding(8, 4, 8, 8);
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.AutoEllipsis = true;
            statusLabel.UseMnemonic = false;
            statusLabel.Margin = new Padding(2, 0, 6, 0);
            statusLabel.Text = "Search thousands of stations from radio-browser.info.";

            playButton = new Button();
            playButton.Text = "&Play";
            playButton.Size = new Size(90, 27);
            playButton.Margin = new Padding(0, 0, 6, 0);

            addButton = new Button();
            addButton.Text = "&Add to streams file";
            addButton.AutoSize = true;
            addButton.MinimumSize = new Size(0, 27);
            addButton.Margin = new Padding(0, 0, 6, 0);

            closeButton = new Button();
            closeButton.Text = "Close";
            closeButton.Size = new Size(90, 27);
            closeButton.Margin = new Padding(0);
            closeButton.DialogResult = DialogResult.Cancel;

            bottom.Controls.Add(statusLabel, 0, 0);
            bottom.Controls.Add(playButton, 1, 0);
            bottom.Controls.Add(addButton, 2, 0);
            bottom.Controls.Add(closeButton, 3, 0);

            // fill must be added first to be laid out last:
            Controls.Add(middle);
            Controls.Add(top);
            Controls.Add(nowLabel);
            Controls.Add(bottom);

            AcceptButton = searchButton;
            CancelButton = closeButton;

            searchButton.Click += OnSearchClick;
            playButton.Click += OnPlayClick;
            addButton.Click += OnAddClick;
            closeButton.Click += (sender, e) => Close();
            resultsListView.ItemActivate += OnPlayClick;
            resultsListView.SelectedIndexChanged += (sender, e) => { UpdateButtons(); NowScheduleLookUp(); };
            FormClosed += (sender, e) => { nowNumber++; nowTimer.Dispose(); };
            ResizeEnd += (sender, e) => { if (WindowState == FormWindowState.Normal) LastSize = Size; };

            UpdateButtons();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ColumnsFit();
            queryTextBox.Focus();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern Boolean ShowScrollBar(IntPtr hWnd, Int32 wBar, Boolean bShow);

        /// <summary>
        /// Columns share the width of the list: no empty column on the right.
        /// </summary>
        private void ColumnsFit()
        {
            // a few pixels short, so that no horizontal scrollbar shows up:
            Int32 width = resultsListView.ClientSize.Width - 4;
            if (width <= 0)
                return;

            Int32 station = width * 38 / 100;
            Int32 country = width * 14 / 100;
            Int32 format = width * 12 / 100;

            resultsListView.Columns[0].Width = station;
            resultsListView.Columns[1].Width = country;
            resultsListView.Columns[2].Width = format;
            // -2: the last column takes exactly what is left
            resultsListView.Columns[3].Width = -2;

            // the columns always fit: never a horizontal scrollbar
            if (resultsListView.IsHandleCreated)
                ShowScrollBar(resultsListView.Handle, 0 /* SB_HORZ */, false);

            // the vertical scrollbar shows up after the items: fit again then
            if (resultsListView.Items.Count > 0 && !refitting)
            {
                refitting = true;
                BeginInvoke(new Action(() => { ColumnsFit(); refitting = false; }));
            }
        }

        ///
        /// Now playing on the selected station
        ///

        private void NowScheduleLookUp()
        {
            nowNumber++;
            nowTimer.Stop();

            RadioStation station = SelectedStation;
            nowLabel.Text = (station == null) ? "" : ("Now on " + station.Name + ": ...");

            if (station != null)
                nowTimer.Start();
        }

        /// <summary>
        /// Listen to the selected station for a few seconds, through the same
        /// relay the player uses (so ICY, Ogg and HLS titles all work),
        /// until it names the track. Nothing is played aloud.
        /// </summary>
        private void NowLookUp()
        {
            RadioStation station = SelectedStation;
            if (station == null || station.StreamUri == null)
                return;

            Int32 number = nowNumber;
            Uri uri = station.StreamUri;
            String name = station.Name;

            ThreadPool.QueueUserWorkItem(state =>
            {
                String title = null;
                ManualResetEvent got = new ManualResetEvent(false);

                try
                {
                    using (HttpsRelay relay = new HttpsRelay(uri))
                    {
                        relay.TitleChanged += t => { if (title == null) { title = t; got.Set(); } };

                        System.Net.HttpWebRequest request = (System.Net.HttpWebRequest) System.Net.WebRequest.Create(relay.LocalUri);
                        request.Timeout = 10000;
                        request.ReadWriteTimeout = 10000;

                        using (System.Net.WebResponse response = request.GetResponse())
                        using (System.IO.Stream body = response.GetResponseStream())
                        {
                            Byte[] buffer = new Byte[16384];
                            DateTime until = DateTime.UtcNow.AddSeconds(12);

                            // read (and drop) audio until the title shows up:
                            while (!got.WaitOne(0) && DateTime.UtcNow < until && number == nowNumber)
                            {
                                if (body.Read(buffer, 0, buffer.Length) <= 0)
                                    break;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // dead station, timeout: just no title
                }

                try
                {
                    if (!IsDisposed)
                        BeginInvoke(new Action(() =>
                        {
                            if (number != nowNumber)
                                return;

                            nowLabel.Text = (title != null)
                                ? ("Now on " + name + ":  " + title)
                                : (name + " doesn't say what it plays.");
                        }));
                }
                catch (InvalidOperationException)
                {
                    // closed meanwhile
                }
            });
        }

        private RadioStation SelectedStation
        {
            get
            {
                if (resultsListView.SelectedItems.Count == 0)
                    return null;
                return (RadioStation) resultsListView.SelectedItems[0].Tag;
            }
        }

        private void UpdateButtons()
        {
            Boolean selected = SelectedStation != null;
            playButton.Enabled = selected;
            addButton.Enabled = selected;
        }

        ///
        /// Searching
        ///

        private void OnSearchClick(Object sender, EventArgs e)
        {
            String term = queryTextBox.Text.Trim();
            if (term.Length == 0)
            {
                statusLabel.Text = "Type a station name, a genre (rock, jazz...) or a country.";
                queryTextBox.Focus();
                return;
            }

            RadioSearchField field = (fieldComboBox.SelectedIndex == 1) ? RadioSearchField.Tag
                                   : (fieldComboBox.SelectedIndex == 2) ? RadioSearchField.Country
                                   : RadioSearchField.Name;

            Int32 number = ++searchNumber;
            statusLabel.Text = "Searching...";
            searchButton.Enabled = false;
            UseWaitCursor = true;

            ThreadPool.QueueUserWorkItem(state =>
            {
                List<RadioStation> stations = null;
                String error = null;
                try
                {
                    stations = RadioBrowser.Search(field, term);
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                }

                try
                {
                    if (!IsDisposed)
                        BeginInvoke(new Action(() => ShowResults(number, stations, error)));
                }
                catch (InvalidOperationException)
                {
                    // closed meanwhile
                }
            });
        }

        private void ShowResults(Int32 number, List<RadioStation> stations, String error)
        {
            if (number != searchNumber || IsDisposed)
                return;

            searchButton.Enabled = true;
            UseWaitCursor = false;

            if (error != null)
            {
                statusLabel.Text = "Search failed: " + error;
                return;
            }

            resultsListView.BeginUpdate();
            resultsListView.Items.Clear();
            foreach (RadioStation station in stations)
            {
                ListViewItem item = new ListViewItem(station.Name);
                item.SubItems.Add(station.Country ?? "");
                item.SubItems.Add(station.Format);
                item.SubItems.Add((station.Tags ?? "").Replace(",", ", "));
                item.ToolTipText = station.StreamUri.AbsoluteUri;
                item.Tag = station;
                resultsListView.Items.Add(item);
            }
            resultsListView.EndUpdate();

            if (stations.Count == 0)
            {
                statusLabel.Text = "Nothing found.";
            }
            else
            {
                statusLabel.Text = "Found " + stations.Count + " stations, most popular first. Double-click to listen.";
                resultsListView.Items[0].Selected = true;
                resultsListView.Focus();
            }
            UpdateButtons();
        }

        ///
        /// Actions
        ///

        private void OnPlayClick(Object sender, EventArgs e)
        {
            RadioStation station = SelectedStation;
            if (station == null)
                return;

            Action<String, Uri> handler = PlayRequested;
            if (handler != null)
                handler(station.Name, station.StreamUri);

            RadioBrowser.CountClick(station.Uuid);
            statusLabel.Text = "Playing " + station.Name + ".";
        }

        private void OnAddClick(Object sender, EventArgs e)
        {
            RadioStation station = SelectedStation;
            if (station == null)
                return;

            try
            {
                if (StreamsFileWriter.Contains(streamsFilepath, station.StreamUri))
                {
                    statusLabel.Text = station.Name + " is already in your streams file.";
                    return;
                }

                StreamsFileWriter.AddStream(streamsFilepath, StreamsFileSection, station.Name, station.StreamUri);
                statusLabel.Text = "Added " + station.Name + " to \"" + StreamsFileSection + "\" in the menu.";
            }
            catch (Exception exception)
            {
                statusLabel.Text = "Unable to update the streams file: " + exception.Message;
            }
        }
    }
}
