# UI-Core Integration Guide

**สำหรับ:** Ali Raza (UI Developer) และทีมทั้งหมด
**เขียนโดย:** Pong (File & Storage Developer)
**วันที่:** 2026-03-27

---

## 1. สิ่งที่ต้องรู้ก่อนเริ่ม

### 1.1 สถานะปัจจุบัน

**Core Layer (Pong — เสร็จแล้ว):**
- 6 Interfaces, 6 Models, 10 Services
- 81 unit tests ผ่านทั้งหมด
- Build: 0 errors, 0 warnings
- Push ขึ้น `main` แล้ว

**UI Layer (Ali — prototype):**
- 9 Views + MainWindow (ทำงานได้แล้ว แต่ใช้ demo data)
- Logic ทั้งหมดฝังใน code-behind (ไม่เรียก Core services)
- ใช้ MD5 แทน SHA-256, ใช้ DPAPI แทน AES-256
- File I/O ใช้ hardcoded paths ไปที่ LocalApplicationData

**Connector Layer (Cheng — ยังไม่เสร็จ):**
- ต้อง implement ILLMConnector, ChatGPTConnector, GeminiConnector

### 1.2 เป้าหมายของ Integration

เปลี่ยน UI จาก "demo data + hardcoded logic" ให้เป็น "เรียก Core services จริง"
หลังจาก integration เสร็จ workflow จะเป็น:

```
User → UI View → Core Service → JSON file / keys.enc / logs
                → Connector Layer → HTTP → LLM Provider
```

### 1.3 Build Commands

```bash
# Build เฉพาะ Core (macOS/Windows)
dotnet build src/MultiLLMProjectAssistant.Core

# Build ทั้ง Solution (Windows เท่านั้น เพราะ WPF)
dotnet build Multi-LLM-Project-Assistant.sln

# Run tests
dotnet test tests/MultiLLMProjectAssistant.Tests
```

---

## 2. Core Services ที่พร้อมใช้ — Quick Reference

### 2.1 วิธีสร้าง Instance (ลำดับสำคัญ)

```csharp
// ขั้นที่ 1: สร้าง base services (ไม่มี dependency)
var encryption = new EncryptionService();         // AES-256, device-bound

// ขั้นที่ 2: สร้าง ProjectService (ไม่มี dependency)
var projectService = new ProjectService();        // default path: LocalApplicationData/MultiLLMProjectAssistant/ProjectData

// ขั้นที่ 3: สร้าง services ที่ต้องการ projectService
var fileStore   = new FileStoreService(projectService);
var keyStore    = new KeyStore(encryption, projectService);
var productKey  = new ProductKeyService(encryption, projectService);
var logger      = new AppLogger(projectService.GetProjectPath(currentProjectKey));
var trace       = new TraceService(projectService);

// ขั้นที่ 4: สร้าง services ที่ต้องการ services อื่น
var embedder    = new FileEmbedder(fileStore);
var export      = new ExportService(projectService, fileStore);
```

### 2.2 Service Summary Table

| Service | หน้าที่ | Methods หลัก |
|---------|---------|--------------|
| `ProjectService` | สร้าง/เปิด/ปิด project | `CreateProject(name, desc)`, `OpenProject(key)`, `CloseProject(key)`, `ListProjects()`, `DeleteProject(key)` |
| `EncryptionService` | เข้ารหัส/ถอดรหัส | `Encrypt(plainText)` → byte[], `Decrypt(cipherData)` → string |
| `KeyStore` | จัดการ API keys | `SaveKey(projKey, provider, apiKey)`, `GetKey(projKey, provider)`, `DeleteKey(projKey, provider)`, `ListProviders(projKey)` |
| `ProductKeyService` | จัดการ Product Key | `SaveProductKey(projKey, productKey)`, `ValidateProductKey(projKey)`, `GetStatus(projKey)`, `IsFeatureEnabled(projKey, feature)` |
| `FileStoreService` | Import/จัดการไฟล์ | `ImportFileAsync(projKey, path)`, `GetFileList(projKey)`, `GetFileContent(projKey, fileId)`, `DeleteFile(projKey, fileId)` |
| `FileEmbedder` | แปลง fileId เป็น content | `EmbedFileContent(projKey, fileId, maxChars)`, `EmbedMultipleFiles(projKey, fileIds)` |
| `RedactionFilter` | ลบ API keys จาก text | `RedactionFilter.Redact(text)` (static method) |
| `AppLogger` | เขียน log | `Log(level, message, requestId?)`, `LogRequest(requestId, provider, statusCode)` |
| `TraceService` | บันทึก traceability | `SaveTrace(projKey, traceRecord)`, `LoadTrace(projKey, reqId)`, `ListTraces(projKey)` |
| `ExportService` | Export project data | `ExportProjectSummary(projKey)`, `ExportAllToFolder(projKey, outputPath)` |

---

## 3. Integration ทีละ View — ขั้นตอนโดยละเอียด

### ขั้นที่ 0: เพิ่ม Project Reference

แก้ไขไฟล์ `src/MultiLLMProjectAssistant.UI/MultiLLMProjectAssistant.UI.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <!-- เพิ่มบรรทัดนี้ -->
  <ItemGroup>
    <ProjectReference Include="..\MultiLLMProjectAssistant.Core\MultiLLMProjectAssistant.Core.csproj" />
  </ItemGroup>
</Project>
```

### ขั้นที่ 0.5: สร้าง Service Locator ใน App.xaml.cs

เพื่อให้ทุก View ใช้ services ชุดเดียวกัน:

```csharp
using MultiLLMProjectAssistant.Core.Services;
using MultiLLMProjectAssistant.Core.Interfaces;

namespace MultiLLMProjectAssistant.UI;

public partial class App : Application
{
    // Shared services — สร้างครั้งเดียว ใช้ทั้ง app
    public static IProjectService ProjectService { get; private set; } = null!;
    public static IEncryptionService Encryption { get; private set; } = null!;
    public static IKeyStore KeyStore { get; private set; } = null!;
    public static IProductKeyService ProductKey { get; private set; } = null!;
    public static IFileStore FileStore { get; private set; } = null!;
    public static FileEmbedder FileEmbedder { get; private set; } = null!;
    public static TraceService TraceService { get; private set; } = null!;
    public static ExportService ExportService { get; private set; } = null!;

    // Current project state
    public static string? CurrentProjectKey { get; set; }
    public static AppLogger? Logger { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize core services
        Encryption = new EncryptionService();
        ProjectService = new ProjectService();
        KeyStore = new KeyStore(Encryption, ProjectService);
        ProductKey = new ProductKeyService(Encryption, ProjectService);
        FileStore = new FileStoreService(ProjectService);
        FileEmbedder = new FileEmbedder(FileStore);
        TraceService = new TraceService(ProjectService);
        ExportService = new ExportService(ProjectService, FileStore);
    }

    /// <summary>Call this when user opens a project.</summary>
    public static void SetCurrentProject(string projectKey)
    {
        CurrentProjectKey = projectKey;
        var path = ProjectService.GetProjectPath(projectKey);
        Logger = new AppLogger(path);
    }
}
```

---

### 3.1 ProjectSelectionView — เปิด/สร้าง Project

**ปัจจุบัน:** อ่าน/เขียน `projects.json` เอง + demo data
**เปลี่ยนเป็น:** เรียก `App.ProjectService`

#### สิ่งที่ต้องเปลี่ยน:

**A) LoadProjects() — แทนที่ File.ReadAllText ด้วย ProjectService:**
```csharp
// ❌ เดิม (hardcoded file I/O)
var json = File.ReadAllText(_projectsPath);
var store = JsonSerializer.Deserialize<ProjectStore>(json);

// ✅ ใหม่
var projects = App.ProjectService.ListProjects();
// แปลง ProjectInfo → ProjectItem สำหรับ UI binding
foreach (var p in projects)
{
    _projects.Add(new ProjectItem
    {
        Id = p.ProjectKey,
        Name = p.ProjectName,
        Description = p.Description,
        Status = "Active",
        UpdatedAt = p.LastOpenedAt
    });
}
```

**B) CreateNewProject_Click() — ใช้ ProjectService.CreateProject():**
```csharp
// ❌ เดิม
var item = new ProjectItem { Id = Guid.NewGuid().ToString("N"), Name = dlg.ProjectName, ... };
_projects.Add(item);
SaveProjects();

// ✅ ใหม่
var info = App.ProjectService.CreateProject(dlg.ProjectName, dlg.ProjectDescription);
App.SetCurrentProject(info.ProjectKey);  // ตั้ง current project
// Refresh list
```

**C) ProjectsListBox_SelectionChanged() — เปิด project:**
```csharp
// ✅ ใหม่
if (selectedItem is ProjectItem item)
{
    var info = App.ProjectService.OpenProject(item.Id);
    App.SetCurrentProject(item.Id);
}
```

**D) ลบ SeedDemo() ทั้งหมด** — ไม่ต้อง seed เพราะ ProjectService.ListProjects() จะ return list จริง

---

### 3.2 FileManagementView — Import/ดูไฟล์

**ปัจจุบัน:** ใช้ MD5, demo data, ไม่ได้ import จริง
**เปลี่ยนเป็น:** เรียก `App.FileStore`

#### สิ่งที่ต้องเปลี่ยน:

**A) AddFiles() — แทน ComputeMd5 ด้วย FileStoreService.ImportFileAsync():**
```csharp
// ❌ เดิม (MD5 + manual FileItem creation)
var md5 = ComputeMd5(filePath);
var item = new FileItem { Path = filePath, Name = name, Md5 = md5, ... };

// ✅ ใหม่
var projectKey = App.CurrentProjectKey;
var metadata = await App.FileStore.ImportFileAsync(projectKey, filePath);
var item = new FileItem
{
    Path = filePath,
    Name = metadata.OriginalName,
    Type = metadata.FileType,
    SizeBytes = metadata.FileSizeBytes,
    Timestamp = metadata.ImportedAt,
    Md5 = metadata.Sha256Hash,  // หรือเปลี่ยนชื่อ property เป็น Hash
    IsAttached = false
};
```

**B) ลบ ComputeMd5() ทั้ง method** — ไม่ต้องใช้แล้ว (FileStoreService คำนวณ SHA-256 ให้)

**C) BuildPreview() — ใช้ FileStore.GetFileContent():**
```csharp
// ❌ เดิม
var text = File.ReadAllText(item.Path);
if (text.Length > 6000) text = text[..6000] + "\n... [truncated]";

// ✅ ใหม่
try
{
    var content = App.FileStore.GetFileContent(App.CurrentProjectKey, fileId);
    PreviewTextBox.Text = content;
}
catch (NotSupportedException)
{
    PreviewTextBox.Text = $"[Binary file — metadata only]\n{App.FileEmbedder.GetAttachmentSummary(App.CurrentProjectKey, fileId)}";
}
```

**D) ลบ SeedDemoFiles()** — โหลดจาก FileStore.GetFileList() แทน:
```csharp
var files = App.FileStore.GetFileList(App.CurrentProjectKey);
foreach (var f in files)
{
    _files.Add(new FileItem { Name = f.OriginalName, Type = f.FileType, ... });
}
```

---

### 3.3 SettingsAndApiKeysView — จัดการ API Keys

**ปัจจุบัน:** ใช้ DPAPI + hardcoded settings.json
**เปลี่ยนเป็น:** เรียก `App.KeyStore` + `App.ProductKey`

#### สิ่งที่ต้องเปลี่ยน:

**A) ลบ EncryptDpapiToBase64() และ DecryptDpapiFromBase64() ทั้ง 2 methods**

**B) SaveKey_Click() — ใช้ KeyStore:**
```csharp
// ❌ เดิม
var encrypted = EncryptDpapiToBase64(keyValue);
item.EncryptedValue = encrypted;
SaveSettings();

// ✅ ใหม่
var projectKey = App.CurrentProjectKey;
App.KeyStore.SaveKey(projectKey, provider, keyValue);
App.Logger?.Log("INFO", $"API key updated for {provider}");
```

**C) LoadSettings() — โหลด keys จาก KeyStore:**
```csharp
// ✅ ใหม่
var projectKey = App.CurrentProjectKey;
var providers = App.KeyStore.ListProviders(projectKey);
foreach (var provider in providers)
{
    _apiKeys.Add(new ApiKeyItem
    {
        Provider = provider,
        Masked = "••••••••••••XXXX",  // ไม่ต้อง decrypt — แค่แสดงว่ามี key
        UpdatedAt = DateTime.UtcNow
    });
}
```

**D) เพิ่ม Product Key section:**
```csharp
// บันทึก Product Key
App.ProductKey.SaveProductKey(App.CurrentProjectKey, productKeyTextBox.Text);

// ตรวจสอบ
var status = App.ProductKey.GetStatus(App.CurrentProjectKey);
ProductKeyStatusText.Text = status.IsValid
    ? $"Valid ({status.KeyType}) — {status.Message}"
    : $"Invalid — {status.Message}";
```

---

### 3.4 ProjectMemoryView — CRUD Memory Items

**ปัจจุบัน:** In-memory เท่านั้น + demo data, ไม่ persist
**เปลี่ยนเป็น:** Kushal's MemoryService (อ่าน/เขียน `memory/items.json`)

**หมายเหตุ:** MemoryNote model ของ Pong พร้อมใช้แล้ว ดูที่:
`src/MultiLLMProjectAssistant.Core/Models/MemoryNote.cs`

Fields: Id, Title, Content, Tags (List<string>), CreatedAt, UpdatedAt

Kushal ต้องสร้าง service ที่อ่าน/เขียน `memory/items.json`
โดยใช้ `App.ProjectService.GetProjectPath(projectKey)` เพื่อหา path

---

### 3.5 RequestBuilderView — ส่ง Request ไป LLM

**ปัจจุบัน:** Demo response + hardcoded file I/O
**เปลี่ยนเป็น:** Connector Layer (Cheng) + TraceService (Pong)

#### สิ่งที่ต้องเปลี่ยน:

**A) SubmitRequest_Click() — เรียก Connector แทน demo:**
```csharp
// ❌ เดิม (hardcoded demo response)
RawJsonTextBox.Text = "{ \"id\": \"demo-001\", ... }";

// ✅ ใหม่ (เรียก Connector Layer ของ Cheng)
var projectKey = App.CurrentProjectKey;

// 1. ตรวจ Product Key ก่อนส่ง
if (!App.ProductKey.IsFeatureEnabled(projectKey, "send_request"))
{
    MessageBox.Show("Product Key ไม่ valid — ไม่สามารถส่ง request ได้");
    return;
}

// 2. Load API key
var apiKey = App.KeyStore.GetKey(projectKey, selectedProvider);
if (apiKey == null)
{
    MessageBox.Show($"ไม่พบ API key สำหรับ {selectedProvider}");
    return;
}

// 3. Embed attached files
var fileContents = App.FileEmbedder.EmbedMultipleFiles(projectKey, attachedFileIds);

// 4. ส่ง request (Cheng's Connector Layer)
// var response = await connector.SendAsync(requestJson, cancellationToken);

// 5. บันทึก Trace
var trace = new TraceRecord
{
    RequestId = requestId,
    ReferencedFileIds = attachedFileIds,
    InjectedMemoryIds = injectedMemoryIds,
    Timestamp = DateTime.UtcNow
};
App.TraceService.SaveTrace(projectKey, trace);

// 6. Log
App.Logger?.LogRequest(requestId, selectedProvider, response.StatusCode);
```

---

### 3.6 RequestLogView — ดู Request History

**ปัจจุบัน:** อ่าน requests_log.json + demo data
**เปลี่ยนเป็น:** อ่าน traces จาก `App.TraceService`

```csharp
// ✅ ใหม่
var traces = App.TraceService.ListTraces(App.CurrentProjectKey);
foreach (var t in traces)
{
    _logEntries.Add(new LogEntry
    {
        Id = t.RequestId,
        Timestamp = t.Timestamp,
        // อ่าน request/response JSON จาก requests/ folder
    });
}
```

---

### 3.7 TaskTemplatesView — ไม่เปลี่ยนมาก

Template logic เป็นงาน Domain Layer — ไม่เกี่ยวกับ Storage โดยตรง
เปลี่ยนแค่:
- `ApplyToRequestBuilder_Click()` → ใช้ shared state แทน file I/O
- ลบ demo data → โหลดจาก file หรือ hardcode default templates ไว้

---

## 4. Checklist สำหรับ Integration

### Phase 1 — Foundation (ทำก่อน)
- [ ] เพิ่ม `<ProjectReference>` ใน UI.csproj
- [ ] สร้าง Service Locator ใน App.xaml.cs
- [ ] Build ทดสอบว่า compile ผ่าน

### Phase 2 — Project Management
- [ ] ProjectSelectionView → App.ProjectService
- [ ] CreateProjectWindow → App.ProjectService.CreateProject()
- [ ] ลบ demo projects

### Phase 3 — API Keys & Security
- [ ] SettingsAndApiKeysView → App.KeyStore
- [ ] ลบ DPAPI methods → ใช้ App.Encryption
- [ ] เพิ่ม Product Key UI section
- [ ] UI masking: แสดง key เป็น ••••••••XXXX (last 4 chars)

### Phase 4 — File Management
- [ ] FileManagementView → App.FileStore.ImportFileAsync()
- [ ] ลบ ComputeMd5() → SHA-256 อัตโนมัติ
- [ ] Preview → App.FileStore.GetFileContent()
- [ ] ลบ demo files

### Phase 5 — Memory (Kushal)
- [ ] ProjectMemoryView → MemoryService (Kushal สร้าง)
- [ ] ลบ demo memory items

### Phase 6 — Request Builder (Cheng + Ali)
- [ ] RequestBuilderView → Connector Layer
- [ ] เพิ่ม ProductKey check ก่อนส่ง
- [ ] เพิ่ม FileEmbedder สำหรับ attachments
- [ ] เพิ่ม TraceService.SaveTrace() หลังส่ง
- [ ] ลบ demo responses

### Phase 7 — Request Log
- [ ] RequestLogView → TraceService.ListTraces()
- [ ] ลบ demo log entries

### Phase 8 — Final Cleanup
- [ ] ลบ hardcoded LocalApplicationData paths ทั้งหมด
- [ ] ลบ demo data ทั้งหมด
- [ ] Test end-to-end workflow

---

## 5. สิ่งที่ไม่ต้องเปลี่ยน

| View | เหตุผล |
|------|--------|
| `CreateProjectWindow.xaml.cs` | เป็นแค่ dialog เก็บ Name/Description — ไม่มี logic |
| `MemoryItemEditView.xaml.cs` | Empty view — ใช้ XAML binding อย่างเดียว |
| `MainWindow.xaml.cs` (navigation) | Navigation logic ใช้ได้ ไม่ต้องเปลี่ยน |

---

## 6. ข้อควรระวัง

**1. MD5 → SHA-256:** FileManagementView ใช้ MD5 อยู่ หลัง integration จะเป็น SHA-256 อัตโนมัติ (FileStoreService ทำให้) — hash เก่ากับใหม่จะไม่ตรงกัน

**2. File paths เปลี่ยน:** ปัจจุบัน UI เก็บไฟล์ที่ `LocalApplicationData/` แบบ flat file หลัง integration จะเก็บใน `ProjectData/{ProjectKey}/files/` แบบ folder hierarchy

**3. Async:** `FileStoreService.ImportFileAsync()` เป็น async — ต้องใช้ `await` และเพิ่ม `async` ให้ event handler:
```csharp
private async void ImportFile_Click(object sender, RoutedEventArgs e)
{
    var metadata = await App.FileStore.ImportFileAsync(projectKey, filePath);
}
```

**4. Thread Safety:** `AppLogger` ใช้ lock อยู่แล้ว — safe สำหรับ multi-thread

**5. Error Handling:** Core services throw exceptions เมื่อมีปัญหา ควรใส่ try-catch ใน UI:
```csharp
try
{
    var metadata = await App.FileStore.ImportFileAsync(projectKey, path);
}
catch (NotSupportedException ex)
{
    MessageBox.Show($"ไฟล์ประเภทนี้ไม่รองรับ: {ex.Message}");
}
catch (InvalidOperationException ex)
{
    MessageBox.Show($"ไฟล์ใหญ่เกินไป: {ex.Message}");
}
```

---

## 7. ติดต่อ Pong

ถ้าเจอปัญหาหรือต้องการ method เพิ่ม:
- ดู source code: `src/MultiLLMProjectAssistant.Core/`
- ดู tests เป็นตัวอย่างการใช้งาน: `tests/MultiLLMProjectAssistant.Tests/`
- ดู `STORAGE_ARCHITECTURE.md` สำหรับ folder structure

Core services ออกแบบมาให้ใช้ง่าย — สร้าง instance แล้วเรียก method ได้เลย ไม่ต้อง config อะไรเพิ่ม
