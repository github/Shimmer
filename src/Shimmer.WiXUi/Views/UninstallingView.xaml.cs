using System.Reactive.Linq;
using System.Windows;
using ReactiveUI;
using Shimmer.WiXUi.ViewModels;

namespace Shimmer.WiXUi.Views
{
    public partial class UninstallingView : IViewFor<UninstallingViewModel>
    {
        public UninstallingView()
        {
            InitializeComponent();

            this.WhenAny(x => x.ViewModel.LatestProgress, x => (double) x.Value)
                .ObserveOn(RxApp.DeferredScheduler) // XXX: WHYYYYY
                .BindTo(ProgressValue, x => x.Value);
        }

        public UninstallingViewModel ViewModel {
            get { return (UninstallingViewModel)GetValue(ViewModelProperty); }
            set { SetValue(ViewModelProperty, value); }
        }
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register("ViewModel", typeof(UninstallingViewModel), typeof(UninstallingView), new PropertyMetadata(null));

        object IViewFor.ViewModel {
            get { return ViewModel; }
            set { ViewModel = (UninstallingViewModel) value; }
        }
    }
}
