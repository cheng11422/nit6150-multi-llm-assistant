# MultiLLMProjectAssistant.Core — Complete Test Report

**Project:** Multi-LLM Project Assistant (NIT6150 Advanced Project)
**Module:** Storage Layer (`MultiLLMProjectAssistant.Core`)
**Developer:** Pong (Pongporn Takham)
**Date:** 2026-03-27
**Framework:** .NET 8.0 | xUnit 2.6.6 | Moq 4.20.70

---

## Executive Summary

| Metric | Value |
|--------|-------|
| Total Test Cases | **70** |
| Passed | **70** |
| Failed | **0** |
| Skipped | **0** |
| Build Warnings | **0** |
| Build Errors | **0** |
| Total Execution Time | **1.50 seconds** |
| Test Files | **10** |
| Services Tested | **9** |
| Interfaces Defined | **5** |
| Models Defined | **5** |

---

## Architecture Overview

```
MultiLLMProjectAssistant.Core/
├── Interfaces/           5 interfaces define the public contracts
│   ├── IProjectService.cs
│   ├── IFileStore.cs
│   ├── IEncryptionService.cs
│   ├── IKeyStore.cs
│   └── IAppLogger.cs
├── Models/               5 models define the data structures
│   ├── ProjectInfo.cs
│   ├── FileMetadata.cs
│   ├── MemoryNote.cs
│   ├── TraceRecord.cs
│   └── LogEntry.cs
└── Services/             9 services implement all business logic
    ├── ProjectService.cs
    ├── EncryptionService.cs
    ├── RedactionFilter.cs
    ├── KeyStore.cs
    ├── AppLogger.cs
    ├── FileStoreService.cs
    ├── FileEmbedder.cs
    ├── TraceService.cs
    └── ExportService.cs
```

### Class Dependency Flow

```
IProjectService ──→ ProjectService
                        │
         ┌──────────────┼──────────────┬───────────────┐
         ▼              ▼              ▼               ▼
    IFileStore     IKeyStore      IAppLogger     ExportService
         │              │              │               │
    FileStoreService  KeyStore     AppLogger     (uses RedactionFilter
         │              │              │          + IFileStore)
         │         IEncryptionService  │
         ▼              │              ▼
    FileEmbedder   EncryptionService  RedactionFilter (static)
         │
         ▼
    TraceService
```

---

## Test Results by Service

---

### 1. ProjectService (13 tests) — Project Lifecycle Management

**Purpose:** Creates, opens, closes, lists, and deletes user projects. Each project gets a unique 32-character key and a standardised folder hierarchy.

**Constructor:** `ProjectService(string? basePath = null)` — accepts injectable base path for testing.

#### Methods and How They Work

**`CreateProject(string projectName, string description) → ProjectInfo`**
1. Validates projectName is not empty/whitespace and not longer than 100 characters
2. Generates a unique `ProjectKey` via `Guid.NewGuid().ToString("N")` (32 hex chars, no dashes)
3. Creates the folder hierarchy under `{BasePath}/{ProjectKey}/`:
   - `project.json` — serialised ProjectInfo
   - `keys.enc` — empty placeholder for encrypted API keys
   - `files/index.json` — empty array `[]`
   - `memory/items.json` — empty array `[]`
   - `requests/` — empty directory
   - `logs/` — empty directory
4. Returns the populated `ProjectInfo` object

**`OpenProject(string projectKey) → ProjectInfo`**
1. Checks project directory exists (throws `DirectoryNotFoundException` if not)
2. Reads and deserialises `project.json`
3. Updates `LastOpenedAt` to `DateTime.UtcNow`
4. Writes updated `project.json` back to disk
5. Returns the updated `ProjectInfo`

**`CloseProject(string projectKey) → void`**
1. Same as OpenProject but returns void — updates `LastOpenedAt` timestamp

**`ListProjects() → List<ProjectInfo>`**
1. Scans all subdirectories under `BasePath`
2. Reads `project.json` from each valid subdirectory
3. Returns list sorted by `LastOpenedAt` descending (most recent first)

**`DeleteProject(string projectKey) → bool`**
1. Checks if project directory exists
2. Recursively deletes the entire directory
3. Returns `true` if deleted, `false` if not found

**`GetProjectPath(string projectKey) → string`**
1. Returns `Path.Combine(BasePath, projectKey)`

#### Test Results

| # | Test Name | What It Verifies | Result | Time |
|---|-----------|-----------------|--------|------|
| 1 | `CreateProject_ShouldCreateFolderHierarchyAndFiles` | All 4 subdirectories (files/, memory/, requests/, logs/) and all placeholder files (project.json, keys.enc, index.json, items.json) are created with correct initial content | PASSED | 16ms |
| 2 | `CreateProject_ShouldReturnValidProjectInfo` | Returned ProjectInfo has correct name, description, 32-char key, and non-default timestamps | PASSED | 2ms |
| 3 | `OpenProject_ShouldReturnProjectInfoAndUpdateLastOpened` | After opening, LastOpenedAt is newer than creation time | PASSED | 52ms |
| 4 | `OpenProject_ShouldThrowIfNotFound` | Throws DirectoryNotFoundException for non-existent key | PASSED | <1ms |
| 5 | `ListProjects_ShouldReturnAllProjectsSortedByLastOpened` | Creates 3 projects, verifies returned list is sorted descending by LastOpenedAt | PASSED | 107ms |
| 6 | `ListProjects_ShouldReturnEmptyIfNoProjects` | Returns empty list when BasePath has no projects | PASSED | <1ms |
| 7 | `DeleteProject_ShouldRemoveDirectoryAndReturnTrue` | Directory is removed from disk, returns true | PASSED | 1ms |
| 8 | `DeleteProject_ShouldReturnFalseIfNotFound` | Returns false for non-existent project key | PASSED | <1ms |
| 9 | `CloseProject_ShouldUpdateLastOpenedAt` | LastOpenedAt increases after CloseProject | PASSED | 52ms |
| 10 | `GetProjectPath_ShouldReturnCorrectPath` | Returns `{basePath}/{projectKey}` | PASSED | <1ms |
| 11 | `CreateProject_EmptyName_Throws` | ArgumentException for empty string | PASSED | <1ms |
| 12 | `CreateProject_WhitespaceName_Throws` | ArgumentException for whitespace-only name | PASSED | 4ms |
| 13 | `CreateProject_NameTooLong_Throws` | ArgumentException for name exceeding 100 characters | PASSED | <1ms |

---

### 2. EncryptionService (7 tests) — AES-256-CBC Encryption

**Purpose:** Encrypts and decrypts strings using AES-256-CBC with PBKDF2 key derivation. This is the foundation for securing API keys in `keys.enc`. Fully cross-platform — no DPAPI dependency.

**Constructor:**
- `EncryptionService()` — uses `Environment.MachineName + Environment.UserName` as passphrase
- `EncryptionService(string passphrase)` — accepts explicit passphrase for testing

#### Methods and How They Work

**`Encrypt(string plainText) → byte[]`**
1. Generates 16 random bytes for **salt** (`RandomNumberGenerator.GetBytes`)
2. Generates 16 random bytes for **IV** (Initialisation Vector)
3. Derives a 32-byte (256-bit) encryption key using **PBKDF2**:
   - `Rfc2898DeriveBytes(passphrase, salt, 100000, SHA256)`
4. Creates AES cipher in **CBC mode** with **PKCS7 padding**
5. Encrypts the UTF-8 encoded plaintext
6. Returns concatenated bytes: `[salt 16B][IV 16B][ciphertext]`

**`Decrypt(byte[] cipherData) → string`**
1. Validates minimum length (at least 33 bytes: 16 salt + 16 IV + 1 ciphertext)
2. Extracts salt (bytes 0-15), IV (bytes 16-31), ciphertext (bytes 32+)
3. Derives the same key using PBKDF2 with extracted salt
4. Decrypts using AES-CBC
5. Returns UTF-8 decoded plaintext
6. Throws `CryptographicException` if data is corrupted or wrong passphrase

#### Why This Design Is Secure
- **Random salt per encryption** — same plaintext produces different ciphertext each time
- **Random IV** — prevents pattern analysis across encrypted blocks
- **100,000 PBKDF2 iterations** — makes brute-force attacks computationally expensive
- **AES-256** — military-grade encryption standard
- **No DPAPI** — works on macOS/Linux/Windows without platform lock-in

#### Test Results

| # | Test Name | What It Verifies | Result | Time |
|---|-----------|-----------------|--------|------|
| 1 | `EncryptDecrypt_Roundtrip_ReturnsOriginalText` | Encrypt then decrypt "Hello, World!" returns exact original | PASSED | 124ms |
| 2 | `EncryptDecrypt_EmptyString_ReturnsEmpty` | Empty string survives round-trip | PASSED | 122ms |
| 3 | `EncryptDecrypt_UnicodeString_ReturnsOriginal` | Thai (สวัสดีครับ), emoji, Japanese characters survive round-trip | PASSED | 124ms |
| 4 | `EncryptDecrypt_LongString_ReturnsOriginal` | 10,000 character string survives round-trip | PASSED | 145ms |
| 5 | `Encrypt_SameTextTwice_ProducesDifferentCiphertext` | Two encryptions of "same text" produce different byte arrays (random salt/IV) | PASSED | 122ms |
| 6 | `Decrypt_WrongPassphrase_ThrowsCryptographicException` | Decrypting with wrong passphrase throws CryptographicException | PASSED | 125ms |
| 7 | `Decrypt_CorruptedData_ThrowsCryptographicException` | Flipping a bit in ciphertext throws CryptographicException | PASSED | 119ms |

---

### 3. RedactionFilter (8 tests) — API Key Pattern Removal

**Purpose:** Static utility that strips API key patterns from any text before it is written to logs, exported, or stored in request JSON. This is the primary defence against accidental secret leakage.

#### Method

**`static string Redact(string? text) → string`**
1. Returns empty string if input is null
2. Applies 4 compiled Regex patterns sequentially:

| Provider | Regex Pattern | Example Match |
|----------|--------------|---------------|
| OpenAI | `sk-[a-zA-Z0-9_-]{20,}` | `sk-abc123def456ghi789jkl012mno345pqr678` |
| Gemini | `AIza[a-zA-Z0-9_-]{35}` | `AIzaSyA1B2C3D4E5F6G7H8I9J0KlMnOpQrStUvWxYz12` |
| Grok | `xai-[a-zA-Z0-9_-]{20,}` | `xai-abc123def456ghi789jkl012mno345` |
| Bearer | `Bearer\s+[A-Za-z0-9_-]{20,}` | `Bearer eyJhbGciOiJIUzI1NiJ9...` |

3. Each match is replaced with `[REDACTED]`
4. All regex patterns use `RegexOptions.Compiled` for performance

#### Test Results

| # | Test Name | What It Verifies | Result | Time |
|---|-----------|-----------------|--------|------|
| 1 | `Redact_OpenAIKey_IsRedacted` | `sk-abc123...` is replaced with `[REDACTED]` | PASSED | <1ms |
| 2 | `Redact_GeminiKey_IsRedacted` | `AIzaSy...` is replaced with `[REDACTED]` | PASSED | <1ms |
| 3 | `Redact_GrokKey_IsRedacted` | `xai-abc123...` is replaced with `[REDACTED]` | PASSED | <1ms |
| 4 | `Redact_BearerToken_IsRedacted` | `Bearer eyJhbG...` is replaced with `[REDACTED]` | PASSED | <1ms |
| 5 | `Redact_MultipleKeysInOneString_AllRedacted` | String with 3 different keys — all 3 are redacted | PASSED | <1ms |
| 6 | `Redact_NoKeys_ReturnsUnchanged` | Normal text without keys passes through unchanged | PASSED | <1ms |
| 7 | `Redact_Null_ReturnsEmpty` | Null input returns empty string (no crash) | PASSED | <1ms |
| 8 | `Redact_PartialKeyTooShort_NotMatched` | `sk-abc1234567` (only 10 chars after prefix) is NOT redacted — minimum 20 required | PASSED | <1ms |

---

### 4. KeyStore (10 tests) — Encrypted API Key Management

**Purpose:** Stores and retrieves API keys per project. Keys are kept in `keys.enc` as an encrypted binary file — never as plain text on disk. Only whitelisted providers are accepted.

**Constructor:** `KeyStore(IEncryptionService encryption, IProjectService projectService)`

#### Methods and How They Work

**`SaveKey(string projectKey, string provider, string apiKey) → void`**
1. Validates provider is one of: `chatgpt`, `gemini`, `grok`
2. Validates apiKey is not empty/whitespace
3. Loads existing keys: reads `keys.enc` → decrypt → deserialise to `Dictionary<string, string>`
4. Adds/updates the provider entry
5. Serialises dictionary to JSON → encrypt → write binary to `keys.enc`

**`GetKey(string projectKey, string provider) → string?`**
1. Validates provider name
2. Loads and decrypts `keys.enc`
3. Returns the key value, or `null` if provider not found

**`DeleteKey(string projectKey, string provider) → bool`**
1. Validates provider name
2. Loads keys, removes the entry, re-encrypts and saves
3. Returns `true` if removed, `false` if provider was not in dictionary

**`ListProviders(string projectKey) → List<string>`**
1. Loads and decrypts `keys.enc`
2. Returns list of all provider names that have stored keys

#### Security Verification
The test `KeysEnc_IsBinary_NoPainTextVisible` reads the raw bytes of `keys.enc` and converts to UTF-8 string, then asserts that neither the API key nor the provider name appears in the raw data — confirming the file is truly encrypted binary.

#### Test Results

| # | Test Name | What It Verifies | Result | Time |
|---|-----------|-----------------|--------|------|
| 1 | `SaveAndGetKey_ReturnsCorrectValue` | Save key for "chatgpt", get it back — exact match | PASSED | 159ms |
| 2 | `GetKey_NoKeySaved_ReturnsNull` | Getting a key that was never saved returns null | PASSED | 1ms |
| 3 | `DeleteKey_ExistingKey_ReturnsTrueAndRemoves` | Delete returns true, subsequent get returns null | PASSED | 239ms |
| 4 | `DeleteKey_NoKeySaved_ReturnsFalse` | Delete for provider with no stored key returns false | PASSED | 1ms |
| 5 | `ListProviders_ReturnsAllSavedProviders` | After saving 3 providers, list returns all 3 names | PASSED | 373ms |
| 6 | `SaveKey_OverwriteExisting_UpdatesValue` | Saving same provider twice keeps latest value | PASSED | 290ms |
| 7 | `SaveKey_InvalidProvider_Throws` | Provider "invalid_provider" throws ArgumentException | PASSED | 1ms |
| 8 | `SaveKey_EmptyApiKey_Throws` | Empty API key throws ArgumentException | PASSED | 1ms |
| 9 | `GetKey_InvalidProvider_Throws` | Provider "openai" (not in whitelist) throws ArgumentException | PASSED | 1ms |
| 10 | `KeysEnc_IsBinary_NoPainTextVisible` | Raw bytes of keys.enc contain no readable key text or provider names | PASSED | 62ms |

---

### 5. AppLogger (6 tests) — Structured Logging with Redaction

**Purpose:** Writes structured log entries to `{projectPath}/logs/app.log`. Every message passes through RedactionFilter before writing, ensuring no API keys ever appear in log files. Thread-safe with lock mechanism.

**Constructor:** `AppLogger(string projectPath)` — creates `logs/` directory if needed.

#### Methods and How They Work

**`Log(string level, string message, string? requestId = null) → void`**
1. Calls `RedactionFilter.Redact(message)` to remove any secrets
2. Formats: `[2026-03-27 14:30:00 UTC] [INFO] message`
3. If requestId provided: `[2026-03-27 14:30:00 UTC] [INFO] [REQ-001] message`
4. Appends line to `app.log` inside a `lock` block for thread safety

**`LogRequest(string requestId, string provider, int statusCode) → void`**
1. Determines level: `INFO` if 200-299, `ERROR` otherwise
2. Formats message: `"Request {requestId} to {provider} returned {statusCode}"`
3. Delegates to `Log()` with the requestId

#### Test Results

| # | Test Name | What It Verifies | Result | Time |
|---|-----------|-----------------|--------|------|
| 1 | `Log_CreatesFileAndWrites` | app.log file is created; contains [INFO], message text, and UTC timestamp | PASSED | <1ms |
| 2 | `Log_AppendsNotOverwrites` | Two log calls produce two lines (not one) | PASSED | 2ms |
| 3 | `Log_RedactsApiKeys` | Message containing `sk-abc123...` has the key replaced with `[REDACTED]` in the log file | PASSED | 12ms |
| 4 | `LogRequest_FormatsCorrectly` | Log output contains [INFO], [REQ-001], and the formatted request message | PASSED | <1ms |
| 5 | `LogRequest_ErrorStatusCode_LogsAsError` | Status code 500 produces [ERROR] level | PASSED | <1ms |
| 6 | `Log_WithRequestId_IncludesIdInOutput` | Request ID appears in brackets in the log line | PASSED | <1ms |

---

### 6. FileStoreService (11 tests) — File Import and Metadata Management

**Purpose:** Imports external files into a project, computes SHA-256 hashes for integrity verification, and manages file metadata through `files/index.json`. Enforces file size limits and extension whitelist.

**Constructor:** `FileStoreService(IProjectService projectService, long maxFileSizeBytes = 10MB)`

#### Methods and How They Work

**`ImportFileAsync(string projectKey, string sourceFilePath) → Task<FileMetadata>`**
1. Validates source file exists (throws `FileNotFoundException`)
2. Validates file size does not exceed `maxFileSizeBytes` (throws `InvalidOperationException`)
3. Validates file extension is in the allowed whitelist (throws `NotSupportedException`)
4. Reads `index.json` to determine next FileId (F001, F002, ...)
5. Copies file to `{projectPath}/files/{FileId}_{originalName}`
6. Resolves duplicate filenames by appending `_2`, `_3` suffixes
7. Computes **SHA-256 hash** of the stored file (lowercase hex, 64 characters)
8. Creates `FileMetadata` record with all properties
9. Appends to `index.json` and saves
10. Returns the `FileMetadata` object

**`GetFileList(string projectKey) → List<FileMetadata>`**
1. Reads and deserialises `index.json`

**`GetFileContent(string projectKey, string fileId) → string`**
1. Looks up metadata by FileId in index
2. Validates extension is a supported text type (16 supported extensions)
3. Throws `NotSupportedException` for binary files (.png, .pdf, etc.)
4. Reads and returns file content as string

**`GetFileById(string projectKey, string fileId) → FileMetadata?`**
1. Searches index for matching FileId
2. Returns null if not found

**`DeleteFile(string projectKey, string fileId) → bool`**
1. Finds file in index, deletes from disk and removes from index.json
2. Returns true if deleted, false if not found

#### Supported Text Extensions
`.cs`, `.md`, `.txt`, `.json`, `.xml`, `.html`, `.css`, `.js`, `.py`, `.java`, `.cpp`, `.h`, `.log`, `.csv`, `.yaml`, `.yml`

#### Allowed Import Extensions (whitelist)
All text extensions above, plus: `.png`, `.jpg`, `.jpeg`, `.gif`, `.bmp`, `.svg`, `.pdf`, `.doc`, `.docx`, `.xls`, `.xlsx`, `.pptx`, `.zip`, `.tar`, `.gz`

#### Test Results

| # | Test Name | What It Verifies | Result | Time |
|---|-----------|-----------------|--------|------|
| 1 | `ImportFileAsync_CreatesCopyAndMetadata` | File is copied to files/ with F001 prefix; metadata has correct name, extension, size | PASSED | 42ms |
| 2 | `ImportFileAsync_GeneratesCorrectSha256` | Hash is 64 chars, matches independently computed SHA-256 of the stored file | PASSED | 6ms |
| 3 | `ImportFileAsync_IncrementsFileId` | Three imports produce F001, F002, F003 sequentially | PASSED | 4ms |
| 4 | `GetFileList_ReturnsAll` | After 2 imports, list returns 2 entries | PASSED | 3ms |
| 5 | `GetFileContent_ReadsText` | Content of imported .txt file matches original | PASSED | 1ms |
| 6 | `GetFileContent_BinaryExtension_Throws` | .png file throws NotSupportedException | PASSED | 2ms |
| 7 | `DeleteFile_RemovesFromDiskAndIndex` | File removed from disk; index.json becomes empty | PASSED | 4ms |
| 8 | `ImportFileAsync_NonexistentFile_Throws` | Non-existent path throws FileNotFoundException | PASSED | 1ms |
| 9 | `GetFileById_NotFound_ReturnsNull` | F999 returns null | PASSED | 1ms |
| 10 | `ImportFileAsync_FileTooLarge_Throws` | File exceeding maxFileSizeBytes throws InvalidOperationException | PASSED | 3ms |
| 11 | `ImportFileAsync_DisallowedExtension_Throws` | .exe file throws NotSupportedException | PASSED | 3ms |

---

### 7. FileEmbedder (4 tests) — LLM Request Preparation

**Purpose:** Converts imported files into formatted text blocks ready for injection into LLM API requests. Supports truncation for large files and metadata-only summaries.

**Constructor:** `FileEmbedder(IFileStore fileStore)`

#### Methods and How They Work

**`EmbedFileContent(string projectKey, string fileId, int maxChars = 6000) → string`**
1. Retrieves file metadata and content via IFileStore
2. If content exceeds `maxChars`, truncates and appends: `\n... [truncated, {totalChars} chars total]`
3. Wraps in delimiters:
```
--- File: {originalName} (ID: {fileId}) ---
{content}
--- End of File ---
```

**`EmbedMultipleFiles(string projectKey, List<string> fileIds, int maxCharsPerFile = 6000) → string`**
1. Calls `EmbedFileContent` for each file
2. Joins results with `\n\n` separator

**`GetAttachmentSummary(string projectKey, string fileId) → string`**
1. Returns: `"{originalName} ({fileType}, {fileSizeBytes} bytes, SHA-256: {hash})"`
2. Used when sending file metadata without full content

#### Test Results

| # | Test Name | What It Verifies | Result | Time |
|---|-----------|-----------------|--------|------|
| 1 | `EmbedFileContent_ShortFile_NoTruncation` | Short content appears in full with proper delimiters; no truncation marker | PASSED | 27ms |
| 2 | `EmbedFileContent_LongFile_Truncated` | 10,000 char file with maxChars=100 shows truncation marker with original char count | PASSED | 4ms |
| 3 | `EmbedMultipleFiles_CombinesWithSeparator` | Two files appear with their respective names and content in one combined string | PASSED | 7ms |
| 4 | `GetAttachmentSummary_CorrectFormat` | Output contains filename, extension, byte count, and SHA-256 hash | PASSED | 2ms |

---

### 8. TraceService (3 tests) — Per-Request Traceability

**Purpose:** Records which files and memory notes were referenced in each LLM request. Creates `{RequestId}_trace.json` files for audit trail and debugging.

**Constructor:** `TraceService(IProjectService projectService)`

#### Methods and How They Work

**`SaveTrace(string projectKey, TraceRecord traceRecord) → void`**
1. Creates `requests/` directory if needed
2. Serialises TraceRecord to indented JSON
3. Writes to `{projectPath}/requests/{RequestId}_trace.json`

**`LoadTrace(string projectKey, string requestId) → TraceRecord?`**
1. Checks if trace file exists
2. Returns deserialised TraceRecord, or null if not found

**`ListTraces(string projectKey) → List<TraceRecord>`**
1. Scans `requests/` for all `*_trace.json` files
2. Deserialises each one
3. Returns sorted by `Timestamp` descending (newest first)

#### Test Results

| # | Test Name | What It Verifies | Result | Time |
|---|-----------|-----------------|--------|------|
| 1 | `SaveAndLoadTrace_RoundTrip` | Save trace with 2 file IDs and 1 memory ID; load returns exact same data | PASSED | 6ms |
| 2 | `LoadTrace_Nonexistent_ReturnsNull` | Loading REQ-999 returns null without crashing | PASSED | 1ms |
| 3 | `ListTraces_SortedByTimestampDescending` | 3 traces with different dates are returned newest-first | PASSED | 26ms |

---

### 9. ExportService (6 tests) — Sanitised Data Export

**Purpose:** Exports project data for sharing or archival. All exported content passes through RedactionFilter. The `keys.enc` file is **never** included in exports.

**Constructor:** `ExportService(IProjectService projectService, IFileStore fileStore)`

#### Methods and How They Work

**`ExportProjectSummary(string projectKey) → string`**
1. Reads project.json (redacted), counts files, memory notes, and requests
2. Returns formatted text summary with project metadata

**`ExportRequestLog(string projectKey, string requestId) → string`**
1. Reads request JSON, normalised response JSON, and trace file
2. Redacts every file's content via RedactionFilter
3. Returns combined string with section headers
4. Throws FileNotFoundException if no request files exist

**`ExportAllToFolder(string projectKey, string outputPath) → void`**
1. Creates output directory
2. Copies `project.json` (redacted)
3. Copies `files/index.json` (unchanged — contains no secrets)
4. Copies all `requests/*.json` files (redacted)
5. Copies `logs/app.log` (redacted)
6. **NEVER copies `keys.enc`** — this is enforced by design

#### Test Results

| # | Test Name | What It Verifies | Result | Time |
|---|-----------|-----------------|--------|------|
| 1 | `ExportProjectSummary_ReturnsFormattedSummary` | Contains project name, file count (0), memory count (0), request count (0) | PASSED | 1ms |
| 2 | `ExportProjectSummary_WithFiles_ShowsFileCount` | After importing 1 file, summary shows "Files: 1" | PASSED | 26ms |
| 3 | `ExportRequestLog_WithTraceFile_ReturnsSanitisedContent` | Trace file containing `sk-abc123...` is redacted in output | PASSED | 2ms |
| 4 | `ExportRequestLog_NoFiles_Throws` | Non-existent request ID throws FileNotFoundException | PASSED | 1ms |
| 5 | `ExportAllToFolder_CopiesFilesButNotKeysEnc` | project.json, index.json exist in output; keys.enc does NOT; app.log is redacted | PASSED | 23ms |
| 6 | `ExportAllToFolder_NonexistentProject_Throws` | Non-existent project throws DirectoryNotFoundException | PASSED | 2ms |

---

### 10. Integration Tests (2 tests) — End-to-End Verification

**Purpose:** Validates the entire storage layer works together as a cohesive system. Tests both functional correctness and security guarantees.

#### Test: `FullWorkflow_EndToEnd` (255ms)

This test exercises **every service** in a realistic sequence:

```
Step 1: ProjectService.CreateProject("Integration Test")
   → Verify: directory exists, project.json created

Step 2: KeyStore.SaveKey("chatgpt", "sk-...")
         KeyStore.SaveKey("gemini", "AIzaSy...")
   → Verify: keys.enc is binary (no plain text in raw bytes)
   → Verify: GetKey round-trip returns exact original key

Step 3: FileStoreService.ImportFileAsync("spec.md")
         FileStoreService.ImportFileAsync("main.cs")
   → Verify: FileIds are F001, F002
   → Verify: SHA-256 hash is 64 characters
   → Verify: GetFileList returns 2 entries

Step 4: FileEmbedder.EmbedFileContent("F001")
         FileEmbedder.EmbedMultipleFiles(["F001","F002"])
         FileEmbedder.GetAttachmentSummary("F001")
   → Verify: output contains file delimiters, content, SHA-256

Step 5: TraceService.SaveTrace(REQ-001, files=[F001,F002], memory=[MEM-001])
   → Verify: LoadTrace returns matching data

Step 6: AppLogger.Log("WARN", "Key used: sk-...")
         AppLogger.LogRequest("REQ-001", "chatgpt", 200)
   → Verify: app.log contains [INFO], [REQ-001]
   → Verify: app.log does NOT contain "sk-integration..."
   → Verify: app.log DOES contain "[REDACTED]"

Step 7: ExportService.ExportAllToFolder(outputPath)
   → Verify: project.json exists in export
   → Verify: index.json exists in export
   → Verify: app.log exists in export
   → Verify: keys.enc does NOT exist in export
   → Verify: exported app.log has no secrets

Step 8: ProjectService.DeleteProject()
   → Verify: project directory no longer exists on disk
```

**Result: PASSED (255ms)**

---

#### Test: `SecurityAudit_NoSecretsLeaked` (341ms)

Dedicated security verification test:

```
Step 1: Store 3 API keys (OpenAI, Gemini, Grok)

Step 2: Binary inspection of keys.enc
   → Assert: raw bytes do NOT contain "sk-"
   → Assert: raw bytes do NOT contain "AIza"
   → Assert: raw bytes do NOT contain "xai-"
   → Assert: raw bytes do NOT contain "chatgpt"
   → Assert: raw bytes do NOT contain "gemini"
   → Assert: raw bytes do NOT contain "grok"

Step 3: Log all 3 keys + Bearer token to app.log
   → Assert: log file does NOT contain "sk-audit"
   → Assert: log file does NOT contain "AIzaSy"
   → Assert: log file does NOT contain "xai-audit"
   → Assert: log file does NOT contain "Bearer eyJ"
   → Assert: exactly 4 "[REDACTED]" occurrences

Step 4: Redact request JSON containing API key
   → Assert: redacted output has no key
   → Assert: non-secret data ("gpt-4") is preserved

Step 5: Verify .gitignore rules
   → Assert: contains "*.enc"
   → Assert: contains "ProjectData/"
   → Assert: contains "logs/"
```

**Result: PASSED (341ms)**

---

## Security Feature Summary

| Security Control | Implementation | Verified By |
|-----------------|----------------|-------------|
| API keys encrypted at rest | AES-256-CBC + PBKDF2 (100K iterations) in keys.enc | EncryptionServiceTests, KeyStoreTests, IntegrationTests |
| No plain text secrets in keys.enc | Binary format — raw bytes contain no readable text | `KeysEnc_IsBinary_NoPainTextVisible`, `SecurityAudit_NoSecretsLeaked` |
| Log redaction | RedactionFilter.Redact() called before every log write | `Log_RedactsApiKeys`, `SecurityAudit_NoSecretsLeaked` |
| Export redaction | All exported files pass through RedactionFilter | `ExportAllToFolder_CopiesFilesButNotKeysEnc`, `ExportRequestLog_WithTraceFile_ReturnsSanitisedContent` |
| keys.enc excluded from export | ExportAllToFolder never copies keys.enc | `ExportAllToFolder_CopiesFilesButNotKeysEnc` |
| .gitignore rules | *.enc, ProjectData/, logs/ all excluded | `SecurityAudit_NoSecretsLeaked` |
| Provider whitelist | Only chatgpt, gemini, grok accepted | `SaveKey_InvalidProvider_Throws`, `GetKey_InvalidProvider_Throws` |
| File size limit | 10 MB default, configurable | `ImportFileAsync_FileTooLarge_Throws` |
| Extension whitelist | Only approved file types importable | `ImportFileAsync_DisallowedExtension_Throws` |
| No DPAPI dependency | Pure AES-256-CBC — works on macOS/Linux | Verified by security audit scan |
| SHA-256 only (no MD5) | FileStoreService uses SHA256.HashData() | Verified by security audit scan |
| No hardcoded secrets | grep scan of entire source code | Verified by security audit scan |

---

## Complete Test Execution Log

```
Total tests: 70
     Passed: 70
     Failed: 0
    Skipped: 0
 Total time: 1.50 seconds

Test Breakdown:
  ProjectServiceTests ........ 13 passed
  EncryptionServiceTests .....  7 passed
  RedactionFilterTests .......  8 passed
  KeyStoreTests .............. 10 passed
  AppLoggerTests .............  6 passed
  FileStoreServiceTests ...... 11 passed
  FileEmbedderTests ..........  4 passed
  TraceServiceTests ..........  3 passed
  ExportServiceTests .........  6 passed
  IntegrationTests ...........  2 passed
                               ─────────
                               70 passed
```

---

## Conclusion

All 70 test cases pass successfully, covering:
- **Functional correctness** of all 9 services across their complete API surface
- **Security guarantees** including encryption, redaction, export sanitisation, and binary verification
- **Edge cases** including null handling, empty strings, Unicode, large data, invalid inputs, and missing files
- **End-to-end workflow** simulating real user interaction from project creation through export to deletion

The storage layer is production-ready for integration with the UI (Ali), Connectors (Cheng), and Memory/QA (Kushal) modules.
