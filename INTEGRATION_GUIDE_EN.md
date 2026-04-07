# UI–Core Integration Guide (English)

**For:** Ali Raza (UI Developer) and the whole team
**Written by:** Pong (File & Storage Developer)
**Date:** 2026-03-27

---

## 1. Background — What You Need to Know Before Starting

### 1.1 Current Status of Each Layer

**Core Layer (Pong — Complete):**
- 6 Interfaces, 6 Models, 10 Services
- 81 unit tests — all passing
- Build: 0 errors, 0 warnings
- Pushed to `main`

**UI Layer (Ali — Prototype):**
- 9 Views + MainWindow (functional, but using demo/hardcoded data)
- All logic is embedded in code-behind (no Core service calls)
- Uses MD5 instead of SHA-256, and DPAPI instead of AES-256 (see Section 6 for why these must change)
- File I/O uses hardcoded paths to `LocalApplicationData`

**Connector Layer (Cheng — In Progress):**
- Must implement `ILLMConnector`, `ChatGPTConnector`, `GeminiConnector`

### 1.2 Goal of Integration

Replace the UI's "demo data + hardcoded logic" with real Core service calls.
After integration, the data flow becomes:

```
User → UI View → Core Service → JSON file / keys.enc / logs
               → Connector Layer → HTTP → LLM Provider
```

### 1.3 Build Commands

```bash
# Build Core only (macOS or Windows)
dotnet build src/MultiLLMProjectAssistant.Core

# Build full Solution (Windows only — WPF requires Windows)
dotnet build Multi-LLM-Project-Assistant.sln

# Run tests
dotnet test tests/MultiLLMProjectAssistant.Tests
```

---

## 2. Core Services — Quick Reference

### 2.1 How to Create Instances (Order Matters)

```csharp
// Step 1: Create base services (no dependencies)
var encryption = new EncryptionService();         // AES-256, device-bound
var projectService = new ProjectService();        // default: LocalApplicationData/MultiLLMProjectAssistant/ProjectData

// Step 2: Create services that depend on projectService
var fileStore   = new FileStoreService(projectService);
var keyStore    = new KeyStore(encryption, projectService);
var productKey  = new ProductKeyService(encryption, projectService);
var logger      = new AppLogger(projectService.GetProjectPath(currentProjectKey));
var trace       = new TraceService(projectService);

// Step 3: Create services that depend on other services
var embedder    = new FileEmbedder(fileStore);
var export      = new ExportService(projectService, fileStore);
```

### 2.2 Service Summary

| Service | Purpose | Key Methods |
|---------|---------|-------------|
| `ProjectService` | Create/open/close projects | `CreateProject(name, desc)`, `OpenProject(key)`, `CloseProject(key)`, `ListProjects()`, `DeleteProject(key)` |
| `EncryptionService` | Encrypt/decrypt data | `Encrypt(plainText)` → byte[], `Decrypt(cipherData)` → string |
| `KeyStore` | Manage LLM API keys | `SaveKey(projKey, provider, apiKey)`, `GetKey(projKey, provider)`, `DeleteKey(projKey, provider)`, `ListProviders(projKey)` |
| `ProductKeyService` | Manage Product Key (licensing) | `SaveProductKey(projKey, productKey)`, `ValidateProductKey(projKey)`, `GetStatus(projKey)`, `IsFeatureEnabled(projKey, feature)` |
| `FileStoreService` | Import and manage files | `ImportFileAsync(projKey, path)`, `GetFileList(projKey)`, `GetFileContent(projKey, fileId)`, `DeleteFile(projKey, fileId)` |
| `FileEmbedder` | Convert fileId to embeddable content | `EmbedFileContent(projKey, fileId, maxChars)`, `EmbedMultipleFiles(projKey, fileIds)` |
| `RedactionFilter` | Strip API keys from text | `RedactionFilter.Redact(text)` (static method) |
| `AppLogger` | Structured logging | `Log(level, message, requestId?)`, `LogRequest(requestId, provider, statusCode)` |
| `TraceService` | Per-request traceability | `SaveTrace(projKey, traceRecord)`, `LoadTrace(projKey, reqId)`, `ListTraces(projKey)` |
| `ExportService` | Export project data | `ExportProjectSummary(projKey)`, `ExportAllToFolder(projKey, outputPath)` |

---

## 3. View-by-View Integration — Step-by-Step

### Step 0: Add Project Reference

Edit `src/MultiLLMProjectAssistant.UI/MultiLLMProjectAssistant.UI.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <!-- ADD THIS -->
  <ItemGroup>
    <ProjectReference Include="..\MultiLLMProjectAssistant.Core\MultiLLMProjectAssistant.Core.csproj" />
  </ItemGroup>
</Project>
```

### Step 0.5: Create a Service Locator in App.xaml.cs

This ensures all Views share the same service instances:

```csharp
using MultiLLMProjectAssistant.Core.Services;
using MultiLLMProjectAssistant.Core.Interfaces;

namespace MultiLLMProjectAssistant.UI;

public partial class App : Application
{
    // Shared services — created once, used across the entire app
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

### 3.1 ProjectSelectionView — Open / Create Projects

**Currently:** Reads/writes its own `projects.json` + demo data
**Replace with:** `App.ProjectService`

#### What to change:

**A) LoadProjects() — Replace File.ReadAllText with ProjectService:**
```csharp
// ❌ OLD (hardcoded file I/O)
var json = File.ReadAllText(_projectsPath);
var store = JsonSerializer.Deserialize<ProjectStore>(json);

// ✅ NEW
var projects = App.ProjectService.ListProjects();
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

**B) CreateNewProject_Click() — Use ProjectService.CreateProject():**
```csharp
// ❌ OLD
var item = new ProjectItem { Id = Guid.NewGuid().ToString("N"), Name = dlg.ProjectName, ... };
_projects.Add(item);
SaveProjects();

// ✅ NEW
var info = App.ProjectService.CreateProject(dlg.ProjectName, dlg.ProjectDescription);
App.SetCurrentProject(info.ProjectKey);  // set current project
// Refresh list
```

**C) ProjectsListBox_SelectionChanged() — Open project:**
```csharp
// ✅ NEW
if (selectedItem is ProjectItem item)
{
    var info = App.ProjectService.OpenProject(item.Id);
    App.SetCurrentProject(item.Id);
}
```

**D) Delete SeedDemo() entirely** — not needed, `ProjectService.ListProjects()` returns real data.

---

### 3.2 FileManagementView — Import / View Files

**Currently:** Uses MD5, demo data, no real import
**Replace with:** `App.FileStore`

#### What to change:

**A) AddFiles() — Replace ComputeMd5 with FileStoreService.ImportFileAsync():**
```csharp
// ❌ OLD (MD5 + manual FileItem creation)
var md5 = ComputeMd5(filePath);
var item = new FileItem { Path = filePath, Name = name, Md5 = md5, ... };

// ✅ NEW
var projectKey = App.CurrentProjectKey;
var metadata = await App.FileStore.ImportFileAsync(projectKey, filePath);
var item = new FileItem
{
    Path = filePath,
    Name = metadata.OriginalName,
    Type = metadata.FileType,
    SizeBytes = metadata.FileSizeBytes,
    Timestamp = metadata.ImportedAt,
    Hash = metadata.Sha256Hash,  // renamed from "Md5" — now SHA-256
    IsAttached = false
};
```

**B) Delete the ComputeMd5() method entirely** — FileStoreService computes SHA-256 automatically.

**C) BuildPreview() — Use FileStore.GetFileContent():**
```csharp
// ❌ OLD
var text = File.ReadAllText(item.Path);
if (text.Length > 6000) text = text[..6000] + "\n... [truncated]";

// ✅ NEW
try
{
    var content = App.FileStore.GetFileContent(App.CurrentProjectKey, fileId);
    PreviewTextBox.Text = content;
}
catch (NotSupportedException)
{
    PreviewTextBox.Text = "[Binary file — metadata only]";
}
```

**D) Delete SeedDemoFiles()** — Load from FileStore instead:
```csharp
var files = App.FileStore.GetFileList(App.CurrentProjectKey);
foreach (var f in files)
{
    _files.Add(new FileItem { Name = f.OriginalName, Type = f.FileType, ... });
}
```

---

### 3.3 SettingsAndApiKeysView — Manage API Keys

**Currently:** Uses DPAPI + hardcoded `settings.json`
**Replace with:** `App.KeyStore` + `App.ProductKey`

#### What to change:

**A) Delete `EncryptDpapiToBase64()` and `DecryptDpapiFromBase64()` — both methods.**

**B) SaveKey_Click() — Use KeyStore:**
```csharp
// ❌ OLD
var encrypted = EncryptDpapiToBase64(keyValue);
item.EncryptedValue = encrypted;
SaveSettings();

// ✅ NEW
var projectKey = App.CurrentProjectKey;
App.KeyStore.SaveKey(projectKey, provider, keyValue);
App.Logger?.Log("INFO", $"API key updated for {provider}");
```

**C) LoadSettings() — Load keys from KeyStore:**
```csharp
// ✅ NEW
var projectKey = App.CurrentProjectKey;
var providers = App.KeyStore.ListProviders(projectKey);
foreach (var provider in providers)
{
    _apiKeys.Add(new ApiKeyItem
    {
        Provider = provider,
        Masked = "••••••••••••XXXX",  // no decryption needed for display
        UpdatedAt = DateTime.UtcNow
    });
}
```

**D) Add Product Key section:**
```csharp
// Save Product Key
App.ProductKey.SaveProductKey(App.CurrentProjectKey, productKeyTextBox.Text);

// Validate and display status
var status = App.ProductKey.GetStatus(App.CurrentProjectKey);
ProductKeyStatusText.Text = status.IsValid
    ? $"Valid ({status.KeyType}) — {status.Message}"
    : $"Invalid — {status.Message}";
```

---

### 3.4 ProjectMemoryView — CRUD Memory Items

**Currently:** In-memory only + demo data, no persistence
**Replace with:** Kushal's MemoryService (reads/writes `memory/items.json`)

**Note:** The `MemoryNote` model is already available:
`src/MultiLLMProjectAssistant.Core/Models/MemoryNote.cs`

Fields: `Id`, `Title`, `Content`, `Tags` (List\<string\>), `CreatedAt`, `UpdatedAt`

Kushal needs to create a service that reads/writes `memory/items.json`,
using `App.ProjectService.GetProjectPath(projectKey)` to find the correct path.

---

### 3.5 RequestBuilderView — Send Requests to LLM

**Currently:** Demo response + hardcoded file I/O
**Replace with:** Connector Layer (Cheng) + TraceService (Pong)

#### What to change:

**A) SubmitRequest_Click() — Call Connector instead of demo:**
```csharp
// ❌ OLD (hardcoded demo response)
RawJsonTextBox.Text = "{ \"id\": \"demo-001\", ... }";

// ✅ NEW
var projectKey = App.CurrentProjectKey;

// 1. Check Product Key before sending
if (!App.ProductKey.IsFeatureEnabled(projectKey, "send_request"))
{
    MessageBox.Show("Invalid Product Key — cannot send requests.");
    return;
}

// 2. Load API key for the selected provider
var apiKey = App.KeyStore.GetKey(projectKey, selectedProvider);
if (apiKey == null)
{
    MessageBox.Show($"No API key found for {selectedProvider}");
    return;
}

// 3. Embed attached files into the request
var fileContents = App.FileEmbedder.EmbedMultipleFiles(projectKey, attachedFileIds);

// 4. Send request (Cheng's Connector Layer)
// var response = await connector.SendAsync(requestJson, cancellationToken);

// 5. Save Trace record
var trace = new TraceRecord
{
    RequestId = requestId,
    ReferencedFileIds = attachedFileIds,
    InjectedMemoryIds = injectedMemoryIds,
    Timestamp = DateTime.UtcNow
};
App.TraceService.SaveTrace(projectKey, trace);

// 6. Log the request
App.Logger?.LogRequest(requestId, selectedProvider, response.StatusCode);
```

---

### 3.6 RequestLogView — View Request History

**Currently:** Reads `requests_log.json` + demo data
**Replace with:** `App.TraceService`

```csharp
// ✅ NEW
var traces = App.TraceService.ListTraces(App.CurrentProjectKey);
foreach (var t in traces)
{
    _logEntries.Add(new LogEntry
    {
        Id = t.RequestId,
        Timestamp = t.Timestamp,
        // Read request/response JSON from the requests/ folder
    });
}
```

---

### 3.7 TaskTemplatesView — Minor Changes Only

Template logic belongs to the Domain Layer, not Storage.
Only change:
- `ApplyToRequestBuilder_Click()` → use shared state instead of file I/O
- Delete demo data → either load from a file or keep default templates hardcoded

---

## 4. Integration Checklist

### Phase 1 — Foundation (Do First)
- [ ] Add `<ProjectReference>` in UI.csproj
- [ ] Create Service Locator in App.xaml.cs
- [ ] Test build compiles successfully

### Phase 2 — Project Management
- [ ] ProjectSelectionView → App.ProjectService
- [ ] CreateProjectWindow → App.ProjectService.CreateProject()
- [ ] Remove demo projects

### Phase 3 — API Keys & Security
- [ ] SettingsAndApiKeysView → App.KeyStore
- [ ] Remove DPAPI methods → use App.Encryption (see Warning #2 below)
- [ ] Add Product Key UI section
- [ ] UI masking: display keys as `••••••••XXXX` (last 4 chars only)

### Phase 4 — File Management
- [ ] FileManagementView → App.FileStore.ImportFileAsync()
- [ ] Remove ComputeMd5() → SHA-256 is automatic (see Warning #1 below)
- [ ] Preview → App.FileStore.GetFileContent()
- [ ] Remove demo files

### Phase 5 — Memory (Kushal)
- [ ] ProjectMemoryView → MemoryService (Kushal to create)
- [ ] Remove demo memory items

### Phase 6 — Request Builder (Cheng + Ali)
- [ ] RequestBuilderView → Connector Layer
- [ ] Add ProductKey check before sending
- [ ] Add FileEmbedder for attachments
- [ ] Add TraceService.SaveTrace() after each request
- [ ] Remove demo responses

### Phase 7 — Request Log
- [ ] RequestLogView → TraceService.ListTraces()
- [ ] Remove demo log entries

### Phase 8 — Final Cleanup
- [ ] Remove all hardcoded `LocalApplicationData` paths
- [ ] Remove all demo/seed data
- [ ] End-to-end test of the full workflow

---

## 5. Views That Don't Need Changes

| View | Reason |
|------|--------|
| `CreateProjectWindow.xaml.cs` | Simple dialog for Name/Description — no logic to replace |
| `MemoryItemEditView.xaml.cs` | Empty view — XAML binding only |
| `MainWindow.xaml.cs` (navigation) | Navigation logic works as-is |

---

## 6. Important Warnings — Why These Changes Are Required

### Warning 1: MD5 Must Be Replaced with SHA-256

**What's happening now:**
`FileManagementView.xaml.cs` contains a `ComputeMd5()` method that uses `System.Security.Cryptography.MD5` to hash imported files.

**Why it must change:**

1. **Design Report requirement (FR-S3):** The System Analysis and Design Report explicitly specifies SHA-256 as the hashing algorithm for file integrity verification. Using MD5 violates the approved design.

2. **MD5 is cryptographically broken:** MD5 has known collision vulnerabilities — two different files can produce the same hash. This was demonstrated as far back as 2004. In a project that deals with file integrity and traceability, relying on MD5 means you cannot guarantee that a file hasn't been tampered with.

3. **Industry standard:** SHA-256 is the current industry standard for file integrity checks. It produces a 256-bit hash (vs MD5's 128-bit) and has no known practical collision attacks.

**What to do:**
Simply delete the `ComputeMd5()` method. After integration, `FileStoreService.ImportFileAsync()` automatically computes SHA-256 for every imported file and stores it in the `FileMetadata.Sha256Hash` property. No extra code needed.

**Note:** Old hashes (from demo data) and new hashes will not match, since they use completely different algorithms. This is expected — the demo data should be removed during integration anyway.

---

### Warning 2: DPAPI Must Be Replaced with AES-256-CBC

**What's happening now:**
`SettingsAndApiKeysView.xaml.cs` contains two methods — `EncryptDpapiToBase64()` and `DecryptDpapiFromBase64()` — that use `System.Security.Cryptography.ProtectedData` (DPAPI) to encrypt/decrypt API keys.

**Why it must change:**

1. **Cross-platform development constraint:** Pong (the Storage developer) works on macOS, not Windows. DPAPI (`ProtectedData` class) is a **Windows-only API** — it literally does not exist on macOS or Linux. This means the Core layer cannot use DPAPI, or it would be impossible to build and test on Pong's machine.

2. **Design Report decision:** The Design Report (Section 4.3) originally specified "DPAPI as primary, AES-256 as fallback." However, since the Core layer must be buildable on all platforms (for development and testing), the team agreed to use AES-256-CBC + PBKDF2 as the single encryption method. This is documented as an accepted deviation.

3. **AES-256 is equally secure (or stronger for this use case):**
   - DPAPI ties encryption to the Windows user account — if the user profile is lost, so are the keys. There's no way to migrate or back up the encryption.
   - AES-256-CBC with PBKDF2-SHA256 (100,000 iterations) provides strong, industry-standard encryption that works identically on any platform.
   - The `EncryptionService` in Core generates a unique salt and IV for each encryption operation, and derives the key using PBKDF2 with a device-bound passphrase.

4. **Consistency:** Having two different encryption mechanisms (DPAPI in UI, AES-256 in Core) would mean keys encrypted by the UI couldn't be decrypted by Core services, and vice versa. Integration requires a single encryption path.

**What to do:**
Delete both `EncryptDpapiToBase64()` and `DecryptDpapiFromBase64()`. After integration, all encryption goes through `App.KeyStore.SaveKey()` and `App.KeyStore.GetKey()`, which internally use `EncryptionService` (AES-256-CBC). The UI never touches raw encryption — it simply calls `SaveKey` / `GetKey`.

---

### Warning 3: File Paths Will Change

**Currently:** UI stores files as flat files in `LocalApplicationData/`
**After integration:** Files are stored in `ProjectData/{ProjectKey}/files/` with a structured folder hierarchy.

All hardcoded paths must be removed. Use `App.ProjectService.GetProjectPath(projectKey)` to get the correct base path for any project.

---

### Warning 4: Async Methods

`FileStoreService.ImportFileAsync()` is async — you must use `await` and mark event handlers as `async`:

```csharp
private async void ImportFile_Click(object sender, RoutedEventArgs e)
{
    var metadata = await App.FileStore.ImportFileAsync(projectKey, filePath);
}
```

---

### Warning 5: Thread Safety

`AppLogger` already uses internal locking — it is safe for multi-threaded use from the UI thread.

---

### Warning 6: Error Handling

Core services throw exceptions when something goes wrong. Always wrap service calls in try-catch in the UI:

```csharp
try
{
    var metadata = await App.FileStore.ImportFileAsync(projectKey, path);
}
catch (NotSupportedException ex)
{
    MessageBox.Show($"File type not supported: {ex.Message}");
}
catch (InvalidOperationException ex)
{
    MessageBox.Show($"File too large: {ex.Message}");
}
```

---

## 7. Need Help?

If you run into issues or need additional methods:
- **Source code:** `src/MultiLLMProjectAssistant.Core/`
- **Tests as usage examples:** `tests/MultiLLMProjectAssistant.Tests/`
- **Folder structure docs:** `STORAGE_ARCHITECTURE.md`

Core services are designed to be simple to use — create an instance and call methods directly. No extra configuration needed.
