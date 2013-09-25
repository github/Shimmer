using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using Shimmer.Core;

namespace Shimmer.Client
{
    public enum FrameworkVersion {
        Net40,
        Net45,
    }

    public class UpdateInfo
    {
        public ReleaseEntry CurrentlyInstalledVersion { get; protected set; }
        public ReleaseEntry FutureReleaseEntry { get; protected set; }
        public IEnumerable<ReleaseEntry> ReleasesToApply { get; protected set; }
        public FrameworkVersion AppFrameworkVersion { get; protected set; }

        public bool IsBootstrapping {
            get { return CurrentlyInstalledVersion == null;  }
        }

        readonly string packageDirectory;

        protected UpdateInfo(ReleaseEntry currentlyInstalledVersion, IEnumerable<ReleaseEntry> releasesToApply, string packageDirectory, FrameworkVersion appFrameworkVersion)
        {
            // NB: When bootstrapping, CurrentlyInstalledVersion is null!
            CurrentlyInstalledVersion = currentlyInstalledVersion;
            ReleasesToApply = releasesToApply ?? Enumerable.Empty<ReleaseEntry>();
            FutureReleaseEntry = ReleasesToApply.Any()
                    ? ReleasesToApply.MaxBy(x => x.Version).FirstOrDefault()
                    : null;
            AppFrameworkVersion = appFrameworkVersion;

            this.packageDirectory = packageDirectory;
        }

        public Dictionary<ReleaseEntry, string> FetchReleaseNotes()
        {
            return ReleasesToApply
                .Select(x => new { Entry = x, Readme = x.GetReleaseNotes(packageDirectory) })
                .ToDictionary(k => k.Entry, v => v.Readme);
        }

        public static UpdateInfo Create(ReleaseEntry currentVersion, IEnumerable<ReleaseEntry> availableReleases, string packageDirectory, FrameworkVersion appFrameworkVersion)
        {
            Contract.Requires(availableReleases != null);
            Contract.Requires(!String.IsNullOrEmpty(packageDirectory));

            var latestFull = availableReleases.MaxBy(x => x.Version).FirstOrDefault(x => !x.IsDelta);
            if (latestFull == null) {
                throw new Exception("There should always be at least one full release");
            }

            if (currentVersion == null) {
                return new UpdateInfo(currentVersion, new[] { latestFull }, packageDirectory, appFrameworkVersion);
            }

            if (currentVersion.Version == latestFull.Version) {
                return new UpdateInfo(currentVersion, Enumerable.Empty<ReleaseEntry>(), packageDirectory, appFrameworkVersion);
            }

            var newerThanUs = availableReleases.Where(x => x.Version > currentVersion.Version)
                                               .OrderBy(v => v.Version);
            var deltasSize = newerThanUs.Where(x => x.IsDelta).Sum(x => x.Filesize);

            return (deltasSize < latestFull.Filesize && deltasSize > 0)
                ? new UpdateInfo(currentVersion, newerThanUs.Where(x => x.IsDelta).ToArray(), packageDirectory, appFrameworkVersion)
                : new UpdateInfo(currentVersion, new[] { latestFull }, packageDirectory, appFrameworkVersion);
        }
    }
}