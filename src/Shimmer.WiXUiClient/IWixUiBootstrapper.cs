using ReactiveUI.Routing;

namespace Shimmer.Client.WiXUi
{
    public interface IWixUiBootstrapper : IScreen
    {
        IWiXEvents WiXEvents { get; }
    }
}