// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

using GaGa.Controls;
using GaGa.NotifyIconPlayer;
using GaGa.StreamsFile;


namespace GaGa
{
    /// <summary>
    /// Favorites, recording, start with Windows and updates.
    /// </summary>
    internal partial class GaGa
    {
        /// <summary>
        /// Enable, check and label the items that depend on the player state.
        /// </summary>
        private void MenuUpdateStates()
        {
            PlayerStream current = player.Source;

            Boolean favorite = false;
            try
            {
                favorite = current != null && StreamsFileWriter.ContainsInSection(streamsFilepath, FavoritesSection, current.Uri);
            }
            catch (Exception)
            {
            }

            favoriteItem.Enabled = current != null;
            favoriteItem.Checked = favorite;
            favoriteItem.Text = favorite ? "Remove from &favorites" : "Add to &favorites";

            Boolean recording = player.IsRecording;
            recordToggleItem.Enabled = recording || player.CanRecord;
            recordToggleItem.Checked = recording;
            recordToggleItem.Text = recording ? "&Recording (click to stop)" : "&Record this station";
            recordMenuItem.Checked = recording;
            recordOpenFolderItem.Enabled = Directory.Exists(recordingsFolder);

            optionsStartWithWindowsItem.Checked = Autostart.IsEnabled;
        }

        ///
        /// Theme
        ///

        /// <summary>
        /// Colors that the renderer doesn't paint itself: the trackbars
        /// in the audio menu, the theme checkmarks and the search window.
        /// </summary>
        private void ThemeUpdate()
        {
            Color back = toolStripRenderer.ColorTable.ToolStripDropDownBackground;
            Color fore = Theme.IsDark ? Theme.DarkText : SystemColors.ControlText;

            foreach (ToolStripLabeledTrackBar bar in new ToolStripLabeledTrackBar[] { volumeTrackBar })
            {
                bar.BackColor = back;
                bar.Label.BackColor = back;
                bar.Label.ForeColor = fore;
                bar.TrackBar.BackColor = back;
            }

            for (Int32 i = 0; i < optionsThemeItems.Length; i++)
                optionsThemeItems[i].Checked = (Int32) Theme.Mode == i;

            if (searchDialog != null && !searchDialog.IsDisposed)
            {
                Theme.Apply(searchDialog);
                Theme.ApplyTitleBar(searchDialog);
            }
        }

        private void ThemeSet(ThemeMode mode)
        {
            Theme.Mode = mode;
            ThemeUpdate();
            notifyIcon.ContextMenuStrip.Invalidate();
        }

        ///
        /// Favorites
        ///

        /// <summary>
        /// Add the current station to the favorites section of the streams file
        /// (on top of the menu), or remove it from there. The station may also be
        /// in other sections. The menu picks it up on its own (the file is reloaded on changes).
        /// </summary>
        private void OnFavoriteItemClick(Object sender, EventArgs e)
        {
            PlayerStream current = player.Source;
            if (current == null)
                return;

            try
            {
                if (StreamsFileWriter.ContainsInSection(streamsFilepath, FavoritesSection, current.Uri))
                    StreamsFileWriter.RemoveStream(streamsFilepath, FavoritesSection, current.Uri);
                else
                    StreamsFileWriter.AddStream(streamsFilepath, FavoritesSection, current.Name, current.Uri, true);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Unable to add to favorites");
            }
        }

        ///
        /// Recording
        ///

        /// <summary>
        /// Path for a new recording, without extension:
        /// "Recordings\Station 2026-11-13 21-05-00".
        /// </summary>
        private String RecordingBasePath(PlayerStream stream)
        {
            String name = stream.Name ?? "";
            foreach (Char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            name = name.Trim().TrimEnd('.');
            if (name.Length == 0)
                name = "Recording";
            if (name.Length > 80)
                name = name.Substring(0, 80);

            return Path.Combine(recordingsFolder, name + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));
        }

        private void OnRecordToggleItemClick(Object sender, EventArgs e)
        {
            if (player.IsRecording)
            {
                player.StopRecording();
                String path = player.RecordingPath;    // after stopping: .aac may have become .m4a

                if (path != null)
                    notifyIcon.ShowBalloonTip(5000, "Recording saved", Path.GetFileName(path), ToolTipIcon.Info);
                return;
            }

            if (!player.StartRecording(RecordingBasePath(player.Source)))
                MessageBox.Show("Start playing an http:// or https:// station first.", "Unable to record");
        }

        private void OnRecordOpenFolderItemClick(Object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(recordingsFolder);
                Process.Start(recordingsFolder);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Error opening recordings folder");
            }
        }

        ///
        /// Start with Windows
        ///

        private void OnStartWithWindowsItemClick(Object sender, EventArgs e)
        {
            try
            {
                Autostart.Set(!Autostart.IsEnabled);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Unable to change start with Windows");
            }

            optionsStartWithWindowsItem.Checked = Autostart.IsEnabled;
        }

        ///
        /// Updates
        ///

        /// <summary>
        /// Look for a newer release in the background.
        /// Interactive: also report "up to date" and errors.
        /// </summary>
        private void UpdateCheck(Boolean interactive)
        {
            if (updateCheckRunning)
                return;

            updateCheckRunning = true;
            ContextMenuStrip ui = notifyIcon.ContextMenuStrip;

            Thread thread = new Thread(() =>
            {
                Updater.Update update = null;
                Exception error = null;

                try
                {
                    update = Updater.Check();
                }
                catch (Exception exception)
                {
                    error = exception;
                }

                ui.BeginInvoke(new Action(() =>
                {
                    updateCheckRunning = false;
                    UpdateCheckDone(update, error, interactive);
                }));
            });

            thread.IsBackground = true;
            thread.Name = "GaGa update check";
            thread.Start();
        }

        private void UpdateCheckDone(Updater.Update update, Exception error, Boolean interactive)
        {
            if (error != null)
            {
                if (interactive)
                    MessageBox.Show("Unable to check for updates: \n" + error.Message, "GaGa updates");
                return;
            }

            if (update == null)
            {
                if (interactive)
                    MessageBox.Show("You have the latest GaGa (" + Updater.CurrentVersion + ").", "GaGa updates");
                return;
            }

            String text = String.Format(
                "GaGa {0} is available (you have {1}). \n\n" +
                "Download it and restart GaGa now?",
                update.Tag,
                Updater.CurrentVersion
            );

            if (!Util.MessageBoxYesNo(text, "GaGa update"))
                return;

            try
            {
                Updater.Install(update);
            }
            catch (Exception exception)
            {
                String message = String.Format(
                    "Unable to install the update: \n{0} \n\n" +
                    "You can download it from: \n{1}",
                    exception.Message,
                    update.PageUrl
                );
                MessageBox.Show(message, "GaGa update");
                return;
            }

            // save everything and hand over to the new exe:
            Exit();

            try
            {
                Updater.Restart();
            }
            catch (Exception exception)
            {
                MessageBox.Show("GaGa was updated, but couldn't restart: \n" + exception.Message, "GaGa update");
            }
        }
    }
}
