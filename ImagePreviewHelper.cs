using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GuoBuZiLiaoGuanLi;

public static class ImagePreviewHelper
{
    private static DateTime _lastCloseTime = DateTime.MinValue;
    private static readonly TimeSpan ReopenThrottle = TimeSpan.FromMilliseconds(500);

    public static bool CanOpen()
    {
        return (DateTime.Now - _lastCloseTime) > ReopenThrottle;
    }

    public static BitmapImage LoadImage(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public static void Show(Window owner, string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            return;

        string ext = Path.GetExtension(imagePath)?.ToLower() ?? "";
        bool isPdf = ext == ".pdf";

        if (isPdf)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = imagePath,
                    UseShellExecute = true
                });
            }
            catch
            {
                MessageBox.Show("无法打开PDF文件，请确保已安装PDF阅读器。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return;
        }

        bool isImage = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff" }.Contains(ext);
        if (!isImage)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = imagePath,
                    UseShellExecute = true
                });
            }
            catch { }
            return;
        }

        var zoomWindow = new Window
        {
            Title = Path.GetFileName(imagePath),
            WindowState = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            Background = Brushes.Black,
            Cursor = Cursors.SizeAll
        };

        var zoomImage = new Image
        {
            Source = LoadImage(imagePath),
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        var group = new TransformGroup();
        var scaleTransform = new ScaleTransform(1.0, 1.0);
        var translateTransform = new TranslateTransform(0, 0);
        group.Children.Add(scaleTransform);
        group.Children.Add(translateTransform);
        zoomImage.RenderTransform = group;

        double minScale = 0.1;
        double maxScale = 20.0;

        var closeHint = new TextBlock
        {
            Text = "滚轮缩放 | 按住拖动平移 | 双击或 R 重置 | ESC 关闭",
            Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 20)
        };

        var grid = new Grid();
        grid.Children.Add(zoomImage);
        grid.Children.Add(closeHint);
        zoomWindow.Content = grid;

        bool isDragging = false;
        Point dragStart = new Point(0, 0);
        double startOffsetX = 0;
        double startOffsetY = 0;

        grid.MouseLeftButtonDown += (s, args) =>
        {
            if (args.ClickCount == 2)
            {
                _lastCloseTime = DateTime.Now;
                zoomWindow.Close();
                return;
            }
            isDragging = true;
            dragStart = args.GetPosition(grid);
            startOffsetX = translateTransform.X;
            startOffsetY = translateTransform.Y;
            grid.CaptureMouse();
            args.Handled = true;
        };

        grid.MouseMove += (s, args) =>
        {
            if (isDragging)
            {
                Point current = args.GetPosition(grid);
                translateTransform.X = startOffsetX + (current.X - dragStart.X);
                translateTransform.Y = startOffsetY + (current.Y - dragStart.Y);
                ClampTranslate();
            }
        };

        grid.MouseLeftButtonUp += (s, args) =>
        {
            if (isDragging)
            {
                isDragging = false;
                grid.ReleaseMouseCapture();
            }
        };

        zoomWindow.MouseWheel += (s, args) =>
        {
            // 鼠标在 grid（屏幕）坐标系中的位置
            Point mouseInGrid = args.GetPosition(grid);
            double oldScale = scaleTransform.ScaleX;
            double scaleFactor = args.Delta > 0 ? 1.15 : 1.0 / 1.15;
            double newScale = oldScale * scaleFactor;

            // 触达上下限时夹紧到边界，而不是直接跳过，避免手感突兀
            if (newScale < minScale) { newScale = minScale; scaleFactor = newScale / oldScale; }
            if (newScale > maxScale) { newScale = maxScale; scaleFactor = newScale / oldScale; }
            if (Math.Abs(newScale - oldScale) < 1e-9) { args.Handled = true; return; }

            // 图片中心在屏幕上的当前位置。ScaleTransform 以 RenderTransformOrigin(0.5,0.5)
            // 即图片中心为锚点，所以缩放本身不会移动中心，中心位置只受 TranslateTransform 影响。
            // 因此在改 scale 前后调用 TranslatePoint 都能得到相同的"图片中心屏幕坐标"。
            Point imageCenterOnScreen = zoomImage.TranslatePoint(
                new Point(zoomImage.ActualWidth / 2, zoomImage.ActualHeight / 2), grid);

            // 鼠标相对图片中心（屏幕坐标系）的偏移：M - Lc - t
            Vector delta = mouseInGrid - imageCenterOnScreen;

            // 应用新缩放
            scaleTransform.ScaleX = newScale;
            scaleTransform.ScaleY = newScale;

            // 调整平移，使鼠标下的图片点保持在原位：t' = t + (1 - s'/s)·(M - Lc - t)
            translateTransform.X += (1 - scaleFactor) * delta.X;
            translateTransform.Y += (1 - scaleFactor) * delta.Y;
            ClampTranslate();

            args.Handled = true;
        };

        // 边界约束：保证图片至少有 margin 像素留在窗口内，永远不会完全消失到屏幕外。
        // 图片居中对齐 + 绕中心缩放，所以图片中心基础位置 = grid 中心；变换后中心 = grid 中心 + translate。
        // 至少 margin 像素可见 => 图片左/右边缘不能跨过 grid 的另一边。
        void ClampTranslate()
        {
            double sw = zoomImage.ActualWidth * scaleTransform.ScaleX;
            double sh = zoomImage.ActualHeight * scaleTransform.ScaleY;
            if (sw <= 0 || sh <= 0) return;
            if (grid.ActualWidth <= 0 || grid.ActualHeight <= 0) return;

            double baseCx = grid.ActualWidth / 2;
            double baseCy = grid.ActualHeight / 2;
            double cx = baseCx + translateTransform.X;
            double cy = baseCy + translateTransform.Y;

            double margin = 80;

            // 水平：要求图片右边缘 >= margin 且 左边缘 <= grid.Width - margin
            double minX = margin - sw / 2;
            double maxX = grid.ActualWidth - margin + sw / 2;
            if (minX <= maxX)
                cx = Math.Max(minX, Math.Min(maxX, cx));
            else
                cx = baseCx; // 图片太小，强制居中

            // 垂直
            double minY = margin - sh / 2;
            double maxY = grid.ActualHeight - margin + sh / 2;
            if (minY <= maxY)
                cy = Math.Max(minY, Math.Min(maxY, cy));
            else
                cy = baseCy;

            translateTransform.X = cx - baseCx;
            translateTransform.Y = cy - baseCy;
        }

        zoomWindow.KeyDown += (s, args) =>
        {
            if (args.Key == Key.Escape)
            {
                _lastCloseTime = DateTime.Now;
                zoomWindow.Close();
            }
            else if (args.Key == Key.R)
            {
                scaleTransform.ScaleX = 1.0;
                scaleTransform.ScaleY = 1.0;
                translateTransform.X = 0;
                translateTransform.Y = 0;
            }
        };

        zoomWindow.Closed += (s, args) =>
        {
            _lastCloseTime = DateTime.Now;
        };

        zoomWindow.ShowDialog();
    }
}
