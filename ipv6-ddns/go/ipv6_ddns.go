package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/url"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"time"
)

type Config struct {
	ApiToken    string `json:"ApiToken"`
	ZoneId      string `json:"ZoneId"`
	Domain      string `json:"Domain"`
	IntervalMin int    `json:"IntervalMin"`
}

type CfResponse struct {
	Success bool            `json:"success"`
	Result  json.RawMessage `json:"result"`
	Errors  json.RawMessage `json:"errors"`
}

type DnsRecord struct {
	Id      string `json:"id"`
	Name    string `json:"name"`
	Type    string `json:"type"`
	Content string `json:"content"`
}

type DnsRecordList []DnsRecord

type DnsUpdateBody struct {
	Type    string `json:"type"`
	Name    string `json:"name"`
	Content string `json:"content"`
	Proxied bool   `json:"proxied"`
	Ttl     int    `json:"ttl"`
}

var httpClient = &http.Client{Timeout: 30 * time.Second}
var taskName = "Cloudflare_DDNS_IPv6"
const maxLogSize = 512 * 1024

func exeDir() string {
	exe, err := os.Executable()
	if err != nil {
		return "."
	}
	return filepath.Dir(exe)
}

func loadOrCreateConfig() *Config {
	path := filepath.Join(exeDir(), "ddns_config.json")
	data, err := os.ReadFile(path)
	if err == nil {
		var cfg Config
		if json.Unmarshal(data, &cfg) == nil {
			return &cfg
		}
	}
	return promptConfig()
}

func promptConfig() *Config {
	fmt.Println("=========================================")
	fmt.Println(" 首次运行，未检测到配置文件，请录入信息：")
	fmt.Println("=========================================")

	reader := bufio.NewReader(os.Stdin)

	fmt.Print("1. 请输入 Cloudflare API Token: ")
	apiToken, _ := reader.ReadString('\n')
	apiToken = strings.TrimSpace(apiToken)

	fmt.Print("2. 请输入 Cloudflare Zone ID: ")
	zoneId, _ := reader.ReadString('\n')
	zoneId = strings.TrimSpace(zoneId)

	fmt.Print("3. 请输入需要解析的域名: ")
	domain, _ := reader.ReadString('\n')
	domain = strings.TrimSpace(domain)

	cfg := &Config{
		ApiToken:    apiToken,
		ZoneId:      zoneId,
		Domain:      domain,
		IntervalMin: 10,
	}

	path := filepath.Join(exeDir(), "ddns_config.json")
	data, _ := json.MarshalIndent(cfg, "", "  ")
	os.WriteFile(path, data, 0644)
	fmt.Printf("配置已保存至: %s\n", path)
	fmt.Println("如需修改配置，请编辑该 JSON 文件或删除后重新运行。")
	return cfg
}

func createTaskCmd(cfg *Config) *exec.Cmd {
	exePath, err := os.Executable()
	if err != nil {
		return nil
	}
	tr := fmt.Sprintf(`"%s" --once`, exePath)
	return exec.Command("schtasks.exe", "/create",
		"/tn", taskName,
		"/tr", tr,
		"/sc", "minute",
		"/mo", strconv.Itoa(cfg.IntervalMin),
		"/st", "00:00",
		"/ru", "SYSTEM",
		"/rl", "HIGHEST",
		"/f",
	)
}

func tryInstallTask(cfg *Config) bool {
	cmd := createTaskCmd(cfg)
	if cmd == nil {
		return false
	}
	return cmd.Run() == nil
}

func installTask(cfg *Config) {
	fmt.Printf("正在创建计划任务 \"%s\" ...\n", taskName)
	cmd := createTaskCmd(cfg)
	if cmd == nil {
		fmt.Println("无法获取程序路径")
		return
	}
	out, err := cmd.CombinedOutput()
	if err != nil {
		fmt.Printf("创建失败: %s\n请以管理员身份运行。\n", string(out))
		return
	}
	fmt.Printf("计划任务已创建，每 %d 分钟自动运行，窗口关闭后在后台持续运行。\n", cfg.IntervalMin)
}

func removeTask() {
	cmd := exec.Command("schtasks.exe", "/delete", "/tn", taskName, "/f")
	out, err := cmd.CombinedOutput()
	if err != nil {
		fmt.Printf("删除失败: %s\n", string(out))
		return
	}
	fmt.Println("计划任务已删除。")
}

func getPublicIPv6() string {
	ifaces, err := net.Interfaces()
	if err != nil {
		return ""
	}

	for _, iface := range ifaces {
		if iface.Flags&net.FlagUp == 0 {
			continue
		}
		if iface.Flags&net.FlagLoopback != 0 {
			continue
		}

		addrs, err := iface.Addrs()
		if err != nil {
			continue
		}

		for _, addr := range addrs {
			ipnet, ok := addr.(*net.IPNet)
			if !ok {
				continue
			}
			ip := ipnet.IP
			if ip.To4() != nil || len(ip) != net.IPv6len {
				continue
			}
			if ip.IsLinkLocalUnicast() || ip.IsPrivate() {
				continue
			}
			if ip[0]&0xE0 == 0x20 {
				return ip.String()
			}
		}
	}
	return ""
}

func compareIPv6(a, b string) bool {
	ipa := net.ParseIP(a)
	ipb := net.ParseIP(b)
	if ipa == nil || ipb == nil {
		return a == b
	}
	return ipa.Equal(ipb)
}

func cfAPI(method, urlStr string, body io.Reader, token string) (*CfResponse, error) {
	req, err := http.NewRequest(method, urlStr, body)
	if err != nil {
		return nil, fmt.Errorf("创建请求失败: %w", err)
	}
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Authorization", "Bearer "+token)

	resp, err := httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("HTTP 请求失败: %w", err)
	}
	defer resp.Body.Close()

	respData, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, fmt.Errorf("读取响应失败: %w", err)
	}

	var cfResp CfResponse
	if err := json.Unmarshal(respData, &cfResp); err != nil {
		return nil, fmt.Errorf("解析响应失败: %s", string(respData))
	}
	return &cfResp, nil
}

func appendLog(line string) {
	logPath := filepath.Join(exeDir(), "ddns_run.log")

	if info, err := os.Stat(logPath); err == nil && info.Size() > maxLogSize {
		data, _ := os.ReadFile(logPath)
		half := len(data) / 2
		start := half
		for start < len(data) && data[start] != '\n' {
			start++
		}
		if start < len(data) {
			os.WriteFile(logPath, data[start+1:], 0644)
		}
	}

	f, err := os.OpenFile(logPath, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
	if err == nil {
		f.WriteString(line)
		f.Close()
	}
}

func logStatus(prevIp, currIp, status string) {
	line := fmt.Sprintf("[%s] %s | %s → %s\n",
		time.Now().Format("2006-01-02 15:04:05"), status, prevIp, currIp)
	appendLog(line)
	fmt.Print(line)
}

func runOnce(cfg *Config) {
	currentIp := getPublicIPv6()
	if currentIp == "" {
		logStatus("", "", "未获取到公网 IPv6")
		return
	}

	baseURL := "https://api.cloudflare.com/client/v4/zones/" + cfg.ZoneId
	encodedDomain := url.QueryEscape(cfg.Domain)
	listURL := baseURL + "/dns_records?type=AAAA&name=" + encodedDomain

	listResp, err := cfAPI("GET", listURL, nil, cfg.ApiToken)
	if err != nil {
		logStatus("", "", "查询 DNS 记录失败: "+err.Error())
		return
	}
	if !listResp.Success {
		logStatus("", "", "查询 DNS 记录失败: "+string(listResp.Errors))
		return
	}

	body := DnsUpdateBody{
		Type:    "AAAA",
		Name:    cfg.Domain,
		Content: currentIp,
		Proxied: false,
		Ttl:     1,
	}
	bodyJson, _ := json.Marshal(body)

	var records DnsRecordList
	json.Unmarshal(listResp.Result, &records)

	if len(records) == 0 {
		createURL := baseURL + "/dns_records"
		prevIp := "无(未发现记录)"
		createResp, err := cfAPI("POST", createURL, strings.NewReader(string(bodyJson)), cfg.ApiToken)
		if err != nil {
			logStatus(prevIp, currentIp, "新建失败: "+err.Error())
			return
		}
		if createResp.Success {
			logStatus(prevIp, currentIp, "新建成功")
		} else {
			logStatus(prevIp, currentIp, "新建失败: "+string(createResp.Errors))
		}
		return
	}

	record := records[0]
	if compareIPv6(currentIp, record.Content) {
		logStatus(record.Content, currentIp, "无变化(已跳过)")
		return
	}

	updateURL := baseURL + "/dns_records/" + record.Id
	updateResp, err := cfAPI("PUT", updateURL, strings.NewReader(string(bodyJson)), cfg.ApiToken)
	if err != nil {
		logStatus(record.Content, currentIp, "更新失败: "+err.Error())
		return
	}
	if updateResp.Success {
		logStatus(record.Content, currentIp, "更新成功")
	} else {
		logStatus(record.Content, currentIp, "更新失败: "+string(updateResp.Errors))
	}
}

func clearScreen() {
	cmd := exec.Command("cmd", "/c", "cls")
	cmd.Stdout = os.Stdout
	cmd.Run()
}

func pressToContinue() {
	fmt.Println("\n按 Enter 键继续...")
	bufio.NewReader(os.Stdin).ReadBytes('\n')
	clearScreen()
}

func isAdmin() bool {
	return exec.Command("net", "session").Run() == nil
}

func taskExists() bool {
	err := exec.Command("schtasks.exe", "/query", "/tn", taskName).Run()
	return err == nil
}

func readLine(reader *bufio.Reader) string {
	s, _ := reader.ReadString('\n')
	return strings.TrimSpace(s)
}

func confirmConfig(cfg *Config) bool {
	return cfg.ApiToken != "" && cfg.ZoneId != "" && cfg.Domain != ""
}

func showConfig(cfg *Config) {
	token := cfg.ApiToken
	if len(token) > 8 {
		token = token[:4] + "****" + token[len(token)-4:]
	} else if token != "" {
		token = "****"
	}
	fmt.Println("\n===== 当前配置 =====")
	fmt.Printf("  ApiToken: %s\n", token)
	fmt.Printf("  ZoneId:   %s\n", cfg.ZoneId)
	fmt.Printf("  Domain:   %s\n", cfg.Domain)
	fmt.Printf("  间隔:     %d 分钟\n", cfg.IntervalMin)
}

func updateConfig(cfg *Config) {
	reader := bufio.NewReader(os.Stdin)
	fmt.Printf("ApiToken [%s]: ", cfg.ApiToken)
	if s := readLine(reader); s != "" {
		cfg.ApiToken = s
	}
	fmt.Printf("ZoneId [%s]: ", cfg.ZoneId)
	if s := readLine(reader); s != "" {
		cfg.ZoneId = s
	}
	fmt.Printf("Domain [%s]: ", cfg.Domain)
	if s := readLine(reader); s != "" {
		cfg.Domain = s
	}
	fmt.Printf("检查间隔(分钟) [%d]: ", cfg.IntervalMin)
	if s := readLine(reader); s != "" {
		if n, err := strconv.Atoi(s); err == nil && n > 0 {
			cfg.IntervalMin = n
		}
	}
	path := filepath.Join(exeDir(), "ddns_config.json")
	data, _ := json.MarshalIndent(cfg, "", "  ")
	os.WriteFile(path, data, 0644)
	fmt.Println("配置已保存。")
}

func adminMenu(cfg *Config) {
	clearScreen()
	for {
		hasTask := taskExists()

		token := cfg.ApiToken
		if len(token) > 8 {
			token = token[:4] + "****" + token[len(token)-4:]
		} else if token != "" {
			token = "****"
		}

		fmt.Println("\n========== DDNS 管理菜单 ==========")
		fmt.Printf("  域名:   %s\n", cfg.Domain)
		fmt.Printf("  ZoneID: %s\n", cfg.ZoneId)
		fmt.Printf("  Token:  %s\n", token)
		fmt.Printf("  间隔:   %d 分钟\n", cfg.IntervalMin)
		if hasTask {
			fmt.Println("  计划任务: 已创建")
		} else {
			fmt.Println("  计划任务: 未创建")
		}
		fmt.Println("-----------------------------------")
		fmt.Println("  1. 更新配置")
		fmt.Println("  2. 查看配置")
		fmt.Println("  3. 执行一次检查")
		if hasTask {
			fmt.Println("  4. 删除计划任务")
			fmt.Println("  5. 重新创建计划任务")
			fmt.Println("  6. 退出")
			fmt.Print("请选择 (1-6): ")
		} else {
			fmt.Println("  4. 创建计划任务")
			fmt.Println("  5. 退出")
			fmt.Print("请选择 (1-5): ")
		}

		choice := readLine(bufio.NewReader(os.Stdin))
		switch choice {
		case "1":
			updateConfig(cfg)
			pressToContinue()
		case "2":
			showConfig(cfg)
			pressToContinue()
		case "3":
			runOnce(cfg)
			pressToContinue()
		case "4":
			if hasTask {
				fmt.Print("确认删除计划任务? (y/n): ")
				if readLine(bufio.NewReader(os.Stdin)) == "y" {
					removeTask()
				} else {
					fmt.Println("已取消。")
				}
			} else {
				installTask(cfg)
			}
			pressToContinue()
		case "5":
			if hasTask {
				installTask(cfg)
				pressToContinue()
				break
			}
			fmt.Print("计划任务未创建，确定退出? (y/n): ")
			if readLine(bufio.NewReader(os.Stdin)) == "y" {
				return
			}
		case "6":
			if hasTask {
				fmt.Println("退出。")
				return
			} else {
				fmt.Println("无效选择。")
			}
		default:
			fmt.Println("无效选择。")
			pressToContinue()
		}
	}
}

func main() {
	once := false
	for _, arg := range os.Args[1:] {
		if arg == "--once" {
			once = true
		}
	}

	if once {
		cfg := loadOrCreateConfig()
		if !confirmConfig(cfg) {
			fmt.Println("配置不完整，检查 ddns_config.json")
			return
		}
		if cfg.IntervalMin <= 0 {
			cfg.IntervalMin = 10
		}
		runOnce(cfg)
		return
	}

	// 默认模式：管理工具 — 必须以管理员身份运行
	if !isAdmin() {
		fmt.Println("请以管理员身份运行本程序。")
		fmt.Println("按任意键退出...")
		bufio.NewReader(os.Stdin).ReadBytes('\n')
		return
	}

	cfg := loadOrCreateConfig()

	if !confirmConfig(cfg) {
		fmt.Println("配置不完整，请录入：")
		updateConfig(cfg)
		pressToContinue()
	}

	adminMenu(cfg)
}
