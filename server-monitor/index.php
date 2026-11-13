<?php
// 定义服务状态文件夹的路径
$statusDir = '配置文件/'; // 设置存放服务状态文件的文件夹路径，替换为你实际的文件夹路径

// 检查文件夹是否存在
if (!is_dir($statusDir)) {
    die("状态文件夹不存在！"); // 如果文件夹不存在，终止执行并输出错误信息
}

// 获取文件夹中的所有文件
$files = array_diff(scandir($statusDir), ['.', '..']); // 获取文件夹中的文件，并排除"."和".."特殊目录
?>
<!DOCTYPE html>
<html lang="zh-CN"> <!-- 设置文档的语言为中文 -->
<head>
    <meta charset="UTF-8"> <!-- 设置字符编码为 UTF-8，支持中文字符 -->
    <meta name="viewport" content="width=device-width, initial-scale=1.0"> <!-- 设置响应式布局，适配不同设备 -->
    <title>服务状态</title> <!-- 页面标题，显示在浏览器标签上 -->
    <link rel="stylesheet" href="style.css">
</head>
<body>
<div id="loading">正在加载中...</div> <!-- 页面加载时的提示信息，初始显示在页面中间 -->

<div id="content" style="display:none;"> <!-- 页面内容，初始隐藏 -->
    <h1>服务运行状态</h1> <!-- 页面主标题 -->
    <?php if (empty($files)) : ?> <!-- 检查是否有文件 -->
        <p>没有服务状态文件。</p> <!-- 如果没有文件，显示提示信息 -->
    <?php else: ?> <!-- 如果有文件 -->
        <ul> <!-- 开始文件列表 -->
            <?php foreach ($files as $file): ?> <!-- 遍历文件 -->
                <section> <!-- 每个状态的区块 -->
                <h2 onclick="toggleSection(this)"> <!-- 可点击标题 -->
                        <?= htmlspecialchars(pathinfo($file, PATHINFO_FILENAME)) ?> <!-- 显示文件名，避免XSS -->
                    </h2>
                    <div class="content"> <!-- 内容区域 -->
                        <?php
                        $filePath = $statusDir . $file; // 构建文件路径
                        if (file_exists($filePath)) { // 检查文件是否存在
                            // 读取 INI 文件

                            $iniArray = parse_ini_file($filePath, true, INI_SCANNER_TYPED);

                            foreach ($iniArray as $sectionName => $sectionContent) {
                                echo "<div>";
                                echo '<h3 onclick="toggleSection(this)">' . htmlspecialchars($sectionName) . "</h3>";
                                echo '<div class="content">';
                                foreach ($sectionContent as $key => $value) {
                                    if (is_array($value)) {
                                        foreach ($value as $v) {
                                            echo "<strong>" . htmlspecialchars($key) . "：</strong><br> " . htmlspecialchars(trim($v, '"')) . "<br>";
                                        }
                                    } else {
                                        echo "<strong>" . htmlspecialchars($key) . "：</strong><br> " . htmlspecialchars(trim($value, '"')) . "<br>";
                                    }
                                }
                                echo "</div>";
                                echo "</div>";
                            }
                        }
                        ?>
                    </div>
                </section>
            <?php endforeach; ?> <!-- 结束文件遍历 -->
        </ul>
    <?php endif; ?> <!-- 结束文件检查 -->
</div>
<script>
// toggleSection函数用于切换内容显示或隐藏
function toggleSection(element) {
    const content = element.nextElementSibling;
    if (content.classList.contains('show')) {
        // 收起
        content.classList.remove('show');
        content.classList.add('showing'); // 禁止滚动
        content.style.overflowY = 'hidden'; // 强制隐藏滚动条
        content.addEventListener('transitionend', function handler() {
            content.classList.remove('showing');
            content.removeEventListener('transitionend', handler);
        });
    } else {
        // 展开
        content.classList.add('showing'); // 先添加 showing，防止滚动条出现
        content.classList.add('show');    // 触发动画
        content.style.overflowY = 'hidden'; // 开始时隐藏滚动条

        // 动画结束后开启滚动条
        content.addEventListener('transitionend', function handler() {
            content.classList.remove('showing');
            content.style.overflowY = 'auto'; // 动画结束后显示滚动条
            content.removeEventListener('transitionend', handler);
        });
    }
}

// 获取自建的图片api响应头X-Theme-Color主题色
fetch('https://random-image.xct258.top') // 请求外部图片API
    .then(response => {
        const headers = response.headers; // 获取响应头
        const themeColor = headers.get('X-Theme-Color'); // 获取主题色
        function rgbToRgba(alpha) {
            // 提取 rgb 中的红、绿、蓝色值
            const rgbValues = themeColor.match(/\d+/g);
            // 转换为 rgba，使用给定的 alpha 透明度
            return `rgba(${rgbValues[0]}, ${rgbValues[1]}, ${rgbValues[2]}, ${alpha})`;
        }

        const rgba = rgbToRgba(0.6);
        // 设置页面的主题色
        document.querySelector('h1').style.color = rgba; // 设置标题颜色
        const sections = document.querySelectorAll('section'); // 获取所有的section元素
        sections.forEach(section => {
            section.style.background = rgba; // 设置每个section的背景色
        });

        // 计算背景颜色的亮度
        function getLuminance(r, g, b) {
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        const rgbValues = themeColor.match(/\d+/g).map(Number);
        const luminance = getLuminance(rgbValues[0], rgbValues[1], rgbValues[2]);

        // 动态调整悬停颜色
        function getHoverColor(r, g, b) {
            // 增加亮度来设置悬停颜色
            return `rgba(${Math.min(255, r + 40)}, ${Math.min(255, g + 40)}, ${Math.min(255, b + 40)}, 0.7)`;
        }

        // 获取鼠标悬停时的颜色
        const hoverColor = getHoverColor(rgbValues[0], rgbValues[1], rgbValues[2]);

        // 动态修改 h2:hover 的颜色
        const style = document.createElement('style');
        style.innerHTML = `
            h2:hover {
                color: ${hoverColor} !important;
            }
            h3:hover {
                color: ${hoverColor} !important;
            }
        `;
        document.head.appendChild(style);



        // 根据亮度调整文字颜色，使其不会是纯黑或纯白
        function adjustTextColor(r, g, b) {
            const luminanceThreshold = 128; // 亮度的阈值，适用于大部分情况
            let adjustedColor;

            // 如果亮度较高，文字颜色调整为较暗的色调，否则调整为较亮的色调
            if (luminance > luminanceThreshold) {
                // 背景亮，使用较深的颜色（但不使用纯黑）
                adjustedColor = `rgb(${Math.max(0, r - 75)}, ${Math.max(0, g - 75)}, ${Math.max(0, b - 75)})`;
            } else {
                // 背景暗，使用较亮的颜色（但不使用纯白）
                adjustedColor = `rgb(${Math.min(255, r + 75)}, ${Math.min(255, g + 75)}, ${Math.min(255, b + 75)})`;
            }


            return adjustedColor;
        }

        // 调整文字颜色
        const adjustedTextColor = adjustTextColor(rgbValues[0], rgbValues[1], rgbValues[2]);
        document.querySelectorAll('h1, h2, h3, section').forEach(element => {
            element.style.color = adjustedTextColor;
        });
        // 获取图片并加载
        return response.blob().then(imageBlob => {
            const imageUrl = URL.createObjectURL(imageBlob); // 将图片数据转为URL
            document.body.style.backgroundImage = `url('${imageUrl}')`; // 设置页面的背景图片

            // 图片加载完成后隐藏加载中提示并显示页面内容
            document.getElementById('loading').style.display = 'none'; // 隐藏加载提示
            document.getElementById('content').style.display = 'block'; // 显示页面内容
        });
    })
    .catch(error => {
        console.error('图片加载失败:', error); // 捕获并打印加载错误
        // 失败处理，显示错误信息或者做其他处理
        document.getElementById('loading').innerText = '加载失败，请稍后再试。'; // 显示加载失败信息
    });

</script>

</body>
</html>
