/*
 * TheBoostDeck reusable skin manager controller.
 * Requires the block from skin-manager.html and the scoped skin-manager.css.
 * Mount it from a visible control:
 *
 *   TheBoostDeckSkinManager.mount({
 *     trigger: document.querySelector('[data-tbd-skin-manager-trigger]')
 *   });
 *
 * The component talks to the host through the existing chrome.webview bridge and
 * implements the same catalog, switch, import, export, and export-skill actions
 * as the built-in manager.
 */
(function (global) {
  'use strict';

  var LABELS = {
    'zh-CN': {
      kicker: 'UI 皮肤',
      title: '皮肤管理',
      subtitle: '选择皮肤，或导入/导出 .boostskin 文件。',
      close: '关闭皮肤管理',
      loading: '正在读取皮肤列表…',
      empty: '还没有可用的皮肤。',
      count: '共 {count} 个皮肤',
      countOne: '共 1 个皮肤',
      active: '使用中',
      builtIn: '内置',
      noPreview: '无预览图',
      import: '导入皮肤',
      exportSkill: '导出 Skill',
      exportSkillHint: '导出包含本机传感器清单的皮肤制作 Skill',
      exportCurrent: '导出当前皮肤',
      exportDisabled: '内置皮肤不可导出',
      requestFailed: '无法读取皮肤列表。',
      selecting: '正在切换皮肤…',
      actionFailed: '操作失败。',
      webOnly: '当前不在 TheBoostDeck 宿主中，无法读取皮肤列表。'
    },
    'en-US': {
      kicker: 'UI SKINS',
      title: 'Skin manager',
      subtitle: 'Choose a skin, or import/export a .boostskin file.',
      close: 'Close skin manager',
      loading: 'Reading skin list…',
      empty: 'No skins are available yet.',
      count: '{count} skins available',
      countOne: '1 skin available',
      active: 'Active',
      builtIn: 'Built in',
      noPreview: 'No preview',
      import: 'Import skin',
      exportSkill: 'Export skill',
      exportSkillHint: 'Export a skin-authoring skill with this machine\'s sensor inventory',
      exportCurrent: 'Export active skin',
      exportDisabled: 'Built-in skins cannot be exported',
      requestFailed: 'Could not load the skin list.',
      selecting: 'Switching skin…',
      actionFailed: 'The action failed.',
      webOnly: 'TheBoostDeck host is not available, so the skin list cannot be read.'
    }
  };

  function normalizeLanguage(value) {
    var text = String(value || '').toLowerCase();
    return text.indexOf('en') === 0 ? 'en-US' : 'zh-CN';
  }

  function resolveElement(value) {
    if (!value) return null;
    if (typeof value === 'string') return document.querySelector(value);
    return value.nodeType === 1 ? value : null;
  }

  function createSvg(pathData) {
    var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('class', 'tbd-sm-icon');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('aria-hidden', 'true');
    var path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    path.setAttribute('d', pathData);
    svg.appendChild(path);
    return svg;
  }

  function createPlaceholder(labels) {
    var placeholder = document.createElement('div');
    placeholder.className = 'tbd-sm-placeholder';
    placeholder.appendChild(createSvg('M12 3l3 5.4L22 12l-7 3.6L12 21l-3-5.4L2 12l7-3.6z'));
    var text = document.createElement('div');
    text.textContent = labels.noPreview;
    placeholder.appendChild(text);
    return placeholder;
  }

  function mount(options) {
    options = options || {};
    var root = resolveElement(options.root || '[data-tbd-skin-manager]');
    if (!root) throw new Error('TheBoostDeckSkinManager: manager markup was not found.');
    if (root.__tbdSkinManager) return root.__tbdSkinManager;

    var trigger = resolveElement(options.trigger || '[data-tbd-skin-manager-trigger]');
    var closeButton = root.querySelector('[data-tbd-close]');
    var grid = root.querySelector('[data-tbd-grid]');
    var status = root.querySelector('[data-tbd-status]');
    var exportButton = root.querySelector('[data-tbd-export-current]');
    var importButton = root.querySelector('[data-tbd-import]');
    var exportSkillButton = root.querySelector('[data-tbd-export-skill]');
    if (!closeButton || !grid || !status || !exportButton || !importButton || !exportSkillButton) {
      throw new Error('TheBoostDeckSkinManager: manager markup is incomplete.');
    }

    var language = normalizeLanguage(options.language || document.documentElement.lang || navigator.language);
    var labels = LABELS[language] || LABELS['zh-CN'];
    var catalog = null;
    var previousFocus = null;
    var isOpen = false;
    var host = global.chrome && global.chrome.webview;

    function post(message) {
      if (!host || typeof host.postMessage !== 'function') return false;
      host.postMessage(message);
      return true;
    }

    function setStatus(message, isError) {
      status.textContent = message || '';
      status.dataset.tone = isError ? 'error' : 'info';
    }

    function countText(count) {
      return count === 1
        ? labels.countOne
        : labels.count.replace('{count}', String(count));
    }

    function updateExportButton() {
      var active = catalog && Array.isArray(catalog.skins)
        ? catalog.skins.find(function (skin) { return String(skin.id) === String(catalog.activeId); })
        : null;
      var canExport = Boolean(active && !active.isBuiltIn && active.canExport !== false);
      exportButton.disabled = !canExport;
      exportButton.title = canExport ? '' : (active && active.isBuiltIn ? labels.exportDisabled : '');
    }

    function applyLabels() {
      labels = LABELS[language] || LABELS['zh-CN'];
      root.querySelectorAll('[data-tbd-label]').forEach(function (element) {
        var key = element.dataset.tbdLabel;
        if (labels[key]) element.textContent = labels[key];
      });
      root.querySelectorAll('[data-tbd-aria-label]').forEach(function (element) {
        var key = element.dataset.tbdAriaLabel;
        if (labels[key]) element.setAttribute('aria-label', labels[key]);
      });
      root.querySelectorAll('[data-tbd-title]').forEach(function (element) {
        var key = element.dataset.tbdTitle;
        if (labels[key]) element.title = labels[key];
      });
      closeButton.title = labels.close;
      importButton.title = labels.import;
      exportSkillButton.title = labels.exportSkillHint;
      updateExportButton();
      if (catalog && isOpen) {
        setStatus('', false);
        renderCatalog(catalog);
      } else if (isOpen && !catalog) {
        grid.replaceChildren(makeEmpty(labels.loading));
        setStatus(labels.loading, false);
      }
    }

    function makeEmpty(message) {
      var empty = document.createElement('div');
      empty.className = 'tbd-sm-empty';
      empty.textContent = message;
      return empty;
    }

    function renderCatalog(nextCatalog) {
      catalog = nextCatalog && typeof nextCatalog === 'object'
        ? nextCatalog
        : { activeId: '', skins: [] };
      var skins = Array.isArray(catalog.skins) ? catalog.skins : [];
      grid.replaceChildren();

      if (!skins.length) {
        grid.appendChild(makeEmpty(labels.empty));
        updateExportButton();
        setStatus(labels.empty, false);
        return;
      }

      var fragment = document.createDocumentFragment();
      skins.forEach(function (skin) {
        var item = skin && typeof skin === 'object' ? skin : {};
        var isActive = String(item.id || '') === String(catalog.activeId || '');
        var card = document.createElement('button');
        card.type = 'button';
        card.className = 'tbd-sm-card' + (isActive ? ' is-active' : '');
        card.dataset.skinId = String(item.id || '');
        card.setAttribute('aria-pressed', String(isActive));
        card.title = String(item.name || item.id || '');

        var thumb = document.createElement('div');
        thumb.className = 'tbd-sm-thumb';
        if (item.previewUrl) {
          var image = document.createElement('img');
          image.src = String(item.previewUrl);
          image.alt = String(item.name || '');
          image.loading = 'lazy';
          image.addEventListener('error', function () {
            image.remove();
            thumb.appendChild(createPlaceholder(labels));
          }, { once: true });
          thumb.appendChild(image);
        } else {
          thumb.appendChild(createPlaceholder(labels));
        }
        if (isActive) {
          var badge = document.createElement('span');
          badge.className = 'tbd-sm-active-badge';
          badge.textContent = labels.active;
          thumb.appendChild(badge);
        }

        var copy = document.createElement('div');
        copy.className = 'tbd-sm-card-copy';
        var name = document.createElement('div');
        name.className = 'tbd-sm-card-name';
        name.textContent = String(item.name || item.id || '');
        var meta = document.createElement('div');
        meta.className = 'tbd-sm-card-meta';
        var metaParts = [];
        if (item.isBuiltIn) {
          metaParts.push(labels.builtIn);
        } else {
          if (item.version) metaParts.push(String(item.version));
          if (item.author) metaParts.push(String(item.author));
        }
        meta.textContent = metaParts.join(' · ');
        var description = document.createElement('div');
        description.className = 'tbd-sm-card-description';
        description.textContent = String(item.description || '');
        copy.appendChild(name);
        if (meta.textContent) copy.appendChild(meta);
        if (description.textContent) copy.appendChild(description);
        card.appendChild(thumb);
        card.appendChild(copy);

        card.addEventListener('click', function () {
          if (isActive) {
            setStatus(labels.active, false);
            return;
          }
          setStatus(labels.selecting, false);
          if (!post({ type: 'set-skin', id: item.id })) {
            setStatus(labels.webOnly, true);
          }
        });
        fragment.appendChild(card);
      });
      grid.appendChild(fragment);
      updateExportButton();
      var statusText = status.textContent;
      if (isOpen && (!statusText || statusText === labels.loading || statusText === labels.empty)) {
        setStatus(countText(skins.length), false);
      }
    }

    function requestSkins() {
      if (!post({ type: 'request-skins' })) {
        setStatus(labels.webOnly, true);
        return false;
      }
      return true;
    }

    function open() {
      if (isOpen) return;
      previousFocus = document.activeElement;
      isOpen = true;
      root.hidden = false;
      setStatus('', false);
      if (catalog) renderCatalog(catalog);
      else {
        grid.replaceChildren(makeEmpty(labels.loading));
        setStatus(labels.loading, false);
      }
      closeButton.focus();
      requestSkins();
    }

    function close() {
      if (!isOpen) return;
      isOpen = false;
      root.hidden = true;
      if (previousFocus && typeof previousFocus.focus === 'function') previousFocus.focus();
      previousFocus = null;
    }

    function setLanguage(value) {
      var next = normalizeLanguage(value);
      if (next !== language) {
        language = next;
        applyLabels();
      } else {
        labels = LABELS[language] || LABELS['zh-CN'];
      }
    }

    function onHostMessage(event) {
      var data = event.data;
      if (typeof data === 'string') {
        try { data = JSON.parse(data); } catch { return; }
      }
      if (!data || typeof data !== 'object') return;
      if (data.type === 'skin-catalog') renderCatalog(data);
      if (data.type === 'settings' && data.language) setLanguage(data.language);
      if (data.type === 'skin-action') {
        var ok = data.ok !== false;
        setStatus(data.message || (ok ? '' : labels.actionFailed), !ok);
      }
    }

    function onTriggerClick(event) {
      if (event) event.preventDefault();
      open();
    }

    function onKeyDown(event) {
      if (isOpen && event.key === 'Escape') {
        event.preventDefault();
        close();
      }
    }

    closeButton.addEventListener('click', close);
    root.addEventListener('click', function (event) {
      if (event.target === root) close();
    });
    importButton.addEventListener('click', function () {
      setStatus('', false);
      if (!post({ type: 'import-skin' })) setStatus(labels.webOnly, true);
    });
    exportButton.addEventListener('click', function () {
      if (!catalog || !catalog.activeId) return;
      if (!post({ type: 'export-skin', id: catalog.activeId })) setStatus(labels.webOnly, true);
    });
    exportSkillButton.addEventListener('click', function () {
      setStatus('', false);
      if (!post({ type: 'export-skill' })) setStatus(labels.webOnly, true);
    });
    if (trigger) {
      trigger.setAttribute('aria-haspopup', 'dialog');
      trigger.setAttribute('aria-controls', 'tbd-sm-dialog');
      trigger.addEventListener('click', onTriggerClick);
    }
    if (host && typeof host.addEventListener === 'function') {
      host.addEventListener('message', onHostMessage);
    }
    document.addEventListener('keydown', onKeyDown);
    applyLabels();

    var controller = {
      open: open,
      close: close,
      refresh: requestSkins,
      setLanguage: setLanguage,
      destroy: function () {
        close();
        document.removeEventListener('keydown', onKeyDown);
        if (trigger) trigger.removeEventListener('click', onTriggerClick);
        if (host && typeof host.removeEventListener === 'function') {
          host.removeEventListener('message', onHostMessage);
        }
        delete root.__tbdSkinManager;
      }
    };
    root.__tbdSkinManager = controller;
    return controller;
  }

  global.TheBoostDeckSkinManager = { mount: mount };
})(window);
