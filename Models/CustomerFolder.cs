using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GuoBuZiLiaoGuanLi.Models;

public class CustomerFolder
{
    public string FolderPath { get; set; }
    public string OriginalFolderName { get; set; }
    public string FolderName { get; set; }
    public string DisplayName => FolderName;
    public DateTime CreationTime { get; set; }
    public FolderStatus Status { get; set; }
    public bool IsUploaded { get; set; }
    public bool IsUploadedUnconfirmed { get; set; }
    public bool IsPending { get; set; }
    public bool IsManuallyComplete { get; set; }
    public Dictionary<DocumentType, DocumentItem> Documents { get; set; }

    public string InvoiceMMDD { get; private set; }
    public string CustomerName { get; private set; }
    public string ProductName { get; private set; }
    public bool HasInvoiceDate => !string.IsNullOrEmpty(InvoiceMMDD);

    private const string UploadedMarkerFile = ".uploaded";
    private const string PendingMarkerFile = ".pending";
    private const string CompleteMarkerFile = ".complete";
    private const string UploadedSuffix = "已上传";

    private static readonly Regex DateInNamePattern = new Regex(@"^(\d{4})[-_]?(.+)$", RegexOptions.Compiled);
    private static readonly Regex DateInNamePattern2 = new Regex(@"^(\d{2})(\d{2})[-_]?(.+)$", RegexOptions.Compiled);

    public CustomerFolder(string path)
    {
        FolderPath = path;
        string folderName = Path.GetFileName(path);
        
        if (folderName.EndsWith(UploadedSuffix))
        {
            OriginalFolderName = folderName.Substring(0, folderName.Length - UploadedSuffix.Length);
        }
        else
        {
            OriginalFolderName = folderName;
        }
        FolderName = folderName;
        
        CreationTime = Directory.GetCreationTime(path);
        Documents = new Dictionary<DocumentType, DocumentItem>();

        ParseFolderName();
        InitializeDocuments();
        Refresh();
    }

    private void ParseFolderName()
    {
        string name = OriginalFolderName;
        
        var m2 = DateInNamePattern2.Match(name);
        if (m2.Success)
        {
            InvoiceMMDD = m2.Groups[1].Value + m2.Groups[2].Value;
            string rest = m2.Groups[3].Value;
            ParseCustomerAndProduct(rest);
            return;
        }

        var m1 = DateInNamePattern.Match(name);
        if (m1.Success)
        {
            string datePart = m1.Groups[1].Value;
            if (datePart.Length == 4)
            {
                InvoiceMMDD = datePart;
            }
            else if (datePart.Length >= 8)
            {
                InvoiceMMDD = datePart.Substring(4, 4);
            }
            ParseCustomerAndProduct(m1.Groups[2].Value);
            return;
        }

        ParseCustomerAndProduct(name);
    }

    private void ParseCustomerAndProduct(string rest)
    {
        rest = rest.TrimStart('-', '_', ' ');
        
        int separatorIdx = rest.IndexOfAny(new[] { '-', '_', ' ', '买', '购' });
        if (separatorIdx > 0)
        {
            char sep = rest[separatorIdx];
            var parts = rest.Split(new[] { sep }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1)
            {
                CustomerName = parts[0].Trim();
                if (parts.Length >= 2)
                {
                    ProductName = parts[1].TrimStart('-', '_', ' ');
                }
            }
        }
        else
        {
            CustomerName = rest.Trim();
        }
    }

    private void InitializeDocuments()
    {
        Documents.Clear();

        foreach (DocumentType docType in System.Enum.GetValues(typeof(DocumentType)))
        {
            var doc = new DocumentItem
            {
                DocumentType = docType,
                DisplayName = docType.GetDisplayName(),
                FileNamePrefix = docType.GetFileNamePrefix(),
                FileExtensions = docType.GetFileExtensions()
            };
            Documents[docType] = doc;
        }
    }

    public void Refresh()
    {
        foreach (var doc in Documents.Values)
        {
            FindDocumentFile(doc);
        }

        string markerPath = Path.Combine(FolderPath, UploadedMarkerFile);
        bool hasMarkerFile = File.Exists(markerPath);
        bool hasSuffix = FolderName.EndsWith(UploadedSuffix);
        
        string pendingMarkerPath = Path.Combine(FolderPath, PendingMarkerFile);
        bool hasPendingMarker = File.Exists(pendingMarkerPath);

        string completeMarkerPath = Path.Combine(FolderPath, CompleteMarkerFile);
        bool hasCompleteMarker = File.Exists(completeMarkerPath);

        IsUploaded = hasMarkerFile;
        IsUploadedUnconfirmed = !hasMarkerFile && hasSuffix;

        bool hasAllDocuments = Documents.Values.All(d => d.Exists);

        if (hasAllDocuments)
        {
            IsPending = false;
            IsManuallyComplete = false;
            if (hasPendingMarker)
            {
                try
                {
                    File.Delete(pendingMarkerPath);
                }
                catch { }
            }
            if (hasCompleteMarker)
            {
                try
                {
                    File.Delete(completeMarkerPath);
                }
                catch { }
            }
        }
        else
        {
            IsPending = hasPendingMarker;
            IsManuallyComplete = hasCompleteMarker;
        }
        
        if (hasMarkerFile && !hasSuffix)
        {
            string parentDir = Path.GetDirectoryName(FolderPath);
            string newFolderName = OriginalFolderName + UploadedSuffix;
            string newFolderPath = Path.Combine(parentDir, newFolderName);
            
            if (Directory.Exists(FolderPath) && !Directory.Exists(newFolderPath))
            {
                try
                {
                    Directory.Move(FolderPath, newFolderPath);
                    FolderPath = newFolderPath;
                    FolderName = newFolderName;
                    UpdateDocumentPaths();
                }
                catch { }
            }
        }

        UpdateStatus();
    }

    private void FindDocumentFile(DocumentItem doc)
    {
        doc.Exists = false;
        doc.FilePath = null;
        doc.AllFiles = new List<string>();

        if (!Directory.Exists(FolderPath))
            return;

        var files = Directory.GetFiles(FolderPath);
        var matchedFiles = new List<string>();

        foreach (var file in files)
        {
            string fileName = Path.GetFileName(file);
            if (fileName.StartsWith(".")) continue;

            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName).ToLower();
            string extension = Path.GetExtension(file).ToLower();

            if (nameWithoutExt.StartsWith(doc.FileNamePrefix.ToLower()) &&
                doc.FileExtensions.Any(ext => extension == ext.ToLower()))
            {
                matchedFiles.Add(file);
            }
        }

        matchedFiles.Sort((a, b) =>
        {
            string na = Path.GetFileNameWithoutExtension(a);
            string nb = Path.GetFileNameWithoutExtension(b);
            return string.Compare(na, nb, StringComparison.OrdinalIgnoreCase);
        });

        if (matchedFiles.Count > 0)
        {
            doc.Exists = true;
            doc.FilePath = matchedFiles[0];
            doc.AllFiles = matchedFiles;
        }
    }

    public string GetNextFileName(DocumentType docType)
    {
        string prefix = docType.GetFileNamePrefix();
        int maxNum = 0;

        var files = Directory.GetFiles(FolderPath);
        foreach (var file in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(file).ToLower();
            if (fileName.StartsWith(prefix))
            {
                string numPart = fileName.Substring(prefix.Length);
                if (int.TryParse(numPart, out int num) && num > maxNum)
                {
                    maxNum = num;
                }
            }
        }

        return $"{prefix}{maxNum + 1:D2}";
    }

    private void UpdateStatus()
    {
        int missingCount = Documents.Values.Count(d => !d.Exists);
        bool isEffectivelyComplete = missingCount == 0 || IsManuallyComplete;

        if (!isEffectivelyComplete)
        {
            if (IsPending)
            {
                Status = FolderStatus.Pending;
            }
            else
            {
                Status = FolderStatus.Missing;
            }
        }
        else if (IsUploaded)
        {
            Status = FolderStatus.Uploaded;
        }
        else if (IsUploadedUnconfirmed)
        {
            Status = FolderStatus.UploadedUnconfirmed;
        }
        else
        {
            Status = FolderStatus.NotUploaded;
        }
    }

    public void MarkAsUploaded()
    {
        string markerPath = Path.Combine(FolderPath, UploadedMarkerFile);
        if (!File.Exists(markerPath))
        {
            File.WriteAllText(markerPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        IsUploaded = true;
        IsUploadedUnconfirmed = false;
        
        if (!FolderName.EndsWith(UploadedSuffix))
        {
            string parentDir = Path.GetDirectoryName(FolderPath);
            string newFolderName = OriginalFolderName + UploadedSuffix;
            string newFolderPath = Path.Combine(parentDir, newFolderName);
            
            if (!Directory.Exists(newFolderPath))
            {
                try
                {
                    Directory.Move(FolderPath, newFolderPath);
                    FolderPath = newFolderPath;
                    FolderName = newFolderName;
                    UpdateDocumentPaths();
                }
                catch
                {
                }
            }
        }
        
        UpdateStatus();
    }

    public void UnmarkAsUploaded()
    {
        string markerPath = Path.Combine(FolderPath, UploadedMarkerFile);
        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }
        IsUploaded = false;
        
        bool wasConfirmedUploaded = FolderName.EndsWith(UploadedSuffix);
        
        if (wasConfirmedUploaded)
        {
            string parentDir = Path.GetDirectoryName(FolderPath);
            string newFolderPath = Path.Combine(parentDir, OriginalFolderName);
            
            if (!Directory.Exists(newFolderPath))
            {
                try
                {
                    Directory.Move(FolderPath, newFolderPath);
                    FolderPath = newFolderPath;
                    FolderName = OriginalFolderName;
                    UpdateDocumentPaths();
                    IsUploadedUnconfirmed = false;
                }
                catch
                {
                    IsUploadedUnconfirmed = FolderName.EndsWith(UploadedSuffix);
                }
            }
            else
            {
                IsUploadedUnconfirmed = FolderName.EndsWith(UploadedSuffix);
            }
        }
        
        UpdateStatus();
    }
    
    public void MarkAsPending()
    {
        string pendingMarkerPath = Path.Combine(FolderPath, PendingMarkerFile);
        if (!File.Exists(pendingMarkerPath))
        {
            File.WriteAllText(pendingMarkerPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        IsPending = true;
        UpdateStatus();
    }

    public void UnmarkAsPending()
    {
        string pendingMarkerPath = Path.Combine(FolderPath, PendingMarkerFile);
        if (File.Exists(pendingMarkerPath))
        {
            try
            {
                File.Delete(pendingMarkerPath);
            }
            catch { }
        }
        IsPending = false;
        UpdateStatus();
    }

    public void MarkAsComplete()
    {
        string completeMarkerPath = Path.Combine(FolderPath, CompleteMarkerFile);
        if (!File.Exists(completeMarkerPath))
        {
            File.WriteAllText(completeMarkerPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        IsManuallyComplete = true;
        UpdateStatus();
    }

    public void UnmarkAsComplete()
    {
        string completeMarkerPath = Path.Combine(FolderPath, CompleteMarkerFile);
        if (File.Exists(completeMarkerPath))
        {
            try
            {
                File.Delete(completeMarkerPath);
            }
            catch { }
        }
        IsManuallyComplete = false;
        UpdateStatus();
    }
    
    private void UpdateDocumentPaths()
    {
        foreach (var doc in Documents.Values)
        {
            if (doc.Exists && doc.AllFiles != null)
            {
                var newFiles = new List<string>();
                foreach (var file in doc.AllFiles)
                {
                    string fileName = Path.GetFileName(file);
                    string newPath = Path.Combine(FolderPath, fileName);
                    newFiles.Add(newPath);
                }
                doc.AllFiles = newFiles;
                doc.FilePath = newFiles.Count > 0 ? newFiles[0] : null;
            }
        }
    }
}

public class DocumentItem
{
    public DocumentType DocumentType { get; set; }
    public string DisplayName { get; set; }
    public string FileNamePrefix { get; set; }
    public string[] FileExtensions { get; set; }
    public bool Exists { get; set; }
    public string FilePath { get; set; }
    public List<string> AllFiles { get; set; } = new List<string>();
    public int FileCount => AllFiles?.Count ?? 0;
}
