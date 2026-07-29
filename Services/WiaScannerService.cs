using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GuoBuZiLiaoGuanLi.Services;

public class WiaScannerService : IScannerService
{
    private const string WIA_BMP_FORMAT = "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}";
    private const int WIA_IPS_XRES = 6147;
    private const int WIA_IPS_YRES = 6148;
    private const int WIA_IPS_DATATYPE = 6145;
    private const int WIA_DATA_COLOR = 3;
    private const int WIA_IPS_BITDEPTH = 6154;
    private const int DEVICE_TYPE_SCANNER = 1;
    private const int TARGET_DPI = 300;

    public async Task<bool> IsScannerAvailableAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                Type deviceManagerType = Type.GetTypeFromProgID("WIA.DeviceManager");
                if (deviceManagerType == null) return false;

                dynamic deviceManager = Activator.CreateInstance(deviceManagerType);
                var devices = deviceManager.DeviceInfos;

                for (int i = 1; i <= devices.Count; i++)
                {
                    try
                    {
                        if (devices[i].Type == DEVICE_TYPE_SCANNER)
                            return true;
                    }
                    catch { }
                }
                return false;
            }
            catch
            {
                return false;
            }
        });
    }

    public Task<string> ScanToFileAsync(string outputPath, string fileName)
    {
        string result = ScanWithWiaNative(outputPath, fileName);
        return Task.FromResult(result);
    }

    private string ScanWithWiaNative(string outputPath, string fileName)
    {
        try
        {
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            Type deviceManagerType = Type.GetTypeFromProgID("WIA.DeviceManager");
            if (deviceManagerType == null)
            {
                Debug.WriteLine("WIA.DeviceManager not available");
                return null;
            }

            Type commonDialogType = Type.GetTypeFromProgID("WIA.CommonDialog");
            if (commonDialogType == null)
            {
                Debug.WriteLine("WIA.CommonDialog not available");
                return null;
            }

            dynamic deviceManager = null;
            dynamic commonDialog = null;
            dynamic device = null;
            dynamic item = null;
            dynamic imageFile = null;

            try
            {
                deviceManager = Activator.CreateInstance(deviceManagerType);
                var devices = deviceManager.DeviceInfos;

                dynamic scannerDeviceInfo = null;
                for (int i = 1; i <= devices.Count; i++)
                {
                    try
                    {
                        if (devices[i].Type == DEVICE_TYPE_SCANNER)
                        {
                            scannerDeviceInfo = devices[i];
                            break;
                        }
                    }
                    catch { }
                }

                if (scannerDeviceInfo == null)
                {
                    Debug.WriteLine("No scanner device found");
                    CleanupComObjects(deviceManager);
                    return null;
                }

                Debug.WriteLine("Connecting to scanner device...");
                device = scannerDeviceInfo.Connect();
                if (device == null)
                {
                    Debug.WriteLine("Failed to connect to scanner");
                    CleanupComObjects(deviceManager);
                    return null;
                }

                item = device.Items[1];
                if (item == null)
                {
                    Debug.WriteLine("No scanner item available");
                    CleanupComObjects(device, deviceManager);
                    return null;
                }

                SetScannerResolution(item);

                commonDialog = Activator.CreateInstance(commonDialogType);

                Debug.WriteLine(string.Format("Starting scan at {0} DPI...", TARGET_DPI));
                imageFile = commonDialog.ShowTransfer(item, WIA_BMP_FORMAT, false);

                if (imageFile == null)
                {
                    Debug.WriteLine("Scan returned null (error or cancelled)");
                    CleanupComObjects(imageFile, item, device, commonDialog, deviceManager);
                    return null;
                }

                string pngFinalPath = Path.Combine(outputPath, string.Format("{0}.png", fileName));
                SaveAsPngWithDpi(imageFile, pngFinalPath);

                CleanupComObjects(imageFile, item, device, commonDialog, deviceManager);

                Debug.WriteLine(string.Format("PNG saved successfully: {0}", pngFinalPath));
                return File.Exists(pngFinalPath) ? pngFinalPath : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format("Scan error: {0}\n{1}", ex.Message, ex.StackTrace));
                CleanupComObjects(imageFile, item, device, commonDialog, deviceManager);
                return null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(string.Format("ScanWithWiaNative fatal error: {0}\n{1}", ex.Message, ex.StackTrace));
            return null;
        }
    }

    private void SetScannerResolution(dynamic item)
    {
        try { SetWiaProperty(item.Properties, WIA_IPS_DATATYPE, WIA_DATA_COLOR); }
        catch (Exception ex) { Debug.WriteLine(string.Format("Set DATATYPE warning: {0}", ex.Message)); }

        try { SetWiaProperty(item.Properties, WIA_IPS_BITDEPTH, 24); }
        catch (Exception ex) { Debug.WriteLine(string.Format("Set BITDEPTH warning: {0}", ex.Message)); }

        try { SetWiaProperty(item.Properties, WIA_IPS_XRES, TARGET_DPI); }
        catch (Exception ex) { Debug.WriteLine(string.Format("Set XRES warning: {0}", ex.Message)); }

        try { SetWiaProperty(item.Properties, WIA_IPS_YRES, TARGET_DPI); }
        catch (Exception ex) { Debug.WriteLine(string.Format("Set YRES warning: {0}", ex.Message)); }
    }

    private void SetWiaProperty(dynamic properties, int propertyId, int value)
    {
        try
        {
            dynamic prop = properties[propertyId];
            if (prop != null)
            {
                object currentValue = prop.Value;
                prop.Value = value;
                Debug.WriteLine(string.Format("Set WIA property {0}: {1} -> {2}", propertyId, currentValue, value));
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(string.Format("Direct set failed for {0}: {1}", propertyId, ex.Message));
        }

        try
        {
            foreach (dynamic p in properties)
            {
                try
                {
                    if (p.PropertyID == propertyId)
                    {
                        p.Value = value;
                        Debug.WriteLine(string.Format("Set WIA property {0} (via iteration) to {1}", propertyId, value));
                        return;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(string.Format("Iteration set failed for {0}: {1}", propertyId, ex.Message));
        }
    }

    private void SaveAsPngWithDpi(dynamic imageFile, string pngPath)
    {
        byte[] imageBytes;
        try
        {
            var fileData = imageFile.FileData;
            imageBytes = (byte[])fileData.BinaryData;
        }
        catch
        {
            string tempBmp = Path.GetTempFileName();
            try
            {
                imageFile.SaveFile(tempBmp);
                imageBytes = File.ReadAllBytes(tempBmp);
            }
            finally
            {
                try { File.Delete(tempBmp); } catch { }
            }
        }

        using (var ms = new MemoryStream(imageBytes))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.None;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();

            double dpi = TARGET_DPI;
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            int stride = width * ((bitmap.Format.BitsPerPixel + 7) / 8);
            byte[] pixelData = new byte[height * stride];
            bitmap.CopyPixels(pixelData, stride, 0);

            var targetBitmap = new WriteableBitmap(width, height, dpi, dpi, bitmap.Format, bitmap.Palette);
            targetBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixelData, stride, 0);
            targetBitmap.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(targetBitmap));

            using (var fs = new FileStream(pngPath, FileMode.Create, FileAccess.Write))
            {
                encoder.Save(fs);
            }
        }

        Debug.WriteLine(string.Format("Saved PNG at {0} DPI: {1}", TARGET_DPI, pngPath));
    }

    private void CleanupComObjects(params object[] comObjects)
    {
        foreach (var obj in comObjects)
        {
            if (obj != null && Marshal.IsComObject(obj))
            {
                try { Marshal.ReleaseComObject(obj); } catch { }
            }
        }
    }

    public async Task<string[]> ScanMultipleToFileAsync(string outputPath, string baseFileName)
    {
        var result = await ScanToFileAsync(outputPath, baseFileName);
        return result != null ? new[] { result } : Array.Empty<string>();
    }
}
