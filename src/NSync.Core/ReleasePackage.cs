using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Ionic.Zip;
using NuGet;

namespace NSync.Core
{
    public class ReleasePackage : IEnableLogger
    {
        readonly string packageFile;
        public ReleasePackage(string inputPackageFile)
        {
            packageFile = inputPackageFile;
        }

        public void CreateReleasePackage(string outputFile, string packagesRootDir = null)
        {
            var package = new ZipPackage(packageFile);
            var dependencies = findAllDependentPackages(package, packagesRootDir);

            var tempPath = new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
            tempPath.Create();

            try {
                var zf = new ZipFile(packageFile);
                zf.ExtractAll(tempPath.FullName);
    
                dependencies.ForEach(pkg => {
                    this.Log().Info("Scanning {0}", pkg.Id);

                    pkg.GetFiles()
                        .Where(x => x.Path.StartsWith("lib", true, CultureInfo.InvariantCulture))
                        .ForEach(file => {
                            var outPath = new FileInfo(Path.Combine(tempPath.FullName, file.Path));

                            outPath.Directory.CreateRecursive();

                            using (var of = File.Create(outPath.FullName)) {
                                this.Log().Info("Writing {0} to {1}", file.Path, outPath);
                                file.GetStream().CopyTo(of);
                            }
                        });
                });

                removeDependenciesFromPackageSpec(tempPath.GetFiles("*.nuspec").First().FullName);

                // NB: Nuke Silverlight. We can't tell as easily if other
                // profiles can be removed because you can load net20 DLLs
                // inside .NET 4.0 apps
                var libPath = tempPath.GetDirectories().First(x => x.Name.ToLowerInvariant() == "lib");
                this.Log().Debug(libPath.FullName);
                libPath.GetDirectories()
                    .Where(x => x.Name.ToLowerInvariant().StartsWith("sl"))
                    .Do(x => this.Log().Info("Deleting {0}", x.Name))
                    .ForEach(x => x.Delete(true));
    
                zf = new ZipFile(outputFile);
                zf.AddDirectory(tempPath.FullName);
                zf.Save();
            } finally {
                tempPath.Delete(true);
            }
        }

        void removeDependenciesFromPackageSpec(string specPath)
        {
            var xdoc = new XmlDocument();
            xdoc.Load(specPath);

            var metadata = xdoc.DocumentElement.FirstChild;
            metadata.RemoveChild(
                metadata.ChildNodes.OfType<XmlElement>().First(x => x.Name.ToLowerInvariant() == "dependencies"));

            xdoc.Save(specPath);
        }

        IEnumerable<IPackage> findAllDependentPackages(IPackage package = null, string packagesRootDir = null)
        {
            package = package ?? new ZipPackage(packageFile);

            return package.Dependencies.SelectMany(x => {
                var ret = findPackageFromName(x.Id, x.VersionSpec, packagesRootDir);
                if (ret == null) {
                    return Enumerable.Empty<IPackage>();
                }

                return findAllDependentPackages(ret).StartWith(ret);
            });
        }

        IPackage findPackageFromName(string id, IVersionSpec versionSpec, string packagesRootDir = null, IQueryable<IPackage> machineCache = null)
        {
            IQueryable<IPackage> localPackages = Enumerable.Empty<IPackage>().AsQueryable();
            machineCache = machineCache ?? Enumerable.Empty<IPackage>().AsQueryable();

            if (packagesRootDir != null) {
                localPackages = new DirectoryInfo(packagesRootDir).GetAllFilesRecursively()
                    .Where(x => x.Name.ToLowerInvariant().EndsWith("nupkg"))
                    .Select(x => new ZipPackage(x.FullName))
                    .ToArray().AsQueryable();
            }

            return findPackageFromNameInList(id, versionSpec, localPackages) ?? findPackageFromNameInList(id, versionSpec, machineCache);
        }

        IPackage findPackageFromNameInList(string id, IVersionSpec versionSpec, IQueryable<IPackage> packageList)
        {
            return packageList.Where(x => x.Id == id).ToArray().FirstOrDefault(x => {
                if (((versionSpec != null) && (versionSpec.MinVersion != null)) && (versionSpec.MaxVersion != null)) {
                    if ((!versionSpec.IsMaxInclusive || !versionSpec.IsMinInclusive) && (versionSpec.MaxVersion == versionSpec.MinVersion)) {
                        return false;
                    }

                    if (versionSpec.MaxVersion < versionSpec.MinVersion) {
                        return false;
                    }
                }

                return true;
            });
        }
    }
}