using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using LanFileTransfer.Models;
using LanFileTransfer.Services;
using Microsoft.Win32;

namespace LanFileTransfer.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly HttpFileServer _fileServer;
    private readonly DiscoveryService _discoveryService;

    private string _downloadPath;
    private int _port = 8888;
    private bool _isServerRunning;
    private string _serverStatus = "未启动";
    private string _localIP = "获取中...";
    private string _qrCodeContent = "";
    private string _logText = "";
    private string _statusColor = "#E74C3C";

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        var config = AppConfig.Load();
        _downloadPath = config.DownloadPath;
        _port = config.Port;

        _fileServer = new HttpFileServer(_downloadPath, _port);
        _discoveryService = new DiscoveryService();

        _fileServer.OnLog += AppendLog;
        _fileServer.OnTransferComplete += OnTransferComplete;
        _fileServer.OnServerStarted += () =>
        {
            IsServerRunning = true;
            ServerStatus = "运行中";
            StatusColor = "#27AE60";
            LocalIP = HttpFileServer.GetLocalIPAddress();
            UpdateQRCode();
        };
        _fileServer.OnServerStopped += () =>
        {
            IsServerRunning = false;
            ServerStatus = "已停止";
            StatusColor = "#E74C3C";
        };

        _discoveryService.OnLog += AppendLog;

        StartServerCommand = new RelayCommand(async _ => await StartServerAsync(), _ => !IsServerRunning);
        StopServerCommand = new RelayCommand(_ => StopServer(), _ => IsServerRunning);
        AddFilesCommand = new RelayCommand(_ => AddFiles());
        RemoveFileCommand = new RelayCommand(file => RemoveFile(file as FileItem));
        ClearFilesCommand = new RelayCommand(_ => ClearFiles());
        OpenDownloadFolderCommand = new RelayCommand(_ => OpenDownloadFolder());
        ChangeDownloadPathCommand = new RelayCommand(_ => ChangeDownloadPath());
        ClearLogCommand = new RelayCommand(_ => ClearLog());
    }

    public ObservableCollection<FileItem> SharedFiles { get; } = new();
    public ObservableCollection<TransferLog> TransferLogs { get; } = new();

    public string DownloadPath
    {
        get => _downloadPath;
        set
        {
            _downloadPath = value;
            _fileServer.SetDownloadPath(_downloadPath);
            OnPropertyChanged();
            SaveConfig();
        }
    }

    public int Port
    {
        get => _port;
        set
        {
            _port = value;
            _fileServer.SetPort(_port);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ServerUrl));
            SaveConfig();
        }
    }

    public bool IsServerRunning
    {
        get => _isServerRunning;
        set { _isServerRunning = value; OnPropertyChanged(); }
    }

    public string ServerStatus
    {
        get => _serverStatus;
        set { _serverStatus = value; OnPropertyChanged(); }
    }

    public string LocalIP
    {
        get => _localIP;
        set { _localIP = value; OnPropertyChanged(); OnPropertyChanged(nameof(ServerUrl)); }
    }

    public string ServerUrl => $"http://{LocalIP}:{Port}";

    public string QRCodeContent
    {
        get => _qrCodeContent;
        set { _qrCodeContent = value; OnPropertyChanged(); }
    }

    public string LogText
    {
        get => _logText;
        set { _logText = value; OnPropertyChanged(); }
    }

    public string StatusColor
    {
        get => _statusColor;
        set { _statusColor = value; OnPropertyChanged(); }
    }

    public ICommand StartServerCommand { get; }
    public ICommand StopServerCommand { get; }
    public ICommand AddFilesCommand { get; }
    public ICommand RemoveFileCommand { get; }
    public ICommand ClearFilesCommand { get; }
    public ICommand OpenDownloadFolderCommand { get; }
    public ICommand ChangeDownloadPathCommand { get; }
    public ICommand ClearLogCommand { get; }

    private async Task StartServerAsync()
    {
        try
        {
            AppendLog("正在启动服务器...");
            await _fileServer.StartAsync();
            _ = _discoveryService.StartBroadcastingAsync(_port);
            _ = _discoveryService.StartListeningAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            AppendLog($"错误: {ex.Message}");
            MessageBox.Show(ex.Message, "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AppendLog($"启动失败: {ex.Message}");
            MessageBox.Show($"启动服务器失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopServer()
    {
        _fileServer.Stop();
        _discoveryService.Stop();
    }

    public void Shutdown()
    {
        StopServer();
        SaveConfig();
    }

    private void SaveConfig()
    {
        new AppConfig { Port = _port, DownloadPath = _downloadPath }.Save();
    }

    private void AddFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要共享的文件",
            Multiselect = true,
            Filter = "所有文件|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var filePath in dialog.FileNames)
            {
                _fileServer.AddSharedFile(filePath);
            }
            RefreshSharedFiles();
        }
    }

    private void RemoveFile(FileItem? file)
    {
        if (file == null) return;
        _fileServer.RemoveSharedFile(file.Id);
        RefreshSharedFiles();
    }

    private void ClearFiles()
    {
        _fileServer.ClearSharedFiles();
        RefreshSharedFiles();
    }

    private void OpenDownloadFolder()
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", DownloadPath);
        }
        catch (Exception ex)
        {
            AppendLog($"无法打开文件夹: {ex.Message}");
        }
    }

    private void ChangeDownloadPath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择文件下载保存目录",
            InitialDirectory = DownloadPath
        };

        if (dialog.ShowDialog() == true)
        {
            DownloadPath = dialog.FolderName;
            AppendLog($"下载目录已更改为: {DownloadPath}");
        }
    }

    private void ClearLog()
    {
        LogText = "";
    }

    private void OnTransferComplete(TransferLog log)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            TransferLogs.Insert(0, log);
            if (TransferLogs.Count > 100)
                TransferLogs.RemoveAt(TransferLogs.Count - 1);
        });
    }

    private void RefreshSharedFiles()
    {
        SharedFiles.Clear();
        foreach (var file in _fileServer.SharedFiles)
        {
            SharedFiles.Add(file);
        }
    }

    private void UpdateQRCode()
    {
        QRCodeContent = ServerUrl;
    }

    private void AppendLog(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            LogText += message + Environment.NewLine;
        });
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}