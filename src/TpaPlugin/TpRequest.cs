using PluginManager.Api.Contracts;

namespace TpaPlugin;

public class TpRequest
{
    public ClientInfo Sender { get; set; }
    public long ExpiredAt { get; set; }
}
