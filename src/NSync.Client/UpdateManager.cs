using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using NSync.Core;

namespace NSync.Client
{
    public interface IUpdateManager
    {
        IObservable<UpdateInfo> CheckForUpdate();
        void ApplyReleases(IEnumerable<ReleaseEntry> releasesToApply);
    }

    public class UpdateManager : IEnableLogger, IUpdateManager
    {
        Func<string, Stream> openPath;
        Func<string, IObservable<string>> downloadUrl;
        string updateUrl;

        // TODO: overload with default implementations for resolving package store and downloading resource
        public UpdateManager(string url, 
            Func<string, Stream> openPath = null,
            Func<string, IObservable<string>> downloadUrl = null)
        {
            updateUrl = url;
            this.openPath = openPath;
            this.downloadUrl = downloadUrl;
        }

        public IObservable<UpdateInfo> CheckForUpdate()
        {
            IEnumerable<ReleaseEntry> localReleases;

            using (var sr = new StreamReader(openPath(Path.Combine("packages", "RELEASES")))) {
                localReleases = ReleaseEntry.ParseReleaseFile(sr.ReadToEnd());
            }

            var ret = downloadUrl(updateUrl)
                .Select(ReleaseEntry.ParseReleaseFile)
                .Select(releases => determineUpdateInfo(localReleases, releases))
                .Multicast(new AsyncSubject<UpdateInfo>());

            ret.Connect();
            return ret;
        }

        public void ApplyReleases(IEnumerable<ReleaseEntry> releasesToApply)
        {
            foreach (var p in releasesToApply)
            {
                var file = p.Filename;
                // TODO: determine if we can use delta package
                // TODO: download optimal package
                // TODO: verify integrity of packages
                // TODO: apply package changes to destination

                // Q: is file in release relative path or can it support absolute path?
                // Q: have left destination parameter out of this call
                //      - shall NSync take care of the switching between current exe and new exe?
                //      - pondering how to do this right now
            }

            // TODO: what shall we return? we may have issues with integrity of packages/missing packages etc
        }

        UpdateInfo determineUpdateInfo(IEnumerable<ReleaseEntry> localReleases, IEnumerable<ReleaseEntry> remoteReleases)
        {
            if (localReleases.Count() == remoteReleases.Count()) {
                this.Log().Info("No updates, remote and local are the same");
                return null;
            }

            if (localReleases.Max(x => x.Version) >= remoteReleases.Max(x => x.Version)) {
                this.Log().Warn("hwhat, local version is greater than remote version");
                return null;
            }

            return UpdateInfo.Create(findCurrentVersion(localReleases), remoteReleases);
        }

        ReleaseEntry findCurrentVersion(IEnumerable<ReleaseEntry> localReleases)
        {
            return localReleases.MaxBy(x => x.Version).Single(x => !x.IsDelta);
        }
    }

    public class UpdateInfo
    {
        public Version Version { get; protected set; }
        public IEnumerable<ReleaseEntry> ReleasesToApply { get; protected set; }

        protected UpdateInfo(ReleaseEntry latestRelease, IEnumerable<ReleaseEntry> releasesToApply)
        {
            Version = latestRelease.Version;
            ReleasesToApply = releasesToApply;
        }

        public static UpdateInfo Create(ReleaseEntry currentVersion, IEnumerable<ReleaseEntry> availableReleases)
        {
            var newerThanUs = availableReleases.Where(x => x.Version > currentVersion.Version);
            var latestFull = availableReleases.MaxBy(x => x.Version).Single(x => !x.IsDelta);
            var deltasSize = newerThanUs.Where(x => x.IsDelta).Sum(x => x.Filesize);

            return (deltasSize > latestFull.Filesize)
                ? new UpdateInfo(latestFull, newerThanUs.Where(x => x.IsDelta).ToArray())
                : new UpdateInfo(latestFull, new[] {latestFull});
        }
    }
}