using System;
using PluginManager.Api.Contracts;

namespace TpaPlugin;

public class TpRequest
{
    public ClientInfo Sender { get; set; }
    public DateTime CreatedAt { get; set; }
}
