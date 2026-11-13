<?php
declare(strict_types=1);

const CACHE_FILE = __DIR__ . '/.color_cache.json';
const IMAGE_LIST_CACHE_FILE = __DIR__ . '/.image_list_cache.json';
const IMAGE_ID_MAP_FILE = __DIR__ . '/.image_id_map.json';
const LAST_IMAGE_COOKIE_PREFIX = 'last_image_path_';
const LAST_IMAGE_COOKIE_TTL = 86400 * 30;
const DEFAULT_TEXT_TARGET_CONTRAST = 4.5;

$config = [
    'folders' => [
        'mobile' => [
            'path' => '/home/xct258/images/pe/',
            'id_prefix' => 'pe-image',
        ],
        'desktop' => [
            'path' => '/home/xct258/images/pc/',
            'id_prefix' => 'pc-image',
        ],
    ],
    'mime_types' => [
        'jpg' => 'image/jpeg',
        'jpeg' => 'image/jpeg',
        'png' => 'image/png',
        'gif' => 'image/gif',
        'webp' => 'image/webp',
    ],
];

serveRandomImage($config);

function serveRandomImage(array $config): void
{
    $source = resolveImageSource($config['folders']);
    $folder = $source['path'];
    ensureReadableDirectory($folder);
    $images = loadImageListCacheForFolder($folder, array_keys($config['mime_types']));
    if ($images === []) {
        respondJsonError('No images were found in the configured folder.', 500, ['folder' => $folder]);
    }

    $imagePath = pickRandomImage($images, $folder, $source['id_prefix'], $images);
    $analysis = analyzeImage($imagePath, $config);
    $imageId = getImageId($source['id_prefix'], $folder, $images, $imagePath);

    header('Access-Control-Allow-Origin: *');
    header('Access-Control-Expose-Headers: X-Theme-Color, X-Theme-Color-Hex, X-Text-Color, X-Text-Color-Hex, X-Text-Contrast, X-Image-Id, Content-Length');
    header('Cache-Control: no-store, no-cache, must-revalidate, max-age=0');
    header('Content-Length: ' . (string) filesize($imagePath));
    header('Content-Type: ' . detectMimeType($imagePath, $config['mime_types']));
    header('X-Theme-Color: ' . formatRgb($analysis['theme_rgb']));
    header('X-Theme-Color-Hex: ' . formatHex($analysis['theme_rgb']));
    header('X-Text-Color: ' . formatRgb($analysis['text_rgb']));
    header('X-Text-Color-Hex: ' . formatHex($analysis['text_rgb']));
    header('X-Text-Contrast: ' . number_format($analysis['text_contrast'], 2, '.', ''));
    header('X-Image-Id: ' . $imageId);

    readfile($imagePath);
    exit;
}

function resolveImageSource(array $folders): array
{
    $type = strtolower((string) ($_GET['type'] ?? ''));
    if ($type === 'pc' || $type === 'desktop') {
        return $folders['desktop'];
    }
    if ($type === 'pe' || $type === 'mobile') {
        return $folders['mobile'];
    }

    $userAgent = $_SERVER['HTTP_USER_AGENT'] ?? '';
    $isMobile = (bool) preg_match('/Mobile|Android|iPhone|iPad|iPod/i', $userAgent);
    return $isMobile ? $folders['mobile'] : $folders['desktop'];
}

function ensureReadableDirectory(string $folder): void
{
    if (!is_dir($folder) || !is_readable($folder)) {
        respondJsonError('Image folder is not available.', 500, ['folder' => $folder]);
    }
}

function collectImages(string $folder, array $extensions): array
{
    $pattern = '/\.(' . implode('|', array_map('preg_quote', $extensions)) . ')$/i';
    $images = [];

    $iterator = new RecursiveIteratorIterator(
        new RecursiveDirectoryIterator($folder, FilesystemIterator::SKIP_DOTS)
    );

    foreach ($iterator as $file) {
        if ($file->isFile() && preg_match($pattern, $file->getFilename())) {
            $images[] = $file->getPathname();
        }
    }

    sort($images);
    return $images;
}

function loadImageListCacheForFolder(string $folder, array $extensions): array
{
    $cache = loadJsonFile(IMAGE_LIST_CACHE_FILE);
    $cacheKey = hash('sha256', $folder);
    $folderMtime = (int) (filemtime($folder) ?: 0);

    if (
        isset($cache[$cacheKey]['folder_mtime'], $cache[$cacheKey]['images']) &&
        (int) $cache[$cacheKey]['folder_mtime'] === $folderMtime &&
        is_array($cache[$cacheKey]['images'])
    ) {
        return array_values(array_filter($cache[$cacheKey]['images'], 'is_string'));
    }

    $images = collectImages($folder, $extensions);
    $cache[$cacheKey] = [
        'folder' => $folder,
        'folder_mtime' => $folderMtime,
        'images' => $images,
        'updated_at' => time(),
    ];
    saveJsonFile(IMAGE_LIST_CACHE_FILE, $cache);

    return $images;
}

function getImageId(string $prefix, string $folder, array $images, string $imagePath): string
{
    $map = loadJsonFile(IMAGE_ID_MAP_FILE);
    $folderKey = hash('sha256', $folder);
    $items = [];
    $needsSave = false;

    if (isset($map[$folderKey]['items']) && is_array($map[$folderKey]['items'])) {
        foreach ($map[$folderKey]['items'] as $path => $id) {
            if (is_string($path) && is_string($id)) {
                $items[$path] = $id;
            }
        }
    }

    $nextIndex = 1;
    foreach ($items as $id) {
        if (preg_match('/^' . preg_quote($prefix, '/') . '-(\d+)$/', $id, $matches)) {
            $nextIndex = max($nextIndex, ((int) $matches[1]) + 1);
        }
    }

    foreach ($images as $path) {
        if (!isset($items[$path])) {
            $items[$path] = sprintf('%s-%d', $prefix, $nextIndex);
            $nextIndex++;
            $needsSave = true;
        }
    }

    foreach (array_keys($items) as $path) {
        if (!in_array($path, $images, true)) {
            unset($items[$path]);
            $needsSave = true;
        }
    }

    if (!isset($map[$folderKey]['items']) || $needsSave) {
        $map[$folderKey] = [
            'folder' => $folder,
            'items' => $items,
            'updated_at' => time(),
        ];
        saveJsonFile(IMAGE_ID_MAP_FILE, $map);
    }

    return isset($items[$imagePath]) && is_string($items[$imagePath])
        ? $items[$imagePath]
        : sprintf('%s-%d', $prefix, 1);
}

function pickRandomImage(array $images, string $scope, string $idPrefix = '', array $allImages = []): string
{
    $scopeKey = LAST_IMAGE_COOKIE_PREFIX . hash('sha256', $scope);
    $lastImageFingerprint = isset($_COOKIE[$scopeKey]) && is_string($_COOKIE[$scopeKey]) ? $_COOKIE[$scopeKey] : '';

    // 支持 ?exclude=pc-image-1 参数（跨域时 Cookie 不可用的备用方案）
    $excludeId = isset($_GET['exclude']) && is_string($_GET['exclude']) ? trim($_GET['exclude']) : '';
    $excludePath = '';
    if ($excludeId !== '' && $idPrefix !== '' && $allImages !== []) {
        $map = loadJsonFile(IMAGE_ID_MAP_FILE);
        $folderKey = hash('sha256', $scope);
        if (isset($map[$folderKey]['items']) && is_array($map[$folderKey]['items'])) {
            $excludePath = array_search($excludeId, $map[$folderKey]['items'], true);
            if ($excludePath === false) $excludePath = '';
        }
    }

    $selected = $images[array_rand($images)];

    if (count($images) > 1) {
        $maxAttempts = 50;
        $attempts = 0;
        while ($attempts < $maxAttempts) {
            $skip = false;
            if ($lastImageFingerprint !== '' && hash('sha256', $selected) === $lastImageFingerprint) {
                $skip = true;
            }
            if (!$skip && $excludePath !== '' && $selected === $excludePath) {
                $skip = true;
            }
            if (!$skip) break;
            $selected = $images[array_rand($images)];
            $attempts++;
        }
    }

    $selectedFingerprint = hash('sha256', $selected);

    setcookie($scopeKey, $selectedFingerprint, [
        'expires' => time() + LAST_IMAGE_COOKIE_TTL,
        'path' => '/',
        'httponly' => false,
        'samesite' => 'Lax',
    ]);
    $_COOKIE[$scopeKey] = $selectedFingerprint;

    return $selected;
}

function analyzeImage(string $imagePath, array $config): array
{
    $cache = loadColorCache(CACHE_FILE);
    $mtime = filemtime($imagePath) ?: 0;

    if (isset($cache[$imagePath], $cache[$imagePath]['mtime'], $cache[$imagePath]['theme'])) {
        if ((int) $cache[$imagePath]['mtime'] === $mtime) {
            $themeRgb = parseColorToRgb((string) $cache[$imagePath]['theme']);
            if ($themeRgb !== null) {
                $textRgb = getReadableTextColor($themeRgb);
                return [
                    'theme_rgb' => $themeRgb,
                    'text_rgb' => $textRgb,
                    'text_contrast' => contrastRatio(relativeLuminance($textRgb), relativeLuminance($themeRgb)),
                    'cache_status' => 'hit',
                ];
            }
        }
    }

    $themeString = extractThemeColor($imagePath);
    $themeRgb = parseColorToRgb($themeString) ?? averageImageFallbackColor($imagePath) ?? [52, 73, 94];
    $textRgb = getReadableTextColor($themeRgb);

    $cache[$imagePath] = [
        'mtime' => $mtime,
        'theme' => formatHex($themeRgb),
    ];
    saveColorCache(CACHE_FILE, $cache);

    return [
        'theme_rgb' => $themeRgb,
        'text_rgb' => $textRgb,
        'text_contrast' => contrastRatio(relativeLuminance($textRgb), relativeLuminance($themeRgb)),
        'cache_status' => 'miss',
    ];
}

function extractThemeColor(string $imagePath): string
{
    $command = 'convert '
        . escapeshellarg($imagePath)
        . " -resize 160x160^ -gravity center -extent 160x160 -colors 1 -unique-colors -format '%[pixel:p{0,0}]' info:-";

    $output = shell_exec($command);
    return is_string($output) ? trim($output) : '';
}

function averageImageFallbackColor(string $imagePath): ?array
{
    $command = 'convert '
        . escapeshellarg($imagePath)
        . " -resize 1x1\\! -format '%[pixel:p{0,0}]' info:-";

    $output = shell_exec($command);
    return parseColorToRgb(is_string($output) ? trim($output) : '');
}

function parseColorToRgb(string $value): ?array
{
    $value = trim($value);
    if ($value === '') {
        return null;
    }

    if (preg_match('/^#([0-9a-f]{6})$/i', $value, $matches)) {
        $hex = $matches[1];
        return [
            hexdec(substr($hex, 0, 2)),
            hexdec(substr($hex, 2, 2)),
            hexdec(substr($hex, 4, 2)),
        ];
    }

    if (preg_match('/^#([0-9a-f]{3})$/i', $value, $matches)) {
        $hex = $matches[1];
        return [
            hexdec(str_repeat($hex[0], 2)),
            hexdec(str_repeat($hex[1], 2)),
            hexdec(str_repeat($hex[2], 2)),
        ];
    }

    if (preg_match('/^srgba?\((.+)\)$/i', $value, $matches)) {
        $parts = array_map('trim', explode(',', $matches[1]));
        if (count($parts) >= 3) {
            return [
                parseChannelValue($parts[0]),
                parseChannelValue($parts[1]),
                parseChannelValue($parts[2]),
            ];
        }
    }

    if (preg_match('/^rgba?\((.+)\)$/i', $value, $matches)) {
        $parts = array_map('trim', explode(',', $matches[1]));
        if (count($parts) >= 3) {
            return [
                (int) round((float) $parts[0]),
                (int) round((float) $parts[1]),
                (int) round((float) $parts[2]),
            ];
        }
    }

    return null;
}

function parseChannelValue(string $value): int
{
    if (strpos($value, '%') !== false) {
        return (int) round(max(0, min(100, (float) rtrim($value, '%'))) * 2.55);
    }

    return (int) round(max(0, min(255, (float) $value)));
}

function getReadableTextColor(array $themeRgb): array
{
    $themeLum = relativeLuminance($themeRgb);
    $hsl = rgbToHsl($themeRgb);
    $hue = $hsl[0];

    // 低饱和度，保留微弱的主题色倾向
    $saturation = min($hsl[1], 0.15);

    // 根据背景明暗选择文字方向
    $needLight = $themeLum <= 0.18;

    // 二分搜索合适的亮度，确保对比度 ≥ 4.5:1
    $lo = $needLight ? 0.75 : 0.0;
    $hi = $needLight ? 1.0 : 0.25;
    $bestRgb = null;

    for ($i = 0; $i < 20; $i++) {
        $mid = ($lo + $hi) / 2;
        $candidateRgb = hslToRgb($hue, $saturation, $mid);
        $candidateLum = relativeLuminance($candidateRgb);
        $contrast = contrastRatio($candidateLum, $themeLum);

        if ($contrast >= DEFAULT_TEXT_TARGET_CONTRAST) {
            $bestRgb = $candidateRgb;
            // 继续搜索更接近背景的亮度（更和谐）
            if ($needLight) {
                $hi = $mid;
            } else {
                $lo = $mid;
            }
        } else {
            // 对比度不足，向远离背景的方向搜索
            if ($needLight) {
                $lo = $mid;
            } else {
                $hi = $mid;
            }
        }
    }

    if ($bestRgb !== null) {
        return $bestRgb;
    }

    // 回退到纯黑/白
    return $needLight ? [255, 255, 255] : [0, 0, 0];
}

function rgbToHsl(array $rgb): array
{
    $r = $rgb[0] / 255;
    $g = $rgb[1] / 255;
    $b = $rgb[2] / 255;

    $max = max($r, $g, $b);
    $min = min($r, $g, $b);
    $l = ($max + $min) / 2;
    $d = $max - $min;

    if ($d < 1e-6) {
        return [0.0, 0.0, $l];
    }

    $s = $d / (1 - abs(2 * $l - 1));

    if ($max === $r) {
        $h = fmod(($g - $b) / $d + 6, 6) / 6;
    } elseif ($max === $g) {
        $h = (($b - $r) / $d + 2) / 6;
    } else {
        $h = (($r - $g) / $d + 4) / 6;
    }

    return [$h, $s, $l];
}

function hslToRgb(float $h, float $s, float $l): array
{
    if ($s < 1e-6) {
        $v = (int) round($l * 255);
        return [$v, $v, $v];
    }

    $c = (1 - abs(2 * $l - 1)) * $s;
    $x = $c * (1 - abs(fmod($h * 6, 2) - 1));
    $m = $l - $c / 2;

    if ($h < 1/6)     { $r = $c; $g = $x; $b = 0; }
    elseif ($h < 2/6) { $r = $x; $g = $c; $b = 0; }
    elseif ($h < 3/6) { $r = 0;  $g = $c; $b = $x; }
    elseif ($h < 4/6) { $r = 0;  $g = $x; $b = $c; }
    elseif ($h < 5/6) { $r = $x; $g = 0;  $b = $c; }
    else              { $r = $c; $g = 0;  $b = $x; }

    return [
        (int) round(($r + $m) * 255),
        (int) round(($g + $m) * 255),
        (int) round(($b + $m) * 255),
    ];
}

function relativeLuminance(array $rgb): float
{
    $srgb = array_map(static function (int $channel): float {
        $channel = $channel / 255;
        return $channel <= 0.03928 ? $channel / 12.92 : (($channel + 0.055) / 1.055) ** 2.4;
    }, $rgb);

    return 0.2126 * $srgb[0] + 0.7152 * $srgb[1] + 0.0722 * $srgb[2];
}

function contrastRatio(float $l1, float $l2): float
{
    $lighter = max($l1, $l2);
    $darker = min($l1, $l2);
    return ($lighter + 0.05) / ($darker + 0.05);
}

function formatRgb(array $rgb): string
{
    return sprintf('rgb(%d, %d, %d)', $rgb[0], $rgb[1], $rgb[2]);
}

function formatHex(array $rgb): string
{
    return sprintf('#%02x%02x%02x', $rgb[0], $rgb[1], $rgb[2]);
}

function detectMimeType(string $imagePath, array $mimeTypes): string
{
    $ext = strtolower(pathinfo($imagePath, PATHINFO_EXTENSION));
    return $mimeTypes[$ext] ?? 'application/octet-stream';
}

function loadColorCache(string $file): array
{
    return loadJsonFile($file);
}

function saveColorCache(string $file, array $cache): void
{
    saveJsonFile($file, $cache);
}

function loadJsonFile(string $file): array
{
    if (!is_file($file)) {
        return [];
    }

    $decoded = json_decode((string) file_get_contents($file), true);
    return is_array($decoded) ? $decoded : [];
}

function saveJsonFile(string $file, array $data): void
{
    file_put_contents($file, json_encode($data, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE), LOCK_EX);
}

function respondJsonError(string $message, int $statusCode, array $extra = []): void
{
    http_response_code($statusCode);
    header('Content-Type: application/json; charset=UTF-8');
    echo json_encode(array_merge(['error' => $message], $extra), JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
    exit;
}
