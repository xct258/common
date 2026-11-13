# 通用 JS 文件

前端通用工具函数和 API 封装，可直接在 HTML 中通过 `<script>` 标签引入。

## 文件

| 文件 | 说明 |
|------|------|
| `background-switcher.js` | 全屏背景图切换器 + AuthGuard 登录拦截 |
| `auth-api.php` | 后端认证 API 接口（密码验证、Token 管理） |

## background-switcher.js

全屏背景图切换器，支持：
- 流式加载图片并显示进度
- 提取图片主题色应用到页面（CSS 变量 `--bg-image-theme-color`、`--bg-image-text-color`）
- 加载期间锁滚动、隐藏页面内容
- 加载失败自动重试 + 重试按钮
- 自动刷新（定时更换背景）
- AuthGuard 登录拦截器（可选，通过 `data-auth` 属性启用）

### 使用

```html
<script src="background-switcher.js" data-auth="auth-api.php"></script>
<script>
  BackgroundSwitcher.init('https://your-api/random-image');
  BackgroundSwitcher.setAutoRefresh('https://your-api/random-image', 3600);
</script>
```

## auth-api.php

后端密码认证接口，提供登录验证和 Token 管理，配合 AuthGuard 使用。
