﻿using System;
using System.Reactive.Linq;
using NuGet;
using ReactiveUIMicro;
using ReactiveUIMicro.Routing;
using Squirrel.Client.WiXUi;

namespace Squirrel.WiXUi.ViewModels
{
    public class UninstallingViewModel : ReactiveObject, IUninstallingViewModel
    {
        public string UrlPathSegment { get { return "installing";  } }
        public IScreen HostScreen { get; private set; }

#pragma warning disable 649
        IPackage _PackageMetadata;
#pragma warning restore 649
        public IPackage PackageMetadata {
            get { return _PackageMetadata; }
            set { this.RaiseAndSetIfChanged(x => x.PackageMetadata, value); }
        }

        public IObserver<int> ProgressValue { get; private set; }

#pragma warning disable 649
        ObservableAsPropertyHelper<int> _LatestProgress;
#pragma warning restore 649
        public int LatestProgress {
            get { return _LatestProgress.Value; }
        }

#pragma warning disable 649
        ObservableAsPropertyHelper<string> _Title;
#pragma warning restore 649
        public string Title {
            get { return _Title.Value; }
        }

#pragma warning disable 649
        ObservableAsPropertyHelper<string> _Description;
#pragma warning restore 649
        public string Description {
            get { return _Description.Value; }
        }

        public UninstallingViewModel(IScreen hostScreen)
        {
            HostScreen = hostScreen;

            var progress = new ScheduledSubject<int>(RxApp.DeferredScheduler);
            ProgressValue = progress;

            progress.ToProperty(this, x => x.LatestProgress, 0);

            this.WhenAny(x => x.PackageMetadata, x => x.Value)
                .SelectMany(x => x != null ? Observable.Return(x.Title) : Observable.Empty<string>())
                .ToProperty(this, x => x.Title);

            this.WhenAny(x => x.PackageMetadata, x => x.Value)
                .SelectMany(x => x != null ? Observable.Return(x.Description) : Observable.Empty<string>())
                .ToProperty(this, x => x.Description);
        }
    }
}
