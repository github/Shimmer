﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using Squirrel.Client;
using Squirrel.Core;
using Squirrel.Tests.TestHelpers;
using ReactiveUIMicro;
using Xunit;

namespace Squirrel.Tests.Client
{
#if FALSE
    public class BitsManagerTests
    {
        [Fact(Skip = "Reenable this once we have proper BITS bindings")]
        public void BitsDownloadsSomeUrls()
        {
            var urls = new[] {
                "https://a248.e.akamai.net/assets.github.com/images/modules/about_page/octocat.png",
                "https://a248.e.akamai.net/assets.github.com/images/modules/about_page/github_logo.png",
            };

            var files = new[] {
                "octocat.png",
                "gh_logo.png",
            };

            string tempPath = null;
            using (Utility.WithTempDirectory(out tempPath))
            using (var fixture = new BitsUrlDownloader("BITSTests")) {
                fixture.QueueBackgroundDownloads(urls, files.Select(x => Path.Combine(tempPath, x)))
                    .Timeout(TimeSpan.FromSeconds(120), RxApp.TaskpoolScheduler)
                    .Last();

                files.Select(x => Path.Combine(tempPath, x))
                    .Select(x => new FileInfo(x))
                    .ForEach(x => {
                        x.Exists.ShouldBeTrue(); 
                        x.Length.ShouldNotEqual(0);
                    });
            }
        }

        [Fact(Skip = "Reenable this once we have proper BITS bindings")]
        public void BitsFailsOnGarbageUrls()
        {
            var urls = new[] {
                "https://example.com/nothere.png",
                "https://a248.e.akamai.net/assets.github.com/images/modules/about_page/github_logo.png",
            };

            var files = new[] {
                "octocat.png",
                "gh_logo.png",
            };

            string tempPath = null;
            using (Utility.WithTempDirectory(out tempPath))
            using (var fixture = new BitsUrlDownloader("BITSTests")) {
                Assert.Throws<Exception>(() => {
                    fixture.QueueBackgroundDownloads(urls, files.Select(x => Path.Combine(tempPath, x)))
                        .Timeout(TimeSpan.FromSeconds(120), RxApp.TaskpoolScheduler)
                        .Last();
                });
            }
        }
    }
#endif
}
