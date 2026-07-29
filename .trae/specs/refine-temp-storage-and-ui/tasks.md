# 国补资料管理系统 - 功能调整与UI优化 实现计划

## [ ] Task 1: 移除批量更新日期功能及相关代码
- **Priority**: high
- **Depends On**: None
- **Description**: 
  - 删除 BatchUpdateDateWindow.xaml 和 BatchUpdateDateWindow.xaml.cs 文件
  - 删除 Services/InvoiceDateExtractor.cs 文件
  - 从 MainWindow.xaml 中移除 UpdateDatesButton 按钮（工具栏和欢迎页两处）
  - 从 MainWindow.xaml.cs 中移除 UpdateDatesButton_Click 方法
  - 从 .csproj 中移除对 BatchUpdateDateWindow 的引用（如有）
  - 移除 ClosedXML 包引用如果不再需要（检查统计窗口是否还在用）
- **Acceptance Criteria Addressed**: AC-1, AC-9
- **Test Requirements**:
  - `programmatic` TR-1.1: dotnet build 编译成功，0错误
  - `human-judgement` TR-1.2: 主界面和欢迎页不再显示"更新日期"按钮
- **Notes**: 统计窗口的Excel导出功能仍使用ClosedXML，保留该包引用

## [ ] Task 2: 清理CustomerFolder中不属于本程序的日期逻辑
- **Priority**: high
- **Depends On**: Task 1
- **Description**:
  - 从 CustomerFolder.cs 移除 InvoiceDate 属性（DateTime?）
  - 移除 TryExtractInvoiceDateFromFile() 方法
  - 移除 UpdateFolderNameWithDate() 方法
  - 移除对 GuoBuZiLiaoGuanLi.Services 中 InvoiceDateExtractor 的 using 引用
  - 移除 Refresh() 方法中对 TryExtractInvoiceDateFromFile() 的调用
  - 保留 ParseFolderName() 中的文件夹名称日期解析（InvoiceMMDD提取、CustomerName/ProductName解析）
  - 保留 HasInvoiceDate 属性（用于UI显示）
  - 修改 MainWindow.xaml.cs 中 LoadCustomerFolders 的排序逻辑：从 `OrderByDescending(f => f.InvoiceDate ?? f.CreationTime)` 改回 `OrderByDescending(f => f.CreationTime)`
- **Acceptance Criteria Addressed**: AC-3, AC-9
- **Test Requirements**:
  - `programmatic` TR-2.1: dotnet build 编译成功，0错误
  - `human-judgement` TR-2.2: 文件夹列表按创建时间排序，文件夹卡片仍显示从名称解析的MMDD日期
- **Notes**: ParseFolderName 中通过正则从文件夹名提取MMDD的逻辑保留，这是统计功能所需

## [ ] Task 3: 创建独立Python日期更新脚本
- **Priority**: high
- **Depends On**: None (can run in parallel with Tasks 1-2)
- **Description**:
  - 创建 update_folder_dates.py 脚本
  - 功能：接收根目录路径参数，遍历所有子文件夹
  - 在每个文件夹中查找发票PDF文件
  - 使用pdfplumber/pypdf提取PDF文本，用正则匹配订单号（如0020260529...）
  - 从订单号第3-10位提取YYYYMMDD，转换为MMDD格式
  - 从发票文本或文件夹名解析人名和商品名
  - 匹配逻辑：按人名+商品名匹配，匹配成功则重命名文件夹为"MMDD-人名-商品名"格式
  - 未匹配的文件夹记录到Excel，使用openpyxl导出
  - 支持已上传后缀（"已上传"）的文件夹重命名
  - 添加运行说明注释和依赖说明
- **Acceptance Criteria Addressed**: AC-2
- **Test Requirements**:
  - `human-judgement` TR-3.1: 脚本可运行，能正确从订单号提取日期
  - `human-judgement` TR-3.2: 未匹配项正确导出Excel
- **Notes**: 脚本放在项目根目录，需在脚本头部注明pip install依赖

## [ ] Task 4: 修改暂存服务 - 导入后自动删除文件
- **Priority**: high
- **Depends On**: Task 2
- **Description**:
  - 修改 TempStorageService：添加方法 `DeleteFiles(IEnumerable<string> ids)` 已有，添加 `DeleteFilesByPaths(IEnumerable<string> paths)` 或修改导入逻辑传入文件ID
  - 修改 MainWindow.xaml.cs 中 ImportFilesToDocument 方法：当sourceFiles来自暂存（TempFilePickerWindow）时，导入成功后从暂存服务删除对应文件
  - 需要在TempFilePickerWindow返回时同时返回文件ID或TempStorageFile对象列表，而不仅仅是路径
  - 修改TempFilePickerWindow的SelectedFilePaths为SelectedFiles（返回TempStorageFile对象列表）
  - 导入成功后调用 _tempStorage.DeleteFiles(ids) 删除已导入的文件
- **Acceptance Criteria Addressed**: AC-4, AC-9
- **Test Requirements**:
  - `programmatic` TR-4.1: dotnet build 编译成功，0错误
  - `human-judgement` TR-4.2: 从暂存选择文件导入客户文件夹后，这些文件从暂存列表中消失
- **Notes**: 直接拖拽到预览区导入（非暂存方式）不删除源文件

## [ ] Task 5: 修改暂存服务 - 备注改为文件名重命名
- **Priority**: high
- **Depends On**: Task 4
- **Description**:
  - 修改 TempStorageService：
    - 将 AddFile 中的文件命名策略从GUID改为使用原始文件名（不覆盖已有文件则加序号）
    - 移除 Remark 属性或不再独立存储Remark，备注就是文件名（不含扩展名）
    - FileName 属性改为从磁盘文件名实时获取
    - 修改 UpdateRemark 方法为 RenameFile(string id, string newNameWithoutExt)：实际重命名磁盘文件
    - 添加非法字符过滤（\ / : * ? " < > |）
    - 处理重名情况：如果目标文件名已存在，自动添加(1)(2)后缀
    - metadata.json 中不再需要 Remark 字段，保留兼容读取但不写入
    - 新添加的文件直接使用原始文件名存储（不重命名为GUID）
  - 修改 TempStorageWindow.xaml.cs：
    - 列表显示使用磁盘上的实际文件名（FileName显示为实际文件名）
    - RemarkTextBox 改为文件名编辑框，标签改为"文件名："
    - 文本变化时调用 RenameFile 而非 UpdateRemark
    - 预览区 FileNameText 显示实际文件名
    - 添加失焦事件或实时更新重命名（建议实时更新加防抖，或失焦时更新）
  - 修改 TempFilePickerWindow：显示实际文件名
- **Acceptance Criteria Addressed**: AC-5, AC-6, AC-9
- **Test Requirements**:
  - `programmatic` TR-5.1: dotnet build 编译成功，0错误
  - `human-judgement` TR-5.2: 在暂存区修改文件名后，磁盘文件被重命名
  - `human-judgement` TR-5.3: 暂存列表显示实际文件名
  - `human-judgement` TR-5.4: 新拖入的文件保留原始文件名
- **Notes**: 非法字符替换为下划线或空；重名自动加序号

## [ ] Task 6: 重写统计窗口日期选择UI
- **Priority**: medium
- **Depends On**: Task 2
- **Description**:
  - 重写 StatisticsWindow.xaml 的日期选择区域（Grid Row="1"的Border部分）
  - 设计方向：
    - 使用卡片式切换替代RadioButton（单日/范围两个可点击卡片）
    - 日期选择区域使用更好的布局：标签+自定义样式的DatePicker
    - 开始/结束日期在范围模式下水平排列，中间有"至"连接
    - 查询按钮使用渐变色或醒目的主色调，圆角，悬停效果
    - 整体Padding和间距调整更舒适
    - 单选切换时有平滑的视觉反馈
  - 保持StatisticsWindow.xaml.cs的后台逻辑基本不变，仅调整XAML和少量控件名称
  - 保留所有统计计算和导出功能
- **Acceptance Criteria Addressed**: AC-7, AC-8, AC-9
- **Test Requirements**:
  - `programmatic` TR-6.1: dotnet build 编译成功，0错误
  - `human-judgement` TR-6.2: 日期选择区域美观现代，与主界面风格一致
  - `human-judgement` TR-6.3: 单日查询和范围查询功能正常
  - `human-judgement` TR-6.4: 统计结果显示和Excel导出正常
- **Notes**: 参考主界面已有的圆角卡片、柔和色调设计风格
