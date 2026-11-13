# === 配置区域 ===
$ApiToken     = "" # 这里填入你新创建的 API Token
$ZoneId       = "" # 这里填入你的 Zone ID
$Domain       = "" # 这里填入域名
$IntervalMin  = 10 # 自动运行间隔时间（分钟）
$MaxLogSizeMB = 2  # 日志文件最大体积（单位：MB），超过此大小将自动清理
# ===============

$Headers = @{
    "Authorization" = "Bearer $ApiToken"
    "Content-Type"  = "application/json"
}

# ----------------- 🎯 日志功能与自动清理初始化 -----------------
# 获取脚本当前所在的绝对目录
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ScriptDir) { $ScriptDir = $PSScriptRoot }
if (-not $ScriptDir) { $ScriptDir = $pwd }
$LogFile = Join-Path $ScriptDir "ddns_run.log"

# 【核心更新】日志自动清理逻辑
if (Test-Path $LogFile) {
    $LogSize = (Get-Item $LogFile).Length / 1MB
    if ($LogSize -gt $MaxLogSizeMB) {
        # 备份当前大日志，覆盖上一次的备份（保留一个历史副本，防止刚出问题就被删）
        $BackupFile = Join-Path $ScriptDir "ddns_run.log.bak"
        Move-Item -Path $LogFile -Destination $BackupFile -Force
        
        # 创建新日志并写入清理记录
        $TimeStamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        "[$TimeStamp] [INFO] 日志体积超过 $MaxLogSizeMB MB，已自动归档旧日志并重新开始记录。" | Out-File -FilePath $LogFile -Encoding UTF8
    }
}

# 定义日志记录函数
function Write-Log {
    param (
        [string]$Message,
        [string]$Level = "INFO"
    )
    $TimeStamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $CleanMessage = $Message -replace "❌|✅|☁️|🔄|🎉|➕|⚙️|⚠️", ""
    $LogLine = "[$TimeStamp] [$Level] $CleanMessage"
    
    Add-Content -Path $LogFile -Value $LogLine -Encoding UTF8
    Write-Host $Message
}
# ---------------------------------------------------

# ----------------- 自动注册任务计划程序逻辑 -----------------
$TaskName = "Cloudflare_DDNS_IPv6"
if (-not (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)) {
    Write-Log "⚙️ 正在尝试将脚本自动注册到 Windows 任务计划程序..."
    
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Log "⚠️ 权限不足：首次运行需要【管理员身份】来自动创建定时任务！" "WARN"
        Write-Host "💡 请右键以管理员身份运行 PowerShell 再次执行此脚本。"
    } else {
        $ScriptPath = $MyInvocation.MyCommand.Path
        if ($ScriptPath) {
            $Action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-WindowStyle Hidden -NoProfile -ExecutionPolicy Bypass -File `"$ScriptPath`""
            $Trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes $IntervalMin)
            $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
            
            Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Settings $Settings -User "NT AUTHORITY\SYSTEM" -RunLevel Highest | Out-Null
            Write-Log "🎉 成功！任务计划已创建。以后每 $IntervalMin 分钟将在后台静默运行一次。"
        } else {
            Write-Log "❌ 错误：无法获取当前脚本的路径，请先保存脚本为 .ps1 文件" "ERROR"
        }
    }
}
# ------------------------------------------------------------

# 1. 直接从 Windows 本机网卡获取真实的公网 IPv6 地址
try {
    $CurrentIpv6 = (Get-NetIPAddress -AddressFamily IPv6 | 
        Where-Object { 
            $_.IPAddress -match "^2" -and 
            $_.PrefixOrigin -eq "RouterAdvertisement" -and 
            $_.SuffixOrigin -eq "Link" -and
            $_.AddressState -eq "Preferred"
        } | Select-Object -First 1).IPAddress

    if (-not $CurrentIpv6) {
        Write-Log "❌ 错误：本机网卡未获取到首选的公网 IPv6，请检查网络。" "ERROR"
        exit
    }
    
    Write-Log "✅ 成功读取本机公网 IPv6: $CurrentIpv6"
} catch {
    Write-Log "❌ 错误：读取本机网卡信息失败。" "ERROR"
    exit
}

# 2. 获取 Cloudflare 上当前的解析记录
try {
    $RecordUrl = "https://api.cloudflare.com/client/v4/zones/$ZoneId/dns_records?type=AAAA&name=$Domain"
    $RecordResponse = Invoke-RestMethod -Uri $RecordUrl -Headers $Headers -Method Get
} catch {
    Write-Log "❌ 错误：请求 Cloudflare API 失败，请检查网络连接或 API Token。" "ERROR"
    exit
}

# 3. 判断是新建还是更新
$BaseBody = @{
    type    = "AAAA"
    name    = $Domain
    content = $CurrentIpv6
    proxied = $false
    ttl     = 1
}

if ($RecordResponse.result.Count -eq 0) {
    # 🌟 情况 A：没有找到记录，执行自动创建 (POST)
    Write-Log "➕ 未找到域名 $Domain 的 AAAA 记录，正在自动创建..."
    
    $CreateUrl = "https://api.cloudflare.com/client/v4/zones/$ZoneId/dns_records"
    $Body = $BaseBody | ConvertTo-Json
    $CreateResponse = Invoke-RestMethod -Uri $CreateUrl -Headers $Headers -Method Post -Body $Body

    if ($CreateResponse.success) {
        Write-Log "🎉 自动创建成功！DNS 已指向 $CurrentIpv6"
    } else {
        Write-Log "❌ 创建失败！请检查 API Token 权限是否正确。" "ERROR"
    }

} else {
    # 🌟 情况 B：找到了记录，对比并更新 (PUT)
    $RecordId = $RecordResponse.result[0].id
    $CloudflareIpv6 = $RecordResponse.result[0].content
    Write-Log "☁️ Cloudflare 当前解析: $CloudflareIpv6"

    if ($CurrentIpv6 -ne $CloudflareIpv6) {
        Write-Log "🔄 检测到 IPv6 发生变化，正在更新..."

        $UpdateUrl = "https://api.cloudflare.com/client/v4/zones/$ZoneId/dns_records/$RecordId"
        $Body = $BaseBody | ConvertTo-Json
        $UpdateResponse = Invoke-RestMethod -Uri $UpdateUrl -Headers $Headers -Method Put -Body $Body

        if ($UpdateResponse.success) {
            Write-Log "🎉 更新成功！DNS 已指向 $CurrentIpv6"
        } else {
            Write-Log "❌ 更新失败！请检查 Token 权限。" "ERROR"
        }
    } else {
        Write-Log "✅ IP 未发生变化，无需更新。"
    }
}