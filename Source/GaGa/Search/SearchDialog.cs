
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
        private readonly ComboBox countryComboBox;
        private readonly TextBox filterTextBox;
        private readonly System.Windows.Forms.Timer filterTimer;
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

        /// <summary>
        /// Last search mode and browsed country (two letters), kept like LastSize.
        /// </summary>
        public static Int32 LastMode;
        public static String LastCountry;

        // modes in the "by" box:
        private const Int32 ModeName = 0, ModeGenre = 1, ModeCountry = 2, ModeBrowseCountry = 3;

        // what was loaded (in the directory's order: most popular first),
        // and the part of it the filter lets through, as shown:
        private List<RadioStation> loaded = new List<RadioStation>();
        private List<RadioStation> shown = new List<RadioStation>();
        private Int32 sortColumn = -1;
        private Boolean sortDescending;

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

            // the query box, or the country list when browsing a country, in the same place:
            Panel queryPanel = new Panel();
            queryPanel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            queryPanel.Margin = new Padding(0, 0, 6, 0);

            queryTextBox = new TextBox();
            queryTextBox.Dock = DockStyle.Fill;

            countryComboBox = new ComboBox();
            countryComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            countryComboBox.Dock = DockStyle.Fill;
            countryComboBox.MaxDropDownItems = 25;
            countryComboBox.Visible = false;

            queryPanel.Controls.Add(queryTextBox);
            queryPanel.Controls.Add(countryComboBox);
            queryPanel.Height = Math.Max(queryTextBox.PreferredHeight, countryComboBox.PreferredHeight);

            fieldComboBox = new ComboBox();
            fieldComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            fieldComboBox.Items.AddRange(new Object[] { "by name", "by genre", "by country", "browse a country" });
            fieldComboBox.SelectedIndex = 0;
            fieldComboBox.Width = 130;
            fieldComboBox.Anchor = AnchorStyles.Left;
            fieldComboBox.Margin = new Padding(0, 0, 6, 0);

            searchButton = new Button();
            searchButton.Text = "&Search";
            searchButton.Size = new Size(96, 27);
            searchButton.Anchor = AnchorStyles.Left;
            searchButton.Margin = new Padding(0);

            top.Controls.Add(queryPanel, 0, 0);
            top.Controls.Add(fieldComboBox, 1, 0);
            top.Controls.Add(searchButton, 2, 0);

            // results:
            resultsListView = new ListView();
            resultsListView.Dock = DockStyle.Fill;
            resultsListView.View = View.Details;
            resultsListView.FullRowSelect = true;
            resultsListView.HideSelection = false;
            resultsListView.MultiSelect = false;
            resultsListView.ShowItemToolTips = true;

            // virtual: only the visible rows exist, so the whole directory (tens
            // of thousands of stations) scrolls and filters without delay:
            resultsListView.VirtualMode = true;
            resultsListView.VirtualListSize = 0;
            resultsListView.RetrieveVirtualItem += OnRetrieveItem;
            resultsListView.ColumnClick += OnColumnClick;

            // filter row: what is loaded, narrowed down as you type:
            TableLayoutPanel filterRow = new TableLayoutPanel();
            filterRow.Dock = DockStyle.Top;
            filterRow.AutoSize = true;
            filterRow.ColumnCount = 2;
            filterRow.RowCount = 1;
            filterRow.Padding = new Padding(8, 0, 8, 0);
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            Label filterLabel = new Label();
            filterLabel.Text = "Filter the list:";
            filterLabel.AutoSize = true;
            filterLabel.Anchor = AnchorStyles.Left;
            filterLabel.Margin = new Padding(0, 0, 6, 0);

            filterTextBox = new TextBox();
            filterTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            filterTextBox.Margin = new Padding(0);

            filterRow.Controls.Add(filterLabel, 0, 0);
            filterRow.Controls.Add(filterTextBox, 1, 0);

            filterTimer = new System.Windows.Forms.Timer();
            filterTimer.Interval = 200;
            filterTimer.Tick += (sender, e) => { filterTimer.Stop(); ApplyFilter(); };
            filterTextBox.TextChanged += (sender, e) => { filterTimer.Stop(); filterTimer.Start(); };
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
            Controls.Add(filterRow);
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
            fieldComboBox.SelectedIndexChanged += (sender, e) => ModeChanged();
            countryComboBox.SelectionChangeCommitted += (sender, e) => OnSearchClick(sender, e);
            FormClosed += (sender, e) => { nowNumber++; nowTimer.Dispose(); filterTimer.Dispose(); };

            if (LastMode >= 0 && LastMode < fieldComboBox.Items.Count)
                fieldComboBox.SelectedIndex = LastMode;
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
            if (resultsListView.VirtualListSize > 0 && !refitting)
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
                if (resultsListView.SelectedIndices.Count == 0)
                    return null;

                Int32 index = resultsListView.SelectedIndices[0];
                return (index < shown.Count) ? shown[index] : null;
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

        private Int32 Mode
        {
            get { return fieldComboBox.SelectedIndex; }
        }

        /// <summary>
        /// The "by" box changed: a country list instead of the query box when browsing.
        /// </summary>
        private void ModeChanged()
        {
            LastMode = Mode;
            Boolean browsing = Mode == ModeBrowseCountry;

            countryComboBox.Visible = browsing;
            queryTextBox.Visible = !browsing;
            searchButton.Text = (Mode == ModeName || Mode == ModeGenre || Mode == ModeCountry) ? "&Search" : "&Load";

            if (browsing)
            {
                if (countryComboBox.Items.Count == 0)
                    LoadCountries();
                else
                    countryComboBox.Focus();
            }
            else
            {
                queryTextBox.Focus();
            }
        }

        private void LoadCountries()
        {
            statusLabel.Text = "Getting the list of countries...";

            ThreadPool.QueueUserWorkItem(state =>
            {
                List<RadioBrowser.Country> list = null;
                String error = null;
                try
                {
                    list = RadioBrowser.Countries();
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                }

                OnUiThread(() =>
                {
                    if (error != null)
                    {
                        statusLabel.Text = "Unable to get the countries: " + error;
                        return;
                    }

                    countryComboBox.BeginUpdate();
                    countryComboBox.Items.Clear();
                    foreach (RadioBrowser.Country country in list)
                    {
                        countryComboBox.Items.Add(country);
                        if (country.Code == LastCountry)
                            countryComboBox.SelectedItem = country;
                    }
                    countryComboBox.EndUpdate();

                    statusLabel.Text = "Pick a country to see all its stations (type its first letters to jump).";
                    countryComboBox.Focus();
                });
            });
        }

        private void OnUiThread(Action action)
        {
            try
            {
                if (!IsDisposed)
                    BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // closed meanwhile
            }
        }

        private void OnSearchClick(Object sender, EventArgs e)
        {
            if (Mode == ModeBrowseCountry)
            {
                RadioBrowser.Country country = countryComboBox.SelectedItem as RadioBrowser.Country;
                if (country == null)
                {
                    statusLabel.Text = "Pick a country first.";
                    return;
                }

                LastCountry = country.Code;
                String code = country.Code;
                Load("Loading the stations of " + country.Name + "...", () => RadioBrowser.ByCountry(code), null);
                return;
            }

            String term = queryTextBox.Text.Trim();
            if (term.Length == 0)
            {
                statusLabel.Text = "Type a station name, a genre (rock, jazz...) or a country.";
                queryTextBox.Focus();
                return;
            }

            RadioSearchField field = (Mode == ModeGenre) ? RadioSearchField.Tag
                                   : (Mode == ModeCountry) ? RadioSearchField.Country
                                   : RadioSearchField.Name;

            Load("Searching...", () => RadioBrowser.Search(field, term), null);
        }

        /// <summary>
        /// Fetch stations in the background, then show them.
        /// </summary>
        private void Load(String status, Func<List<RadioStation>> fetch, Func<String> note)
        {
            Int32 number = ++searchNumber;
            statusLabel.Text = status;
            searchButton.Enabled = false;
            UseWaitCursor = true;

            ThreadPool.QueueUserWorkItem(state =>
            {
                List<RadioStation> stations = null;
                String error = null;
                try
                {
                    stations = fetch();
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                }

                String extra = (error == null && note != null) ? note() : "";
                OnUiThread(() => ShowResults(number, stations, error, extra));
            });
        }

        private void ShowResults(Int32 number, List<RadioStation> stations, String error, String extra)
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

            loaded = stations;
            sortColumn = -1;
            loadedNote = extra;
            ApplyFilter();

            if (shown.Count > 0)
                resultsListView.Focus();
        }

        private String loadedNote = "";

        ///
        /// The list: filter, sort, rows on demand
        ///

        /// <summary>
        /// Show the loaded stations that match the filter: any part of the
        /// name, genres or country, ignoring case.
        /// </summary>
        private void ApplyFilter()
        {
            String filter = filterTextBox.Text.Trim();

            List<RadioStation> list;
            if (filter.Length == 0)
            {
                list = new List<RadioStation>(loaded);
            }
            else
            {
                list = new List<RadioStation>();
                foreach (RadioStation station in loaded)
                {
                    if (Contains(station.Name, filter) || Contains(station.Tags, filter) || Contains(station.Country, filter))
                        list.Add(station);
                }
            }

            if (sortColumn >= 0)
            {
                // stable, so that equal ones stay most popular first:
                List<RadioStation> order = new List<RadioStation>(list);
                Dictionary<RadioStation, Int32> rank = new Dictionary<RadioStation, Int32>();
                for (Int32 i = 0; i < order.Count; i++)
                    rank[order[i]] = i;

                Int32 column = sortColumn;
                Int32 sign = sortDescending ? -1 : 1;
                list.Sort((a, b) =>
                {
                    Int32 c = String.Compare(Cell(a, column), Cell(b, column), StringComparison.CurrentCultureIgnoreCase);
                    return (c != 0) ? sign * c : rank[a] - rank[b];
                });
            }

            shown = list;

            resultsListView.BeginUpdate();
            resultsListView.SelectedIndices.Clear();
            resultsListView.VirtualListSize = shown.Count;
            resultsListView.EndUpdate();
            resultsListView.Invalidate();

            if (shown.Count > 0)
            {
                resultsListView.SelectedIndices.Add(0);
                resultsListView.EnsureVisible(0);
            }

            if (loaded.Count == 0)
                statusLabel.Text = "Nothing found." + loadedNote;
            else if (filter.Length == 0)
                statusLabel.Text = loaded.Count + " stations, most popular first. Double-click to listen." + loadedNote;
            else
                statusLabel.Text = shown.Count + " of " + loaded.Count + " stations match \"" + filter + "\".";

            UpdateButtons();
            NowScheduleLookUp();
        }

        private static Boolean Contains(String text, String part)
        {
            return text != null && text.IndexOf(part, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private static String Cell(RadioStation station, Int32 column)
        {
            switch (column)
            {
                case 0: return station.Name;
                case 1: return station.Country ?? "";
                case 2: return station.Format;
                default: return (station.Tags ?? "").Replace(",", ", ");
            }
        }

        private void OnRetrieveItem(Object sender, RetrieveVirtualItemEventArgs e)
        {
            RadioStation station = (e.ItemIndex < shown.Count) ? shown[e.ItemIndex] : null;
            if (station == null)
            {
                e.Item = new ListViewItem(new String[] { "", "", "", "" });
                return;
            }

            ListViewItem item = new ListViewItem(station.Name);
            item.SubItems.Add(Cell(station, 1));
            item.SubItems.Add(Cell(station, 2));
            item.SubItems.Add(Cell(station, 3));
            item.ToolTipText = station.StreamUri.AbsoluteUri;
            e.Item = item;
        }

        /// <summary>
        /// Header clicked: sort by that column, again to reverse,
        /// a third time back to most popular first.
        /// </summary>
        private void OnColumnClick(Object sender, ColumnClickEventArgs e)
        {
            if (sortColumn != e.Column)
            {
                sortColumn = e.Column;
                sortDescending = false;
            }
            else if (!sortDescending)
            {
                sortDescending = true;
            }
            else
            {
                sortColumn = -1;
            }

            ApplyFilter();
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
