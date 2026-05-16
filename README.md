# LanFileTransfer - 局域网文件传输工具 / LAN File Transfer Tool

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/WPF-Windows-blue)](https://github.com/dotnet/wpf)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

[English](#english) | [中文](#中文)

---

<a id="english"></a>
## English

A lightweight Windows desktop application that transfers files between your PC and mobile phone over the local network. No app installation required on the phone — just open a browser.

### Features

- 📤 **Upload files from phone** — Drag & drop or tap to upload files from your phone to PC
- 📥 **Download files from PC** — Share files on your PC for phone download
- 📱 **Mobile-friendly web UI** — Responsive web page with progress bar and toast notifications
- 🔍 **QR code connection** — Scan a QR code to instantly open the transfer page
- 🔥 **Auto firewall configuration** — One-time UAC prompt to open the port permanently
- 💾 **Persistent settings** — Port and download directory are saved and restored on restart
- 📋 **Transfer log** — Real-time log of all sent and received files

### Screenshot

```
┌──────────────────────────────────────────────────────────┐
│  📁 LAN File Transfer   [📂 Open Dir] [📁 Change Dir]    │
│                                                          │
│  ┌─ Shared Files ─────────────┐  ┌─ Server Info ──────┐ │
│  │  🖼 photo.jpg       ✕     │  │  Address: 192....  │ │
│  │  📄 document.pdf    ✕     │  │  Port:    8888     │ │
│  │                            │  │  IP:      192....  │ │
│  └────────────────────────────┘  ├─ Phone Connect ────┤ │
│                                   │    ┌───────┐      │ │
│  ┌─ Transfer Log ──────────────┐ │    │ QR    │      │ │
│  │  📥 received_file.mp3       │ │    │ Code  │      │ │
│  │  📤 shared_doc.pdf          │ │    └───────┘      │ │
│  └─────────────────────────────┘ └────────────────────┘ │
└──────────────────────────────────────────────────────────┘
```

### How to Use

#### PC Side
1. Download and run `LanFileTransfer.exe` (requires .NET 8 Desktop Runtime)
2. Click **▶ Start Service** — first run will prompt for admin permission (firewall + URL ACL)
3. The app shows a URL like `http://192.168.1.100:8888`

#### Phone Side
1. Connect your phone to the **same WiFi** as your PC
2. Open the phone browser and visit the URL shown on PC, **or** scan the QR code
3. **Upload:** Tap the upload area or drag files → files are saved to the download directory on PC
4. **Download:** On PC, add files to the shared list → they appear on the phone for download

### Build from Source

```powershell
git clone https://github.com/viego-qiin/LanFileTransfer.git
cd LanFileTransfer
dotnet restore
dotnet build
dotnet run --project LanFileTransfer\LanFileTransfer.csproj
```

### Architecture

```
LanFileTransfer/
├── Models/          # Data models (FileItem, TransferLog, AppConfig)
├── Services/        # Core services
│   ├── HttpFileServer.cs      # Embedded HTTP server (HttpListener)
│   ├── DiscoveryService.cs    # UDP broadcast LAN discovery
│   └── FirewallHelper.cs      # Auto-configure Windows Firewall
├── ViewModels/      # MVVM ViewModels
└── Converters/      # XAML value converters
```

### Requirements

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- Both devices on the same local network

---

<a id="中文"></a>
## 中文

一款轻量级的 Windows 桌面应用，通过局域网在电脑和手机之间互传文件。手机无需安装任何 App，打开浏览器即可使用。

### 功能特性

- 📤 **手机上传文件** — 拖拽或点击上传手机文件到电脑
- 📥 **手机下载文件** — 在电脑上添加共享文件，手机端即可下载
- 📱 **移动端网页界面** — 响应式页面，带进度条和提示通知
- 🔍 **二维码连接** — 扫描二维码直接打开传输页面
- 🔥 **自动配置防火墙** — 首次弹窗授权后，端口永久放行
- 💾 **配置持久化** — 端口、下载目录修改后自动保存，重启恢复
- 📋 **传输记录** — 实时记录所有收发文件

### 使用教程

#### 电脑端
1. 下载并运行 `LanFileTransfer.exe`（需要安装 .NET 8 桌面运行时）
2. 点击 **▶ 启动服务** — 首次需授权管理员权限（防火墙 + URL 保留）
3. 界面显示访问地址，如 `http://192.168.1.100:8888`

#### 手机端
1. 确保手机和电脑连接**同一 WiFi**
2. 手机浏览器访问电脑上显示的地址，**或**扫描二维码
3. **上传**：点击或拖拽文件到上传区域 → 文件保存到电脑下载目录
4. **下载**：电脑端添加共享文件 → 手机端列表出现，点击下载

#### 修改保存目录
点击顶栏 **📁 修改目录** 按钮，选择新的文件夹。后续上传的文件将保存到新目录。

#### 修改端口
在右侧面板的「端口」输入框中直接修改。防火墙规则会自动为新端口重新申请。

### 从源码构建

```powershell
git clone https://github.com/viego-qiin/LanFileTransfer.git
cd LanFileTransfer
dotnet restore
dotnet build
dotnet run --project LanFileTransfer\LanFileTransfer.csproj
```

### 项目结构

```
LanFileTransfer/
├── Models/          # 数据模型 (FileItem, TransferLog, AppConfig)
├── Services/        # 核心服务
│   ├── HttpFileServer.cs      # 嵌入式 HTTP 服务器 (基于 HttpListener)
│   ├── DiscoveryService.cs    # UDP 广播局域网发现
│   └── FirewallHelper.cs      # 自动配置 Windows 防火墙
├── ViewModels/      # MVVM 视图模型
└── Converters/      # XAML 值转换器
```

### 技术栈

| 技术 | 说明 |
|---|---|
| .NET 8 | 运行时框架 |
| WPF | Windows 桌面 UI |
| HttpListener | 嵌入式 HTTP 服务 |
| QRCoder | 二维码生成 |
| MVVM | 架构模式 |

### 环境要求

- Windows 10 / 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)
- 手机和电脑在同一局域网

### 常见问题

**Q: 手机打不开网页，一直加载中？**  
A: 检查 Windows 防火墙是否放行了配置的端口。启动服务时会自动申请，如果被拒绝，手动执行：
```
netsh advfirewall firewall add rule name="LanFileTransfer" dir=in action=allow protocol=TCP localport=8888
```

**Q: 每次启动都要弹 UAC 授权？**  
A: 正常只需首次授权。如果每次都弹，说明 URL ACL 或防火墙规则未成功添加。以管理员身份运行一次即可。

**Q: 改端口后手机连不上？**  
A: 防火墙规则是为旧端口创建的，新端口需要重新申请。程序会在启动时自动检测并重新配置。
