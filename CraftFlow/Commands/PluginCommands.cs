using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using CraftFlow.UI;

namespace CraftFlow.Commands;

/// <summary>
/// 插件斜杠命令管理，注册 /craftflow 命令用于开关主窗口。
/// </summary>
public sealed class PluginCommands : IDisposable
{
    private const string CommandName = "/craftflow";
    private readonly ICommandManager _commandManager;
    private readonly MainWindow _mainWindow;
    private readonly IPluginLog _log;

    /// <summary>
    /// 初始化斜杠命令并注册到 Dalamud 命令系统。
    /// </summary>
    /// <param name="commandManager">Dalamud 命令管理器。</param>
    /// <param name="mainWindow">主窗口实例。</param>
    /// <param name="log">插件日志。</param>
    public PluginCommands(ICommandManager commandManager, MainWindow mainWindow, IPluginLog log)
    {
        _commandManager = commandManager;
        _mainWindow = mainWindow;
        _log = log;

        _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开/关闭 CraftFlow 生产辅助窗口"
        });

        _log.Debug("已注册 /craftflow 命令");
    }

    /// <summary>
    /// 处理 /craftflow 命令调用，切换主窗口可见性。
    /// </summary>
    private void OnCommand(string command, string arguments)
    {
        _mainWindow.IsOpen = !_mainWindow.IsOpen;
        _log.Debug($"主窗口状态: {(_mainWindow.IsOpen ? "已打开" : "已关闭")}");
    }

    /// <summary>
    /// 释放命令注册。
    /// </summary>
    public void Dispose()
    {
        _commandManager.RemoveHandler(CommandName);
        _log.Debug("已注销 /craftflow 命令");
    }
}
