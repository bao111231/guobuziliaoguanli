using System.Windows.Media;

namespace GuoBuZiLiaoGuanLi.Models;

public enum FolderStatus
{
    Missing,
    Pending,
    NotUploaded,
    UploadedUnconfirmed,
    Uploaded
}

public static class FolderStatusExtensions
{
    public static string GetDisplayName(this FolderStatus status)
    {
        return status switch
        {
            FolderStatus.Missing => "缺资料",
            FolderStatus.Pending => "暂滞",
            FolderStatus.NotUploaded => "未上传",
            FolderStatus.UploadedUnconfirmed => "已上传未确认",
            FolderStatus.Uploaded => "已上传",
            _ => status.ToString()
        };
    }

    public static Color GetStatusColor(this FolderStatus status)
    {
        return status switch
        {
            FolderStatus.Missing => Color.FromRgb(220, 53, 69),
            FolderStatus.Pending => Color.FromRgb(111, 66, 193),
            FolderStatus.NotUploaded => Color.FromRgb(0, 123, 255),
            FolderStatus.UploadedUnconfirmed => Color.FromRgb(255, 152, 0),
            FolderStatus.Uploaded => Color.FromRgb(40, 167, 69),
            _ => Colors.Gray
        };
    }

    public static SolidColorBrush GetStatusBrush(this FolderStatus status)
    {
        return new SolidColorBrush(status.GetStatusColor());
    }
}
