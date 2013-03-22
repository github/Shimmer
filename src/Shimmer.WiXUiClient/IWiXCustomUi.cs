using TinyIoC;

namespace Shimmer.Client.WiXUi
{
    public interface IWiXCustomUi
    {
        void RegisterTypes(TinyIoCContainer kernel);
    }
}
