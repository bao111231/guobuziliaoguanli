# 依赖安装: pip install pdfplumber openpyxl
"""
根据发票PDF更新文件夹日期
功能：遍历客户文件夹，从发票PDF中提取订单日期，重命名文件夹为"MMDD-人名-商品"格式
"""
import os
import re
import sys
import glob
import subprocess

def install_package(package):
    print(f"正在安装 {package}...")
    subprocess.check_call([sys.executable, "-m", "pip", "install", package, "-q"])

try:
    import pdfplumber
except ImportError:
    install_package("pdfplumber")
    import pdfplumber

try:
    from openpyxl import Workbook
except ImportError:
    install_package("openpyxl")
    from openpyxl import Workbook

UPLOADED_SUFFIX = "已上传"
order_pattern = re.compile(r'(\d{2}\d{4}\d{4}\d+)')
buyer_name_pattern = re.compile(r'购\s*买\s*方[\s\S]*?名\s*称[：:]\s*([^\n\r]+)')
buyer_name_pattern2 = re.compile(r'购买方名称[：:]\s*([^\n\r]+)')

def extract_date_from_order(order_no):
    if len(order_no) >= 10:
        year_part = order_no[2:6]
        month_part = order_no[6:8]
        day_part = order_no[8:10]
        try:
            year = int(year_part)
            month = int(month_part)
            day = int(day_part)
            if 2020 <= year <= 2030 and 1 <= month <= 12 and 1 <= day <= 31:
                return f"{month:02d}{day:02d}", f"{year}-{month:02d}-{day:02d}"
        except:
            pass
    return None, None

def extract_buyer_name(text):
    match = buyer_name_pattern.search(text)
    if match:
        name = match.group(1).strip()
        name = re.sub(r'\s+', '', name)
        return name
    match = buyer_name_pattern2.search(text)
    if match:
        name = match.group(1).strip()
        name = re.sub(r'\s+', '', name)
        return name
    return None

def extract_invoice_info(pdf_path):
    mmdd_date = None
    full_date = None
    buyer_name = None
    
    try:
        with pdfplumber.open(pdf_path) as pdf:
            for page_num, page in enumerate(pdf.pages):
                text = page.extract_text()
                if not text:
                    continue
                
                if not buyer_name:
                    buyer_name = extract_buyer_name(text)
                
                all_matches = order_pattern.findall(text)
                
                for match in all_matches:
                    if len(match) >= 14:
                        mmdd, full = extract_date_from_order(match)
                        if mmdd and not mmdd_date:
                            mmdd_date = mmdd
                            full_date = full
                            break
                
                if mmdd_date:
                    break
                
                tables = page.extract_tables()
                for table in tables:
                    for row in table:
                        if row:
                            row_text = ' '.join([str(cell) for cell in row if cell])
                            for match in order_pattern.findall(row_text):
                                if len(match) >= 14:
                                    mmdd, full = extract_date_from_order(match)
                                    if mmdd and not mmdd_date:
                                        mmdd_date = mmdd
                                        full_date = full
                                        break
                            if mmdd_date:
                                break
                    if mmdd_date:
                        break
    except Exception as e:
        return None, None, None, str(e)
    
    return mmdd_date, full_date, buyer_name, None

def parse_customer_and_product(folder_name):
    is_uploaded = folder_name.endswith(UPLOADED_SUFFIX)
    original_name = folder_name
    if is_uploaded:
        original_name = folder_name[:-len(UPLOADED_SUFFIX)]
    
    name = original_name.strip()
    
    mmdd_date = None
    date_pattern1 = re.compile(r'^(\d{4})[-_]?(.+)$')
    date_pattern2 = re.compile(r'^(\d{2})(\d{2})[-_]?(.+)$')
    
    m2 = date_pattern2.match(name)
    if m2:
        mmdd_date = m2.group(1) + m2.group(2)
        rest = m2.group(3)
    else:
        m1 = date_pattern1.match(name)
        if m1:
            date_part = m1.group(1)
            if len(date_part) == 4:
                mmdd_date = date_part
            elif len(date_part) >= 8:
                mmdd_date = date_part[4:8]
            rest = m1.group(2)
        else:
            rest = name
    
    rest = rest.lstrip('-_ ')
    
    customer_name = None
    product_name = None
    
    separators = ['-', '_', ' ', '买', '购']
    separator_idx = -1
    sep_char = None
    
    for i, ch in enumerate(rest):
        if ch in separators:
            separator_idx = i
            sep_char = ch
            break
    
    if separator_idx > 0:
        parts = rest.split(sep_char, 1)
        if len(parts) >= 1:
            customer_name = parts[0].strip()
            if len(parts) >= 2:
                product_name = parts[1].lstrip('-_ ').strip()
    else:
        customer_name = rest.strip()
    
    return customer_name, product_name, mmdd_date, is_uploaded

def find_invoice_pdf(folder_path):
    pdf_files = []
    for pattern in ["发票*.pdf", "dzfp_*.pdf", "*发票*.pdf"]:
        pdf_files.extend(glob.glob(os.path.join(folder_path, pattern)))
    
    pdf_files = list(set(pdf_files))
    if not pdf_files:
        return None
    return pdf_files[0]

def get_unique_folder_name(parent_dir, base_name, is_uploaded):
    target_name = base_name
    if is_uploaded:
        target_name += UPLOADED_SUFFIX
    
    target_path = os.path.join(parent_dir, target_name)
    if not os.path.exists(target_path):
        return target_name
    
    counter = 1
    while True:
        new_name = f"{base_name}_{counter}"
        if is_uploaded:
            new_name += UPLOADED_SUFFIX
        new_path = os.path.join(parent_dir, new_name)
        if not os.path.exists(new_path):
            return new_name
        counter += 1

def main():
    import argparse
    parser = argparse.ArgumentParser(description='根据发票PDF更新文件夹日期')
    parser.add_argument('root_dir', nargs='?', help='根目录路径')
    parser.add_argument('--dry-run', action='store_true', help='预览模式，不实际重命名')
    args = parser.parse_args()
    
    if not args.root_dir:
        parser.print_help()
        print("\n使用方法: python update_folder_dates.py <根目录路径> [--dry-run]")
        print("示例: python update_folder_dates.py C:\\客户资料")
        print("      python update_folder_dates.py C:\\客户资料 --dry-run")
        sys.exit(1)
    
    root_dir = args.root_dir
    dry_run = args.dry_run
    
    if not os.path.isdir(root_dir):
        print(f"错误: 目录不存在 - {root_dir}")
        sys.exit(1)
    
    print(f"{'='*60}")
    print(f"处理目录: {root_dir}")
    print(f"模式: {'预览模式（不实际重命名）' if dry_run else '实际执行'}")
    print(f"{'='*60}\n")
    
    subfolders = []
    for entry in os.listdir(root_dir):
        entry_path = os.path.join(root_dir, entry)
        if os.path.isdir(entry_path):
            subfolders.append(entry)
    
    subfolders.sort()
    
    print(f"找到 {len(subfolders)} 个子文件夹\n")
    
    success_count = 0
    skipped_count = 0
    unmatched = []
    
    for idx, folder_name in enumerate(subfolders, 1):
        folder_path = os.path.join(root_dir, folder_name)
        print(f"[{idx}/{len(subfolders)}] 处理: {folder_name}")
        
        customer_name, product_name, existing_mmdd, is_uploaded = parse_customer_and_product(folder_name)
        
        if not customer_name:
            reason = "无法解析人名"
            print(f"  -> 跳过: {reason}")
            unmatched.append((folder_name, reason))
            skipped_count += 1
            continue
        
        invoice_pdf = find_invoice_pdf(folder_path)
        if not invoice_pdf:
            reason = "未找到发票PDF文件"
            print(f"  -> 跳过: {reason}")
            unmatched.append((folder_name, reason))
            skipped_count += 1
            continue
        
        print(f"  找到发票: {os.path.basename(invoice_pdf)}")
        
        mmdd_date, full_date, buyer_name, error = extract_invoice_info(invoice_pdf)
        
        if error:
            reason = f"PDF解析错误: {error}"
            print(f"  -> 跳过: {reason}")
            unmatched.append((folder_name, reason))
            skipped_count += 1
            continue
        
        if not mmdd_date:
            reason = "未从发票中提取到日期"
            print(f"  -> 跳过: {reason}")
            unmatched.append((folder_name, reason))
            skipped_count += 1
            continue
        
        print(f"  提取日期: {mmdd_date} ({full_date})")
        if buyer_name:
            print(f"  购买方: {buyer_name}")
        print(f"  人名: {customer_name}, 商品: {product_name or '(未解析)'}")
        
        if not product_name:
            reason = "无法解析商品名"
            print(f"  -> 跳过: {reason}")
            unmatched.append((folder_name, reason))
            skipped_count += 1
            continue
        
        base_new_name = f"{mmdd_date}-{customer_name}-{product_name}"
        new_folder_name = get_unique_folder_name(root_dir, base_new_name, is_uploaded)
        new_folder_path = os.path.join(root_dir, new_folder_name)
        
        if new_folder_name == folder_name:
            print(f"  -> 文件夹名已是正确格式，无需重命名")
            success_count += 1
            continue
        
        print(f"  -> 重命名为: {new_folder_name}")
        
        if not dry_run:
            try:
                os.rename(folder_path, new_folder_path)
                print(f"  -> 重命名成功")
                success_count += 1
            except Exception as e:
                reason = f"重命名失败: {str(e)}"
                print(f"  -> 错误: {reason}")
                unmatched.append((folder_name, reason))
                skipped_count += 1
        else:
            print(f"  -> [预览] 将重命名为: {new_folder_name}")
            success_count += 1
        
        print()
    
    print(f"\n{'='*60}")
    print(f"处理完成!")
    print(f"成功/可重命名: {success_count}")
    print(f"跳过/未匹配: {skipped_count}")
    print(f"总计: {len(subfolders)}")
    print(f"{'='*60}\n")
    
    if unmatched:
        script_dir = os.path.dirname(os.path.abspath(__file__))
        excel_path = os.path.join(script_dir, "未匹配文件夹.xlsx")
        
        wb = Workbook()
        ws = wb.active
        ws.title = "未匹配文件夹"
        ws.append(["文件夹名", "原因"])
        
        for folder_name, reason in unmatched:
            ws.append([folder_name, reason])
        
        for column in ws.columns:
            max_length = 0
            column_letter = column[0].column_letter
            for cell in column:
                try:
                    if len(str(cell.value)) > max_length:
                        max_length = len(str(cell.value))
                except:
                    pass
            adjusted_width = min(max_length + 2, 80)
            ws.column_dimensions[column_letter].width = adjusted_width
        
        wb.save(excel_path)
        print(f"未匹配列表已导出到: {excel_path}")
        print(f"共 {len(unmatched)} 个文件夹未匹配\n")
    
    if dry_run:
        print("注意: 当前为预览模式，未实际执行重命名操作。去掉 --dry-run 参数以实际执行。")

if __name__ == "__main__":
    main()
