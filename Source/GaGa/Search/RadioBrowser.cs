
// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;


namespace GaGa.Search
{
    // fields filled in by DataContractJsonSerializer:
#pragma warning disable 0649

    /// <summary>
    /// A station from the radio-browser.info directory.
    /// Only the fields GaGa uses; missing fields are left empty.
    /// </summary>
    [DataContract]
    internal class RadioStation
    {
        [DataMember(Name = "stationuuid")] public String Uuid;
        [DataMember(Name = "name")] public String Name;
        [DataMember(Name = "url")] public String Url;
        [DataMember(Name = "url_resolved")] public String UrlResolved;
        [DataMember(Name = "homepage")] public String Homepage;
        [DataMember(Name = "country")] public String Country;
        [DataMember(Name = "countrycode")] public String CountryCode;
        [DataMember(Name = "tags")] public String Tags;
        [DataMember(Name = "codec")] public String Codec;
        [DataMember(Name = "bitrate")] public Int32 Bitrate;
        [DataMember(Name = "hls")] public Int32 Hls;
        [DataMember(Name = "votes")] public Int32 Votes;
        [DataMember(Name = "clickcount")] public Int32 ClickCount;

        /// <summary>
        /// The address to play: the resolved one (playlists already
        /// followed by the directory) when known, the original otherwise.
        /// </summary>
        public Uri StreamUri
        {
            get
            {
                Uri uri;
                foreach (String candidate in new String[] { UrlResolved, Url })
                {
                    if (!String.IsNullOrEmpty(candidate)
                        && Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out uri)
                        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                        return uri;
                }
                return null;
            }
        }

        /// <summary>
        /// "MP3 128k", "AAC+ 64k", "FLAC"...
        /// </summary>
        public String Format
        {
            get
            {
                String codec = Codec;
                Boolean unknown = String.IsNullOrEmpty(codec) || codec.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase);

                if (unknown)
                {
                    Uri uri = StreamUri;
                    Boolean hls = Hls == 1 || (uri != null && uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase));
                    codec = hls ? "HLS" : "";
                }

                return (Bitrate > 0) ? (codec + " " + Bitrate + "k").Trim() : codec;
            }
        }
    }

    /// <summary>
    /// What to search by.
    /// </summary>
    internal enum RadioSearchField
    {
        Name,
        Tag,
        Country,
    }

    /// <summary>
    /// Minimal client for the radio-browser.info API (https://api.radio-browser.info),
    /// a free, community-maintained directory of internet radio stations.
    /// </summary>
    internal static class RadioBrowser
    {
        // the directory asks clients for a descriptive user agent:
        private const String UserAgent = "GaGa/2026 (Windows tray radio player; https://github.com/Solomag/GaGa)";

        // used when the server list can't be fetched:
        private static readonly String[] FallbackServers = {
            "de1.api.radio-browser.info",
            "de2.api.radio-browser.info",
            "fi1.api.radio-browser.info",
        };

        static RadioBrowser()
        {
            // the API is https-only and needs TLS 1.2 (3072), see HttpsRelay:
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType) 3072 | (SecurityProtocolType) 768;
            }
            catch (NotSupportedException)
            {
            }
        }

        private static readonly Object serversLock = new Object();
        private static List<String> servers;
        private static Int32 nextServer;

        [DataContract]
        private class ServerInfo
        {
            [DataMember(Name = "name")] public String Name;
        }

        private static T GetJson<T>(Uri uri, Int32 timeout)
        {
            HttpWebRequest request = (HttpWebRequest) WebRequest.Create(uri);
            request.UserAgent = UserAgent;
            request.Accept = "application/json";
            request.Timeout = timeout;
            request.ReadWriteTimeout = timeout;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (WebResponse response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                return (T) serializer.ReadObject(stream);
            }
        }

        /// <summary>
        /// The directory runs on several mirrors; ask which ones exist (once)
        /// and spread requests across them.
        /// </summary>
        private static List<String> Servers()
        {
            lock (serversLock)
            {
                if (servers != null)
                    return servers;
            }

            List<String> found = new List<String>();
            try
            {
                List<ServerInfo> list = GetJson<List<ServerInfo>>(new Uri("https://all.api.radio-browser.info/json/servers"), 10000);
                foreach (ServerInfo info in list)
                {
                    if (!String.IsNullOrEmpty(info.Name) && !found.Contains(info.Name))
                        found.Add(info.Name);
                }
            }
            catch (Exception)
            {
                // fall back to the well-known mirrors below
            }

            if (found.Count == 0)
                found.AddRange(FallbackServers);

            lock (serversLock)
            {
                if (servers == null)
                {
                    servers = found;
                    nextServer = new Random().Next(found.Count);
                }
                return servers;
            }
        }

        private static String NextServer()
        {
            List<String> list = Servers();
            lock (serversLock)
            {
                String server = list[nextServer % list.Count];
                nextServer++;
                return server;
            }
        }

        /// <summary>
        /// Windows MediaPlayer can't decode Ogg Vorbis or Opus (Ogg FLAC is
        /// unwrapped by the relay and listed by the directory as "FLAC").
        /// </summary>
        private static Boolean IsPlayableCodec(String codec)
        {
            String c = (codec ?? "").Trim().ToUpperInvariant();
            return c != "OGG" && c != "OPUS" && c != "VORBIS";
        }

        /// <summary>
        /// Search the directory. Most popular stations first, broken ones left out.
        /// HLS stations are kept: the relay turns them into a plain stream.
        /// Blocking: call from a background thread.
        /// </summary>
        public static List<RadioStation> Search(RadioSearchField field, String term)
        {
            String parameter;
            switch (field)
            {
                case RadioSearchField.Tag:
                    parameter = "tag";
                    break;
                case RadioSearchField.Country:
                    // two letters: country code ("DE"), otherwise part of the name:
                    parameter = (term.Trim().Length == 2) ? "countrycode" : "country";
                    break;
                default:
                    parameter = "name";
                    break;
            }

            String query = "/json/stations/search?" + parameter + "=" + Uri.EscapeDataString(term.Trim())
                         + "&hidebroken=true&order=clickcount&reverse=true&limit=500";

            Exception lastError = null;
            for (Int32 attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    List<RadioStation> stations = GetJson<List<RadioStation>>(new Uri("https://" + NextServer() + query), 20000);

                    List<RadioStation> playable = new List<RadioStation>();
                    foreach (RadioStation station in stations)
                    {
                        if (station.StreamUri != null && !String.IsNullOrEmpty(station.Name) && IsPlayableCodec(station.Codec))
                        {
                            station.Name = station.Name.Trim();
                            playable.Add(station);
                        }
                    }
                    return playable;
                }
                catch (Exception e)
                {
                    // try another mirror:
                    lastError = e;
                }
            }
            throw lastError;
        }

        /// <summary>
        /// Tell the directory that a station was played: this is how it ranks
        /// stations by popularity. Fire and forget, errors are ignored.
        /// </summary>
        public static void CountClick(String uuid)
        {
            if (String.IsNullOrEmpty(uuid))
                return;

            ThreadPool.QueueUserWorkItem(state =>
            {
                try
                {
                    Uri uri = new Uri("https://" + NextServer() + "/json/url/" + Uri.EscapeDataString(uuid));
                    HttpWebRequest request = (HttpWebRequest) WebRequest.Create(uri);
                    request.UserAgent = UserAgent;
                    request.Timeout = 10000;
                    using (request.GetResponse())
                    {
                    }
                }
                catch (Exception)
                {
                }
            });
        }
    }
#pragma warning restore 0649
}
