import os
import re
import sys
import glob

try:
    import pdfplumber
except ImportError:
    print("正在安装 pdfplumber...")
    os.system(f"{sys.executable} -m pip install pdfplumber -q")
    import pdfplumber

if len(sys.argv) > 1:
    downloads_dir = sys.argv[1]
else:
    downloads_dir = input("请输入发票文件所在目录: ").strip().strip('"')

if not os.path.isdir(downloads_dir):
    print(f"错误：目录 {downloads_dir} 不存在")
    sys.exit(1)

output_file = os.path.join(os.path.dirname(os.path.abspath(__file__)), "invoice_date_verification.txt")

order_pattern = re.compile(r'(\d{2}\d{4}\d{4}\d+)')

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

def extract_invoice_info(pdf_path):
    results = []
    filename = os.path.basename(pdf_path)
    
    try:
        with pdfplumber.open(pdf_path) as pdf:
            for page_num, page in enumerate(pdf.pages):
                text = page.extract_text()
                if not text:
                    continue
                
                all_matches = order_pattern.findall(text)
                
                for match in all_matches:
                    if len(match) >= 14:
                        mmdd, full_date = extract_date_from_order(match)
                        if mmdd:
                            results.append({
                                'page': page_num + 1,
                                'order_no': match,
                                'mmdd': mmdd,
                                'full_date': full_date
                            })
                
                tables = page.extract_tables()
                for table in tables:
                    for row in table:
                        if row:
                            row_text = ' '.join([str(cell) for cell in row if cell])
                            for match in order_pattern.findall(row_text):
                                if len(match) >= 14:
                                    mmdd, full_date = extract_date_from_order(match)
                                    if mmdd and not any(r['order_no'] == match for r in results):
                                        results.append({
                                            'page': page_num + 1,
                                            'order_no': match,
                                            'mmdd': mmdd,
                                            'full_date': full_date
                                        })
    except Exception as e:
        results.append({'error': str(e)})
    
    return filename, results

def main():
    pdf_files = glob.glob(os.path.join(downloads_dir, "dzfp_*.pdf"))
    pdf_files += glob.glob(os.path.join(downloads_dir, "*发票*.pdf"))
    
    ofd_files = glob.glob(os.path.join(downloads_dir, "*.ofd"))
    
    all_files = pdf_files + ofd_files
    print(f"找到 {len(pdf_files)} 个PDF发票文件, {len(ofd_files)} 个OFD文件")
    
    results = []
    success = 0
    failed = 0
    
    for pdf_path in sorted(all_files):
        filename, info = extract_invoice_info(pdf_path)
        if info and 'error' not in info[0]:
            for item in info:
                results.append(f"文件: {filename}")
                results.append(f"  订单号: {item['order_no']}")
                results.append(f"  提取日期: {item['mmdd']} ({item['full_date']})")
                results.append("")
            success += 1
        elif info and 'error' in info[0]:
            results.append(f"文件: {filename}")
            results.append(f"  错误: {info[0]['error']}")
            results.append("")
            failed += 1
        else:
            results.append(f"文件: {filename}")
            results.append(f"  未找到订单号")
            results.append("")
            failed += 1
    
    output = "\n".join(results)
    print(output)
    print(f"\n{'='*60}")
    print(f"成功提取: {success}, 失败/未找到: {failed}")
    
    with open(output_file, 'w', encoding='utf-8') as f:
        f.write(output)
        f.write(f"\n{'='*60}\n")
        f.write(f"成功提取: {success}, 失败/未找到: {failed}\n")
    
    print(f"\n结果已保存到: {output_file}")

if __name__ == "__main__":
    main()
