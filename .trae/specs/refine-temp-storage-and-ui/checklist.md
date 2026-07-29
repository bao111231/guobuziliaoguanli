# 国补资料管理系统 - 功能调整与UI优化 验证清单

## 代码清理验证
- [ ] BatchUpdateDateWindow.xaml 文件已删除
- [ ] BatchUpdateDateWindow.xaml.cs 文件已删除
- [ ] Services/InvoiceDateExtractor.cs 文件已删除
- [ ] MainWindow.xaml 中无 UpdateDatesButton 按钮（工具栏WrapPanel中无"🔄 更新日期"）
- [ ] MainWindow.xaml 欢迎页中无"更新日期"按钮
- [ ] MainWindow.xaml.cs 中无 UpdateDatesButton_Click 方法
- [ ] CustomerFolder.cs 中无 InvoiceDate 属性
- [ ] CustomerFolder.cs 中无 TryExtractInvoiceDateFromFile 方法
- [ ] CustomerFolder.cs 中无 UpdateFolderNameWithDate 方法
- [ ] CustomerFolder.cs 中无对 InvoiceDateExtractor 的 using 引用
- [ ] CustomerFolder.cs 的 Refresh() 中不调用 TryExtractInvoiceDateFromFile
- [ ] CustomerFolder.cs 保留 ParseFolderName 和 ParseCustomerAndProduct（从文件夹名解析MMDD/姓名/商品）
- [ ] CustomerFolder.cs 保留 InvoiceMMDD、CustomerName、ProductName、HasInvoiceDate 属性
- [ ] MainWindow.xaml.cs 排序改为按 CreationTime 排序

## 编译验证
- [ ] dotnet build 编译成功，0个错误
- [ ] 无因删除文件导致的编译错误

## Python脚本验证
- [ ] update_folder_dates.py 已创建在项目根目录
- [ ] 脚本头部注明了所需pip依赖（pdfplumber/pypdf, openpyxl）
- [ ] 脚本能接收命令行参数（根目录路径）
- [ ] 脚本正则能正确从订单号提取MMDD日期（如0020260529...→0529）
- [ ] 脚本支持按人名+商品名匹配文件夹
- [ ] 脚本支持重命名带"已上传"后缀的文件夹
- [ ] 未匹配的文件夹导出为Excel文件

## 暂存导入后删除验证
- [ ] TempFilePickerWindow 返回 TempStorageFile 对象列表（而非仅路径）
- [ ] MainWindow ImportFilesToDocument 在导入成功后，对来自暂存的文件调用 DeleteFiles
- [ ] 从暂存导入文件到客户文件夹后，暂存列表中该文件消失
- [ ] 磁盘上暂存目录中的文件被删除
- [ ] 直接拖拽到预览区的文件（非暂存方式）不删除源文件

## 暂存备注改文件名验证
- [ ] 新拖入暂存的文件保留原始文件名（不再重命名为GUID）
- [ ] 暂存服务文件存储以原始文件名保存（重名加序号）
- [ ] 修改文件名编辑框时，磁盘文件被重命名
- [ ] 文件名不含扩展名部分被修改，扩展名保持不变
- [ ] 文件名中的非法字符被过滤
- [ ] 重名文件自动添加序号后缀
- [ ] 暂存列表显示实际磁盘文件名
- [ ] 预览区标题显示实际文件名
- [ ] TempFilePickerWindow 中显示实际文件名
- [ ] metadata.json 不再写入 Remark 字段（兼容读取旧数据）

## 统计窗口UI验证
- [ ] 统计窗口日期选择区域使用新的美化布局
- [ ] 单日/范围切换使用卡片式按钮（非默认RadioButton样式）
- [ ] 日期选择器样式与整体风格协调
- [ ] 查询按钮醒目美观，有悬停效果
- [ ] 单日查询功能正常
- [ ] 日期范围查询功能正常
- [ ] 统计结果正确显示
- [ ] Excel导出功能正常
- [ ] 文件夹卡片仍显示从名称解析的MMDD日期

## 整体功能回归验证
- [ ] 主界面正常加载
- [ ] 文件夹列表正常显示
- [ ] 筛选功能正常
- [ ] 搜索功能正常
- [ ] 暂存窗口正常打开、拖入文件、扫描、清空
- [ ] 从暂存导入到客户文件夹正常工作
- [ ] 统计窗口正常打开和使用
- [ ] 手机传输功能不受影响
