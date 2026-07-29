namespace GuoBuZiLiaoGuanLi.Models;

public enum DocumentType
{
    Invoice,
    SNCode,
    PurchaseOrder,
    SalesOrder
}

public static class DocumentTypeExtensions
{
    public static string GetDisplayName(this DocumentType docType)
    {
        return docType switch
        {
            DocumentType.Invoice => "发票",
            DocumentType.SNCode => "SN码",
            DocumentType.PurchaseOrder => "签购单",
            DocumentType.SalesOrder => "销售单",
            _ => docType.ToString()
        };
    }

    public static string GetFileNamePrefix(this DocumentType docType)
    {
        return docType switch
        {
            DocumentType.Invoice => "fp",
            DocumentType.SNCode => "sn",
            DocumentType.PurchaseOrder => "qg",
            DocumentType.SalesOrder => "xd",
            _ => docType.ToString()
        };
    }

    public static string[] GetFileExtensions(this DocumentType docType)
    {
        return new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".pdf" };
    }
}
