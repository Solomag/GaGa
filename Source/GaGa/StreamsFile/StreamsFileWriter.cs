
// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.Collections.Generic;
using System.IO;
using System.Text;


namespace GaGa.StreamsFile
{
    /// <summary>
    /// Adds streams to the streams file, keeping everything else
    /// (comments, order, formatting) exactly as the user wrote it.
    /// </summary>
    internal static class StreamsFileWriter
    {
        /// <summary>
        /// Make a station name safe as an INI key: no '=' (it splits key and value),
        /// no leading characters that would make the line a comment or a section.
        /// </summary>
        public static String CleanName(String name)
        {
            String clean = (name ?? "").Replace('=', '-').Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            clean = clean.TrimStart('#', ';', '[').Trim();
            return (clean.Length == 0) ? "Station" : clean;
        }

        private static String[] ReadLines(String filepath)
        {
            if (!File.Exists(filepath))
                return new String[0];

            // StreamReader detects the BOM; default to UTF-8 like the reader:
            String text = File.ReadAllText(filepath, Encoding.UTF8);
            return text.Replace("\r\n", "\n").Split('\n');
        }

        /// <summary>
        /// Whether the streams file already has a stream with this address.
        /// </summary>
        public static Boolean Contains(String filepath, Uri uri)
        {
            foreach (String raw in ReadLines(filepath))
            {
                String line = raw.Trim();
                if (line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("["))
                    continue;

                Int32 equals = line.IndexOf('=');
                if (equals < 0)
                    continue;

                Uri existing;
                if (Uri.TryCreate(line.Substring(equals + 1).Trim(), UriKind.Absolute, out existing) && existing == uri)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Add "name = uri" at the end of a section, creating the section
        /// at the end of the file when it doesn't exist yet
        /// (or before the first section, with newSectionFirst).
        /// </summary>
        public static void AddStream(String filepath, String section, String name, Uri uri, Boolean newSectionFirst = false)
        {
            List<String> lines = new List<String>(ReadLines(filepath));

            // drop the empty entry produced by a trailing newline:
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0)
                lines.RemoveAt(lines.Count - 1);

            String entry = CleanName(name) + " = " + uri.AbsoluteUri;
            String header = "[" + section + "]";

            Int32 sectionLine = lines.FindIndex(l => l.Trim().Equals(header, StringComparison.OrdinalIgnoreCase));
            Int32 firstSection = lines.FindIndex(l => l.Trim().StartsWith("[") && l.Trim().EndsWith("]"));

            if (sectionLine < 0 && newSectionFirst && firstSection >= 0)
            {
                // keep any comments on top of the file above the new section:
                lines.InsertRange(firstSection, new String[] { header, entry, "" });
            }
            else if (sectionLine < 0)
            {
                if (lines.Count > 0)
                    lines.Add("");
                lines.Add(header);
                lines.Add(entry);
            }
            else
            {
                // after the last non-empty line of the section:
                Int32 insertAt = sectionLine + 1;
                for (Int32 i = sectionLine + 1; i < lines.Count; i++)
                {
                    String line = lines[i].Trim();
                    if (line.StartsWith("[") && line.EndsWith("]"))
                        break;
                    if (line.Length > 0)
                        insertAt = i + 1;
                }
                lines.Insert(insertAt, entry);
            }

            // write through a temporary file, so a crash can't leave half a file:
            String temporary = filepath + ".tmp";
            File.WriteAllText(temporary, String.Join("\r\n", lines.ToArray()) + "\r\n", new UTF8Encoding(true));
            if (!File.Exists(filepath))
            {
                File.Move(temporary, filepath);
                return;
            }

            try
            {
                File.Replace(temporary, filepath, null);
            }
            catch (IOException)
            {
                // File.Replace isn't supported everywhere (e.g. some network drives):
                File.Copy(temporary, filepath, true);
                File.Delete(temporary);
            }
        }
    }
}
