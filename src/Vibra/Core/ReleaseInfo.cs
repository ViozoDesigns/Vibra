using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Vibra.Core
{
    /// <summary>The parts of a GitHub release (from the REST API) the updater needs.</summary>
    [DataContract]
    internal sealed class ReleaseInfo
    {
        [DataMember(Name = "tag_name")] public string TagName { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
        [DataMember(Name = "html_url")] public string PageUrl { get; set; }
        [DataMember(Name = "assets")] public List<ReleaseAsset> Assets { get; set; }

        public Version Version => ParseVersion(TagName);

        public static ReleaseInfo Parse(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(ReleaseInfo));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty)))
                return (ReleaseInfo)serializer.ReadObject(stream);
        }

        /// <summary>"v0.1.12" -> 0.1.12.0; null if the tag isn't a version.</summary>
        public static Version ParseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;
            string text = tag.Trim().TrimStart('v', 'V');
            return Version.TryParse(text, out Version version) ? Normalize(version) : null;
        }

        /// <summary>Fills missing parts with zero so 0.1.12 and 0.1.12.0 compare equal.</summary>
        public static Version Normalize(Version version) =>
            new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));

        public bool IsNewerThan(Version current) =>
            !Draft && !Prerelease && Version != null && current != null && Version > Normalize(current);

        public ReleaseAsset FindAsset(string name) =>
            Assets?.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    [DataContract]
    internal sealed class ReleaseAsset
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "browser_download_url")] public string DownloadUrl { get; set; }
        [DataMember(Name = "size")] public long Size { get; set; }
        /// <summary>e.g. "sha256:abc…" (GitHub fills this in for uploaded assets).</summary>
        [DataMember(Name = "digest")] public string Digest { get; set; }

        public string Sha256 =>
            Digest != null && Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? Digest.Substring("sha256:".Length).Trim().ToLowerInvariant()
                : null;
    }
}
