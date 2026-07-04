/**
 * popup.js — 轻量级弹窗组件（Toast + Modal）
 *
 * 引入方式：
 *   <script src="popup.js"></script>
 *
 * ╔══════════════════════════════════════════════════════════════╗
 * ║                     Toast — 轻提示                          ║
 * ╚══════════════════════════════════════════════════════════════╝
 *
 *   Toast.success(message, opts?)       ── 成功提示（绿色图标）
 *   Toast.error(message, opts?)         ── 错误提示（红色图标）
 *   Toast.warning(message, opts?)       ── 警告提示（橙色图标）
 *   Toast.info(message, opts?)          ── 信息提示（蓝色图标）
 *   Toast.loading(message?, opts?)      ── 加载中（旋转图标，不自动关闭）
 *   Toast.show(message, opts?)          ── 通用（手动指定 type）
 *   Toast.clear()                       ── 清除所有弹窗
 *   Toast.config(opts)                  ── 全局配置
 *
 *   opts（可选配置）:
 *     type       — 'success' | 'error' | 'warning' | 'info' | 'loading'
 *                   默认 'info'
 *     duration   — 显示时长（ms），0 = 不自动关闭。默认 3000
 *     position   — 'top-right' | 'top-center' | 'top-left'
 *                 | 'bottom-right' | 'bottom-center' | 'bottom-left'
 *                   默认 'top-right'
 *     action     — { text: '撤销', onClick: function(dismiss) {} }
 *                   在弹窗右侧显示操作按钮
 *
 *   返回值: { dismiss() }   手动关闭该条弹窗
 *
 *   全局配置:
 *     Toast.config({ position: 'top-center', maxCount: 5 })
 *
 *   示例:
 *     Toast.success('保存成功');
 *     Toast.error('网络错误', { duration: 5000 });
 *     var t = Toast.loading('上传中…');  // 完成后调用 t.dismiss()
 *     Toast.info('已删除', {
 *       action: { text: '撤销', onClick: function(dismiss) {
 *         // 撤销逻辑
 *         dismiss();
 *       }}
 *     });
 *
 * ╔══════════════════════════════════════════════════════════════╗
 * ║                   Modal — 模态对话框                        ║
 * ╚══════════════════════════════════════════════════════════════╝
 *
 *   Modal.alert(message, opts?)         ── 仅确认按钮
 *   Modal.confirm(message, opts?)       ── 确认 + 取消
 *   Modal.show(message, opts?)          ── 完整配置（含自定义按钮）
 *   Modal.prompt(message, opts?)        ── 输入框对话框
 *   Modal.loading(message?, opts?)      ── 全屏加载遮罩
 *
 *   opts（alert / confirm / show）:
 *     title      — 标题（可选）
 *     type       — 'success' | 'error' | 'warning' | 'info'
 *                   影响图标和强调色。默认 'info'
 *     confirm    — 确认按钮文字。默认 '确定'
 *     cancel     — 取消按钮文字。默认 '取消'，传 false 则不显示
 *     buttons    — 自定义按钮数组，覆盖 confirm/cancel：
 *                  [{ text: '删除', value: 'delete', primary: true }]
 *     closable   — 点击遮罩 / ESC 可关闭。默认 true
 *
 *   返回: Promise<string>
 *     resolve 值为按钮 value 或 'confirm'/'cancel'/'overlay'/'escape'
 *
 *   opts（prompt 额外参数）:
 *     inputType     — 'text' | 'password' | 'number' | 'email'。默认 'text'
 *     placeholder   — 输入框占位符
 *     defaultValue  — 默认值
 *     required      — 是否必填。默认 false
 *
 *   返回: Promise<string|null>
 *     确认时 resolve 输入值，取消时 resolve null
 *
 *   Modal.loading(message?) 返回: { close(), update(text) }
 *
 *   示例:
 *     Modal.alert('操作完成', { type: 'success', title: '成功' });
 *
 *     Modal.confirm('确定删除？', { type: 'warning', title: '警告' })
 *       .then(function(v) { if (v === 'confirm') { ... } });
 *
 *     Modal.prompt('新文件名', { title: '重命名', placeholder: '输入名称' })
 *       .then(function(name) { if (name !== null) { ... } });
 *
 *     var loader = Modal.loading('正在处理…');
 *     loader.update('快完成了…');
 *     loader.close();
 *
 * ── 主题色适配 ──
 *   自动读取 CSS 变量 --bg-image-theme-color 和 --bg-image-text-color
 *   所有弹窗背景使用毛玻璃 + 主题色混合，按钮跟随主题色
 */
var Toast = (function () {
  'use strict';

  var CONTAINER_ID = '__toast_container';
  var _defaultPosition = 'top-right';
  var _maxCount = 8;
  var _styleInjected = false;

  var ICONS = {
    success: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>',
    error:   '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg>',
    warning: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>',
    info:    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>',
    loading: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"><path d="M12 2a10 10 0 0 1 10 10" class="__toast_spinner"/></svg>'
  };

  var COLORS = {
    success: { accent: '#10b981' },
    error:   { accent: '#ef4444' },
    warning: { accent: '#f59e0b' },
    info:    { accent: '#3b82f6' },
    loading: { accent: '#6b7280' }
  };

  function injectStyle() {
    if (_styleInjected) return;
    _styleInjected = true;

    var s = document.createElement('style');
    s.id = '__toast_style';
    s.textContent =
      '@keyframes __toast_in_right{from{opacity:0;transform:translateX(30px)}to{opacity:1;transform:translateX(0)}}' +
      '@keyframes __toast_in_left{from{opacity:0;transform:translateX(-30px)}to{opacity:1;transform:translateX(0)}}' +
      '@keyframes __toast_in_center{from{opacity:0;transform:translateY(-15px) scale(.95)}to{opacity:1;transform:translateY(0) scale(1)}}' +
      '@keyframes __toast_out{to{opacity:0;transform:scale(.95) translateY(-5px)}}' +

      '.__toast_container{' +
        'position:fixed;z-index:2147483647;display:flex;flex-direction:column;' +
        'pointer-events:none;max-width:380px;width:calc(100% - 32px);' +
        'font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,"Helvetica Neue",Arial,sans-serif;' +
      '}' +
      '.__toast_container.top-right{top:16px;right:16px;align-items:flex-end;}' +
      '.__toast_container.top-left{top:16px;left:16px;align-items:flex-start;}' +
      '.__toast_container.top-center{top:16px;left:50%;transform:translateX(-50%);align-items:center;}' +
      '.__toast_container.bottom-right{bottom:16px;right:16px;align-items:flex-end;flex-direction:column-reverse;}' +
      '.__toast_container.bottom-left{bottom:16px;left:16px;align-items:flex-start;flex-direction:column-reverse;}' +
      '.__toast_container.bottom-center{bottom:16px;left:50%;transform:translateX(-50%);align-items:center;flex-direction:column-reverse;}' +

      '.__toast_item{' +
        'pointer-events:auto;display:flex;align-items:flex-start;gap:10px;' +
        'padding:12px 16px;margin-bottom:8px;border-radius:10px;max-width:100%;' +
        'background:color-mix(in srgb,var(--bg-image-theme-color,#1e293b) 10%,rgba(255,255,255,.88));' +
        'backdrop-filter:blur(12px);-webkit-backdrop-filter:blur(12px);' +
        'box-shadow:0 4px 20px rgba(0,0,0,.08),0 1px 3px rgba(0,0,0,.06);' +
        'cursor:pointer;overflow:hidden;' +
        'color:#1f2937;' +
      '}' +
      '.__toast_item:hover{box-shadow:0 6px 24px rgba(0,0,0,.12);}' +

      '.__toast_icon{flex-shrink:0;width:20px;height:20px;}' +
      '.__toast_text{flex:1;font-size:.88rem;line-height:1.45;font-weight:500;word-break:break-word;}' +

      '.__toast_progress{position:absolute;bottom:0;left:0;height:2px;border-radius:0 0 0 10px;transition:width linear;}' +

      '@keyframes __toast_spin{to{transform:rotate(360deg)}}' +
      '.__toast_spinner{animation:__toast_spin .7s linear infinite;transform-origin:12px 12px;}' +

      '.__toast_action{' +
        'flex-shrink:0;padding:2px 10px;border:none;border-radius:6px;font-size:.82rem;font-weight:600;' +
        'cursor:pointer;background:rgba(0,0,0,.08);color:inherit;transition:background .15s ease;' +
      '}' +
      '.__toast_action:hover{background:rgba(0,0,0,.14);}';

    document.head.appendChild(s);
  }

  function getContainer(position) {
    var pos = position || _defaultPosition;
    var id = CONTAINER_ID + '_' + pos;
    var container = document.getElementById(id);
    if (container) return container;

    container = document.createElement('div');
    container.id = id;
    container.className = '__toast_container ' + pos;
    document.body.appendChild(container);
    return container;
  }

  function getAnimation(position) {
    if (position.indexOf('right') !== -1) return '__toast_in_right';
    if (position.indexOf('left') !== -1) return '__toast_in_left';
    return '__toast_in_center';
  }

  function createToast(message, opts) {
    injectStyle();

    var type     = opts.type || 'info';
    var duration = opts.duration !== undefined ? opts.duration : 3000;
    var position = opts.position || _defaultPosition;
    var colors   = COLORS[type] || COLORS.info;
    var icon     = ICONS[type] || ICONS.info;

    var container = getContainer(position);
    var anim = getAnimation(position);

    var el = document.createElement('div');
    el.className = '__toast_item';
    el.style.cssText =
      'position:relative;overflow:hidden;' +
      'animation:' + anim + ' .3s ease both;';

    el.innerHTML =
      '<div class="__toast_icon" style="color:' + colors.accent + ';">' + icon + '</div>' +
      '<div class="__toast_text">' + escapeHtml(message) + '</div>';

    // 操作按钮
    if (opts.action) {
      var actionBtn = document.createElement('button');
      actionBtn.className = '__toast_action';
      actionBtn.textContent = opts.action.text || '操作';
      actionBtn.addEventListener('click', function (e) {
        e.stopPropagation();
        if (opts.action.onClick) opts.action.onClick(dismiss);
      });
      el.appendChild(actionBtn);
    }

    // 自动关闭进度条
    if (duration > 0) {
      var bar = document.createElement('div');
      bar.className = '__toast_progress';
      bar.style.cssText = 'width:100%;background:' + colors.accent + ';opacity:.3;transition-duration:' + duration + 'ms;';
      el.appendChild(bar);
      requestAnimationFrame(function () {
        requestAnimationFrame(function () {
          bar.style.width = '0%';
        });
      });
    }

    // 关闭逻辑
    var timer = null;
    var dismissed = false;

    function dismiss() {
      if (dismissed) return;
      dismissed = true;
      if (timer) clearTimeout(timer);
      collapseToast(el, function () {
        if (container.children.length === 0) container.remove();
      });
    }

    el.addEventListener('dismiss', dismiss);
    el.addEventListener('click', dismiss);

    if (duration > 0) {
      timer = setTimeout(dismiss, duration);

      // 鼠标悬停暂停
      el.addEventListener('mouseenter', function () {
        clearTimeout(timer);
        var bar = el.querySelector('.__toast_progress');
        if (bar) {
          var w = bar.getBoundingClientRect().width;
          var pw = el.getBoundingClientRect().width;
          bar.style.transitionDuration = '0ms';
          bar.style.width = (w / pw * 100) + '%';
        }
      });
      el.addEventListener('mouseleave', function () {
        var bar = el.querySelector('.__toast_progress');
        if (bar) {
          var remaining = parseFloat(bar.style.width) / 100 * duration;
          bar.style.transitionDuration = remaining + 'ms';
          requestAnimationFrame(function () { bar.style.width = '0%'; });
        }
        timer = setTimeout(dismiss, parseFloat((el.querySelector('.__toast_progress') || {}).style.width || '0') / 100 * duration || 300);
      });
    }

    // 超出最大数量时移除最早的弹窗（带淡出 + 收缩动画）
    var excess = container.children.length - _maxCount + 1;
    for (var i = 0; i < excess; i++) {
      var oldest = container.children[i];
      if (oldest) {
        (function (el) {
          collapseToast(el);
        })(oldest);
      }
    }

    container.appendChild(el);
    return { dismiss: dismiss };
  }

  function collapseToast(el, callback) {
    el.style.pointerEvents = 'none';
    // 取消入场动画，锁定当前高度
    el.style.animation = 'none';
    el.style.maxHeight = el.scrollHeight + 'px';
    el.style.transition = 'none';
    // 同步提交初始状态
    void el.offsetWidth;
    // 单次 rAF：一帧后启动动画
    requestAnimationFrame(function () {
      el.style.transition =
        'opacity .2s ease-out,' +
        'transform .2s ease-out,' +
        'max-height .28s cubic-bezier(.4,0,.2,1),' +
        'margin-bottom .28s cubic-bezier(.4,0,.2,1),' +
        'padding-top .28s cubic-bezier(.4,0,.2,1),' +
        'padding-bottom .28s cubic-bezier(.4,0,.2,1)';
      el.style.opacity = '0';
      el.style.transform = 'scale(.92)';
      el.style.maxHeight = '0';
      el.style.marginBottom = '0';
      el.style.paddingTop = '0';
      el.style.paddingBottom = '0';
    });
    // 动画结束后清理
    var removed = false;
    function cleanup() {
      if (removed) return;
      removed = true;
      el.removeEventListener('transitionend', onEnd);
      el.remove();
      if (callback) callback();
    }
    function onEnd(e) {
      if (e.propertyName === 'max-height') cleanup();
    }
    el.addEventListener('transitionend', onEnd);
    setTimeout(cleanup, 320);
  }

  /* ========== Modal 模态对话框 ========== */

  var _modalStyleInjected = false;

  function injectModalStyle() {
    if (_modalStyleInjected) return;
    _modalStyleInjected = true;

    var s = document.createElement('style');
    s.id = '__toast_modal_style';
    s.textContent =
      /* -- 入场动画 -- */
      '@keyframes __modal_overlay_in{from{opacity:0}to{opacity:1}}' +
      '@keyframes __modal_overlay_out{from{opacity:1}to{opacity:0}}' +
      '@keyframes __modal_box_in{from{opacity:0;transform:scale(.88) translateY(24px)}to{opacity:1;transform:scale(1) translateY(0)}}' +
      '@keyframes __modal_box_out{from{opacity:1;transform:scale(1) translateY(0)}to{opacity:0;transform:scale(.92) translateY(12px)}}' +

      '.__toast_overlay{' +
        'position:fixed;inset:0;z-index:2147483647;' +
        'display:flex;align-items:center;justify-content:center;' +
        'background:color-mix(in srgb,var(--bg-image-theme-color,#000) 20%,rgba(0,0,0,.45));' +
        'backdrop-filter:blur(4px);-webkit-backdrop-filter:blur(4px);' +
        'animation:__modal_overlay_in .28s ease both;' +
        'font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,"Helvetica Neue",Arial,sans-serif;' +
      '}' +

      '.__toast_modal{' +
        'position:relative;border-radius:14px;' +
        'padding:28px 28px 20px;max-width:420px;width:calc(100% - 40px);' +
        'background:color-mix(in srgb,var(--bg-image-theme-color,#fff) 6%,rgba(255,255,255,.92));' +
        'backdrop-filter:blur(16px);-webkit-backdrop-filter:blur(16px);' +
        'box-shadow:0 20px 60px rgba(0,0,0,.15),0 2px 8px rgba(0,0,0,.08);' +
        'animation:__modal_box_in .32s cubic-bezier(.22,.61,.36,1) both;' +
      '}' +

      '.__toast_modal_header{display:flex;align-items:center;gap:10px;margin-bottom:12px;}' +
      '.__toast_modal_icon{flex-shrink:0;width:24px;height:24px;}' +
      '.__toast_modal_title{font-size:1.05rem;font-weight:600;line-height:1.3;}' +
      '.__toast_modal_body{font-size:.92rem;line-height:1.55;color:#374151;margin-bottom:22px;word-break:break-word;}' +

      '.__toast_modal_footer{display:flex;justify-content:flex-end;gap:10px;}' +
      '.__toast_modal_btn{' +
        'padding:8px 20px;border:none;border-radius:8px;font-size:.88rem;font-weight:500;' +
        'cursor:pointer;transition:background .15s ease,box-shadow .15s ease,transform .1s ease;' +
        'outline:none;' +
      '}' +
      '.__toast_modal_btn:active{transform:scale(.97);}' +
      '.__toast_modal_btn.--cancel{background:#f3f4f6;color:#374151;}' +
      '.__toast_modal_btn.--cancel:hover{background:#e5e7eb;}' +
      '.__toast_modal_btn.--primary{color:#fff;box-shadow:0 2px 8px rgba(0,0,0,.12);}' +
      '.__toast_modal_btn.--primary:hover{box-shadow:0 4px 14px rgba(0,0,0,.18);}';

    document.head.appendChild(s);
  }

  function createModal(message, opts) {
    injectStyle();
    injectModalStyle();

    var type     = opts.type || 'info';
    var title    = opts.title || '';
    var confirmText = opts.confirm !== undefined ? opts.confirm : '确定';
    var cancelText  = opts.cancel !== undefined ? opts.cancel : '取消';
    var customBtns  = opts.buttons || null;
    var closable    = opts.closable !== undefined ? opts.closable : true;
    var colors   = COLORS[type] || COLORS.info;
    var icon     = ICONS[type] || ICONS.info;

    // 遮罩
    var overlay = document.createElement('div');
    overlay.className = '__toast_overlay';

    // 模态框
    var modal = document.createElement('div');
    modal.className = '__toast_modal';

    // 头部（图标 + 标题）
    var headerHtml = '';
    if (title || icon) {
      headerHtml = '<div class="__toast_modal_header">' +
        '<div class="__toast_modal_icon" style="color:' + colors.accent + ';">' + icon + '</div>' +
        (title ? '<div class="__toast_modal_title">' + escapeHtml(title) + '</div>' : '') +
        '</div>';
    }

    // 正文
    var bodyHtml = '<div class="__toast_modal_body">' + escapeHtml(message) + '</div>';

    // 按钮 — 主按钮使用主题色
    var primaryBg = 'background:var(--bg-image-theme-color,' + colors.accent + ');';
    var footerHtml = '<div class="__toast_modal_footer">';
    if (customBtns) {
      customBtns.forEach(function (btn) {
        var cls = btn.primary ? '--primary' : '--cancel';
        var bg = btn.primary ? primaryBg : '';
        footerHtml += '<button class="__toast_modal_btn ' + cls + '" style="' + bg + '" data-value="' + escapeHtml(btn.value || btn.text) + '">' + escapeHtml(btn.text) + '</button>';
      });
    } else {
      if (cancelText !== false) {
        footerHtml += '<button class="__toast_modal_btn --cancel" data-value="cancel">' + escapeHtml(String(cancelText)) + '</button>';
      }
      footerHtml += '<button class="__toast_modal_btn --primary" style="' + primaryBg + '" data-value="confirm">' + escapeHtml(String(confirmText)) + '</button>';
    }
    footerHtml += '</div>';

    modal.innerHTML = headerHtml + bodyHtml + footerHtml;
    overlay.appendChild(modal);
    document.body.appendChild(overlay);

    // Promise 逻辑
    return new Promise(function (resolve) {
      var closed = false;

      function close(value) {
        if (closed) return;
        closed = true;
        // 退出动画：直接替换为 out 动画
        overlay.style.animation = '__modal_overlay_out .22s ease forwards';
        modal.style.animation = '__modal_box_out .2s ease-in forwards';
        // animationend 后移除
        var done = false;
        function remove() {
          if (done) return;
          done = true;
          overlay.remove();
          document.removeEventListener('keydown', onKey);
          resolve(value);
        }
        overlay.addEventListener('animationend', remove);
        setTimeout(remove, 280);
      }

      // 按钮点击
      var buttons = modal.querySelectorAll('.__toast_modal_btn');
      buttons.forEach(function (btn) {
        btn.addEventListener('click', function () {
          close(btn.getAttribute('data-value'));
        });
      });

      // 遮罩点击
      if (closable) {
        overlay.addEventListener('click', function (e) {
          if (e.target === overlay) close('overlay');
        });
      }

      // ESC 键关闭
      function onKey(e) {
        if (e.key === 'Escape' && closable) {
          close('escape');
        }
      }
      document.addEventListener('keydown', onKey);
    });
  }

  function escapeHtml(str) {
    var div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
  }

  return {
    show: function (message, opts) {
      return createToast(message, opts || {});
    },
    success: function (message, opts) {
      return createToast(message, Object.assign({ type: 'success' }, opts));
    },
    error: function (message, opts) {
      return createToast(message, Object.assign({ type: 'error' }, opts));
    },
    warning: function (message, opts) {
      return createToast(message, Object.assign({ type: 'warning' }, opts));
    },
    info: function (message, opts) {
      return createToast(message, Object.assign({ type: 'info' }, opts));
    },
    clear: function () {
      var containers = document.querySelectorAll('.__toast_container');
      containers.forEach(function (c) { c.remove(); });
    },
    config: function (opts) {
      if (opts.position) _defaultPosition = opts.position;
      if (opts.maxCount > 0) _maxCount = opts.maxCount;
    },
    loading: function (message, opts) {
      return createToast(message || '加载中…', Object.assign({
        type: 'loading', duration: 0
      }, opts));
    },
    /** @internal — 供 Modal 调用 */
    _createModal: createModal,
    _injectModalStyle: injectModalStyle,
    _escapeHtml: escapeHtml,
    _COLORS: COLORS,
    _ICONS: ICONS
  };
})();

var Modal = {
  show: function (message, opts) {
    return Toast._createModal(message, opts || {});
  },
  alert: function (message, opts) {
    return Toast._createModal(message, Object.assign({ cancel: false }, opts));
  },
  confirm: function (message, opts) {
    return Toast._createModal(message, Object.assign({}, opts));
  },

  /**
   * Modal.prompt('请输入名称', { title: '重命名', placeholder: '新名称', defaultValue: '' })
   *   .then(function(v) { // v = 输入值字符串 或 null（取消时） });
   *
   * opts.inputType — text | password | number | email（默认 text）
   * opts.placeholder — 占位符
   * opts.defaultValue — 默认值
   * opts.required — 是否必填（默认 false）
   */
  prompt: function (message, opts) {
    opts = Object.assign({}, opts);
    Toast._injectModalStyle();

    // 注入 prompt 专用样式
    if (!document.getElementById('__toast_prompt_style')) {
      var ps = document.createElement('style');
      ps.id = '__toast_prompt_style';
      ps.textContent =
        '.__toast_modal_input{' +
          'width:100%;padding:10px 12px;border:1.5px solid #d1d5db;border-radius:8px;' +
          'font-size:.92rem;outline:none;box-sizing:border-box;' +
          'transition:border-color .15s ease,box-shadow .15s ease;' +
          'background:rgba(255,255,255,.6);' +
        '}' +
        '.__toast_modal_input:focus{' +
          'border-color:var(--bg-image-theme-color,#3b82f6);' +
          'box-shadow:0 0 0 3px color-mix(in srgb,var(--bg-image-theme-color,#3b82f6) 15%,transparent);' +
        '}';
      document.head.appendChild(ps);
    }

    var type = opts.type || 'info';
    var colors = Toast._COLORS[type] || Toast._COLORS.info;
    var icon = Toast._ICONS[type] || Toast._ICONS.info;
    var title = opts.title || '';
    var confirmText = opts.confirm || '确定';
    var cancelText = opts.cancel !== undefined ? opts.cancel : '取消';
    var primaryBg = 'background:var(--bg-image-theme-color,' + colors.accent + ');';

    var overlay = document.createElement('div');
    overlay.className = '__toast_overlay';

    var modal = document.createElement('div');
    modal.className = '__toast_modal';

    var headerHtml = '';
    if (title || icon) {
      headerHtml = '<div class="__toast_modal_header">' +
        '<div class="__toast_modal_icon" style="color:' + colors.accent + ';">' + icon + '</div>' +
        (title ? '<div class="__toast_modal_title">' + Toast._escapeHtml(title) + '</div>' : '') +
        '</div>';
    }

    var bodyHtml = '<div class="__toast_modal_body">' + Toast._escapeHtml(message) + '</div>';
    var inputHtml = '<input class="__toast_modal_input" type="' + (opts.inputType || 'text') + '"' +
      (opts.placeholder ? ' placeholder="' + Toast._escapeHtml(opts.placeholder) + '"' : '') +
      (opts.defaultValue !== undefined ? ' value="' + Toast._escapeHtml(String(opts.defaultValue)) + '"' : '') +
      ' style="margin-bottom:18px;">';

    var footerHtml = '<div class="__toast_modal_footer">';
    if (cancelText !== false) {
      footerHtml += '<button class="__toast_modal_btn --cancel" data-value="cancel">' + Toast._escapeHtml(String(cancelText)) + '</button>';
    }
    footerHtml += '<button class="__toast_modal_btn --primary" style="' + primaryBg + '" data-value="confirm">' + Toast._escapeHtml(confirmText) + '</button>';
    footerHtml += '</div>';

    modal.innerHTML = headerHtml + bodyHtml + inputHtml + footerHtml;
    overlay.appendChild(modal);
    document.body.appendChild(overlay);

    var input = modal.querySelector('.__toast_modal_input');
    setTimeout(function () { input.focus(); input.select(); }, 50);

    return new Promise(function (resolve) {
      var closed = false;

      function close(value) {
        if (closed) return;
        closed = true;
        overlay.style.animation = '__modal_overlay_out .22s ease forwards';
        modal.style.animation = '__modal_box_out .2s ease-in forwards';
        var done = false;
        function remove() {
          if (done) return;
          done = true;
          overlay.remove();
          document.removeEventListener('keydown', onKey);
          resolve(value);
        }
        overlay.addEventListener('animationend', remove);
        setTimeout(remove, 280);
      }

      function submitValue() {
        var val = input.value;
        if (opts.required && !val.trim()) {
          input.style.borderColor = '#ef4444';
          input.focus();
          return;
        }
        close(val);
      }

      modal.querySelectorAll('.__toast_modal_btn').forEach(function (btn) {
        btn.addEventListener('click', function () {
          if (btn.getAttribute('data-value') === 'confirm') {
            submitValue();
          } else {
            close(null);
          }
        });
      });

      input.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') { e.preventDefault(); submitValue(); }
      });

      var closable = opts.closable !== undefined ? opts.closable : true;
      if (closable) {
        overlay.addEventListener('click', function (e) {
          if (e.target === overlay) close(null);
        });
      }

      function onKey(e) {
        if (e.key === 'Escape' && closable) close(null);
      }
      document.addEventListener('keydown', onKey);
    });
  },

  /**
   * var loader = Modal.loading('正在处理…');
   * // 完成后调用
   * loader.close();
   *
   * opts.type — 样式类型（默认 info）
   */
  loading: function (message, opts) {
    opts = Object.assign({}, opts);
    Toast._injectModalStyle();

    // 注入 loading 专用样式
    if (!document.getElementById('__toast_loading_style')) {
      var ls = document.createElement('style');
      ls.id = '__toast_loading_style';
      ls.textContent =
        '@keyframes __modal_spin{to{transform:rotate(360deg)}}' +
        '.__toast_loading_spinner{' +
          'width:40px;height:40px;margin:0 auto 16px;' +
          'border:3px solid rgba(0,0,0,.1);border-top-color:var(--bg-image-theme-color,#3b82f6);' +
          'border-radius:50%;animation:__modal_spin .7s linear infinite;' +
        '}' +
        '.__toast_loading_text{text-align:center;font-size:.92rem;color:#374151;}';
      document.head.appendChild(ls);
    }

    var overlay = document.createElement('div');
    overlay.className = '__toast_overlay';

    var modal = document.createElement('div');
    modal.className = '__toast_modal';
    modal.style.cssText = 'text-align:center;padding:32px 28px;max-width:300px;';

    modal.innerHTML =
      '<div class="__toast_loading_spinner"></div>' +
      '<div class="__toast_loading_text">' + Toast._escapeHtml(message || '加载中…') + '</div>';

    overlay.appendChild(modal);
    document.body.appendChild(overlay);

    var isClosed = false;
    function close() {
      if (isClosed) return;
      isClosed = true;
      overlay.style.animation = '__modal_overlay_out .22s ease forwards';
      modal.style.animation = '__modal_box_out .2s ease-in forwards';
      var done = false;
      function remove() {
        if (done) return;
        done = true;
        overlay.remove();
      }
      overlay.addEventListener('animationend', remove);
      setTimeout(remove, 280);
    }

    function update(text) {
      var el = modal.querySelector('.__toast_loading_text');
      if (el) el.textContent = text;
    }

    return { close: close, update: update };
  }
};
