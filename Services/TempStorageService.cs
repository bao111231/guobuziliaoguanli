using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GuoBuZiLiaoGuanLi.Services;

public class TempStorageFile
{
    public string Id { get; set; }
    public string FilePath { get; set; }
    public string FileName { get; set; }
    public string Extension { get; set; }
    public DateTime AddedTime { get; set; }
    public long FileSize { get; set; }

    public static TempStorageFile FromPath(string filePath)
    {
        var info = new FileInfo(filePath);
        return new TempStorageFile
        {
            Id = filePath.GetHashCode().ToString("x"),
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Extension = Path.GetExtension(filePath),
            AddedTime = info.CreationTime,
            FileSize = info.Length
        };
    }
}

public class TempStorageService
{
    private string _storageRoot;
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars().Concat(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }).Distinct().ToArray();
    private static readonly string DefaultRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GuoBuZiLiaoGuanLi",
        "TempStorage");

    private static readonly HashSet<string> SupportedExtensions = new HashSet<string>
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".pdf"
    };

    public event EventHandler FilesChanged;

    public TempStorageService()
    {
        _storageRoot = DefaultRoot;
        EnsureStorageExists();
    }

    public void SetStorageRoot(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            return;

        string newRoot = Path.Combine(rootDirectory, "暂存文件");
        if (string.Equals(_storageRoot, newRoot, StringComparison.OrdinalIgnoreCase))
            return;

        string oldRoot = _storageRoot;

        _storageRoot = newRoot;
        EnsureStorageExists();

        // 迁移旧文件到新目录
        if (Directory.Exists(oldRoot) && !string.Equals(oldRoot, DefaultRoot, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var file in Directory.GetFiles(oldRoot))
                {
                    string ext = Path.GetExtension(file).ToLower();
                    if (!SupportedExtensions.Contains(ext))
                        continue;

                    string baseName = Path.GetFileNameWithoutExtension(file);
                    string newFileName = GetUniqueFileName(baseName, ext);
                    string newPath = Path.Combine(_storageRoot, newFileName);

                    try
                    {
                        File.Move(file, newPath);
                    }
                    catch { }
                }

                // 尝试删除旧目录（如果为空）
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(oldRoot).Any())
                        Directory.Delete(oldRoot);
                }
                catch { }
            }
            catch { }
        }

        FilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureStorageExists()
    {
        if (!Directory.Exists(_storageRoot))
        {
            Directory.CreateDirectory(_storageRoot);
        }
    }

    private string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "unnamed";

        var sb = new StringBuilder(fileName);
        foreach (var c in InvalidChars)
        {
            sb.Replace(c, '_');
        }
        return sb.ToString().Trim();
    }

    private string GetUniqueFileName(string baseNameWithoutExt, string ext)
    {
        string baseName = SanitizeFileName(baseNameWithoutExt);
        if (string.IsNullOrEmpty(baseName))
            baseName = "unnamed";

        string candidate = baseName + ext;
        int counter = 1;

        while (File.Exists(Path.Combine(_storageRoot, candidate)))
        {
            candidate = $"{baseName}({counter}){ext}";
            counter++;
        }

        return candidate;
    }

    public IReadOnlyList<TempStorageFile> GetAllFiles()
    {
        var files = new List<TempStorageFile>();

        try
        {
            if (Directory.Exists(_storageRoot))
            {
                foreach (var file in Directory.GetFiles(_storageRoot))
                {
                    string ext = Path.GetExtension(file).ToLower();
                    if (SupportedExtensions.Contains(ext))
                    {
                        files.Add(TempStorageFile.FromPath(file));
                    }
                }

                // 按创建时间倒序排列（最新的在前）
                files = files.OrderByDescending(f => f.AddedTime).ToList();
            }
        }
        catch { }

        return files.AsReadOnly();
    }

    public TempStorageFile AddFile(string sourceFilePath)
    {
        EnsureStorageExists();

        string ext = Path.GetExtension(sourceFilePath);
        string originalName = Path.GetFileNameWithoutExtension(sourceFilePath);
        string newFileName = GetUniqueFileName(originalName, ext);
        string targetPath = Path.Combine(_storageRoot, newFileName);

        File.Copy(sourceFilePath, targetPath, true);

        FilesChanged?.Invoke(this, EventArgs.Empty);
        return TempStorageFile.FromPath(targetPath);
    }

    public List<TempStorageFile> AddFiles(IEnumerable<string> sourceFilePaths)
    {
        var added = new List<TempStorageFile>();
        foreach (var path in sourceFilePaths)
        {
            if (File.Exists(path))
            {
                added.Add(AddFile(path));
            }
        }
        return added;
    }

    public TempStorageFile AddScannedFile(string scannedFilePath, string originalName)
    {
        EnsureStorageExists();

        string ext = Path.GetExtension(scannedFilePath);
        string desiredName = originalName ?? Path.GetFileName(scannedFilePath);
        string baseName = Path.GetFileNameWithoutExtension(desiredName);
        string newFileName = GetUniqueFileName(baseName, ext);
        string targetPath = Path.Combine(_storageRoot, newFileName);

        File.Copy(scannedFilePath, targetPath, true);
        if (File.Exists(scannedFilePath) && Path.GetDirectoryName(scannedFilePath) != _storageRoot)
        {
            try { File.Delete(scannedFilePath); } catch { }
        }

        FilesChanged?.Invoke(this, EventArgs.Empty);
        return TempStorageFile.FromPath(targetPath);
    }

    public bool RenameFile(string id, string newNameWithoutExt)
    {
        var files = GetAllFiles();
        var file = files.FirstOrDefault(f => f.Id == id);
        if (file == null)
            return false;

        string sanitizedName = SanitizeFileName(newNameWithoutExt);
        if (string.IsNullOrWhiteSpace(sanitizedName))
            return false;

        string newFileName = GetUniqueFileName(sanitizedName, file.Extension);
        string newPath = Path.Combine(_storageRoot, newFileName);
        string oldPath = file.FilePath;

        try
        {
            if (File.Exists(oldPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(oldPath, newPath);
            }

            FilesChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void DeleteFile(string id)
    {
        var files = GetAllFiles();
        var file = files.FirstOrDefault(f => f.Id == id);
        if (file != null)
        {
            try
            {
                if (File.Exists(file.FilePath))
                    File.Delete(file.FilePath);
            }
            catch { }

            FilesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void DeleteFiles(IEnumerable<string> ids)
    {
        var files = GetAllFiles();
        bool anyDeleted = false;

        foreach (var id in ids)
        {
            var file = files.FirstOrDefault(f => f.Id == id);
            if (file != null)
            {
                try
                {
                    if (File.Exists(file.FilePath))
                        File.Delete(file.FilePath);
                    anyDeleted = true;
                }
                catch { }
            }
        }

        if (anyDeleted)
        {
            FilesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearAll()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_storageRoot))
            {
                string ext = Path.GetExtension(file).ToLower();
                if (SupportedExtensions.Contains(ext))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }

        FilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GetStoragePath()
    {
        EnsureStorageExists();
        return _storageRoot;
    }
}