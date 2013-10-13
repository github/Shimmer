using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;
using ReactiveUIMicro;
using Shimmer.Core;

namespace Shimmer.Client
{
    [Serializable]
    class InstallerHookOperations
    {
        readonly IRxUIFullLogger log;
        readonly IFileSystemFactory fileSystem;
        readonly string applicationName;

        public InstallerHookOperations(IFileSystemFactory fileSystem, string applicationName)
        {
            // XXX: ALWAYS BE LOGGING
            this.log = new WrappingFullLogger(new FileLogger(applicationName), typeof(InstallerHookOperations));
            
            this.fileSystem = fileSystem;
            this.applicationName = applicationName;
        }

        public IEnumerable<string> RunAppSetupInstallers(PostInstallInfo info)
        {
            var appSetups = default(IEnumerable<IAppSetup>);

            try {
                appSetups = findAppSetupsToRun(info.NewAppDirectoryRoot);
            } catch (UnauthorizedAccessException ex) {
                log.ErrorException("Failed to load IAppSetups in post-install due to access denied", ex);
                return new string[0];
            }

            ResolveEventHandler resolveAssembly = (obj, args) => {
                var directory = fileSystem.GetDirectoryInfo(info.NewAppDirectoryRoot);
                return tryResolveAssembly(directory, args);
            };

            AppDomain.CurrentDomain.AssemblyResolve += resolveAssembly;

            var results = appSetups
                .Select(app => installAppVersion(app, info.NewCurrentVersion, info.ShortcutRequestsToIgnore, info.IsFirstInstall))
                .Where(x => x != null)
                .ToArray();

            AppDomain.CurrentDomain.AssemblyResolve -= resolveAssembly;

            return results;

        }

        public IEnumerable<ShortcutCreationRequest> RunAppSetupCleanups(string fullDirectoryPath)
        {
            var dirName = Path.GetFileName(fullDirectoryPath);
            var ver = new Version(dirName.Replace("app-", ""));

            var apps = default(IEnumerable<IAppSetup>);
            try {
                apps = findAppSetupsToRun(fullDirectoryPath);
            } catch (UnauthorizedAccessException ex) {
                log.ErrorException("Couldn't run cleanups", ex);
                return Enumerable.Empty<ShortcutCreationRequest>();
            }

            var ret = apps.SelectMany(app => uninstallAppVersion(app, ver)).ToArray();

            return ret;
        }

        IEnumerable<ShortcutCreationRequest> uninstallAppVersion(IAppSetup app, Version ver)
        {
            try {
                app.OnVersionUninstalling(ver);
            } catch (Exception ex) {
                log.ErrorException("App threw exception on uninstall:  " + app.GetType().FullName, ex);
            }

            var shortcuts = Enumerable.Empty<ShortcutCreationRequest>();
            try {
                shortcuts = app.GetAppShortcutList();
            } catch (Exception ex) {
                log.ErrorException("App threw exception on shortcut uninstall:  " + app.GetType().FullName, ex);
            }

            // Get the list of shortcuts that *should've* been there, but aren't;
            // this means that the user deleted them by hand and that they should 
            // stay dead
            return shortcuts.Aggregate(new List<ShortcutCreationRequest>(), (acc, x) => {
                var path = x.GetLinkTarget(applicationName);
                var fi = fileSystem.GetFileInfo(path);

                if (fi.Exists) {
                    fi.Delete();
                    log.Info("Deleting shortcut: {0}", fi.FullName);
                } else {
                    acc.Add(x);
                    log.Info("Shortcut not found: {0}, capturing for future reference", fi.FullName);
                }
                
                return acc;
            });
        }

        string installAppVersion(IAppSetup app, Version newCurrentVersion, IEnumerable<ShortcutCreationRequest> shortcutRequestsToIgnore, bool isFirstInstall)
        {
            try {
                if (isFirstInstall) {
                    log.Info("installAppVersion: Doing first install for {0}", app.Target);
                    app.OnAppInstall();
                }
                log.Info("installAppVersion: Doing install for version {0} {1}", newCurrentVersion, app.Target);
                app.OnVersionInstalled(newCurrentVersion);
            } catch (Exception ex) {
                log.ErrorException("App threw exception on install:  " + app.GetType().FullName, ex);
                throw;
            }

            var shortcutList = Enumerable.Empty<ShortcutCreationRequest>();
            try {
                shortcutList = app.GetAppShortcutList();
                shortcutList.ForEach(x =>
                    log.Info("installAppVersion: we have a shortcut {0}", x.TargetPath));
            } catch (Exception ex) {
                log.ErrorException("App threw exception on shortcut uninstall:  " + app.GetType().FullName, ex);
                throw;
            }

            shortcutList
                .Where(x => !shortcutRequestsToIgnore.Contains(x))
                .ForEach(x => {
                    var shortcut = x.GetLinkTarget(applicationName, true);

                    var fi = fileSystem.GetFileInfo(shortcut);

                    log.Info("installAppVersion: checking shortcut {0}", fi.FullName);

                    if (fi.Exists) {
                        log.Info("installAppVersion: deleting existing file");
                        fi.Delete();
                    }

                    fileSystem.CreateDirectoryRecursive(fi.Directory.FullName);

                    var sl = new ShellLink {
                        Target = x.TargetPath,
                        IconPath = x.IconLibrary,
                        IconIndex = x.IconIndex,
                        Arguments = x.Arguments,
                        WorkingDirectory = x.WorkingDirectory,
                        Description = x.Description,
                    };

                    sl.Save(shortcut);
                });

            return app.LaunchOnSetup ? app.Target : null;
        }

        IEnumerable<IAppSetup> findAppSetupsToRun(string appDirectory)
        {
            var allExeFiles = default(FileInfoBase[]);

            var directory = fileSystem.GetDirectoryInfo(appDirectory);

            if (!directory.Exists) {
                log.Warn("findAppSetupsToRun: the folder {0} does not exist", appDirectory);
                return Enumerable.Empty<IAppSetup>();
            }

            try {
                allExeFiles = directory.GetFiles("*.exe");
            } catch (UnauthorizedAccessException ex) {
                // NB: This can happen if we run into a MoveFileEx'd directory,
                // where we can't even get the list of files in it.
                log.WarnException("Couldn't search directory for IAppSetups: " + appDirectory, ex);
                return Enumerable.Empty<IAppSetup>();
            }

            var locatedAppSetups = allExeFiles
                .Where(f => f.Exists)
                .Select(x => loadAssemblyOrWhine(x.FullName)).Where(x => x != null)
                .SelectMany(x => x.GetModules())
                .SelectMany(x => {
                     try {
                         return x.GetTypes().Where(y => typeof (IAppSetup).IsAssignableFrom(y));
                     } catch (ReflectionTypeLoadException ex) {
                         var message = String.Format("Couldn't load types from module {0}", x.FullyQualifiedName);
                         log.WarnException(message, ex);
                         ex.LoaderExceptions.ForEach(le => log.WarnException("LoaderException found", le));
                         return Enumerable.Empty<Type>();
                     }
                })
                .Select(createInstanceOrWhine).Where(x => x != null)
                .ToArray();

            if (!locatedAppSetups.Any()) {
                log.Warn("Could not find any AppSetup instances");
                allExeFiles.ForEach(f => log.Info("We have an exe: {0}", f.FullName));
                return allExeFiles.Select(x => new DidntFollowInstructionsAppSetup(x.FullName))
                                  .ToArray();
            }
            return locatedAppSetups;
        }

        public IEnumerable<ShortcutCreationRequest> RunAppUninstall(string fullDirectoryPath)
        {
            ResolveEventHandler resolveAssembly = (obj, args) =>
            {
                var directory = fileSystem.GetDirectoryInfo(fullDirectoryPath);
                return tryResolveAssembly(directory, args);
            };

            AppDomain.CurrentDomain.AssemblyResolve += resolveAssembly;

            var apps = default(IEnumerable<IAppSetup>);
            try {
                apps = findAppSetupsToRun(fullDirectoryPath);
            } catch (UnauthorizedAccessException ex) {
                log.ErrorException("Couldn't run cleanups", ex);
                return Enumerable.Empty<ShortcutCreationRequest>();
            }

            var ret = apps.SelectMany(uninstallApp).ToArray();

            AppDomain.CurrentDomain.AssemblyResolve -= resolveAssembly;

            return ret;
        }

        IEnumerable<ShortcutCreationRequest> uninstallApp(IAppSetup app)
        {
            try {
                app.OnAppUninstall();
            } catch (Exception ex) {
                log.ErrorException("App threw exception on uninstall:  " + app.GetType().FullName, ex);
            }

            var shortcuts = Enumerable.Empty<ShortcutCreationRequest>();
            try {
                shortcuts = app.GetAppShortcutList();
            } catch (Exception ex) {
                log.ErrorException("App threw exception on shortcut uninstall:  " + app.GetType().FullName, ex);
            }

            // Get the list of shortcuts that *should've* been there, but aren't;
            // this means that the user deleted them by hand and that they should 
            // stay dead
            return shortcuts.Aggregate(new List<ShortcutCreationRequest>(), (acc, x) => {
                var path = x.GetLinkTarget(applicationName);
                var fi = fileSystem.GetFileInfo(path);

                if (fi.Exists) {
                    fi.Delete();
                    log.Info("Deleting shortcut: {0}", fi.FullName);
                } else {
                    acc.Add(x);
                    log.Info("Shortcut not found: {0}, capturing for future reference", fi.FullName);
                }

                return acc;
            });
        }

        IAppSetup createInstanceOrWhine(Type typeToCreate)
        {
            try {
                return (IAppSetup) Activator.CreateInstance(typeToCreate);
            }
            catch (Exception ex) {
                log.WarnException("Post-install: Failed to create type " + typeToCreate.FullName, ex);
                return null;
            }
        }

        Assembly loadAssemblyOrWhine(string fileToLoad)
        {
            try {
                var ret = Assembly.LoadFile(fileToLoad);
                return ret;
            }
            catch (Exception ex) {
                log.WarnException("Post-install: load failed for " + fileToLoad, ex);
                return null;
            }
        }

        Assembly tryResolveAssembly(DirectoryInfoBase directory, ResolveEventArgs args)
        {
            try {
                if (directory.Exists) {
                    var files = directory.GetFiles("*.dll")
                        .Concat(directory.GetFiles("*.exe"));

                    foreach (var f in files) {
                        var assemblyName = AssemblyName.GetAssemblyName(f.FullName);

                        if (assemblyName.FullName == args.Name) {
                            return Assembly.Load(assemblyName);
                        }
                    }
                }
            }
            catch (Exception ex) {
                log.WarnException("Could not resolve assembly: " + args.Name, ex);
            }

            return null;
        }
    }
}
