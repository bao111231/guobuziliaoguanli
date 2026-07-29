using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClosedXML.Excel;
using GuoBuZiLiaoGuanLi.Models;
using Microsoft.Win32;

namespace GuoBuZiLiaoGuanLi;

public class DateStatisticsResult
{
    public string MMDD { get; set; }
    public int Total { get; set; }
    public int Uploaded { get; set; }
    public int NotUploaded { get; set; }
    public int Missing { get; set; }
    public List<CustomerFolder> Folders { get; set; } = new();
}

public partial class StatisticsWindow : Window
{
    private readonly string _rootDirectory;
    private List<CustomerFolder> _allFolders = new();
    private List<DateStatisticsResult> _results = new();

    public StatisticsWindow(string rootDirectory)
    {
        InitializeComponent();
        _rootDirectory = rootDirectory;
        SingleDatePicker.SelectedDate = DateTime.Today;
        StartDatePicker.SelectedDate = DateTime.Today.AddDays(-7);
        EndDatePicker.SelectedDate = DateTime.Today;
        LoadAllFolders();
    }

    private void LoadAllFolders()
    {
        _allFolders.Clear();
        try
        {
            foreach (var dir in Directory.GetDirectories(_rootDirectory))
            {
                try
                {
                    _allFolders.Add(new CustomerFolder(dir));
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载文件夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void QueryMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool single = SingleDayRadio.IsChecked == true;
        SingleDatePanel.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
        RangeDatePanel.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
    }

    private void QuerySingleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!SingleDatePicker.SelectedDate.HasValue)
        {
            MessageBox.Show("请选择日期", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DateTime date = SingleDatePicker.SelectedDate.Value;
        string mmdd = date.ToString("MMdd");
        var folders = _allFolders.Where(f => f.InvoiceMMDD == mmdd).ToList();
        _results = new List<DateStatisticsResult> { BuildResult(mmdd, folders) };
        RenderResults($"{date:yyyy年MM月dd日} 统计结果");
    }

    private void QueryRangeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!StartDatePicker.SelectedDate.HasValue || !EndDatePicker.SelectedDate.HasValue)
        {
            MessageBox.Show("请选择开始和结束日期", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DateTime start = StartDatePicker.SelectedDate.Value;
        DateTime end = EndDatePicker.SelectedDate.Value;

        if (start > end)
        {
            MessageBox.Show("开始日期不能晚于结束日期", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _results = new List<DateStatisticsResult>();
        var grouped = _allFolders
            .Where(f => !string.IsNullOrEmpty(f.InvoiceMMDD))
            .GroupBy(f => f.InvoiceMMDD)
            .ToDictionary(g => g.Key, g => g.ToList());

        for (DateTime dt = start; dt <= end; dt = dt.AddDays(1))
        {
            string mmdd = dt.ToString("MMdd");
            var folders = grouped.ContainsKey(mmdd) ? grouped[mmdd] : new List<CustomerFolder>();
            _results.Add(BuildResult(mmdd, folders));
        }

        var foldersWithoutDate = _allFolders.Where(f => string.IsNullOrEmpty(f.InvoiceMMDD)).ToList();
        if (foldersWithoutDate.Count > 0)
        {
            _results.Add(new DateStatisticsResult
            {
                MMDD = "无日期",
                Folders = foldersWithoutDate,
                Total = foldersWithoutDate.Count,
                Uploaded = foldersWithoutDate.Count(f => f.IsUploaded),
                NotUploaded = foldersWithoutDate.Count(f => f.Status == FolderStatus.NotUploaded),
                Missing = foldersWithoutDate.Count(f => f.Status == FolderStatus.Missing || f.Status == FolderStatus.Pending)
            });
        }

        RenderResults($"{start:yyyy-MM-dd} 至 {end:yyyy-MM-dd} 统计结果");
    }

    private DateStatisticsResult BuildResult(string mmdd, List<CustomerFolder> folders)
    {
        return new DateStatisticsResult
        {
            MMDD = mmdd,
            Folders = folders,
            Total = folders.Count,
            Uploaded = folders.Count(f => f.IsUploaded || f.IsUploadedUnconfirmed),
            NotUploaded = folders.Count(f => f.Status == FolderStatus.NotUploaded),
            Missing = folders.Count(f => f.Status == FolderStatus.Missing || f.Status == FolderStatus.Pending)
        };
    }

    private void RenderResults(string title)
    {
        ResultPanel.Children.Clear();

        int totalAll = _results.Sum(r => r.Total);
        int uploadedAll = _results.Sum(r => r.Uploaded);
        int notUploadedAll = _results.Sum(r => r.NotUploaded);
        int missingAll = _results.Sum(r => r.Missing);

        TotalCountText.Text = totalAll.ToString();
        UploadedCountText.Text = uploadedAll.ToString();
        NotUploadedCountText.Text = notUploadedAll.ToString();
        MissingCountText.Text = missingAll.ToString();

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72)),
            Margin = new Thickness(0, 0, 0, 12)
        };
        ResultPanel.Children.Add(titleBlock);

        if (totalAll == 0)
        {
            ResultPanel.Children.Add(new TextBlock
            {
                Text = "未找到匹配的文件夹",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 174, 192)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(40)
            });
            ExportButton.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var result in _results.Where(r => r.Total > 0).OrderBy(r => r.MMDD))
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(247, 250, 252)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dateText = new TextBlock
            {
                Text = result.MMDD == "无日期" ? "📅 无日期" : $"📅 {result.MMDD.Substring(0, 2)}月{result.MMDD.Substring(2, 2)}日",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dateText, 0);

            var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(CreateStatBadge($"总计 {result.Total}", "#3B82F6"));
            stack.Children.Add(CreateStatBadge($"已上传 {result.Uploaded}", "#059669"));
            stack.Children.Add(CreateStatBadge($"未上传 {result.NotUploaded}", "#2563EB"));
            if (result.Missing > 0)
                stack.Children.Add(CreateStatBadge($"缺资料 {result.Missing}", "#DC2626"));
            Grid.SetColumn(stack, 1);

            var expandBtn = new Button
            {
                Content = "查看详情",
                Tag = result,
                Background = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            expandBtn.Click += (s, e) => ShowFolderDetails(result);
            Grid.SetColumn(expandBtn, 2);

            grid.Children.Add(dateText);
            grid.Children.Add(stack);
            grid.Children.Add(expandBtn);
            border.Child = grid;

            ResultPanel.Children.Add(border);

            if (result.Folders.Count <= 20)
            {
                var detailPanel = new StackPanel { Margin = new Thickness(16, 0, 0, 8) };
                foreach (var f in result.Folders.Take(10))
                {
                    var nameText = new TextBlock
                    {
                        Text = "• " + f.DisplayName,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                        Margin = new Thickness(0, 1, 0, 1)
                    };
                    detailPanel.Children.Add(nameText);
                }
                if (result.Folders.Count > 10)
                {
                    detailPanel.Children.Add(new TextBlock
                    {
                        Text = $"  ... 还有 {result.Folders.Count - 10} 个",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(160, 174, 192)),
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }
                ResultPanel.Children.Add(detailPanel);
            }
        }

        ExportButton.Visibility = Visibility.Visible;
    }

    private Border CreateStatBadge(string text, string colorHex)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorHex);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = new SolidColorBrush(color),
                FontWeight = FontWeights.SemiBold
            }
        };
    }

    private void ShowFolderDetails(DateStatisticsResult result)
    {
        var names = string.Join("\n", result.Folders.Select(f => f.DisplayName).Take(50));
        MessageBox.Show(names + (result.Folders.Count > 50 ? $"\n... 共 {result.Folders.Count} 个" : ""),
            $"{result.MMDD} 文件夹列表", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Excel 文件|*.xlsx",
            Title = "导出统计结果",
            FileName = $"日期统计_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("日期统计");

            ws.Cell(1, 1).Value = "日期(MMDD)";
            ws.Cell(1, 2).Value = "总份数";
            ws.Cell(1, 3).Value = "已上传";
            ws.Cell(1, 4).Value = "未上传";
            ws.Cell(1, 5).Value = "缺资料/暂滞";

            int row = 2;
            foreach (var r in _results.Where(x => x.Total > 0).OrderBy(x => x.MMDD))
            {
                ws.Cell(row, 1).Value = r.MMDD;
                ws.Cell(row, 2).Value = r.Total;
                ws.Cell(row, 3).Value = r.Uploaded;
                ws.Cell(row, 4).Value = r.NotUploaded;
                ws.Cell(row, 5).Value = r.Missing;
                row++;
            }

            var detailWs = wb.Worksheets.Add("详细列表");
            detailWs.Cell(1, 1).Value = "日期";
            detailWs.Cell(1, 2).Value = "文件夹名称";
            detailWs.Cell(1, 3).Value = "状态";
            detailWs.Cell(1, 4).Value = "姓名";
            detailWs.Cell(1, 5).Value = "商品";

            int drow = 2;
            foreach (var r in _results.Where(x => x.Total > 0).OrderBy(x => x.MMDD))
            {
                foreach (var f in r.Folders)
                {
                    detailWs.Cell(drow, 1).Value = r.MMDD;
                    detailWs.Cell(drow, 2).Value = f.DisplayName;
                    detailWs.Cell(drow, 3).Value = f.Status.GetDisplayName();
                    detailWs.Cell(drow, 4).Value = f.CustomerName ?? "";
                    detailWs.Cell(drow, 5).Value = f.ProductName ?? "";
                    drow++;
                }
            }

            ws.Columns().AdjustToContents();
            detailWs.Columns().AdjustToContents();
            wb.SaveAs(dialog.FileName);

            MessageBox.Show($"导出成功！\n文件: {dialog.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);

            var result = MessageBox.Show("是否打开文件？", "提示", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dialog.FileName,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
