# 国补资料管理系统 - 功能调整与UI优化 PRD

## Overview
- **Summary**: 本次调整包含四项修改：(1) 将批量更新文件夹日期功能从WPF程序中移除，改为独立Python脚本；(2) 回退CustomerFolder中不应存在的发票PDF日期提取逻辑，保留文件夹名称日期解析用于统计；(3) 修改暂存区行为：导入后自动移除文件，备注改为直接重命名文件；(4) 重写统计窗口日期选择UI。
- **Purpose**: 清理不属于本程序的功能（批量日期更新），修正暂存区文件管理逻辑，改善统计界面用户体验。
- **Target Users**: 国补资料管理人员。

## Goals
- 移除程序内的批量更新日期功能（BatchUpdateDateWindow、更新日期按钮、PDF日期提取、文件夹重命名逻辑）
- 创建独立Python脚本用于批量更新文件夹日期（从发票订单号提取日期并匹配重命名）
- 保留文件夹名称日期解析（ParseFolderName）用于统计功能正常运行
- 暂存文件导入客户文件夹后自动从暂存区删除
- 暂存区备注改为直接修改文件名（不含后缀），不再使用metadata中的Remark字段
- 重写统计窗口日期选择区域的UI，使其更美观易用

## Non-Goals (Out of Scope)
- 不改变统计功能的核心逻辑（仍按文件夹名称中的MMDD日期统计）
- 不改变暂存区的拖拽、扫描、文件选择等已有功能
- 不改变图片预览、手机传输、资料导入等其他功能
- 不重写整个统计窗口，仅重写日期选择区域UI
- Python脚本不集成到程序中，独立运行

## Background & Context
- 上一轮开发中添加了批量更新日期功能（BatchUpdateDateWindow）、发票PDF日期提取（TryExtractInvoiceDateFromFile）、文件夹重命名（UpdateFolderNameWithDate）等，这些功能属于另一个程序，需要从本程序移除
- 暂存区当前使用metadata.json存储备注信息，用户希望备注直接体现在文件名上，更直观
- 暂存区文件导入后目前仍保留在暂存区，用户希望导入后自动清理
- 统计窗口的日期选择区域使用默认DatePicker控件，布局较为简陋，需要美化

## Functional Requirements
- **FR-1**: 移除程序中所有批量更新日期相关代码，包括BatchUpdateDateWindow窗体、更新日期按钮、事件处理、PDF日期提取服务调用、文件夹重命名方法
- **FR-2**: 创建独立Python脚本（update_folder_dates.py），实现：扫描指定根目录下所有文件夹的发票PDF→从订单号提取MMDD日期→按人名+商品匹配→更新文件夹名称→未匹配项导出Excel
- **FR-3**: CustomerFolder保留文件夹名称日期解析（ParseFolderName中的MMDD提取），移除TryExtractInvoiceDateFromFile和UpdateFolderNameWithDate方法及InvoiceDate属性
- **FR-4**: 暂存文件被导入到客户文件夹后，自动从暂存区删除（包括磁盘文件和metadata记录）
- **FR-5**: 暂存区备注功能改为直接重命名文件：用户在备注框输入内容时，实时将磁盘上的文件名改为"备注内容.扩展名"，文件名显示同步更新
- **FR-6**: 重写统计窗口（StatisticsWindow）的日期选择区域UI，使用更现代美观的布局和自定义日期选择控件
- **FR-7**: 主窗口排序逻辑回退为按CreationTime排序（因为移除了InvoiceDate PDF提取），但文件夹卡片仍显示从名称解析的MMDD日期
- **FR-8**: 删除InvoiceDateExtractor.cs服务类，因为Python脚本使用Python PDF库独立运行，C#项目不再需要此服务

## Non-Functional Requirements
- **NFR-1**: Python脚本需在Python 3.8+环境下运行，使用pdfplumber或PyPDF2进行PDF文本提取，使用openpyxl或pandas导出Excel
- **NFR-2**: 所有UI修改需保持与现有界面风格一致（圆角卡片、柔和色彩、现代扁平设计）
- **NFR-3**: 文件重命名操作需处理特殊字符和重名情况
- **NFR-4**: 编译通过，0个错误

## Constraints
- **Technical**: WPF on .NET 8, C#, Python 3.x for standalone script
- **Business**: 文件名备注不包含扩展名，Windows文件名非法字符需过滤
- **Dependencies**: Python脚本依赖pdfplumber/pypdf和openpyxl（需pip install）

## Assumptions
- 用户已有Python环境，或可自行安装依赖
- 文件夹名称中的日期格式保持现有解析逻辑（MMDD-姓名-商品 或 MMDD姓名买商品等）
- 暂存区文件重命名后，文件扩展名保持不变
- 重名文件自动添加序号后缀

## Acceptance Criteria

### AC-1: 批量更新日期功能已从程序中移除
- **Given**: 程序已重新编译运行
- **When**: 用户查看主界面
- **Then**: 主界面不显示"更新日期"按钮，欢迎页也不显示该按钮，BatchUpdateDateWindow相关文件已从项目中删除
- **Verification**: `programmatic`（编译检查+代码检查）
- **Notes**: 检查MainWindow.xaml中无UpdateDatesButton，MainWindow.xaml.cs中无UpdateDatesButton_Click

### AC-2: 独立Python日期更新脚本可用
- **Given**: 用户有Python环境和根目录路径
- **When**: 运行 `python update_folder_dates.py <根目录路径>`
- **Then**: 脚本扫描所有文件夹发票PDF，提取订单号中的MMDD日期，按人名+商品匹配更新文件夹名，未匹配项导出为Excel
- **Verification**: `human-judgment`（运行脚本验证输出）

### AC-3: CustomerFolder中发票PDF日期提取已移除
- **Given**: CustomerFolder类已修改
- **When**: 加载客户文件夹
- **Then**: 不再尝试从PDF文件提取日期，仅从文件夹名称解析MMDD；UpdateFolderNameWithDate方法和InvoiceDate属性已移除
- **Verification**: `programmatic`（代码检查+编译通过）

### AC-4: 暂存文件导入后自动删除
- **Given**: 暂存区有文件，用户选中文件并导入到客户文件夹
- **When**: 导入成功完成后
- **Then**: 已导入的文件从暂存区列表和磁盘中消失
- **Verification**: `human-judgment`（手动测试导入后暂存区是否清空对应文件）

### AC-5: 备注修改直接重命名文件
- **Given**: 暂存区有一个图片文件"abc.jpg"
- **When**: 用户选中该文件，在备注框输入"身份证正面"
- **Then**: 磁盘文件重命名为"身份证正面.jpg"，暂存列表显示"身份证正面.jpg"，不再有独立的备注字段显示
- **Verification**: `human-judgment`（手动测试备注修改后文件名是否变化）

### AC-6: 暂存区文件名显示使用实际文件名
- **Given**: 暂存区文件已重命名
- **When**: 查看暂存列表和预览
- **Then**: 列表和预览区显示的文件名是磁盘上的实际文件名（包含备注）
- **Verification**: `human-judgment`

### AC-7: 统计窗口日期选择UI美观
- **Given**: 用户打开统计窗口
- **When**: 查看日期选择区域
- **Then**: 日期选择区域使用现代化布局，有清晰的视觉层次，单选按钮样式统一，日期选择器美观，查询按钮醒目，整体风格与主界面一致
- **Verification**: `human-judgment`

### AC-8: 统计功能正常工作
- **Given**: 根目录下有带日期的文件夹
- **When**: 用户选择日期并查询
- **Then**: 统计结果正确显示，单日查询和日期范围查询均正常
- **Verification**: `human-judgment`

### AC-9: 程序编译通过
- **Given**: 所有代码修改完成
- **When**: 执行dotnet build
- **Then**: 编译成功，0个错误
- **Verification**: `programmatic`

## Open Questions
- [ ] 统计窗口日期选择UI的具体设计方向：使用自定义日历控件还是美化现有DatePicker？（建议美化现有布局+自定义样式）
- [ ] Python脚本是否需要支持OFD格式发票？（当前Python脚本先支持PDF）
