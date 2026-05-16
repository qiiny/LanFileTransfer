using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;
using LanFileTransfer.Services;
using LanFileTransfer.ViewModels;
using QRCoder;

namespace LanFileTransfer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = (MainViewModel)DataContext;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.QRCodeContent))
        {
            GenerateQRCode(_viewModel.QRCodeContent);
        }
    }

    private void GenerateQRCode(string content)
    {
        if (string.IsNullOrEmpty(content)) return;

        try
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            var qrCodeBytes = qrCode.GetGraphic(10);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new System.IO.MemoryStream(qrCodeBytes);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            QRCodeImage.Source = bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"QR码生成失败: {ex.Message}");
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.Shutdown();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosing(e);
    }
}