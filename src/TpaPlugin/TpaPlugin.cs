using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PluginManager.Api;
using PluginManager.Api.Capabilities.Implementations.Commands;
using PluginManager.Api.Capabilities.Implementations.Events.GameEvents;
using PluginManager.Api.Capabilities.Implementations.Translations;
using PluginManager.Api.Capabilities.Implementations.Utils;
using PluginManager.Api.Contracts;
using PluginManager.Api.Hooks;
using PluginManager.Config;
using PluginManager.Localization;

namespace TpaPlugin;

public class TpaPlugin : BasePlugin
{
    public override string ModuleName => "TpaPlugin";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "kotfoxtrot";
    public override string ModuleDescription => "Player to player teleport request";

    private IPlayerLocalization _localization;
    private IPlayerUtil _playerUtil;
    private PluginConfig _pluginConfig;

    private Dictionary<string, ClientInfo> _online = new();
    private readonly Dictionary<string, TpRequest> _pending = new();
    private readonly Dictionary<string, DateTime> _cooldowns = new();

    private const int RequestTimeoutSeconds = 60;

    protected override void OnLoad()
    {
        _playerUtil = Capabilities.Get<IPlayerUtil>();
        var playerLanguageStore = Capabilities.Get<IPlayerLanguageStore>();
        _localization = new JsonPlayerLocalizationFactory(playerLanguageStore).Create(Path.Combine(ModulePath, "lang"));
        _pluginConfig = new JsonConfigReader().Read<PluginConfig>(Path.Combine(ModulePath, "config.json"));
        _online = _playerUtil.GetClientInfoList().ToDictionary(info => info.CrossplatformId, info => info);

        RegisterCommand("tp", "Request teleport to a player", OnTp);
        RegisterCommand("tpa", "Accept pending teleport request", OnAccept);
        RegisterCommand("tpd", "Deny pending teleport request", OnDeny);

        RegisterEventHandler<PlayerJoinedGameEvent>(OnPlayerJoined, HookMode.Post);
        RegisterEventHandler<PlayerSpawnedInWorldEvent>(OnPlayerSpawned, HookMode.Post);
        RegisterEventHandler<PlayerDisconnectedEvent>(OnPlayerDisconnected, HookMode.Post);
    }

    private HookResult OnPlayerJoined(PlayerJoinedGameEvent evt)
    {
        _online[evt.ClientInfo.CrossplatformId] = evt.ClientInfo;
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawned(PlayerSpawnedInWorldEvent evt)
    {
        _online[evt.ClientInfo.CrossplatformId] = evt.ClientInfo;
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnected(PlayerDisconnectedEvent evt)
    {
        _online.Remove(evt.ClientInfo.CrossplatformId);
        _pending.Remove(evt.ClientInfo.CrossplatformId);
        return HookResult.Continue;
    }

    private void OnTp(ICommandContext ctx)
    {
        _online[ctx.ClientInfo.CrossplatformId] = ctx.ClientInfo;

        if (ctx.Args.Count < 1)
        {
            Reply(ctx.ClientInfo, "Bad args tp");
            return;
        }

        var senderId = ctx.ClientInfo.CrossplatformId;

        if (_cooldowns.TryGetValue(senderId, out var last))
        {
            var remaining = (int)(_pluginConfig.Delay - (DateTime.UtcNow - last).TotalSeconds);
            if (remaining > 0)
            {
                Reply(ctx.ClientInfo, "Cooldown", remaining / 60, remaining % 60);
                return;
            }
        }

        var targetName = ctx.Args[0];
        var target = FindByName(targetName);

        if (target == null)
        {
            Reply(ctx.ClientInfo, "Player not found", targetName);
            return;
        }

        if (target.CrossplatformId == senderId)
        {
            Reply(ctx.ClientInfo, "Self teleport");
            return;
        }

        _pending[target.CrossplatformId] = new TpRequest
        {
            Sender = ctx.ClientInfo,
            CreatedAt = DateTime.UtcNow
        };

        Reply(ctx.ClientInfo, "Request sent", target.Name);
        Reply(target, "Request received", ctx.ClientInfo.Name);
    }

    private void OnAccept(ICommandContext ctx)
    {
        _online[ctx.ClientInfo.CrossplatformId] = ctx.ClientInfo;

        if (!_pending.TryGetValue(ctx.ClientInfo.CrossplatformId, out var req))
        {
            Reply(ctx.ClientInfo, "No pending request");
            return;
        }

        _pending.Remove(ctx.ClientInfo.CrossplatformId);

        if ((DateTime.UtcNow - req.CreatedAt).TotalSeconds > RequestTimeoutSeconds)
        {
            Reply(ctx.ClientInfo, "Request expired");
            return;
        }

        if (!_online.ContainsKey(req.Sender.CrossplatformId))
        {
            Reply(ctx.ClientInfo, "Sender offline", req.Sender.Name);
            return;
        }

        var position = _playerUtil.GetPlayerPosition(ctx.ClientInfo.EntityId);
        if (position == null)
        {
            Reply(ctx.ClientInfo, "Target position unknown");
            return;
        }

        _playerUtil.Teleport(req.Sender.EntityId, position);
        _cooldowns[req.Sender.CrossplatformId] = DateTime.UtcNow;

        Reply(req.Sender, "Request accepted", ctx.ClientInfo.Name);
        Reply(ctx.ClientInfo, "You accepted", req.Sender.Name);
    }

    private void OnDeny(ICommandContext ctx)
    {
        if (!_pending.TryGetValue(ctx.ClientInfo.CrossplatformId, out var req))
        {
            Reply(ctx.ClientInfo, "No pending request");
            return;
        }

        _pending.Remove(ctx.ClientInfo.CrossplatformId);

        Reply(ctx.ClientInfo, "You denied", req.Sender.Name);

        if (_online.ContainsKey(req.Sender.CrossplatformId))
        {
            Reply(req.Sender, "Request denied", ctx.ClientInfo.Name);
        }
    }

    private ClientInfo FindByName(string name)
    {
        foreach (var ci in _online.Values)
        {
            if (string.Equals(ci.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return ci;
            }
        }

        return null;
    }

    private void Reply(ClientInfo ci, string key, params object[] args)
    {
        var tag = _localization.Translate(ci.CrossplatformId, "Tag");
        var text = _localization.Translate(ci.CrossplatformId, key, args);
        _playerUtil.PrintToChat(ci.EntityId, $"{tag}{text}");
    }
}