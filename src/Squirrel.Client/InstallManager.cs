using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using NuGet;
using ReactiveUIMicro;
using Squirrel.Client.Extensions;
using Squirrel.Core;
using System.IO.Pipes;
using System.Security.Principal;
using System.Diagnostics;

namespace Squirrel.Client
{
    public interface IInstallManager
    {
        IObservable<List<string>> ExecuteInstall(string currentAssemblyDir, IPackage bundledPackageMetadata, IObserver<int> progress = null);
        IObservable<Unit> ExecuteUninstall(Version version);
    }

    public class InstallManager : IInstallManager
    {
        public ReleaseEntry BundledRelease { get; protected set; }
        public string TargetRootDirectory { get; protected set; }
        readonly IRxUIFullLogger log;

        public InstallManager(ReleaseEntry bundledRelease, string targetRootDirectory = null)
        {
            BundledRelease = bundledRelease;
            TargetRootDirectory = targetRootDirectory;
            log = LogManager.GetLogger<InstallManager>();
        }

        public IObservable<List<string>> ExecuteInstall(string currentAssemblyDir, IPackage bundledPackageMetadata, IObserver<int> progress = null)
        {
            progress = progress ?? new Subject<int>();

            // NB: This bit of code is a bit clever. The binaries that WiX 
            // has installed *itself* meets the qualifications for being a
            // Squirrel update directory (a RELEASES file and the corresponding 
            // NuGet packages). 
            //
            // So, in order to reuse some code and not write the same things 
            // twice we're going to "Eigenupdate" from our own directory; 
            // UpdateManager will operate in bootstrap mode and create a 
            // local directory for us. 
            //
            // Then, we create a *new* UpdateManager whose target is the normal 
            // update URL - we can then apply delta updates against the bundled 
            // NuGet package to get up to vCurrent. The reason we go through
            // this rigamarole is so that developers don't have to rebuild the 
            // installer as often (never, technically).

            var updateUsingDeltas =
                executeInstall(currentAssemblyDir, bundledPackageMetadata, progress: progress)
                        .ToObservable()
                        .ObserveOn(RxApp.DeferredScheduler)
                        .Catch<List<string>, Exception>(ex => {
                    log.WarnException("Updating using deltas has failed", ex);
                    return executeInstall(currentAssemblyDir, bundledPackageMetadata, true, progress)
                                 .ToObservable();
            });

            return updateUsingDeltas;
        }

        async Task<List<string>> executeInstall(
            string currentAssemblyDir,
            IPackage bundledPackageMetadata,
            bool ignoreDeltaUpdates = false,
            IObserver<int> progress = null)
        {
            var fxVersion = bundledPackageMetadata.DetectFrameworkVersion();

            var eigenCheckProgress = new Subject<int>();
            var eigenCopyFileProgress = new Subject<int>();
            var eigenApplyProgress = new Subject<int>();

            var realCheckProgress = new Subject<int>();
            var realCopyFileProgress = new Subject<int>();
            var realApplyProgress = new Subject<int>();

            List<string> ret = null;

            using (var eigenUpdater = new UpdateManager(
                        currentAssemblyDir, 
                        bundledPackageMetadata.Id, 
                        fxVersion,
                        TargetRootDirectory)) {

                // The real update takes longer than the eigenupdate because we're
                // downloading from the Internet instead of doing everything 
                // locally, so give it more weight
                Observable.Concat(
                    Observable.Concat(eigenCheckProgress, eigenCopyFileProgress, eigenCopyFileProgress)
                        .Select(x => (x/3.0)*0.33),
                    Observable.Concat(realCheckProgress, realCopyFileProgress, realApplyProgress)
                        .Select(x => (x/3.0)*0.67))
                    .Select(x => (int) x)
                    .Subscribe(progress);

                var updateInfo = await eigenUpdater.CheckForUpdate(ignoreDeltaUpdates, eigenCheckProgress);

                log.Info("The checking of releases completed - and there was much rejoicing");

                if (!updateInfo.ReleasesToApply.Any()) {

                    var rootDirectory = TargetRootDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                    var version = updateInfo.CurrentlyInstalledVersion;
                    var releaseFolder = String.Format("app-{0}", version.Version);
                    var absoluteFolder = Path.Combine(rootDirectory, version.PackageName, releaseFolder);

                    if (!Directory.Exists(absoluteFolder)) {
                        log.Warn("executeInstall: the directory {0} doesn't exist - cannot find the current app?!!?");
                    } else {
                        return Directory.GetFiles(absoluteFolder, "*.exe", SearchOption.TopDirectoryOnly)
                                        .ToList();
                    }
                }

                foreach (var u in updateInfo.ReleasesToApply) {
                    log.Info("HEY! We should be applying update {0}", u.Filename);
                }

                await eigenUpdater.DownloadReleases(updateInfo.ReleasesToApply, eigenCopyFileProgress);

                log.Info("The downloading of releases completed - and there was much rejoicing");

                ret = await eigenUpdater.ApplyReleases(updateInfo, eigenApplyProgress);

                log.Info("The applying of releases completed - and there was much rejoicing");
            }

            var updateUrl = bundledPackageMetadata.ProjectUrl != null ? bundledPackageMetadata.ProjectUrl.ToString() : null;
            updateUrl = null; //XXX REMOVE ME
            if (updateUrl == null) {
                realCheckProgress.OnNext(100); realCheckProgress.OnCompleted();
                realCopyFileProgress.OnNext(100); realCopyFileProgress.OnCompleted();
                realApplyProgress.OnNext(100); realApplyProgress.OnCompleted();

                return ret;
            }

            using(var realUpdater = new UpdateManager(
                    updateUrl,
                    bundledPackageMetadata.Id,
                    fxVersion,
                    TargetRootDirectory)) {
                try {
                    var updateInfo = await realUpdater.CheckForUpdate(progress: realCheckProgress);
                    await realUpdater.DownloadReleases(updateInfo.ReleasesToApply, realCopyFileProgress);
                    await realUpdater.ApplyReleases(updateInfo, realApplyProgress);
                } catch (Exception ex) {
                    log.ErrorException("Failed to update to latest remote version", ex);
                    return new List<string>();
                }
            }

            return ret;
        }

        public IObservable<Unit> ExecuteUninstall(Version version = null)
        {
            // Run uninstall
            var updateManager = new UpdateManager("http://lol", BundledRelease.PackageName, FrameworkVersion.Net40, TargetRootDirectory);

            return updateManager.FullUninstall(version)
                .ObserveOn(RxApp.DeferredScheduler)
                .Log(this, "Full uninstall")
                .Finally(updateManager.Dispose);
        }

        public IObservable<bool> RequestExitAndWait(int connectionTimeout = 1000, int waitForExitTimeout = 1000)
        {
            return Observable.Start(() => requestExitAndWait(connectionTimeout, waitForExitTimeout), RxApp.TaskpoolScheduler);
        }

        bool requestExitAndWait(int connectionTimeout, int waitForExitTimeout)
        {
            // TODO: what if the app is running but doesn't respond in time?
            // currently it's interpreted as the app being closed so the uninstall will proceed as usual

            var pipeClient = new NamedPipeClientStream(".", GetPipeName(BundledRelease.PackageName), PipeDirection.InOut, PipeOptions.None);
            bool connected = false;
            try {
                pipeClient.Connect(connectionTimeout);
                connected = true;
            } catch (TimeoutException) { }

            if (connected) {
                var buffer = new byte[validateBytes.Length];
                pipeClient.Read(buffer, 0, validateBytes.Length);

                if (buffer.SequenceEqual(validateBytes)) {
                    pipeClient.Write(exitMessage, 0, exitMessage.Length);
                    log.Info("Attempted to request running instance to exit");

                    var processIdBuffer = new byte[4];
                    pipeClient.Read(processIdBuffer, 0, processIdBuffer.Length);
                    int processId = BitConverter.ToInt32(processIdBuffer, 0);
                    log.Info("Going to wait for running instance (" + processId + ") to exit");

                    var process = default(Process);
                    try {
                        process = Process.GetProcessById(processId);
                    } catch (ArgumentException) { } // Process specified by processId is not running

                    // Check for id 0 because GetProcessById might actually succeed even though the app closed
                    // The resulting Process has an id of 0 and will throw a Win32Exception on most access attempts
                    if (process == null || process.Id == 0 || process.HasExited || process.WaitForExit(waitForExitTimeout)) {
                        log.Info("Running instance exited");
                        return true;
                    } else {
                        log.Info("Running instance did not exit in time");
                        return false;
                    }
                } else {
                    log.Info("Connected to a named pipe but didn't respond or wasn't Squirrel");
                    return true;
                }
            } else {
                log.Info("No running instances found to request exit");
                return true;
            }
        }
        
        // TODO: Clean up
        // TODO: Should the client validate the server or the other way around? Should there be validation?
        private static readonly byte[] validateBytes = new[] { 'S', 'Q', 'U', 'I', 'R', 'R', 'E', 'L' }.Select(x => (byte)x).ToArray();
        private static readonly byte[] exitMessage = new[] { 'E', 'X', 'I', 'T' }.Select(x => (byte)x).ToArray();
        public static IObservable<Unit> ListenForExitRequest(string applicationName)
        {
            var log = LogManager.GetLogger<InstallManager>();

            var subject = new Subject<Unit>();

            Task.Factory.StartNew(() => {
                while (true) {
                    var pipeServer = new NamedPipeServerStream(GetPipeName(applicationName), PipeDirection.InOut);
                    pipeServer.WaitForConnection();
                    try {
                        pipeServer.Write(validateBytes, 0, validateBytes.Length);
                        var message = new byte[8];
                        pipeServer.Read(message, 0, message.Length);
                        if (message.Take(exitMessage.Length).SequenceEqual(exitMessage)) {
                            int processId = Process.GetCurrentProcess().Id;
                            var processBytes = BitConverter.GetBytes(processId);
                            pipeServer.Write(processBytes, 0, processBytes.Length);
                            subject.OnNext(Unit.Default);
                            log.Info("Named pipe server received exit request; sent back process id");
                        } else {
                            log.Info("Named pipe server received unknown message");
                        }
                    } catch (IOException ex) {
                        log.ErrorException("Named pipe server failed", ex);
                    } finally {
                        pipeServer.Close();
                    }
                }
            });

            return subject;
        }

        // TODO: Clean up
        private static string GetPipeName(string applicationName)
        {
            return "SquirrelPipe " + applicationName;
        }
    }
}
