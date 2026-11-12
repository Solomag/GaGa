
// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

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
            Icon = global::GaGa.NotifyIconPlayer.Util.ResourceAsIcon("GaGa.NotifyIconPlayer.Resources.Playing.ico");
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(760, 480);
            MinimumSize = new Size(480, 300);
            ShowInTaskbar = true;
            KeyPreview = true;

            // search row: [query.............] [Name v] [Search]
            TableLayoutPanel top = new TableLayoutPanel();
            top.Dock = DockStyle.Top;
            top.AutoSize = true;
            top.ColumnCount = 3;
            top.RowCount = 1;
            top.Padding = new Padding(6, 6, 6, 2);
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            queryTextBox = new TextBox();
            queryTextBox.Dock = DockStyle.Fill;
            queryTextBox.Margin = new Padding(3, 4, 3, 3);

            fieldComboBox = new ComboBox();
            fieldComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            fieldComboBox.Items.AddRange(new Object[] { "by name", "by genre", "by country" });
            fieldComboBox.SelectedIndex = 0;
            fieldComboBox.Width = 110;
            fieldComboBox.Margin = new Padding(3, 4, 3, 3);

            searchButton = new Button();
            searchButton.Text = "&Search";
            searchButton.AutoSize = true;

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
            resultsListView.Columns.Add("Country", 120);
            resultsListView.Columns.Add("Format", 90);
            resultsListView.Columns.Add("Genres", 240);

            // bottom row: status ............ [Play] [Add to streams file] [Close]
            TableLayoutPanel bottom = new TableLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.AutoSize = true;
            bottom.ColumnCount = 4;
            bottom.RowCount = 1;
            bottom.Padding = new Padding(6, 2, 6, 6);
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.AutoEllipsis = true;
            statusLabel.Text = "Search thousands of stations from radio-browser.info.";

            playButton = new Button();
            playButton.Text = "&Play";
            playButton.AutoSize = true;

            addButton = new Button();
            addButton.Text = "&Add to streams file";
            addButton.AutoSize = true;

            closeButton = new Button();
            closeButton.Text = "Close";
            closeButton.AutoSize = true;
            closeButton.DialogResult = DialogResult.Cancel;

            bottom.Controls.Add(statusLabel, 0, 0);
            bottom.Controls.Add(playButton, 1, 0);
            bottom.Controls.Add(addButton, 2, 0);
            bottom.Controls.Add(closeButton, 3, 0);

            // fill must be added first to be laid out last:
            Controls.Add(resultsListView);
            Controls.Add(top);
            Controls.Add(bottom);

            AcceptButton = searchButton;
            CancelButton = closeButton;

            searchButton.Click += OnSearchClick;
            playButton.Click += OnPlayClick;
            addButton.Click += OnAddClick;
            closeButton.Click += (sender, e) => Close();
            resultsListView.ItemActivate += OnPlayClick;
            resultsListView.SelectedIndexChanged += (sender, e) => UpdateButtons();

            UpdateButtons();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            queryTextBox.Focus();
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
