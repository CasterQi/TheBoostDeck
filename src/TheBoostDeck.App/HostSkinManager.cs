using System.Text.Json;

namespace TheBoostDeck.App;

/// <summary>
/// Injects and controls the host-owned control center overlay inside the active
/// skin WebView. The overlay is an iframe served from the host's own virtual
/// host, so skins never need to copy the settings or manager UI.
/// </summary>
internal static class HostSkinManager
{
    internal const string VirtualHost = "panel.theboostdeck";
    internal const string PageName = "host-skin-manager.html";
    private const string FrameId = "__tbd-host-skin-manager";

    internal static string BuildOpenScript(string pageUrl, string tab)
    {
        var url = JsonSerializer.Serialize(pageUrl);
        var tabName = JsonSerializer.Serialize(tab);
        return $$"""
            (() => {
              const frameId = '{{FrameId}}';
              const targetTab = {{tabName}};
              const existingFrame = document.getElementById(frameId);
              if (existingFrame && existingFrame.contentWindow) {
                existingFrame.contentWindow.postMessage({ __tbdHostManagerNavigate: true, tab: targetTab }, '*');
                return true;
              }

              if (!window.__tbdHostManagerOpen) {
                const style = document.createElement('style');
                style.id = '__tbd-host-skin-manager-style';
                style.textContent = `#${frameId} { position: fixed; inset: 0; width: 100%; height: 100%; border: 0; z-index: 2147483647; background: transparent; display: block; }`;
                document.head.appendChild(style);

                window.__tbdHostManagerPost = (message) => {
                  const frame = document.getElementById(frameId);
                  if (frame && frame.contentWindow) {
                    frame.contentWindow.postMessage({ __tbdHostManagerMessage: true, payload: message }, '*');
                  }
                };

                if (window.chrome && window.chrome.webview) {
                  window.chrome.webview.addEventListener('message', (event) => {
                    const data = event.data;
                    if (data && (data.type === 'skin-catalog' || data.type === 'skin-action' || data.type === 'settings' || data.type === 'settings-error')) {
                      window.__tbdHostManagerPost(data);
                    }
                  });
                }

                window.addEventListener('message', (event) => {
                  const frame = document.getElementById(frameId);
                  if (!frame || event.source !== frame.contentWindow) return;
                  const data = event.data;
                  if (!data || data.__tbdHostManagerRequest !== true) return;
                  if (data.type === 'close') {
                    frame.remove();
                    return;
                  }
                  if (data.type === 'request' && window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage(data.payload);
                  }
                });

                window.__tbdHostManagerOpen = (source) => {
                  if (document.getElementById(frameId)) return;
                  const frame = document.createElement('iframe');
                  frame.id = frameId;
                  frame.title = 'TheBoostDeck control center';
                  frame.allow = 'clipboard-write';
                  frame.src = source;
                  document.body.appendChild(frame);
                };
              }

              window.__tbdHostManagerOpen({{url}});
              return true;
            })();
            """;
    }
}
