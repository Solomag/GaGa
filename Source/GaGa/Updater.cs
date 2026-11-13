// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;


namespace GaGa
{
    /// <summary>
    /// Checks the GitHub releases of Solomag/GaGa for a newer GaGa.exe,
    /// and replaces the running exe with it (a running exe can be renamed,
    /// just not overwritten).
    /// </summary>
    internal static class Updater
    {
        private const String LatestReleaseUrl = "https://api.github.com/repos/Solomag/GaGa/releases/latest";
        private const String AssetName = "GaGa.exe";
        private const String UserAgent = "GaGa updater";

        // fields filled in by DataContractJsonSerializer:
#pragma warning disable 0649

        [DataContract]
        private class Release
        {
            [DataMember(Name = "tag_name")] public String TagName;
            [DataMember(Name = "html_url")] public String HtmlUrl;
            [DataMember(Name = "assets")] public List<Asset> Assets;
        }

        [DataContract]
        private class Asset
        {
            [DataMember(Name = "name")] public String Name;
            [DataMember(Name = "browser_download_url")] public String Url;
        }

#pragma warning restore 0649

        /// <summary>
        /// A release newer than the running GaGa.
        /// </summary>
        internal class Update
        {
            public Version Version;
            public String Tag;
            public String PageUrl;
            public Uri DownloadUri;
        }

        static Updater()
        {
            // GitHub only speaks TLS 1.2+ (3072 = Tls12):
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType) 3072;
            }
            catch (NotSupportedException)
            {
            }
        }

        /// <summary>
        /// Version of the running GaGa (from AssemblyInfo).
        /// </summary>
        public static Version CurrentVersion
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        /// <summary>
        /// "2026.11.12", "v2026.11.12" -> Version, or null.
        /// </summary>
        internal static Version ParseTag(String tag)
        {
            if (String.IsNullOrEmpty(tag))
                return null;

            String clean = tag.Trim().TrimStart('v', 'V');
            Version version;
            return Version.TryParse(clean, out version) ? version : null;
        }

        /// <summary>
        /// Compare without the parts that are not set (2026.11.12 == 2026.11.12.0).
        /// </summary>
        internal static Boolean IsNewer(Version candidate, Version current)
        {
            Func<Version, Version> full = v => new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
            return full(candidate) > full(current);
        }

        /// <summary>
        /// The latest release, when it is newer than us and has a GaGa.exe. Null otherwise.
        /// Throws on network errors.
        /// </summary>
        public static Update Check()
        {
            try
            {
                return CheckWithApi();
            }
            catch (WebException e)
            {
                // the API allows 60 requests an hour per address (403/429 after that):
                HttpWebResponse response = e.Response as HttpWebResponse;
                if (response == null || ((Int32) response.StatusCode != 403 && (Int32) response.StatusCode != 429))
                    throw;
            }

            return CheckWithRedirect();
        }

        /// <summary>
        /// Without the API: .../releases/latest redirects to .../releases/tag/TAG,
        /// and the asset of a tag has a fixed address.
        /// </summary>
        private static Update CheckWithRedirect()
        {
            HttpWebRequest request = (HttpWebRequest) WebRequest.Create("https://github.com/Solomag/GaGa/releases/latest");
            request.UserAgent = UserAgent;
            request.Method = "HEAD";
            request.AllowAutoRedirect = false;
            request.Timeout = 15000;

            String location;
            using (WebResponse response = request.GetResponse())
                location = response.Headers["Location"];

            Int32 at = (location ?? "").LastIndexOf("/tag/", StringComparison.Ordinal);
            if (at < 0)
                return null;

            String tag = Uri.UnescapeDataString(location.Substring(at + 5));
            Version version = ParseTag(tag);
            if (version == null || !IsNewer(version, CurrentVersion))
                return null;

            return new Update
            {
                Version = version,
                Tag = tag,
                PageUrl = location,
                DownloadUri = new Uri("https://github.com/Solomag/GaGa/releases/download/" + Uri.EscapeDataString(tag) + "/" + AssetName),
            };
        }

        private static Update CheckWithApi()
        {
            HttpWebRequest request = (HttpWebRequest) WebRequest.Create(LatestReleaseUrl);
            request.UserAgent = UserAgent;
            request.Accept = "application/vnd.github+json";
            request.Timeout = 15000;

            Release release;
            using (WebResponse response = request.GetResponse())
            using (Stream body = response.GetResponseStream())
            {
                release = (Release) new DataContractJsonSerializer(typeof(Release)).ReadObject(body);
            }

            Version version = ParseTag(release.TagName);
            if (version == null || !IsNewer(version, CurrentVersion) || release.Assets == null)
                return null;

            foreach (Asset asset in release.Assets)
            {
                if (String.Equals(asset.Name, AssetName, StringComparison.OrdinalIgnoreCase) && asset.Url != null)
                {
                    return new Update
                    {
                        Version = version,
                        Tag = release.TagName,
                        PageUrl = release.HtmlUrl,
                        DownloadUri = new Uri(asset.Url),
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// Download the update next to the running exe and swap them:
        /// GaGa.exe -> GaGa.exe.old, new file -> GaGa.exe.
        /// The old file is removed on the next start. Throws on errors,
        /// leaving the running exe in place.
        /// </summary>
        public static void Install(Update update)
        {
            String exe = Application.ExecutablePath;
            String downloaded = exe + ".new";
            String old = exe + ".old";

            using (WebClient client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                client.DownloadFile(update.DownloadUri, downloaded);
            }

            // sanity check: a Windows executable, not an error page:
            Byte[] start = new Byte[2];
            using (FileStream file = File.OpenRead(downloaded))
            {
                if (file.Length < 16 * 1024 || file.Read(start, 0, 2) != 2 || start[0] != 'M' || start[1] != 'Z')
                {
                    file.Dispose();
                    File.Delete(downloaded);
                    throw new InvalidDataException("The downloaded file is not a valid GaGa.exe.");
                }
            }

            if (File.Exists(old))
                File.Delete(old);

            File.Move(exe, old);
            try
            {
                File.Move(downloaded, exe);
            }
            catch (Exception)
            {
                // put the running exe back:
                File.Move(old, exe);
                throw;
            }
        }

        /// <summary>
        /// Start the (new) exe.
        /// </summary>
        public static void Restart()
        {
            Process.Start(new ProcessStartInfo(Application.ExecutablePath)
            {
                WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath),
                UseShellExecute = false,
            });
        }

        /// <summary>
        /// Remove what the last update left behind.
        /// </summary>
        public static void CleanUp()
        {
            foreach (String suffix in new String[] { ".old", ".new" })
            {
                try
                {
                    String path = Application.ExecutablePath + suffix;
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch (Exception)
                {
                    // the old exe may still be running for a moment, next time then
                }
            }
        }
    }
}
