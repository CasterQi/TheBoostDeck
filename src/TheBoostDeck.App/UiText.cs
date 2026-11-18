namespace TheBoostDeck.App;

internal static class UiText
{
    internal const string Chinese = "zh-CN";
    internal const string English = "en-US";

    internal static string Normalize(string? language)
    {
        return language?.Trim().ToLowerInvariant() switch
        {
            "en" or "en-us" or "en-gb" => English,
            "zh" or "zh-cn" or "zh-hans" => Chinese,
            _ => Chinese
        };
    }

    internal static string Get(string? language, string key)
    {
        var isEnglish = Normalize(language) == English;
        return key switch
        {
            "ShowHide" => isEnglish ? "Show / Hide" : "显示 / 隐藏",
            "Starting" => isEnglish ? "TheBoostDeck is starting…" : "TheBoostDeck 正在启动…",
            "PreparingSensors" => isEnglish ? "Preparing sensors and interface" : "正在准备传感器与界面",
            "CheckingDriver" => isEnglish ? "Checking the sensor driver…" : "正在检查传感器驱动…",
            "InstallingDriver" => isEnglish ? "Installing the sensor driver…" : "正在安装传感器驱动…",
            "StartingCollector" => isEnglish ? "Initializing hardware monitoring…" : "正在初始化硬件监控…",
            "StartingInterface" => isEnglish ? "Preparing the dashboard…" : "正在准备仪表盘…",
            "MoveToDisplay" => isEnglish ? "Move to display" : "移动到屏幕",
            "Skins" => isEnglish ? "Skins" : "皮肤",
            "ClassicSkin" => isEnglish ? "Classic HUD" : "经典 HUD",
            "ClassicSkinDescription" => isEnglish
                ? "The built-in responsive performance HUD."
                : "内置的自适应性能 HUD。",
            "ImportSkin" => isEnglish ? "Import skin…" : "导入皮肤…",
            "SkinFileFilter" => isEnglish
                ? "TheBoostDeck skins (*.boostskin)|*.boostskin|All files (*.*)|*.*"
                : "TheBoostDeck 皮肤 (*.boostskin)|*.boostskin|所有文件 (*.*)|*.*",
            "SkinExportTitle" => isEnglish ? "Export skin" : "导出皮肤",
            "SkinExportFilter" => isEnglish
                ? "TheBoostDeck skins (*.boostskin)|*.boostskin|All files (*.*)|*.*"
                : "TheBoostDeck 皮肤 (*.boostskin)|*.boostskin|所有文件 (*.*)|*.*",
            "SkinImported" => isEnglish ? "Skin imported." : "皮肤已导入。",
            "SkinExported" => isEnglish ? "Skin exported." : "皮肤已导出。",
            "SkinImportFailed" => isEnglish
                ? "The skin could not be imported. Check that it is a valid .boostskin package."
                : "皮肤导入失败，请确认它是有效的 .boostskin 包。",
            "SkinExportFailed" => isEnglish
                ? "The skin could not be exported."
                : "皮肤导出失败。",
            "SkinDeleted" => isEnglish ? "Skin deleted." : "皮肤已删除。",
            "SkinDeleteFailed" => isEnglish
                ? "The skin could not be deleted. Close the files that are in use and try again."
                : "皮肤删除失败，请关闭正在占用相关文件的程序后重试。",
            "SkinDeleteBuiltIn" => isEnglish
                ? "The built-in skin cannot be deleted."
                : "内置皮肤不支持删除。",
            "SkinDeleteLast" => isEnglish
                ? "Keep at least one skin installed before removing the built-in skin."
                : "请至少保留一个已安装的皮肤，再移除内置皮肤。",
            "SkinBuiltInRemoved" => isEnglish
                ? "The built-in skin was removed from the list."
                : "内置皮肤已从列表移除。",
            "SkinBuiltInRestored" => isEnglish
                ? "The built-in skin was restored."
                : "内置皮肤已恢复。",
            "RestoreBuiltInSkin" => isEnglish ? "Restore built-in skin" : "恢复内置皮肤",
            "SkinBundledRestored" => isEnglish
                ? "Bundled skins were restored."
                : "自带皮肤已恢复。",
            "SkinBundledRestoreFailed" => isEnglish
                ? "The bundled skins could not be restored."
                : "自带皮肤恢复失败。",
            "SkinBuiltInNoExport" => isEnglish
                ? "The built-in skin cannot be exported."
                : "内置皮肤不支持导出。",
            "SkinSkillExportTitle" => isEnglish ? "Export skin author skill" : "导出皮肤制作 Skill",
            "SkinSkillExportFilter" => isEnglish
                ? "TheBoostDeck skill (*.zip)|*.zip|All files (*.*)|*.*"
                : "TheBoostDeck Skill (*.zip)|*.zip|所有文件 (*.*)|*.*",
            "SkinSkillExported" => isEnglish
                ? "Skin author skill exported."
                : "皮肤制作 Skill 已导出。",
            "SkinSkillExportFailed" => isEnglish
                ? "The skin author skill could not be exported. Please try again."
                : "皮肤制作 Skill 导出失败，请稍后重试。",
            "SkinSkillNoTelemetry" => isEnglish
                ? "The hardware collector is not ready; the skill cannot be exported yet."
                : "硬件采集器尚未就绪，无法导出 Skill。",
            "SkinLoadFailed" => isEnglish
                ? "The selected skin could not be loaded. The current skin has been kept.\n\nCheck that it is a valid .boostskin package and that its entry HTML exists."
                : "所选皮肤无法加载，当前皮肤保持不变。\n\n请确认它是有效的 .boostskin 包，且入口 HTML 存在。",
            "SkinErrorTitle" => isEnglish ? "Skin could not be loaded" : "皮肤加载失败",
            "AlwaysOnTop" => isEnglish ? "Always on top" : "始终置顶",
            "RefreshRate" => isEnglish ? "Refresh rate" : "更新频率",
            "Zoom" => isEnglish ? "Zoom" : "缩放",
            "StartWithWindows" => isEnglish ? "Start with Windows" : "开机自启",
            "StartupFailedTitle" => isEnglish ? "Startup setting failed" : "开机自启设置失败",
            "StartupFailed" => isEnglish
                ? "The startup setting could not be updated."
                : "无法更新开机自启设置。",
            "Language" => isEnglish ? "Language" : "语言",
            "Chinese" => isEnglish ? "Chinese" : "中文",
            "English" => "English",
            "Exit" => isEnglish ? "Exit" : "退出",
            "PrimaryDisplay" => isEnglish ? "Primary" : "主屏",
            "TrayTooltip" => isEnglish ? "TheBoostDeck performance monitor" : "TheBoostDeck 性能监控",
            "WebViewInitError" => isEnglish
                ? "TheBoostDeck could not initialize WebView2.\n\nMake sure Microsoft Edge WebView2 Runtime is available and check %LocalAppData%\\TheBoostDeck\\logs."
                : "TheBoostDeck 无法初始化 WebView2。\n\n请确认 Microsoft Edge WebView2 Runtime 可用，并查看 %LocalAppData%\\TheBoostDeck\\logs。",
            "HardwareCollectorFailed" => isEnglish
                ? "The hardware collector could not start. Check LibreHardwareMonitor, driver, and security software logs."
                : "硬件采集器初始化失败，请检查 LibreHardwareMonitor、驱动与安全软件日志",
            "ApplicationStartupFailed" => isEnglish
                ? "TheBoostDeck could not start. Check %LocalAppData%\\TheBoostDeck\\logs."
                : "TheBoostDeck 启动失败，请查看 %LocalAppData%\\TheBoostDeck\\logs。",
            _ => key
        };
    }
}
