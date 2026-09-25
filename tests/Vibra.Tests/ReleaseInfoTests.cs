using System;
using Vibra.Core;
using Xunit;

namespace Vibra.Tests
{
    public class ReleaseInfoTests
    {
        private const string Json = @"{
  ""url"": ""https://api.github.com/repos/ViozoDesigns/Vibra/releases/1"",
  ""html_url"": ""https://github.com/ViozoDesigns/Vibra/releases/tag/v0.1.12"",
  ""tag_name"": ""v0.1.12"",
  ""name"": ""Vibra 0.1.12"",
  ""draft"": false,
  ""prerelease"": false,
  ""author"": { ""login"": ""github-actions[bot]"" },
  ""assets"": [
    { ""name"": ""Vibra.exe"", ""size"": 231424, ""digest"": ""sha256:ABCDEF0123"", ""browser_download_url"": ""https://github.com/ViozoDesigns/Vibra/releases/download/v0.1.12/Vibra.exe"" },
    { ""name"": ""Vibra.exe.config"", ""size"": 336, ""digest"": null, ""browser_download_url"": ""https://github.com/ViozoDesigns/Vibra/releases/download/v0.1.12/Vibra.exe.config"" }
  ]
}";

        [Fact]
        public void Parses_a_github_release()
        {
            var release = ReleaseInfo.Parse(Json);
            Assert.Equal(new Version(0, 1, 12, 0), release.Version);
            var exe = release.FindAsset("vibra.exe");
            Assert.Equal(231424, exe.Size);
            Assert.Equal("abcdef0123", exe.Sha256);
            Assert.EndsWith("/v0.1.12/Vibra.exe", exe.DownloadUrl);
            Assert.Null(release.FindAsset("Vibra.exe.config").Sha256);
        }

        [Theory]
        [InlineData("0.1.11.0", true)]
        [InlineData("0.1.12.0", false)]
        [InlineData("0.1.13.0", false)]
        [InlineData("0.2.0.0", false)]
        public void Only_newer_versions_count(string current, bool expected)
        {
            Assert.Equal(expected, ReleaseInfo.Parse(Json).IsNewerThan(Version.Parse(current)));
        }

        [Fact]
        public void Drafts_prereleases_and_odd_tags_are_ignored()
        {
            var old = new Version(0, 0, 1, 0);
            Assert.False(ReleaseInfo.Parse(Json.Replace("\"prerelease\": false", "\"prerelease\": true")).IsNewerThan(old));
            Assert.False(ReleaseInfo.Parse(Json.Replace("\"draft\": false", "\"draft\": true")).IsNewerThan(old));
            Assert.False(ReleaseInfo.Parse(Json.Replace("v0.1.12", "nightly")).IsNewerThan(old));
        }
    }
}
