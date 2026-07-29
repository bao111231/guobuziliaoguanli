using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace GuoBuZiLiaoGuanLi;

public partial class QrCodeWindow : Window
{
    private readonly string _url;
    private readonly List<string> _diagnosticMessages;

    public QrCodeWindow(string url, List<string> diagnosticMessages = null)
    {
        InitializeComponent();
        _url = url;
        _diagnosticMessages = diagnosticMessages ?? new List<string>();
        UrlText.Text = url;
        GenerateQrCode(url);

        DiagnosticList.ItemsSource = _diagnosticMessages;

        StatusText.Text = "服务器已启动，等待手机连接...";
    }

    private void GenerateQrCode(string url)
    {
        try
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            byte[] qrCodeBytes = qrCode.GetGraphic(20);

            var bitmap = new BitmapImage();
            using (var stream = new System.IO.MemoryStream(qrCodeBytes))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }

            QrCodeImage.Source = bitmap;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"生成二维码失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyUrlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_url);
            StatusText.Text = "链接已复制到剪贴板！";
        }
        catch
        {
            MessageBox.Show("复制失败，请手动选择链接复制", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TestBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true });
            StatusText.Text = "已在浏览器中打开测试页面...";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开浏览器失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UrlText_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        CopyUrlButton_Click(sender, null);
    }
}
