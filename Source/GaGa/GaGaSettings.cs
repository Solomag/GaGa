
// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.IO;
using System.Runtime.Serialization;

using GaGa.NotifyIconPlayer;


namespace GaGa
{
    [Serializable]
    internal class GaGaSettings
    {
        /// <summary>
        /// Last balance value set in the audio menu.
        /// </summary>
        public Int32 LastBalanceTrackBarValue;

        /// <summary>
        /// Last volume value set in the audio menu, on the old 0-20 scale.
        /// Kept so that older GaGa versions can still read the file.
        /// </summary>
        public Int32 LastVolumeTrackBarValue;

        /// <summary>
        /// Last volume set in the audio menu, 0-100.
        /// Optional: -1 when the file comes from an older version.
        /// </summary>
        [OptionalField]
        public Int32 LastVolumePercent;

        /// <summary>
        /// Last stream played.
        /// </summary>
        public PlayerStream LastPlayerStream;

        /// <summary>
        /// Whether the enable auto play options is checked.
        /// </summary>
        public Boolean OptionsEnableAutoPlayChecked;

        /// <summary>
        /// Whether the multimedia keys option is checked.
        /// </summary>
        public Boolean OptionsEnableMultimediaKeysChecked;

        /// <summary>
        /// Whether the track change notifications option is checked.
        /// Optional: settings files from older versions don't have it.
        /// </summary>
        [OptionalField]
        public Boolean OptionsEnableTrackNotificationsChecked;

        /// <summary>
        /// Whether to look for a newer GaGa on startup.
        /// Optional: settings files from older versions don't have it.
        /// </summary>
        [OptionalField]
        public Boolean OptionsCheckForUpdatesChecked;

        /// <summary>
        /// Menu theme: 0 follows Windows, 1 light, 2 dark.
        /// Optional: 0 for files from older versions.
        /// </summary>
        [OptionalField]
        public Int32 OptionsTheme;

        /// <summary>
        /// Stores program settings.
        /// </summary>
        public GaGaSettings()
        {
            LastBalanceTrackBarValue = 0;
            LastVolumeTrackBarValue = 10;
            LastVolumePercent = 50;
            LastPlayerStream = null;
            OptionsEnableAutoPlayChecked = false;
            OptionsEnableMultimediaKeysChecked = true;
            OptionsEnableTrackNotificationsChecked = true;
            OptionsCheckForUpdatesChecked = true;
        }

        /// <summary>
        /// Volume, 0-100, from whichever field the file has.
        /// </summary>
        public Int32 VolumePercent
        {
            get { return (LastVolumePercent >= 0) ? LastVolumePercent : LastVolumeTrackBarValue * 5; }
        }

        /// <summary>
        /// Defaults for fields missing in older settings files.
        /// </summary>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            OptionsEnableTrackNotificationsChecked = true;
            OptionsCheckForUpdatesChecked = true;
            LastVolumePercent = -1;
        }
    }
}

