# 项目说明

## 项目简介

这个项目目前由两个可直接在浏览器页面中通过 `<script>` 标签引入的原生 JavaScript 组件组成，不依赖打包工具，也没有额外框架依赖：

- `background-switcher.js`：全屏背景图切换器，支持加载遮罩、进度展示、淡入切换、自动刷新、主题色联动，以及可选的登录拦截层。
- `popup.js`：轻量级弹窗组件库，提供 `Toast` 与 `Modal` 两套能力，并会自动读取背景组件注入的主题色变量。

如果你只是想快速使用：

1. 背景图切换用 `background-switcher.js`
2. 提示框/确认框/输入框用 `popup.js`
3. 两个一起用时，`popup.js` 会自动继承背景图接口返回的主题色

---

## 文件结构

```text
.
├─ background-switcher.js   背景图切换、自动初始化、登录拦截
├─ example.html             最小可运行示例页面
├─ example-assets/          示例页面使用的本地 SVG 背景
├─ popup.js                 Toast / Modal 弹窗组件
└─ README.md                项目说明文件
```

---

## 功能概览

### `background-switcher.js`

主要能力：

- 通过接口拉取图片并设置为整页背景
- 背景切换时带淡入过渡效果
- 可显示全屏加载遮罩和百分比进度
- 支持根据响应头动态设置页面主题色
- 支持自动刷新背景图
- 支持通过 `data-*` 属性自动初始化
- 支持与登录接口联动，未登录时显示登录遮罩
- 会记住上一张图片的 ID，请求下一张时自动附加排除参数

### `popup.js`

主要能力：

- `Toast.success / error / warning / info / loading`
- `Modal.alert / confirm / show / prompt / loading`
- 可配置位置、持续时间、操作按钮、标题、类型等
- 自动读取 CSS 变量 `--bg-image-theme-color` 和 `--bg-image-text-color`
- 不依赖第三方 UI 库，可直接落地到任意页面

---

## 快速接入

### 1. 仅使用背景图切换

```html
<script
  src="./background-switcher.js"
  data-api="https://your-image-api.example.com/random"
  data-interval="30">
</script>
```

说明：

- `data-api`：图片接口地址，必填
- `data-interval`：自动刷新间隔，单位秒，可选

### 2. 仅使用弹窗组件

```html
<script src="./popup.js"></script>
<script>
  Toast.success('保存成功');

  Modal.confirm('确定删除吗？', {
    title: '提示',
    type: 'warning'
  }).then(function (value) {
    if (value === 'confirm') {
      console.log('用户确认删除');
    }
  });
</script>
```

### 3. 两个组件一起使用

```html
<script
  src="./background-switcher.js"
  data-api="https://your-image-api.example.com/random"
  data-interval="60">
</script>
<script src="./popup.js"></script>

<script>
  setTimeout(function () {
    Toast.info('页面已加载完成');
  }, 2500);
</script>
```

---

## 运行示例页面

仓库里已经附带了一个最小可运行示例页：

- `example.html`

这个页面会直接引用：

- `./background-switcher.js`
- `./popup.js`
- `./example-assets/*.svg`

为了让 `fetch()` 正常加载本地资源，建议通过静态服务器访问，而不是直接双击打开 HTML 文件。

例如可以在当前目录启动一个最简单的静态服务：

```bash
python -m http.server 8000
```

然后在浏览器打开：

```text
http://localhost:8000/example.html
```

如果本机没有 Python，也可以使用任意其他静态文件服务器，只要能把当前目录作为网站根目录提供出来即可。

---

## `background-switcher.js` 详细说明

### 一、自动初始化方式

该脚本支持“零代码接入”。把脚本直接写进页面后，会读取当前 `<script>` 标签上的配置并自动初始化：

```html
<script
  src="./background-switcher.js"
  data-api="https://random-image.example.com/"
  data-interval="30"
  data-auth="./auth-api.php">
</script>
```

支持的 `data-*` 参数如下：

| 属性名 | 必填 | 说明 |
| --- | --- | --- |
| `data-api` | 是 | 背景图接口地址 |
| `data-apiUrl` | 否 | `data-api` 的备用名称 |
| `data-interval` | 否 | 自动刷新间隔，单位秒，大于 0 时生效 |
| `data-auth` | 否 | 登录验证接口地址，设置后会启用登录拦截 |

### 二、公开 API

虽然脚本支持自动初始化，但也可以在页面中手动调用。

#### 1. `BackgroundSwitcher.init(apiUrl, options)`

初始化或重新拉取背景图。

```html
<script src="./background-switcher.js"></script>
<script>
  BackgroundSwitcher.init('https://your-image-api.example.com/random', {
    silent: false,
    transitionDuration: 1200
  });
</script>
```

`options` 支持以下配置：

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `silent` | `true` | 是否静默加载。`false` 时会隐藏页面并显示加载遮罩 |
| `transitionDuration` | `1000` | 背景切换淡入时长，单位毫秒 |
| `zIndex` | `-999999` | 背景层 `z-index` |
| `loaderText` | `背景加载中` | 加载阶段文案 |
| `decodingText` | `正在解析图片` | 解码阶段文案 |
| `applyingText` | `正在显示页面` | 应用背景阶段文案 |
| `errorText` | `加载失败，请稍后重试` | 加载失败时展示的文案 |
| `loaderFadeDuration` | `350` | 加载遮罩淡出时长，单位毫秒 |

补充说明：

- 调用 `init` 时如果正处于切换中，新的调用会被忽略。
- 每次 `init` 会清空上一次自动刷新定时器，并在本次切换完成后重新启动。
- 当 `silent: false` 且没有设置 `onReady` 回调时，背景加载完成后会额外停留约 2 秒，再显示页面内容。
- 当 `silent: false` 且加载失败时，会显示全屏错误提示和重试按钮。

#### 2. `BackgroundSwitcher.setAutoRefresh(url, intervalSec)`

设置自动刷新参数。

```js
BackgroundSwitcher.setAutoRefresh('https://your-image-api.example.com/random', 30);
```

说明：

- `intervalSec > 0` 时生效
- 刷新逻辑最终还是调用 `BackgroundSwitcher.init(url)`
- 自动初始化模式下，如果 `data-interval` 大于 0，会自动调用这个方法

#### 3. `BackgroundSwitcher.onReady(fn)`

注册背景加载完成回调。

```js
BackgroundSwitcher.onReady(function () {
  console.log('背景已就绪，但页面内容尚未显示');
  BackgroundSwitcher.revealPage();
});
```

适用场景：

- 背景加载完后先执行一段业务逻辑
- 与登录校验联动
- 希望自行决定页面何时显示

注意：

- 当前实现更适合“单次接管首次显示”场景
- 回调执行后会被清空
- `data-auth` 自动登录流程内部也会使用这个回调，因此如果你启用了 `data-auth`，再手动接管 `onReady` 时要注意不要互相覆盖

#### 4. `BackgroundSwitcher.revealPage()`

手动显示页面内容。

```js
BackgroundSwitcher.revealPage();
```

通常与 `onReady` 配合使用。

### 三、背景图接口约定

背景图接口本质上就是一个返回图片二进制数据的 HTTP 接口。脚本会通过 `fetch()` 去请求它。

#### 请求特征

脚本会自动给请求地址补充以下查询参数：

- `_t`：当前时间戳，用于避免缓存
- `exclude`：上一张图片的 ID，仅当上一次响应头中存在 `X-Image-Id` 时才会带上

举例：

```text
https://your-image-api.example.com/random?_t=1710000000000&exclude=abc123
```

#### 响应要求

- 返回值应为图片二进制内容，如 JPG / PNG / WebP
- 返回 `2xx` 状态码才会被视为成功

#### 可选响应头

以下响应头不是必填，但配合后效果更好：

| 响应头 | 作用 |
| --- | --- |
| `X-Image-Id` | 标识本次返回的图片 ID，下一次请求会作为 `exclude` 参数带回去 |
| `X-Theme-Color-Hex` | 注入到 `--bg-image-theme-color`，供页面和弹窗读取主题色 |
| `X-Text-Color-Hex` | 注入到 `--bg-image-text-color`，供页面读取文字色 |
| `Content-Length` | 用于更准确地计算下载进度 |

例如：

```http
X-Image-Id: mountain-001
X-Theme-Color-Hex: #345b8c
X-Text-Color-Hex: #ffffff
Content-Length: 582341
```

### 四、页面行为说明

该脚本不是简单地给 `body` 设置一张背景图，它还会主动处理页面结构：

- 在页面中插入一个全屏背景层
- 把 `body` 里的普通内容包进 `#bg-switcher-content`
- 在需要时通过给 `html` 添加类名来隐藏或显示页面内容
- 将 `html` 和 `body` 的滚动锁定，把滚动交给 `#bg-switcher-content`

这意味着：

- 它更适合“整站背景”场景
- 如果页面里已经有复杂的全屏定位结构，接入前最好先验证层级和滚动行为

### 五、登录拦截 `AuthGuard`

`background-switcher.js` 内置了一个登录拦截器 `AuthGuard`。当脚本标签设置了 `data-auth` 后，会自动启用以下流程：

1. 从 `localStorage` 读取 `__ag_token`
2. 并行发起“背景图请求”和“登录状态检查”
3. 背景加载完成后，如果用户未登录，显示登录遮罩
4. 登录成功后关闭遮罩，并在短暂延迟后显示页面内容

#### 自动启用方式

```html
<script
  src="./background-switcher.js"
  data-api="https://your-image-api.example.com/random"
  data-auth="./auth-api.php">
</script>
```

#### 登录接口协议

##### 1. 检查登录状态

请求：

```http
GET /auth-api.php?action=check
X-Auth-Token: <token>
```

返回：

```json
{ "authenticated": true }
```

或：

```json
{ "authenticated": false }
```

##### 2. 登录

请求：

```http
POST /auth-api.php?action=login
Content-Type: application/json
```

请求体：

```json
{
  "username": "admin",
  "password": "123456",
  "remember": true
}
```

成功返回示例：

```json
{
  "success": true,
  "token": "your-token"
}
```

失败返回示例：

```json
{
  "success": false,
  "error": "用户名或密码错误"
}
```

##### 3. 退出登录

请求：

```http
GET /auth-api.php?action=logout
```

返回值在当前实现里不会被使用，接口只要能正常响应即可。

#### `AuthGuard` 可手动调用的方法

##### `AuthGuard.show(authApi, onAuthenticated)`

显示登录遮罩，登录成功后执行回调。

##### `AuthGuard.passThrough(authApi, onAuthenticated)`

用于“已确认登录”的场景，内部会延迟执行回调。

##### `AuthGuard.logout()`

清除本地 token，调用登出接口并刷新页面。

注意：

- token 存储键名固定为 `__ag_token`
- `logout()` 依赖之前设置过 `_authApi`，最稳妥的方式是通过自动初始化流程启用认证

---

## `popup.js` 详细说明

### 一、使用方式

```html
<script src="./popup.js"></script>
```

脚本加载后，可直接在页面中使用全局变量：

- `Toast`
- `Modal`

### 二、Toast API

#### 1. 快捷方法

```js
Toast.success('保存成功');
Toast.error('网络错误');
Toast.warning('请先选择文件');
Toast.info('操作已完成');
```

#### 2. 通用方法

```js
Toast.show('自定义提示', {
  type: 'info',
  duration: 3000,
  position: 'top-right'
});
```

#### 3. 加载提示

```js
var loadingToast = Toast.loading('上传中…');

setTimeout(function () {
  loadingToast.dismiss();
}, 2000);
```

#### 4. 全局配置

```js
Toast.config({
  position: 'top-center',
  maxCount: 5
});
```

#### 5. 清空全部 Toast

```js
Toast.clear();
```

#### Toast 参数说明

`Toast.show(message, opts)` 中常用参数如下：

| 参数 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `type` | `success \| error \| warning \| info \| loading` | `info` | 提示类型 |
| `duration` | `number` | `3000` | 显示时长，毫秒；`0` 表示不自动关闭 |
| `position` | `string` | `top-right` | 展示位置 |
| `action` | `object` | 无 | 右侧操作按钮 |

`position` 支持：

- `top-right`
- `top-center`
- `top-left`
- `bottom-right`
- `bottom-center`
- `bottom-left`

`action` 示例：

```js
Toast.info('已删除', {
  action: {
    text: '撤销',
    onClick: function (dismiss) {
      console.log('执行撤销逻辑');
      dismiss();
    }
  }
});
```

补充说明：

- `Toast.show()`、`Toast.success()` 等方法会返回 `{ dismiss() }`
- 鼠标悬停到 Toast 上时会暂停自动消失计时
- 超过 `maxCount` 时，旧的 Toast 会被自动移除

### 三、Modal API

#### 1. `Modal.alert(message, opts)`

只有确认按钮。

```js
Modal.alert('操作成功', {
  title: '成功',
  type: 'success'
});
```

#### 2. `Modal.confirm(message, opts)`

确认/取消对话框。

```js
Modal.confirm('确定删除这条记录吗？', {
  title: '警告',
  type: 'warning',
  confirm: '删除',
  cancel: '取消'
}).then(function (value) {
  if (value === 'confirm') {
    console.log('用户确认');
  }
});
```

#### 3. `Modal.show(message, opts)`

完整配置版本，支持自定义按钮。

```js
Modal.show('请选择一个操作', {
  title: '更多操作',
  buttons: [
    { text: '取消', value: 'cancel' },
    { text: '继续', value: 'continue', primary: true }
  ]
}).then(function (value) {
  console.log(value);
});
```

#### 4. `Modal.prompt(message, opts)`

输入框对话框。

```js
Modal.prompt('请输入新的文件名', {
  title: '重命名',
  placeholder: '例如：banner-final',
  defaultValue: '',
  required: true
}).then(function (value) {
  if (value !== null) {
    console.log('用户输入：', value);
  }
});
```

#### 5. `Modal.loading(message, opts)`

全屏加载对话框。

```js
var loader = Modal.loading('正在处理…');

setTimeout(function () {
  loader.update('即将完成…');
}, 1000);

setTimeout(function () {
  loader.close();
}, 2000);
```

### 四、Modal 返回值说明

#### `Modal.show / alert / confirm`

返回 `Promise<string>`，不同关闭方式会得到不同值：

- 点击确认按钮：`confirm`
- 点击取消按钮：`cancel`
- 点击遮罩关闭：`overlay`
- 按 `Esc` 关闭：`escape`
- 如果使用了自定义按钮，则返回对应按钮的 `value`

#### `Modal.prompt`

返回 `Promise<string | null>`：

- 确认时返回输入内容
- 取消、点遮罩、按 `Esc` 时返回 `null`

#### `Modal.loading`

返回：

```js
{
  close: Function,
  update: Function
}
```

### 五、Modal 常用参数

| 参数 | 说明 |
| --- | --- |
| `title` | 标题 |
| `type` | `success / error / warning / info` |
| `confirm` | 确认按钮文案 |
| `cancel` | 取消按钮文案；传 `false` 时不显示取消按钮 |
| `buttons` | 自定义按钮数组，覆盖默认确认/取消按钮 |
| `closable` | 是否允许点击遮罩或按 `Esc` 关闭 |
| `inputType` | `prompt` 使用，支持 `text / password / number / email` |
| `placeholder` | `prompt` 输入框占位文本 |
| `defaultValue` | `prompt` 默认值 |
| `required` | `prompt` 是否必填 |

---

## 主题色联动机制

两个脚本之间有一层天然联动：

- `background-switcher.js` 会读取图片接口响应头中的颜色信息
- 然后将颜色写入页面根节点 CSS 变量：
  - `--bg-image-theme-color`
  - `--bg-image-text-color`
- `popup.js` 内部会优先使用这些变量，为 Toast 和 Modal 自动适配主题色

因此，如果你的图片接口能给出合适的主色和文字色，整套视觉会自动统一。

如果没有返回颜色响应头，也不会报错，只是会退回脚本里的默认颜色。

---

## 完整示例

下面是一个更接近实际使用场景的完整页面示例：

```html
<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>背景切换示例</title>
  <style>
    body {
      margin: 0;
      color: var(--bg-image-text-color, #fff);
      font-family: "Segoe UI", sans-serif;
    }

    .page {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-direction: column;
      gap: 12px;
      text-align: center;
      background: linear-gradient(
        to bottom,
        color-mix(in srgb, var(--bg-image-theme-color, #1e293b) 30%, transparent),
        rgba(0, 0, 0, 0.15)
      );
    }

    button {
      padding: 10px 16px;
      border: 0;
      border-radius: 8px;
      cursor: pointer;
    }
  </style>
</head>
<body>
  <div class="page">
    <h1>欢迎使用背景切换组件</h1>
    <p>页面主题色将跟随图片接口返回值自动变化。</p>
    <button id="toastBtn">显示提示</button>
    <button id="refreshBtn">手动切换背景</button>
  </div>

  <script
    src="./background-switcher.js"
    data-api="https://your-image-api.example.com/random"
    data-interval="60"
    data-auth="./auth-api.php">
  </script>
  <script src="./popup.js"></script>
  <script>
    document.getElementById('toastBtn').addEventListener('click', function () {
      Toast.success('操作成功');
    });

    document.getElementById('refreshBtn').addEventListener('click', function () {
      BackgroundSwitcher.init('https://your-image-api.example.com/random', {
        silent: false
      });
    });
  </script>
</body>
</html>
```

---

## 兼容性与注意事项

### 1. 这是面向现代浏览器的脚本

代码中使用了以下现代能力：

- `fetch`
- `Promise`
- `async / await`
- `ReadableStream`
- `backdrop-filter`
- `color-mix`

因此更适合现代 Chromium 内核浏览器、较新的 Safari / Edge / Firefox 环境。

### 2. 接口跨域需要服务端配合

如果页面与图片接口、登录接口不在同一域名下，服务端需要正确开启 CORS，否则浏览器会拦截请求。

### 3. 自动初始化依赖 `document.currentScript`

也就是说：

- 最适合直接通过单独的 `<script src="...">` 使用
- 如果后续把代码打包、拼接或改造成模块化方案，需要重新评估自动初始化逻辑

### 4. 组件会接管页面的滚动容器

接入 `background-switcher.js` 后，页面内容会被包到 `#bg-switcher-content`，滚动也会从 `body` 转移过去。若你的页面本身依赖特殊滚动方案，请提前验证。

### 5. `popup.js` 更像页面组件，而不是通用 npm 包

当前项目没有：

- `package.json`
- 模块导出
- 构建脚本
- 测试脚本

它的设计目标是“直接丢进页面即可使用”。

---

## 建议的后续完善方向

如果后面准备把这个项目作为可复用组件继续维护，建议优先补以下内容：

1. 提供一个最小可运行的示例 HTML 页面
2. 增加一个示例 `auth-api.php` 或接口协议文档
3. 补上版本说明和变更记录
4. 如需工程化复用，可再补模块化导出与打包配置

---

## 总结

这是一个偏“直接落地到页面”的前端小组件集合：

- `background-switcher.js` 负责背景、主题和登录前展示体验
- `popup.js` 负责提示框和对话框交互

两个文件可以独立使用，也可以组合使用。组合时最大的价值在于：背景接口返回的颜色信息会自动驱动整页 UI 的视觉统一。
