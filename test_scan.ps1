try {
    $dialog = New-Object -ComObject WIA.CommonDialog
    Write-Host "CommonDialog created, calling ShowAcquireImage..."
    $img = $dialog.ShowAcquireImage(1, 0, "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}", $false, $true, $false)
    if ($img) {
        Write-Host "Got image! Saving..."
        $img.SaveFile("C:\Users\bao\Desktop\gongchenggit\Developer\guobuziliaoguanli\test_scan.jpg")
        Write-Host "Saved!"
    } else {
        Write-Host "No image returned (cancelled?)"
    }
} catch {
    Write-Host "Error: $_"
}
