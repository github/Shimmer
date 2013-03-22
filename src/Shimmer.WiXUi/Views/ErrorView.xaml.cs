using System;
using System.Reactive.Linq;
using System.Windows;
using ReactiveUI;
using Shimmer.WiXUi.ViewModels;

namespace Shimmer.WiXUi.Views
{
    public partial class ErrorView : IViewFor<ErrorViewModel>
    {
        public ErrorView()
        {
            InitializeComponent();

            this.WhenAny(x => x.ViewModel.Error, x => x.Value)
                .Where(x => x != null)
                .Select(x => String.Format("{0}\n{1}", x.ErrorMessage, x.ErrorCauseOrResolution))
                .BindTo(this, x => x.ErrorMessage.Text);
        }

        public ErrorViewModel ViewModel {
            get { return (ErrorViewModel)GetValue(ViewModelProperty); }
            set { SetValue(ViewModelProperty, value); }
        }
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register("ViewModel", typeof(ErrorViewModel), typeof(ErrorView), new PropertyMetadata(null));

        object IViewFor.ViewModel {
            get { return ViewModel; }
            set { ViewModel = (ErrorViewModel) value; }
        }
    }
}
