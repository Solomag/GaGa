
// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;


namespace GaGa
{
    internal class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            // the Ogg Vorbis/Opus decoders are embedded in the exe (GaGa is a single file):
            AppDomain.CurrentDomain.AssemblyResolve += LoadEmbeddedAssembly;

            // default path for the settings and the streams file:
            String currentFolder = Util.ApplicationFolder;
            String settingsFilepath = Path.Combine(currentFolder, "GaGa.dat");
            String streamsFilepath = Path.Combine(currentFolder, "Streams.ini");

            // one GaGa per user session: a second copy (started twice with Windows,
            // a double click while it runs) just leaves. After an update the new exe
            // starts while the old one is still closing, so wait for it a little:
            using (Mutex single = new Mutex(false, @"Local\GaGa-Radio-Player-SingleInstance"))
            {
                if (!Acquire(single, TimeSpan.FromSeconds(15)))
                    return;

                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new GaGa(settingsFilepath, streamsFilepath));
                }
                finally
                {
                    single.ReleaseMutex();
                }
            }
        }

        private static Boolean Acquire(Mutex mutex, TimeSpan timeout)
        {
            try
            {
                return mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                // the previous GaGa crashed or was killed: it's ours now
                return true;
            }
        }

        /// <summary>
        /// Load "NVorbis" or "Concentus" from the resources of the exe.
        /// </summary>
        private static Assembly LoadEmbeddedAssembly(Object sender, ResolveEventArgs args)
        {
            String name = new AssemblyName(args.Name).Name;
            if (name != "NVorbis" && name != "Concentus")
                return null;

            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GaGa.Libraries." + name + ".dll"))
            {
                if (stream == null)
                    return null;

                Byte[] data = new Byte[stream.Length];
                stream.Read(data, 0, data.Length);
                return Assembly.Load(data);
            }
        }
    }
}

