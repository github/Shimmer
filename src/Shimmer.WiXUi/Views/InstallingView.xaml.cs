using System.Windows;
using System.Windows.Controls;
using ReactiveUI;
using Shimmer.WiXUi.ViewModels;

namespace Shimmer.WiXUi.Views
{
    public partial class InstallingView : IViewFor<InstallingViewModel>
    {
        public InstallingView()
        {
            InitializeComponent();

            this.OneWayBind(ViewModel, x => x.LatestProgress, x => x.ProgressValue.Value);
        }

        public InstallingViewModel ViewModel {
            get { return (InstallingViewModel)GetValue(ViewModelProperty); }
            set { SetValue(ViewModelProperty, value); }
        }
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register("ViewModel", typeof(InstallingViewModel), typeof(InstallingView), new PropertyMetadata(null));

        object IViewFor.ViewModel {
            get { return ViewModel; }
            set { ViewModel = (InstallingViewModel) value; }
        }
    }
}
