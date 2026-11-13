// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.IO;


namespace GaGa.NotifyIconPlayer
{
    /// <summary>
    /// Saves the audio that the relay hands to the player into a file.
    /// The audio is written exactly as played: no re-encoding, ICY metadata
    /// already removed, Ogg FLAC already unwrapped to native FLAC.
    /// Thread-safe: the relay writes from its own threads.
    /// </summary>
    internal class StreamRecorder : IDisposable
    {
        private readonly Object sync = new Object();

        private String basePath;    // requested file, without extension (null: not recording)
        private String extension;   // known once the stream has started
        private Byte[] header;      // written first in every new file (FLAC STREAMINFO)
        private FileStream file;
        private Object writer;      // only the newest player connection is recorded

        /// <summary>
        /// Whether a recording was requested and hasn't failed.
        /// </summary>
        public Boolean IsRecording
        {
            get { lock (sync) return basePath != null; }
        }

        /// <summary>
        /// Path of the file being written, or null until the stream has started.
        /// </summary>
        public String FilePath { get; private set; }

        /// <summary>
        /// Last write error, if a recording stopped on its own.
        /// </summary>
        public String LastError { get; private set; }

        /// <summary>
        /// File extension for a stream content type.
        /// </summary>
        internal static String ExtensionFor(String contentType)
        {
            String type = (contentType ?? "").ToLowerInvariant();

            if (type.Contains("flac")) return ".flac";
            if (type.Contains("aac") || type.Contains("mp4")) return ".aac";
            if (type.Contains("ogg")) return ".ogg";
            if (type.Contains("opus")) return ".opus";
            if (type.Contains("wav")) return ".wav";
            return ".mp3";
        }

        /// <summary>
        /// Start recording into basePath + an extension that matches the stream.
        /// </summary>
        public void Start(String basePath)
        {
            lock (sync)
            {
                CloseFile();
                this.basePath = basePath;
                LastError = null;

                if (extension != null)
                    OpenFile();
            }
        }

        /// <summary>
        /// Stop recording and close the file.
        /// </summary>
        public void Stop()
        {
            lock (sync)
            {
                basePath = null;
                CloseFile();
            }
        }

        /// <summary>
        /// Called by the relay when the audio starts: the stream format,
        /// and the bytes that must start every file (or null).
        /// </summary>
        public void SetFormat(Object owner, String contentType, Byte[] header)
        {
            lock (sync)
            {
                writer = owner;
                extension = ExtensionFor(contentType);
                this.header = header;

                if (basePath != null && file == null)
                    OpenFile();
            }
        }

        /// <summary>
        /// Audio bytes, as sent to the player.
        /// </summary>
        public void Write(Object owner, Byte[] buffer, Int32 offset, Int32 count)
        {
            lock (sync)
            {
                if (file == null || owner != writer)
                    return;

                try
                {
                    file.Write(buffer, offset, count);
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }

        private void OpenFile()
        {
            try
            {
                String path = basePath + extension;
                for (Int32 n = 2; File.Exists(path); n++)
                    path = basePath + " (" + n + ")" + extension;

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                FilePath = path;

                if (header != null)
                    file.Write(header, 0, header.Length);
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private void Fail(Exception e)
        {
            LastError = e.Message;
            basePath = null;
            CloseFile();
        }

        private void CloseFile()
        {
            if (file != null)
            {
                try
                {
                    file.Dispose();
                }
                catch (Exception)
                {
                }
                file = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// Write-only stream that passes everything to the player connection
        /// and a copy to the recorder. With captureHeader, the first write
        /// (the FLAC header) is kept so that every new file starts with it.
        /// </summary>
        internal class Tee : Stream
        {
            private readonly Stream inner;
            private readonly StreamRecorder recorder;
            private readonly String contentType;
            private readonly Boolean captureHeader;
            private Boolean started;

            public Tee(Stream inner, StreamRecorder recorder, String contentType, Boolean captureHeader)
            {
                this.inner = inner;
                this.recorder = recorder;
                this.contentType = contentType;
                this.captureHeader = captureHeader;
            }

            public override void Write(Byte[] buffer, Int32 offset, Int32 count)
            {
                inner.Write(buffer, offset, count);

                if (!started)
                {
                    started = true;

                    if (captureHeader)
                    {
                        Byte[] header = new Byte[count];
                        Array.Copy(buffer, offset, header, 0, count);
                        recorder.SetFormat(this, contentType, header);
                        return;
                    }

                    recorder.SetFormat(this, contentType, null);
                }

                recorder.Write(this, buffer, offset, count);
            }

            public override Boolean CanRead { get { return false; } }
            public override Boolean CanSeek { get { return false; } }
            public override Boolean CanWrite { get { return true; } }
            public override Int64 Length { get { throw new NotSupportedException(); } }
            public override Int64 Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }
            public override void Flush() { inner.Flush(); }
            public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) { throw new NotSupportedException(); }
            public override Int64 Seek(Int64 offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(Int64 value) { throw new NotSupportedException(); }
        }
    }
}
