using Octokit;
using ProEvergreen;

namespace TrailsAddin.Models
{
    public class EvergreenSettings
    {
        public bool BetaChannel { get; set; }
        public VersionInformation CurrentVersion { get; set; }
        public Release LatestRelease { get; set; }
    }
}
