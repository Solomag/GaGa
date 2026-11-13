// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;


namespace GaGa.NotifyIconPlayer
{
    /// <summary>
    /// Turns an HLS radio stream (an .m3u8 playlist of short segments)
    /// into one continuous audio stream that the MediaPlayer can play:
    ///   - follows a master playlist to its first audio variant,
    ///   - downloads the segments as they appear,
    ///   - unwraps MPEG-TS segments to their ADTS AAC or MP3 audio,
    ///     and packed audio segments (.aac/.mp3) by dropping their ID3 headers,
    ///   - reports track titles from the ID3 tags (TPE1 - TIT2).
    /// Encrypted (EXT-X-KEY) and fragmented MP4 (EXT-X-MAP) streams are not supported.
    /// </summary>
    internal class HlsStream : Stream
    {
        private const String UserAgent = "GaGa/1.0 (Windows; radio player)";

        private readonly Action<String> onTitle;
        private readonly Func<Boolean> stopped;

        private Uri playlistUri;
        private Double targetDuration = 6;
        private Boolean ended;

        private Int64 nextSequence = -1;
        private readonly Queue<KeyValuePair<Int64, Uri>> pending = new Queue<KeyValuePair<Int64, Uri>>();

        private Byte[] output = new Byte[0];
        private Int32 outputOffset;

        private String lastTitle;

        /// <summary>
        /// "audio/aac" or "audio/mpeg", known after Start().
        /// </summary>
        public String ContentType { get; private set; }

        public HlsStream(Uri playlistUri, Action<String> onTitle, Func<Boolean> stopped)
        {
            this.playlistUri = playlistUri;
            this.onTitle = onTitle;
            this.stopped = stopped;
        }

        /// <summary>
        /// Whether a playlist is HLS rather than a list of stream addresses.
        /// </summary>
        public static Boolean IsHls(String text)
        {
            return text.Contains("#EXT-X-");
        }

        ///
        /// Downloading
        ///

        private static Byte[] Download(Uri uri)
        {
            HttpWebRequest request = (HttpWebRequest) WebRequest.Create(uri);
            request.UserAgent = UserAgent;
            request.Accept = "*/*";
            request.Timeout = 15000;
            request.ReadWriteTimeout = 30000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (WebResponse response = request.GetResponse())
            using (Stream body = response.GetResponseStream())
            using (MemoryStream memory = new MemoryStream())
            {
                body.CopyTo(memory);
                return memory.ToArray();
            }
        }

        private static String DownloadText(Uri uri)
        {
            Byte[] data = Download(uri);
            return Encoding.UTF8.GetString(data).TrimStart('﻿');
        }

        /// <summary>
        /// Read the first playlist and the first segment, so that the format
        /// is known before the player is answered. Throws when it can't be played.
        /// </summary>
        public void Start(String firstPlaylistText)
        {
            String text = firstPlaylistText;

            // master playlist: follow the first variant (all carry the same audio):
            for (Int32 depth = 0; text.Contains("#EXT-X-STREAM-INF") && depth < 3; depth++)
            {
                Uri variant = FirstVariant(text, playlistUri);
                if (variant == null)
                    throw new IOException("HLS: no stream in the master playlist.");

                playlistUri = variant;
                text = DownloadText(playlistUri);
            }

            ParseMediaPlaylist(text, true);

            if (!Fill())
                throw new IOException("HLS: no audio in the stream.");
        }

        internal static Uri FirstVariant(String text, Uri baseUri)
        {
            Boolean next = false;
            foreach (String raw in text.Split('\n'))
            {
                String line = raw.Trim();
                if (line.StartsWith("#EXT-X-STREAM-INF"))
                {
                    next = true;
                    continue;
                }

                Uri uri;
                if (next && line.Length > 0 && !line.StartsWith("#") && Uri.TryCreate(baseUri, line, out uri))
                    return uri;
            }
            return null;
        }

        /// <summary>
        /// Queue the segments we haven't seen yet.
        /// On the first read, start a few segments before the live edge.
        /// </summary>
        private void ParseMediaPlaylist(String text, Boolean first)
        {
            if (text.Contains("#EXT-X-KEY") && !text.Contains("METHOD=NONE"))
                throw new IOException("HLS: encrypted streams are not supported.");
            if (text.Contains("#EXT-X-MAP"))
                throw new IOException("HLS: fragmented MP4 streams are not supported.");

            Int64 sequence = 0;
            List<KeyValuePair<Int64, Uri>> segments = new List<KeyValuePair<Int64, Uri>>();

            foreach (String raw in text.Split('\n'))
            {
                String line = raw.Trim();

                if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:"))
                    Int64.TryParse(line.Substring(22), out sequence);
                else if (line.StartsWith("#EXT-X-TARGETDURATION:"))
                    Double.TryParse(line.Substring(22), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out targetDuration);
                else if (line.StartsWith("#EXT-X-ENDLIST"))
                    ended = true;
                else if (line.Length > 0 && !line.StartsWith("#"))
                {
                    Uri uri;
                    if (Uri.TryCreate(playlistUri, line, out uri))
                        segments.Add(new KeyValuePair<Int64, Uri>(sequence + segments.Count, uri));
                }
            }

            Int32 start = 0;
            if (first && !ended)
                start = Math.Max(0, segments.Count - 3);

            for (Int32 i = start; i < segments.Count; i++)
            {
                if (segments[i].Key >= nextSequence)
                {
                    pending.Enqueue(segments[i]);
                    nextSequence = segments[i].Key + 1;
                }
            }
        }

        /// <summary>
        /// Get audio for the next reads: download the next segment,
        /// reloading the playlist (and waiting for it to grow) when needed.
        /// False at the end of the stream.
        /// </summary>
        private Boolean Fill()
        {
            Int32 failures = 0;

            while (!stopped())
            {
                if (pending.Count == 0)
                {
                    if (ended)
                        return false;

                    try
                    {
                        ParseMediaPlaylist(DownloadText(playlistUri), false);
                        failures = 0;
                    }
                    catch (WebException)
                    {
                        if (++failures >= 5)
                            throw;
                    }

                    if (pending.Count == 0)
                    {
                        // nothing new yet: wait for about half a segment
                        Thread.Sleep((Int32) Math.Max(500, Math.Min(10000, targetDuration * 500)));
                    }
                    continue;
                }

                KeyValuePair<Int64, Uri> segment = pending.Dequeue();

                Byte[] data;
                try
                {
                    data = Download(segment.Value);
                }
                catch (WebException)
                {
                    // a missing segment is a gap, not the end:
                    if (++failures >= 5)
                        throw;
                    continue;
                }

                Byte[] audio = Unwrap(data);
                if (audio.Length > 0)
                {
                    output = audio;
                    outputOffset = 0;
                    return true;
                }
            }

            return false;
        }

        ///
        /// Segments
        ///

        private Byte[] Unwrap(Byte[] data)
        {
            if (data.Length >= 188 && data[0] == 0x47)
                return DemuxTransportStream(data);

            // packed audio: ID3 tags (timestamps, titles) followed by ADTS or MP3 frames
            Int32 offset = 0;
            while (data.Length - offset >= 10 && data[offset] == 'I' && data[offset + 1] == 'D' && data[offset + 2] == '3')
            {
                Int32 size = SyncSafe(data, offset + 6) + 10;
                ReadId3Title(data, offset, Math.Min(size, data.Length - offset));
                offset += size;
            }

            if (offset >= data.Length)
                return new Byte[0];

            Byte[] audio = new Byte[data.Length - offset];
            Array.Copy(data, offset, audio, 0, audio.Length);
            SetFormat(audio, 0);
            return audio;
        }

        /// <summary>
        /// The format of the first audio frame: ADTS AAC or MPEG audio.
        /// </summary>
        private void SetFormat(Byte[] audio, Int32 offset)
        {
            if (ContentType != null || audio.Length - offset < 2 || audio[offset] != 0xFF)
                return;

            // ADTS: layer bits are 00
            ContentType = ((audio[offset + 1] & 0x06) == 0) ? "audio/aac" : "audio/mpeg";
        }

        /// <summary>
        /// MPEG-TS: collect the PES payload of the audio stream
        /// (stream id 0xC0-0xDF), and read titles from the ID3 metadata stream.
        /// </summary>
        private Byte[] DemuxTransportStream(Byte[] data)
        {
            MemoryStream audio = new MemoryStream();
            Dictionary<Int32, MemoryStream> metadata = new Dictionary<Int32, MemoryStream>();
            Dictionary<Int32, Boolean> isAudio = new Dictionary<Int32, Boolean>();

            for (Int32 p = 0; p + 188 <= data.Length; p += 188)
            {
                if (data[p] != 0x47)
                {
                    // lost sync: find the next packet start
                    Int32 next = Array.IndexOf<Byte>(data, 0x47, p + 1);
                    if (next < 0)
                        break;
                    p = next - 188;
                    continue;
                }

                Int32 pid = ((data[p + 1] & 0x1F) << 8) | data[p + 2];
                Boolean unitStart = (data[p + 1] & 0x40) != 0;
                Int32 adaptation = (data[p + 3] >> 4) & 0x03;

                if (pid == 0 || pid == 0x1FFF || (adaptation & 0x01) == 0)
                    continue;

                Int32 start = p + 4;
                if ((adaptation & 0x02) != 0)
                    start += 1 + data[p + 4];

                Int32 end = p + 188;
                if (start >= end)
                    continue;

                if (unitStart)
                {
                    // PES header: 00 00 01 <stream id> <length:2> <flags:2> <header length:1>
                    if (end - start < 9 || data[start] != 0 || data[start + 1] != 0 || data[start + 2] != 1)
                    {
                        isAudio.Remove(pid);
                        continue;
                    }

                    Byte streamId = data[start + 3];
                    Int32 payload = start + 9 + data[start + 8];
                    Boolean audioStream = streamId >= 0xC0 && streamId <= 0xDF;
                    Boolean metadataStream = streamId == 0xBD || streamId == 0xFC;

                    // a new metadata unit: read the previous one
                    MemoryStream previous;
                    if (metadata.TryGetValue(pid, out previous))
                    {
                        ReadId3Title(previous.ToArray(), 0, (Int32) previous.Length);
                        metadata.Remove(pid);
                    }

                    if (audioStream)
                        isAudio[pid] = true;
                    else
                        isAudio.Remove(pid);

                    if (metadataStream)
                        metadata[pid] = new MemoryStream();

                    start = payload;
                    if (start >= end)
                        continue;
                }

                if (isAudio.ContainsKey(pid))
                {
                    if (audio.Length == 0)
                        SetFormat(data, start);
                    audio.Write(data, start, end - start);
                }
                else
                {
                    MemoryStream unit;
                    if (metadata.TryGetValue(pid, out unit))
                        unit.Write(data, start, end - start);
                }
            }

            foreach (MemoryStream unit in metadata.Values)
                ReadId3Title(unit.ToArray(), 0, (Int32) unit.Length);

            return audio.ToArray();
        }

        ///
        /// ID3
        ///

        private static Int32 SyncSafe(Byte[] data, Int32 at)
        {
            return ((data[at] & 0x7F) << 21) | ((data[at + 1] & 0x7F) << 14) | ((data[at + 2] & 0x7F) << 7) | (data[at + 3] & 0x7F);
        }

        /// <summary>
        /// Report "Artist - Title" from an ID3v2 tag, when it has them.
        /// </summary>
        private void ReadId3Title(Byte[] data, Int32 offset, Int32 length)
        {
            String title = ParseId3Title(data, offset, length);
            if (title != null && title != lastTitle)
            {
                lastTitle = title;
                if (onTitle != null)
                    onTitle(title);
            }
        }

        internal static String ParseId3Title(Byte[] data, Int32 offset, Int32 length)
        {
            Int32 end = offset + length;
            if (length < 10 || data[offset] != 'I' || data[offset + 1] != 'D' || data[offset + 2] != '3')
                return null;

            Int32 version = data[offset + 3];
            end = Math.Min(end, offset + 10 + SyncSafe(data, offset + 6));

            String artist = null;
            String title = null;

            Int32 at = offset + 10;
            while (at + 10 <= end)
            {
                String id = Encoding.ASCII.GetString(data, at, 4);
                if (id[0] == '\0')
                    break;

                Int32 size = (version >= 4)
                    ? SyncSafe(data, at + 4)
                    : (data[at + 4] << 24) | (data[at + 5] << 16) | (data[at + 6] << 8) | data[at + 7];

                Int32 body = at + 10;
                if (size <= 0 || body + size > end)
                    break;

                if (id == "TIT2")
                    title = Id3Text(data, body, size);
                else if (id == "TPE1")
                    artist = Id3Text(data, body, size);

                at = body + size;
            }

            if (String.IsNullOrEmpty(title))
                return null;

            return String.IsNullOrEmpty(artist) ? title : (artist + " - " + title);
        }

        private static String Id3Text(Byte[] data, Int32 at, Int32 size)
        {
            if (size < 2)
                return null;

            Encoding encoding;
            switch (data[at])
            {
                case 1: encoding = Encoding.Unicode; break;          // UTF-16 with BOM
                case 2: encoding = Encoding.BigEndianUnicode; break; // UTF-16BE
                case 3: encoding = Encoding.UTF8; break;
                default: encoding = Encoding.GetEncoding(28591); break; // ISO-8859-1
            }

            Int32 start = at + 1;
            Int32 count = size - 1;

            // UTF-16 with a byte order mark:
            if (data[at] == 1 && count >= 2)
            {
                if (data[start] == 0xFE && data[start + 1] == 0xFF)
                    encoding = Encoding.BigEndianUnicode;
                if ((data[start] == 0xFE && data[start + 1] == 0xFF) || (data[start] == 0xFF && data[start + 1] == 0xFE))
                {
                    start += 2;
                    count -= 2;
                }
            }

            return encoding.GetString(data, start, count).TrimEnd('\0').Trim();
        }

        ///
        /// Stream
        ///

        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count)
        {
            if (outputOffset >= output.Length && !Fill())
                return 0;

            Int32 n = Math.Min(count, output.Length - outputOffset);
            Array.Copy(output, outputOffset, buffer, offset, n);
            outputOffset += n;
            return n;
        }

        public override Boolean CanRead { get { return true; } }
        public override Boolean CanSeek { get { return false; } }
        public override Boolean CanWrite { get { return false; } }
        public override Int64 Length { get { throw new NotSupportedException(); } }
        public override Int64 Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }
        public override void Flush() { }
        public override Int64 Seek(Int64 offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(Int64 value) { throw new NotSupportedException(); }
        public override void Write(Byte[] buffer, Int32 offset, Int32 count) { throw new NotSupportedException(); }
    }
}
