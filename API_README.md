# 国补资料管理系统 - HTTP API 文档

## 概述

国补资料管理系统内置HTTP服务器，手机端（或其他客户端）可通过WiFi局域网连接，实现资料下载和标记已上传功能。

- **默认端口**：8888（若被占用会自动尝试其他端口：8080、9000、9090、18080、28080、38080）
- **基础URL**：`http://{电脑IP}:{端口}/`
- **数据格式**：所有接口返回UTF-8编码，JSON接口Content-Type为`application/json; charset=utf-8`
- **跨域**：已启用CORS，支持任意来源访问

---

## 接口列表

### 1. 获取待处理文件夹列表

获取所有「资料齐全但未上传」和「已标记待电脑确认」的文件夹。

```
GET /api/folders
```

**响应示例**：

```json
{
  "folders": [
    {
      "name": "张三13800138000",
      "originalName": "张三13800138000",
      "creationTime": "2026-07-20 14:30",
      "imageCount": 4,
      "isMarked": false,
      "pathEncoded": "5byg5LiJMTM4MDAxMzgwMDA=",
      "images": [
        {
          "name": "发票01.jpg",
          "docType": "发票",
          "url": "/d/SW52b2ljZTAxLmpwZw=="
        },
        {
          "name": "SN码01.jpg",
          "docType": "SN码",
          "url": "/d/U07pg5TmnajmnKwxLmpwZw=="
        }
      ]
    },
    {
      "name": "李四13900139000已上传",
      "originalName": "李四13900139000",
      "creationTime": "2026-07-21 09:15",
      "imageCount": 4,
      "isMarked": true,
      "pathEncoded": "5p2O5ZubMTM5MDAxMzkwMDDlt6XkuIrkuIrlupQ=",
      "images": [...]
    }
  ]
}
```

**字段说明**：

| 字段 | 类型 | 说明 |
|------|------|------|
| `name` | string | 文件夹显示名称（标记后会带"已上传"后缀） |
| `originalName` | string | 原始文件夹名（不带"已上传"后缀） |
| `creationTime` | string | 文件夹创建时间，格式：yyyy-MM-dd HH:mm |
| `imageCount` | int | 该文件夹内的图片总数 |
| `isMarked` | bool | 是否已标记待确认（true=等待电脑端确认，false=未标记） |
| `pathEncoded` | string | 文件夹路径的URL-safe Base64编码，用于标记接口 |
| `images` | array | 图片列表 |
| `images[].name` | string | 图片文件名 |
| `images[].docType` | string | 资料类型："发票"、"SN码"、"进货单"、"销售单" |
| `images[].url` | string | 图片下载相对路径 |

**状态说明**：
- `isMarked: false` → 蓝色状态（资料齐未上传），可下载可标记
- `isMarked: true` → 橙色状态（已标记待电脑确认），可重新下载（需二次确认），不能重复标记

---

### 2. 标记文件夹为已上传

手机端下载完图片后调用此接口，将文件夹标记为已上传（文件夹名会加上"已上传"后缀，等待电脑端确认）。

```
POST /api/mark
Content-Type: application/json
```

**请求体**：

```json
{
  "path": "{pathEncoded值}"
}
```

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `path` | string | 是 | 从`/api/folders`接口获取的`pathEncoded`字段值 |

**成功响应**（HTTP 200）：

```json
{
  "success": true
}
```

**失败响应**：
- HTTP 404：文件夹不存在
- HTTP 403：路径非法（尝试访问根目录以外的位置）
- HTTP 500：服务器内部错误

**调用后效果**：
1. 文件夹重命名，末尾加上"已上传"后缀
2. 电脑端自动刷新列表，该文件夹显示为橙色「已上传未确认」状态
3. 再次请求`/api/folders`时，该文件夹的`isMarked`变为`true`并排在列表底部
4. 标记接口具有幂等性：重复标记已标记的文件夹不会重复重命名

---

### 3. 下载图片文件

下载指定图片文件。

```
GET /d/{encodedPath}
```

**路径参数**：
- `encodedPath`：图片完整路径的URL-safe Base64编码（从`/api/folders`接口的`images[].url`字段获取，去掉前缀`/d/`）

**响应**：
- HTTP 200：返回图片二进制数据
- `Content-Type`：根据文件扩展名设置（image/jpeg, image/png, application/pdf等）
- `Content-Disposition: attachment`：触发浏览器下载
- HTTP 404：文件不存在
- HTTP 403：路径非法

---

### 4. 测试连接

测试服务器是否正常运行。

```
GET /api/test
```

**响应**：HTTP 200，返回纯文本 `ok`

---

### 5. 主页面

获取手机端网页版界面（HTML），用于浏览器直接访问。安卓App不需要调用此接口。

```
GET /
```

---

## 路径编码说明

所有文件路径和文件夹路径都使用 **URL-safe Base64** 编码传输，编码方式如下：

### 标准Base64 → URL-safe Base64转换规则：
1. `+` → `-`
2. `/` → `_`
3. 去掉末尾的 `=` 填充字符

### C# 编码示例（服务端使用）：
```csharp
string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(filePath));
string urlSafe = base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
```

### Java 解码示例（安卓端使用）：
```java
public static String decodeUrlSafeBase64(String urlSafe) {
    String base64 = urlSafe.replace('-', '+').replace('_', '/');
    int padding = (4 - base64.length() % 4) % 4;
    for (int i = 0; i < padding; i++) {
        base64 += "=";
    }
    byte[] bytes = android.util.Base64.decode(base64, android.util.Base64.DEFAULT);
    return new String(bytes, StandardCharsets.UTF_8);
}
```

### Kotlin 解码示例：
```kotlin
fun decodeUrlSafeBase64(urlSafe: String): String {
    var base64 = urlSafe.replace('-', '+').replace('_', '/')
    val padding = (4 - base64.length % 4) % 4
    repeat(padding) { base64 += "=" }
    val bytes = android.util.Base64.decode(base64, android.util.Base64.DEFAULT)
    return String(bytes, Charsets.UTF_8)
}
```

---

## 安卓开发注意事项

### 1. 网络安全配置
Android 9+ 默认禁止明文HTTP请求，需要在`AndroidManifest.xml`中配置：

```xml
<application
    android:usesCleartextTraffic="true"
    ...>
```

或创建`res/xml/network_security_config.xml`：
```xml
<?xml version="1.0" encoding="utf-8"?>
<network-security-config>
    <domain-config cleartextTrafficPermitted="true">
        <!-- 允许局域网IP段 -->
        <domain includeSubdomains="false">192.168.0.0</domain>
        <domain includeSubdomains="false">192.168.1.0</domain>
        <domain includeSubdomains="false">10.0.0.0</domain>
    </domain-config>
</network-security-config>
```

### 2. 推荐工作流程
```
1. 用户输入电脑IP和端口（或通过二维码扫描解析）
2. GET /api/test 测试连接是否正常
3. GET /api/folders 获取文件夹列表
4. 显示列表（isMarked=false的排上面，isMarked=true的排下面）
5. 对于未标记文件夹(isMarked=false)：
   - 用户点击"下载全部" → 依次请求 /d/{encodedPath} 下载图片
   - 下载完成后调用 POST /api/mark 标记为已上传
6. 对于已标记文件夹(isMarked=true)：
   - 用户点击"重新下载"时，先弹出确认对话框："该文件夹已标记为已上传，是否需要重新下载图片？"
   - 用户点击"是"才开始下载，点击"否"取消
   - 不需要再次调用标记接口
7. 定时轮询 GET /api/folders 刷新状态（建议5秒间隔）
```

### 3. 文件夹排序与交互规则
- 未标记的（`isMarked: false`）排在前面，显示「下载全部」和「标记已上传」按钮
- 已标记待确认的（`isMarked: true`）排在后面，用分隔线隔开
- 已标记文件夹只显示「重新下载」按钮，不显示「标记已上传」按钮
- 点击「重新下载」时必须弹出确认对话框，防止误操作
- 重新下载完成后提示文字应为"重新下载完成"，不再提示点击标记

### 4. 文件下载处理
- 下载时建议显示进度条
- 图片保存到手机公共相册目录（DCIM/Pictures）
- 由于是批量下载，每张图片之间建议间隔300-800ms，避免服务器压力过大
- 注意处理手机存储权限请求

---

## 资料类型定义

| docType | 文件名前缀 | 说明 |
|---------|-----------|------|
| 发票 | fapiao | 发票图片 |
| SN码 | sn | SN序列号图片 |
| 进货单 | jinhuodan | 进货单图片 |
| 销售单 | xiaoshouidan | 销售单图片 |

每个类型可能有多张图片（如发票01.jpg、发票02.jpg...）。

---

## 文件夹状态流转

```
红色(缺资料) → 蓝色(资料齐未上传) → 橙色(已标记待确认) → 绿色(电脑确认已上传)
```

HTTP API 中只返回蓝色和橙色状态的文件夹（`isMarked: false`或`true`），红色缺资料和绿色已完成的不会返回。

手机端标记操作触发：蓝色 → 橙色
电脑端确认操作触发：橙色 → 绿色（此操作在电脑端UI完成，API不直接提供）
