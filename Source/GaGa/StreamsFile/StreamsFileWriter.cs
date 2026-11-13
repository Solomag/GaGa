
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

            WriteLines(filepath, lines);
        }

        private static Boolean IsSectionHeader(String line)
        {
            String trimmed = line.Trim();
            return trimmed.StartsWith("[") && trimmed.EndsWith("]");
        }

        /// <summary>
        /// The address of a "name = uri" line, or null.
        /// </summary>
        private static Uri LineUri(String raw)
        {
            String line = raw.Trim();
            if (line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("["))
                return null;

            Int32 equals = line.IndexOf('=');
            Uri uri;
            if (equals < 0 || !Uri.TryCreate(line.Substring(equals + 1).Trim(), UriKind.Absolute, out uri))
                return null;
            return uri;
        }

        /// <summary>
        /// Whether a given section (not its subsections) has a stream with this address.
        /// </summary>
        public static Boolean ContainsInSection(String filepath, String section, Uri uri)
        {
            String header = "[" + section + "]";
            Boolean inside = false;

            foreach (String raw in ReadLines(filepath))
            {
                if (IsSectionHeader(raw))
                {
                    inside = raw.Trim().Equals(header, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inside && LineUri(raw) == uri)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Remove the streams with this address from a section.
        /// A section left without lines is removed too.
        /// </summary>
        public static void RemoveStream(String filepath, String section, Uri uri)
        {
            List<String> lines = new List<String>(ReadLines(filepath));
            String header = "[" + section + "]";

            Int32 start = lines.FindIndex(l => l.Trim().Equals(header, StringComparison.OrdinalIgnoreCase));
            if (start < 0)
                return;

            Int32 end = start + 1;
            while (end < lines.Count && !IsSectionHeader(lines[end]))
                end++;

            for (Int32 i = end - 1; i > start; i--)
            {
                if (LineUri(lines[i]) == uri)
                {
                    lines.RemoveAt(i);
                    end--;
                }
            }

            // nothing left but blank lines: drop the section and its blank lines
            Boolean empty = true;
            for (Int32 i = start + 1; i < end; i++)
                if (lines[i].Trim().Length > 0)
                    empty = false;

            if (empty)
                lines.RemoveRange(start, end - start);

            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0)
                lines.RemoveAt(lines.Count - 1);

            WriteLines(filepath, lines);
        }

        /// <summary>
        /// Write the file through a temporary one, so a crash can't leave half a file.
        /// </summary>
        private static void WriteLines(String filepath, List<String> lines)
        {
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
