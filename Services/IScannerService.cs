using System.Threading.Tasks;

namespace GuoBuZiLiaoGuanLi.Services;

public interface IScannerService
{
    Task<bool> IsScannerAvailableAsync();
    Task<string> ScanToFileAsync(string outputPath, string fileName);
    Task<string[]> ScanMultipleToFileAsync(string outputPath, string baseFileName);
}
