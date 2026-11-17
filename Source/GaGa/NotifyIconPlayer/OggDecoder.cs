
// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.IO;
using System.Text;


namespace GaGa.NotifyIconPlayer
{
    /// <summary>
    /// Decodes an Ogg Vorbis or Ogg Opus stream into PCM and writes it as an endless WAV,
    /// which the WPF MediaPlayer plays (it can't read Ogg at all).
    /// Packets come from OggFlacRemuxer, which already splits the Ogg pages.
    ///
    /// Radio streams are chained: every new track starts a new logical stream with
    /// its own headers (and maybe another sample rate). The WAV format is fixed by
    /// the first stream: 16-bit stereo; other rates are resampled to it.
    ///
    /// Decoders: NVorbis (MIT) and Concentus (BSD), embedded in the exe (see Program).
    /// </summary>
    internal class OggDecoder
    {
        private readonly Func<Byte[]> nextPacket;
        private Byte[] pending;

        // output format, fixed by the first stream (kept across reconnections):
        private Int32 outputRate;

        // linear resampling state, when a stream has another rate:
        private Double resamplePosition;
        private Single lastLeft, lastRight;

        public OggDecoder(Func<Byte[]> nextPacket, Byte[] firstPacket)
        {
            this.nextPacket = nextPacket;
            this.pending = firstPacket;
        }

        /// <summary>
        /// Keep the output format of a previous decoder (after a reconnection
        /// the player is still reading the same WAV).
        /// </summary>
        public void ContinueFrom(OggDecoder previous)
        {
            if (previous != null)
                outputRate = previous.outputRate;
        }

        public static Boolean IsVorbisHeader(Byte[] p)
        {
            return p != null && p.Length >= 7 && p[0] == 1 && Encoding.ASCII.GetString(p, 1, 6) == "vorbis";
        }

        public static Boolean IsOpusHeader(Byte[] p)
        {
            return p != null && p.Length >= 19 && Encoding.ASCII.GetString(p, 0, 8) == "OpusHead";
        }

        /// <summary>
        /// Whether a stream starting with this packet can be decoded.
        /// </summary>
        public static Boolean CanDecode(Byte[] first)
        {
            return IsVorbisHeader(first) || IsOpusHeader(first);
        }

        private Byte[] Next()
        {
            if (pending != null)
            {
                Byte[] p = pending;
                pending = null;
                return p;
            }
            return nextPacket();
        }

        /// <summary>
        /// Decode until the input ends or we are stopped.
        /// The WAV header is written only when writeHeader is true.
        /// </summary>
        public void CopyWavTo(Stream output, Func<Boolean> stopped, Action<String> onTitle, Boolean writeHeader)
        {
            Boolean headerDone = !writeHeader;

            while (!stopped())
            {
                Byte[] first = Next();
                if (first == null)
                    return;

                // skip anything until the start of a logical stream:
                Int32 rate;
                if (IsOpusHeader(first))
                    rate = 48000;
                else if (IsVorbisHeader(first))
                    rate = ReadInt32LE(first, 12);
                else
                    continue;

                if (outputRate == 0)
                    outputRate = (rate >= 8000 && rate <= 192000) ? rate : 48000;

                if (!headerDone)
                {
                    WriteWavHeader(output, outputRate);
                    headerDone = true;
                }

                if (IsOpusHeader(first))
                    DecodeOpus(first, output, stopped, onTitle);
                else
                    DecodeVorbis(first, output, stopped, onTitle, rate);
            }
        }

        ///
        /// Opus
        ///

        private void DecodeOpus(Byte[] head, Stream output, Func<Boolean> stopped, Action<String> onTitle)
        {
            Int32 channels = Math.Max(1, (Int32) head[9]);
            Concentus.Structs.OpusDecoder decoder = new Concentus.Structs.OpusDecoder(48000, channels > 1 ? 2 : 1);
            Int32 decodedChannels = channels > 1 ? 2 : 1;

            // up to 120 ms at 48 kHz:
            Int16[] pcm = new Int16[5760 * decodedChannels];

            while (!stopped())
            {
                Byte[] p = Next();
                if (p == null)
                    return;

                // a new logical stream (next track): let the caller start over
                if (CanDecode(p))
                {
                    pending = p;
                    return;
                }

                if (p.Length >= 8 && Encoding.ASCII.GetString(p, 0, 8) == "OpusTags")
                {
                    String title = OggFlacRemuxer.ParseVorbisComment(p, 8);
                    if (title != null && onTitle != null)
                        onTitle(title);
                    continue;
                }

                Int32 samples;
                try
                {
                    samples = decoder.Decode(p, 0, p.Length, pcm, 0, 5760, false);
                }
                catch (Exception)
                {
                    // a damaged packet: skip it
                    continue;
                }

                WriteSamples(output, pcm, samples, decodedChannels, 48000);
            }
        }

        ///
        /// Vorbis
        ///

        private void DecodeVorbis(Byte[] ident, Stream output, Func<Boolean> stopped, Action<String> onTitle, Int32 rate)
        {
            PacketSource source = new PacketSource(this, ident, onTitle);

            NVorbis.VorbisReader reader;
            try
            {
                reader = new NVorbis.VorbisReader(source);
            }
            catch (Exception)
            {
                // broken headers: wait for the next stream
                return;
            }

            using (reader)
            {
                Int32 channels = reader.Channels;
                Single[] buffer = new Single[4096 * channels];
                Int16[] pcm = new Int16[4096 * 2];

                while (!stopped())
                {
                    Int32 read;
                    try
                    {
                        read = reader.ReadSamples(buffer, 0, buffer.Length);
                    }
                    catch (Exception)
                    {
                        // a damaged packet: go on with the next ones
                        if (source.Ended)
                            return;
                        continue;
                    }

                    if (read <= 0)
                        return;

                    Int32 frames = read / channels;
                    for (Int32 i = 0; i < frames; i++)
                    {
                        Single left = buffer[i * channels];
                        Single right = channels > 1 ? buffer[i * channels + 1] : left;
                        pcm[i * 2] = ToInt16(left);
                        pcm[i * 2 + 1] = ToInt16(right);
                    }

                    WriteSamples(output, pcm, frames, 2, rate);
                }
            }
        }

        /// <summary>
        /// Feeds NVorbis the packets of one logical stream; stops at the next one.
        /// </summary>
        private class PacketSource : NVorbis.IPacketProvider
        {
            private readonly OggDecoder owner;
            private readonly Action<String> onTitle;
            private Byte[] first;
            private Byte[] peeked;
            private Int32 headers;

            public Boolean Ended;

            public PacketSource(OggDecoder owner, Byte[] ident, Action<String> onTitle)
            {
                this.owner = owner;
                this.first = ident;
                this.onTitle = onTitle;
            }

            private Byte[] Take()
            {
                if (first != null)
                {
                    Byte[] p = first;
                    first = null;
                    headers++;
                    return p;
                }

                if (Ended)
                    return null;

                Byte[] next = owner.Next();
                if (next == null || CanDecode(next))
                {
                    // the next track: leave it for the caller
                    owner.pending = next;
                    Ended = true;
                    return null;
                }

                // the comment header: tags of the current track
                if (headers < 3)
                {
                    headers++;
                    if (next.Length > 7 && next[0] == 3 && onTitle != null)
                    {
                        String title = OggFlacRemuxer.ParseVorbisComment(next, 7);
                        if (title != null)
                            onTitle(title);
                    }
                }
                return next;
            }

            public NVorbis.DataPacket GetNextPacket()
            {
                Byte[] p;
                if (peeked != null)
                {
                    p = peeked;
                    peeked = null;
                }
                else
                {
                    p = Take();
                }
                return (p == null) ? null : new Packet(p);
            }

            public NVorbis.DataPacket PeekNextPacket()
            {
                if (peeked == null)
                    peeked = Take();
                return (peeked == null) ? null : new Packet(peeked);
            }

            public Int32 StreamSerial { get { return 0; } }
            public Boolean CanSeek { get { return false; } }
            public Int64 ContainerBits { get { return 0; } }
            public Int32 GetTotalPageCount() { throw new NotSupportedException(); }
            public NVorbis.DataPacket GetPacket(Int32 index) { throw new NotSupportedException(); }
            public Int64 GetGranuleCount() { throw new NotSupportedException(); }
            public NVorbis.DataPacket FindPacket(Int64 granulePos, Func<NVorbis.DataPacket, NVorbis.DataPacket, Int32> packetGranuleCountCallback) { throw new NotSupportedException(); }
            public void SeekToPacket(NVorbis.DataPacket packet, Int32 preRoll) { throw new NotSupportedException(); }
            public event EventHandler<NVorbis.ParameterChangeEventArgs> ParameterChange { add { } remove { } }
            public void Dispose() { }
        }

        private class Packet : NVorbis.DataPacket
        {
            private readonly Byte[] data;
            private Int32 position;

            public Packet(Byte[] data) : base(data.Length)
            {
                this.data = data;
                GranulePosition = -1;
            }

            protected override Int32 ReadNextByte()
            {
                return (position < data.Length) ? data[position++] : -1;
            }
        }

        ///
        /// WAV output
        ///

        private static Int16 ToInt16(Single value)
        {
            Int32 v = (Int32) (value * 32767f);
            return (Int16) Math.Max(-32768, Math.Min(32767, v));
        }

        private static Int32 ReadInt32LE(Byte[] p, Int32 at)
        {
            return p[at] | (p[at + 1] << 8) | (p[at + 2] << 16) | (p[at + 3] << 24);
        }

        /// <summary>
        /// "RIFF" header for a 16-bit stereo stream of unknown length.
        /// </summary>
        private static void WriteWavHeader(Stream output, Int32 rate)
        {
            MemoryStream m = new MemoryStream();
            BinaryWriter w = new BinaryWriter(m);
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(UInt32.MaxValue);
            w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);
            w.Write((Int16) 1);          // PCM
            w.Write((Int16) 2);          // stereo
            w.Write(rate);
            w.Write(rate * 4);           // bytes per second
            w.Write((Int16) 4);          // block align
            w.Write((Int16) 16);         // bits
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(UInt32.MaxValue);
            w.Flush();

            Byte[] header = m.ToArray();
            output.Write(header, 0, header.Length);
        }

        /// <summary>
        /// Write interleaved 16-bit samples (mono or stereo) as stereo at the output rate.
        /// </summary>
        private void WriteSamples(Stream output, Int16[] pcm, Int32 frames, Int32 channels, Int32 rate)
        {
            if (frames <= 0)
                return;

            Byte[] bytes;
            Int32 count = 0;

            if (rate == outputRate)
            {
                bytes = new Byte[frames * 4];
                for (Int32 i = 0; i < frames; i++)
                {
                    Int16 left = pcm[i * channels];
                    Int16 right = channels > 1 ? pcm[i * channels + 1] : left;
                    bytes[count++] = (Byte) left;
                    bytes[count++] = (Byte) (left >> 8);
                    bytes[count++] = (Byte) right;
                    bytes[count++] = (Byte) (right >> 8);
                }
            }
            else
            {
                // linear interpolation between input frames:
                Double step = (Double) rate / outputRate;
                bytes = new Byte[(Int32) (frames / step + 2) * 4];

                while (resamplePosition < frames)
                {
                    Int32 index = (Int32) resamplePosition;
                    Double fraction = resamplePosition - index;

                    Single l0 = (index == 0) ? lastLeft : pcm[(index - 1) * channels];
                    Single r0 = (index == 0) ? lastRight : (channels > 1 ? pcm[(index - 1) * channels + 1] : l0);
                    Single l1 = pcm[index * channels];
                    Single r1 = channels > 1 ? pcm[index * channels + 1] : l1;

                    Int16 left = (Int16) (l0 + (l1 - l0) * fraction);
                    Int16 right = (Int16) (r0 + (r1 - r0) * fraction);

                    if (count + 4 > bytes.Length)
                        break;

                    bytes[count++] = (Byte) left;
                    bytes[count++] = (Byte) (left >> 8);
                    bytes[count++] = (Byte) right;
                    bytes[count++] = (Byte) (right >> 8);

                    resamplePosition += step;
                }

                resamplePosition -= frames;
                lastLeft = pcm[(frames - 1) * channels];
                lastRight = channels > 1 ? pcm[(frames - 1) * channels + 1] : lastLeft;
            }

            output.Write(bytes, 0, count);
        }
    }
}
