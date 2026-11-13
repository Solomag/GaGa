// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.Collections.Generic;
using System.IO;
using System.Text;


namespace GaGa.NotifyIconPlayer
{
    /// <summary>
    /// Moves raw AAC (ADTS frames, as radio stations send it) into an .m4a
    /// file, which more players and phones understand and can seek in.
    /// The audio is copied as is: no re-encoding, no quality change.
    /// </summary>
    internal static class AacToM4a
    {
        private static readonly Int32[] SampleRates =
        {
            96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350
        };

        /// <summary>
        /// Convert an .aac file. False (and nothing written) when it isn't ADTS AAC.
        /// </summary>
        public static Boolean Convert(String aacPath, String m4aPath, String title)
        {
            Byte[] data = File.ReadAllBytes(aacPath);

            // the frames: where the audio of each one is, and how long it is:
            List<Int32> offsets = new List<Int32>();
            List<Int32> sizes = new List<Int32>();
            Int32 profile = -1, rateIndex = -1, channels = -1;

            Int32 at = 0;
            while (at + 7 <= data.Length)
            {
                // resync on garbage (a cut at the start of the recording):
                if (data[at] != 0xFF || (data[at + 1] & 0xF6) != 0xF0)
                {
                    at++;
                    continue;
                }

                Boolean noCrc = (data[at + 1] & 0x01) != 0;
                Int32 headerLength = noCrc ? 7 : 9;
                Int32 frameLength = ((data[at + 3] & 0x03) << 11) | (data[at + 4] << 3) | (data[at + 5] >> 5);

                if (frameLength <= headerLength || at + frameLength > data.Length)
                {
                    if (at + frameLength > data.Length)
                        break;      // cut at the end of the recording

                    at++;
                    continue;
                }

                Int32 p = (data[at + 2] >> 6) & 0x03;
                Int32 r = (data[at + 2] >> 2) & 0x0F;
                Int32 c = ((data[at + 2] & 0x01) << 2) | (data[at + 3] >> 6);

                if (profile < 0)
                {
                    if (r >= SampleRates.Length)
                    {
                        at++;
                        continue;
                    }

                    profile = p;
                    rateIndex = r;
                    channels = c;
                }

                // keep only frames of the same format:
                if (p == profile && r == rateIndex && c == channels)
                {
                    offsets.Add(at + headerLength);
                    sizes.Add(frameLength - headerLength);
                }

                at += frameLength;
            }

            if (offsets.Count == 0)
                return false;

            Int32 sampleRate = SampleRates[rateIndex];

            using (FileStream file = new FileStream(m4aPath, FileMode.CreateNew, FileAccess.Write))
            {
                BinaryWriter w = new BinaryWriter(file);

                // ftyp:
                Byte[] ftyp = Box("ftyp", Concat(Ascii("M4A "), U32(0), Ascii("M4A "), Ascii("mp42"), Ascii("isom")));
                w.Write(ftyp);

                // mdat with the frames, back to back:
                Int64 audioBytes = 0;
                foreach (Int32 size in sizes)
                    audioBytes += size;

                Boolean largeMdat = audioBytes + 8 > UInt32.MaxValue;
                Int64 mdatStart = file.Position;

                if (largeMdat)
                {
                    w.Write(U32(1));
                    w.Write(Ascii("mdat"));
                    w.Write(U64((UInt64) (audioBytes + 16)));
                }
                else
                {
                    w.Write(U32((UInt32) (audioBytes + 8)));
                    w.Write(Ascii("mdat"));
                }

                Int64 firstChunk = file.Position;
                for (Int32 i = 0; i < offsets.Count; i++)
                    w.Write(data, offsets[i], sizes[i]);

                // moov, with one chunk holding every frame:
                UInt64 duration = (UInt64) offsets.Count * 1024;
                w.Write(Moov(sizes, firstChunk, sampleRate, duration, AudioSpecificConfig(profile + 1, rateIndex, channels), channels, title));
            }

            return true;
        }

        private static Byte[] AudioSpecificConfig(Int32 objectType, Int32 rateIndex, Int32 channels)
        {
            Int32 bits = (objectType << 11) | (rateIndex << 7) | (channels << 3);
            return new Byte[] { (Byte) (bits >> 8), (Byte) bits };
        }

        private static Byte[] Moov(List<Int32> sizes, Int64 firstChunk, Int32 sampleRate, UInt64 duration,
                                   Byte[] asc, Int32 channels, String title)
        {
            UInt32 dur32 = (UInt32) Math.Min(duration, UInt32.MaxValue);
            Byte[] matrix = Concat(U32(0x00010000), U32(0), U32(0), U32(0), U32(0x00010000), U32(0), U32(0), U32(0), U32(0x40000000));

            Byte[] mvhd = FullBox("mvhd", 0, 0, Concat(
                U32(0), U32(0), U32((UInt32) sampleRate), U32(dur32),
                U32(0x00010000), U16(0x0100), new Byte[10], matrix, new Byte[24], U32(2)));

            Byte[] tkhd = FullBox("tkhd", 0, 3, Concat(
                U32(0), U32(0), U32(1), U32(0), U32(dur32),
                new Byte[8], U16(0), U16(0), U16(0x0100), U16(0), matrix, U32(0), U32(0)));

            Byte[] mdhd = FullBox("mdhd", 0, 0, Concat(
                U32(0), U32(0), U32((UInt32) sampleRate), U32(dur32), U16(0x55C4), U16(0)));

            Byte[] hdlr = FullBox("hdlr", 0, 0, Concat(U32(0), Ascii("soun"), new Byte[12], Ascii("SoundHandler"), new Byte[1]));

            Byte[] smhd = FullBox("smhd", 0, 0, Concat(U16(0), U16(0)));
            Byte[] dinf = Box("dinf", FullBox("dref", 0, 0, Concat(U32(1), FullBox("url ", 0, 1, new Byte[0]))));

            // esds: ES descriptor > decoder config (AAC) > the AudioSpecificConfig:
            Byte[] decSpecific = Descriptor(0x05, asc);
            Byte[] decConfig = Descriptor(0x04, Concat(new Byte[] { 0x40, 0x15, 0, 0x18, 0 }, U32(0), U32(0), decSpecific));
            Byte[] esDescriptor = Descriptor(0x03, Concat(U16(1), new Byte[] { 0 }, decConfig, Descriptor(0x06, new Byte[] { 0x02 })));
            Byte[] esds = FullBox("esds", 0, 0, esDescriptor);

            Byte[] mp4a = Box("mp4a", Concat(
                new Byte[6], U16(1),                // reserved, data reference index
                new Byte[8], U16((UInt16) Math.Max(1, channels)), U16(16), U16(0), U16(0),
                U32((UInt32) Math.Min(sampleRate, 65535) << 16), esds));

            Byte[] stsd = FullBox("stsd", 0, 0, Concat(U32(1), mp4a));
            Byte[] stts = FullBox("stts", 0, 0, Concat(U32(1), U32((UInt32) sizes.Count), U32(1024)));
            Byte[] stsc = FullBox("stsc", 0, 0, Concat(U32(1), U32(1), U32((UInt32) sizes.Count), U32(1)));

            MemoryStream sizeTable = new MemoryStream();
            foreach (Int32 size in sizes)
                sizeTable.Write(U32((UInt32) size), 0, 4);
            Byte[] stsz = FullBox("stsz", 0, 0, Concat(U32(0), U32((UInt32) sizes.Count), sizeTable.ToArray()));

            Byte[] chunkOffset = (firstChunk > UInt32.MaxValue)
                ? FullBox("co64", 0, 0, Concat(U32(1), U64((UInt64) firstChunk)))
                : FullBox("stco", 0, 0, Concat(U32(1), U32((UInt32) firstChunk)));

            Byte[] stbl = Box("stbl", Concat(stsd, stts, stsc, stsz, chunkOffset));
            Byte[] minf = Box("minf", Concat(smhd, dinf, stbl));
            Byte[] mdia = Box("mdia", Concat(mdhd, hdlr, minf));
            Byte[] trak = Box("trak", Concat(tkhd, mdia));

            Byte[] udta = new Byte[0];
            if (!String.IsNullOrEmpty(title))
            {
                // iTunes-style title tag, shown by most players:
                Byte[] text = Encoding.UTF8.GetBytes(title);
                Byte[] nam = Box("©nam", Box("data", Concat(U32(1), U32(0), text)));
                Byte[] ilst = Box("ilst", nam);
                Byte[] metaHdlr = FullBox("hdlr", 0, 0, Concat(U32(0), Ascii("mdir"), Ascii("appl"), new Byte[9]));
                udta = Box("udta", FullBox("meta", 0, 0, Concat(metaHdlr, ilst)));
            }

            return Box("moov", Concat(mvhd, trak, udta));
        }

        // boxes and descriptors:

        private static Byte[] Box(String type, Byte[] content)
        {
            return Concat(U32((UInt32) (8 + content.Length)), Latin1(type), content);
        }

        private static Byte[] FullBox(String type, Byte version, UInt32 flags, Byte[] content)
        {
            return Box(type, Concat(new Byte[] { version, (Byte) (flags >> 16), (Byte) (flags >> 8), (Byte) flags }, content));
        }

        private static Byte[] Descriptor(Byte tag, Byte[] content)
        {
            // size in the 4-byte form, which every reader accepts:
            Int32 n = content.Length;
            return Concat(new Byte[] { tag, (Byte) (0x80 | ((n >> 21) & 0x7F)), (Byte) (0x80 | ((n >> 14) & 0x7F)), (Byte) (0x80 | ((n >> 7) & 0x7F)), (Byte) (n & 0x7F) }, content);
        }

        private static Byte[] Ascii(String text)
        {
            return Encoding.ASCII.GetBytes(text);
        }

        private static Byte[] Latin1(String text)
        {
            Byte[] bytes = new Byte[text.Length];
            for (Int32 i = 0; i < text.Length; i++)
                bytes[i] = (Byte) text[i];
            return bytes;
        }

        private static Byte[] U16(UInt16 v)
        {
            return new Byte[] { (Byte) (v >> 8), (Byte) v };
        }

        private static Byte[] U32(UInt32 v)
        {
            return new Byte[] { (Byte) (v >> 24), (Byte) (v >> 16), (Byte) (v >> 8), (Byte) v };
        }

        private static Byte[] U64(UInt64 v)
        {
            return Concat(U32((UInt32) (v >> 32)), U32((UInt32) v));
        }

        private static Byte[] Concat(params Byte[][] parts)
        {
            Int32 length = 0;
            foreach (Byte[] part in parts)
                length += part.Length;

            Byte[] result = new Byte[length];
            Int32 at = 0;
            foreach (Byte[] part in parts)
            {
                Array.Copy(part, 0, result, at, part.Length);
                at += part.Length;
            }
            return result;
        }
    }
}
