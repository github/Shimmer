﻿using System.Runtime.Versioning;
using MarkdownSharp;
using NuGet;
using ReactiveUI;
using Shimmer.Core;
using Shimmer.Tests.TestHelpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace Shimmer.Tests.Core
{
    public class CreateReleasePackageTests : IEnableLogger
    {
        [Fact]
        public void ReleasePackageIntegrationTest()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "Shimmer.Core.1.0.0.0.nupkg");
            var outputPackage = Path.GetTempFileName() + ".nupkg";
            var sourceDir = IntegrationTestHelper.GetPath("..", "packages");

            var fixture = new ReleasePackage(inputPackage);
            (new DirectoryInfo(sourceDir)).Exists.ShouldBeTrue();

            try {
                fixture.CreateReleasePackage(outputPackage, sourceDir);

                this.Log().Info("Resulting package is at {0}", outputPackage);
                var pkg = new ZipPackage(outputPackage);

                int refs = pkg.FrameworkAssemblies.Count();
                this.Log().Info("Found {0} refs", refs);
                refs.ShouldEqual(0);

                this.Log().Info("Files in release package:");

                List<IPackageFile> files = pkg.GetFiles().ToList();
                files.ForEach(x => this.Log().Info(x.Path));

                List<string> nonDesktopPaths = new[] {"sl", "winrt", "netcore", "win8", "windows8", "MonoAndroid", "MonoTouch", "MonoMac", "wp", }
                    .Select(x => @"lib\" + x)
                    .ToList();

                files.Any(x => nonDesktopPaths.Any(y => x.Path.ToLowerInvariant().Contains(y.ToLowerInvariant()))).ShouldBeFalse();
                files.Any(x => x.Path.ToLowerInvariant().Contains(@".xml")).ShouldBeFalse();
            } finally {
                File.Delete(outputPackage);
            }
        }

        [Fact]
        public void FindPackageInOurLocalPackageList()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "Shimmer.Core.1.0.0.0.nupkg");
            var sourceDir = IntegrationTestHelper.GetPath("..", "packages");
            (new DirectoryInfo(sourceDir)).Exists.ShouldBeTrue();

            var fixture = ExposedObject.From(new ReleasePackage(inputPackage));
            IPackage result = fixture.findPackageFromName("xunit", VersionUtility.ParseVersionSpec("[1.0,2.0]"), sourceDir, null);

            result.Id.ShouldEqual("xunit");
            result.Version.Version.Major.ShouldEqual(1);
            result.Version.Version.Minor.ShouldEqual(9);
        }

        [Fact]
        public void FindDependentPackagesForDummyPackage()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "Shimmer.Core.1.0.0.0.nupkg");
            var fixture = ExposedObject.From(new ReleasePackage(inputPackage));
            var sourceDir = IntegrationTestHelper.GetPath("..", "packages");
            (new DirectoryInfo(sourceDir)).Exists.ShouldBeTrue();

            IEnumerable<IPackage> results = fixture.findAllDependentPackages(null, sourceDir);
            results.Count().ShouldBeGreaterThan(0);
        }

        [Fact]
        public void CanLoadPackageWhichHasNoDependencies()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "Shimmer.Core.NoDependencies.1.0.0.0.nupkg");
            var outputPackage = Path.GetTempFileName() + ".nupkg";
            var fixture = new ReleasePackage(inputPackage);
            var sourceDir = IntegrationTestHelper.GetPath("..", "packages");
            try {
                fixture.CreateReleasePackage(outputPackage, sourceDir);
            }
            finally {
                File.Delete(outputPackage);
            }
        }

        [Fact]
        public void CanResolveMultipleLevelsOfDependencies()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "SampleUpdatingApp.1.0.0.0.nupkg");
            var outputPackage = Path.GetTempFileName() + ".nupkg";
            var sourceDir = IntegrationTestHelper.GetPath("..", "packages");

            var fixture = new ReleasePackage(inputPackage);
            (new DirectoryInfo(sourceDir)).Exists.ShouldBeTrue();

            try {
                fixture.CreateReleasePackage(outputPackage, sourceDir);

                this.Log().Info("Resulting package is at {0}", outputPackage);
                var pkg = new ZipPackage(outputPackage);

                int refs = pkg.FrameworkAssemblies.Count();
                this.Log().Info("Found {0} refs", refs);
                refs.ShouldEqual(0);

                this.Log().Info("Files in release package:");
                pkg.GetFiles().ForEach(x => this.Log().Info(x.Path));

                var filesToLookFor = new[] {
                    "System.Reactive.Core.dll",
                    "ReactiveUI.dll",
                    "MarkdownSharp.dll",
                    "SampleUpdatingApp.exe",
                };

                filesToLookFor.ForEach(name => {
                    this.Log().Info("Looking for {0}", name);
                    pkg.GetFiles().Any(y => y.Path.ToLowerInvariant().Contains(name.ToLowerInvariant())).ShouldBeTrue();
                });
            } finally {
                File.Delete(outputPackage);
            }
        }

        [Fact]
        public void SpecFileMarkdownRenderingTest()
        {
            var dontcare = IntegrationTestHelper.GetPath("fixtures", "Shimmer.Core.1.1.0.0.nupkg");
            var inputSpec = IntegrationTestHelper.GetPath("fixtures", "Shimmer.Core.1.1.0.0.nuspec");
            var fixture = new ReleasePackage(dontcare);

            var targetFile = Path.GetTempFileName();
            File.Copy(inputSpec, targetFile, true);
                
            try {
                var processor = new Func<string, string>(input =>
                    (new Markdown()).Transform(input));

                // NB: For No Reason At All, renderReleaseNotesMarkdown is 
                // invulnerable to ExposedObject. Whyyyyyyyyy
                var renderMinfo = fixture.GetType().GetMethod("renderReleaseNotesMarkdown", 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                renderMinfo.Invoke(fixture, new object[] {targetFile, processor});

                var doc = XDocument.Load(targetFile);
                XNamespace ns = "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd";
                var relNotesElement = doc.Descendants(ns + "releaseNotes").First();
                var htmlText = relNotesElement.Value;

                this.Log().Info("HTML Text:\n{0}", htmlText);

                htmlText.Contains("## Release Notes").ShouldBeFalse();
            } finally {
                File.Delete(targetFile);
            }
        }

        [Fact]
        public void UsesTheRightVersionOfADependencyWhenMultipleAreInPackages()
        {
            var outputPackage = Path.GetTempFileName() + ".nupkg";
            string outputFile = null;

            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "CaliburnMicroDemo.1.0.0.nupkg");

            var wrongPackage = "Caliburn.Micro.1.4.1.nupkg";
            var wrongPackagePath = IntegrationTestHelper.GetPath("fixtures", wrongPackage);
            var rightPackage = "Caliburn.Micro.1.5.2.nupkg";
            var rightPackagePath = IntegrationTestHelper.GetPath("fixtures", rightPackage);

            try {
                var sourceDir = IntegrationTestHelper.GetPath("..", "packages");
                (new DirectoryInfo(sourceDir)).Exists.ShouldBeTrue();

                File.Copy(wrongPackagePath, Path.Combine(sourceDir, wrongPackage), true);
                File.Copy(rightPackagePath, Path.Combine(sourceDir, rightPackage), true);

                var package = new ReleasePackage(inputPackage);
                var outputFileName = package.CreateReleasePackage(outputPackage, sourceDir);

                var zipPackage = new ZipPackage(outputFileName);

                var fileName = "Caliburn.Micro.dll";
                var dependency = zipPackage.GetLibFiles()
                    .Where(f => f.Path.EndsWith(fileName))
                    .Single(f => f.TargetFramework 
                        == new FrameworkName(".NETFramework,Version=v4.0"));

                outputFile = new FileInfo(Path.Combine(sourceDir, fileName)).FullName;

                using (var of = File.Create(outputFile))
                {
                    dependency.GetStream().CopyTo(of);
                }

                var assemblyName = AssemblyName.GetAssemblyName(outputFile);
                Assert.Equal(1, assemblyName.Version.Major);
                Assert.Equal(5, assemblyName.Version.Minor);
            }
            finally {
                File.Delete(outputPackage);
                File.Delete(outputFile);
            }
        }
    }
}