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

    private readonly Dictionary<string, TpRequest> _pending = new();
    private readonly Dictionary<string, long> _nextTeleportTime = new();

    protected override void OnLoad()
    {
        _localization = GetPlayerLocalization();
        _pluginConfig = ReadPluginConfig();
        _playerUtil = Capabilities.Get<IPlayerUtil>();

        RegisterCommand("tp", "Request teleport to a player", OnTp);
        RegisterCommand("tpa", "Accept pending teleport request", OnAccept);
        RegisterCommand("tpd", "Deny pending teleport request", OnDeny);

        RegisterEventHandler<PlayerDisconnectedEvent>(OnPlayerDisconnected, HookMode.Post);
    }

    private HookResult OnPlayerDisconnected(PlayerDisconnectedEvent evt)
    {
        _pending.Remove(evt.ClientInfo.CrossplatformId);
        return HookResult.Continue;
    }

    private void OnTp(ICommandContext ctx)
    {
        if (ctx.Args.Count < 1)
        {
            Reply(ctx.ClientInfo, "Bad args tp");
            return;
        }

        var senderId = ctx.ClientInfo.CrossplatformId;

        var unixTime = DateTimeOffset.Now.ToUnixTimeSeconds();

        if (_nextTeleportTime.TryGetValue(senderId, out var nextTeleportTime) && nextTeleportTime > unixTime)
        {
            var cooldown = TimeSpan.FromSeconds(unixTime - nextTeleportTime);
            Reply(ctx.ClientInfo, "Teleport cooldown", cooldown);
            return;
        }

        var targetName = ctx.Args[0];
        var target = FindTargetByName(targetName);

        switch (target.Count)
        {
            case 0:
                Reply(ctx.ClientInfo, "Player not found", targetName);
                return;
            case > 1:
                Reply(ctx.ClientInfo, "Many results", targetName);
                return;
        }

        var targetClient = target[0];

        if (targetClient.CrossplatformId == senderId)
        {
            Reply(ctx.ClientInfo, "Self teleport");
            return;
        }

        _pending[targetClient.CrossplatformId] = new TpRequest
        {
            Sender = ctx.ClientInfo,
            ExpiredAt = unixTime + _pluginConfig.RequestTimeout,
        };

        Reply(ctx.ClientInfo, "Request sent", targetClient.Name);
        Reply(targetClient, "Request received", ctx.ClientInfo.Name);
    }

    private void OnAccept(ICommandContext ctx)
    {
        if (!_pending.TryGetValue(ctx.ClientInfo.CrossplatformId, out var req))
        {
            Reply(ctx.ClientInfo, "No pending request");
            return;
        }

        var unixTime = DateTimeOffset.Now.ToUnixTimeSeconds();

        _pending.Remove(ctx.ClientInfo.CrossplatformId);

        if (req.ExpiredAt < unixTime)
        {
            Reply(ctx.ClientInfo, "Request expired");
            return;
        }

        if (_playerUtil.GetClientInfoByEntityId(req.Sender.EntityId) == null)
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
        _nextTeleportTime[req.Sender.CrossplatformId] = unixTime + _pluginConfig.Delay;
        ;

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
        Reply(req.Sender, "Request denied", ctx.ClientInfo.Name);
    }

    private List<ClientInfo> FindTargetByName(string name)
    {
        return _playerUtil.GetClientInfoList()
            .Where(client => client.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
    }

    private void Reply(ClientInfo ci, string key, params object[] args)
    {
        var tag = _localization.Translate(ci.CrossplatformId, "Tag");
        var text = _localization.Translate(ci.CrossplatformId, key, args);
        _playerUtil.PrintToChat(ci.EntityId, $"{tag}{text}");
    }

    private IPlayerLocalization GetPlayerLocalization()
    {
        var playerLanguageStore = Capabilities.Get<IPlayerLanguageStore>();
        return _localization = new JsonPlayerLocalizationFactory(playerLanguageStore)
            .Create(Path.Combine(ModulePath, "lang"));
    }

    private PluginConfig ReadPluginConfig()
    {
        return new JsonConfigReader().Read<PluginConfig>(Path.Combine(ModulePath, "config.json"));
    }
}