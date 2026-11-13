# 毛玻璃过渡动画

CSS `backdrop-filter` 毛玻璃效果过渡动画实现指南。

## 核心问题

父元素 `opacity < 1` 时，子元素的 `backdrop-filter` 在过渡期间不会渲染模糊效果，直到 `opacity` 达到 `1` 的瞬间才突然出现。

## 解决方案

在目标元素上使用 `@keyframes` 动画同步过渡 `opacity`、`backdrop-filter` 和 `background`，而非在父元素上过渡 `opacity`。

```css
@keyframes glass-reveal {
  from {
    opacity: 0;
    backdrop-filter: blur(0px);
    background: transparent;
  }
  to {
    opacity: 1;
    backdrop-filter: blur(16px);
    background: rgba(255, 255, 255, 0.15);
  }
}
```

## 内容

- `毛玻璃过渡动画指南.md` — 完整实现指南与错误做法说明
