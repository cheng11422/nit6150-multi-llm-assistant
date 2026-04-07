# Security & Privacy Documentation

**Project:** Multi-LLM Project Assistant (NIT6150 Advanced Project)
**Module:** Storage Layer — `MultiLLMProjectAssistant.Core`
**Author:** Pong (Pongporn Takham) — File & Storage Developer
**Date:** 2026-04-02
**WBS Reference:** 5.7 — Security & Privacy Documentation

---

## 1. Overview

This document describes the security controls and privacy safeguards implemented in the Multi-LLM Project Assistant's Storage Layer. The application handles sensitive data including LLM provider API keys, user-generated content, and request/response logs. Every layer of the storage module is designed to ensure that secrets are encrypted at rest, redacted before logging or export, and excluded from version control.

The security architecture follows three core principles:

- **Defence in depth:** Multiple independent controls protect secrets at different stages (storage, logging, export, version control).
- **Least privilege:** Services only access the data they need; API keys are isolated in encrypted storage and never passed to logging or export functions.
- **Fail-secure:** Missing or corrupted key files return empty results rather than exposing partial data. Decryption with the wrong passphrase throws a `CryptographicException` rather than returning garbage.

---

## 2. Threat Model

The following table identifies the key threats the storage layer defends against, along with the implemented countermeasures.

| # | Threat | Impact | Countermeasure | Implementation |
|---|--------|--------|----------------|----------------|
| T1 | API key exposed in log files | Key compromise, financial loss | Automatic redaction of all log messages | `RedactionFilter.Redact()` called by `AppLogger` before every write |
| T2 | API key exposed in exported artefacts | Key compromise via shared reports | Redaction of all exported content; `keys.enc` excluded from exports | `ExportService` applies `RedactionFilter` to every exported file; `keys.enc` is never copied |
| T3 | API key leaked to Git repository | Public key exposure | `.gitignore` excludes `*.enc`, `ProjectData/`, `logs/` | `.gitignore` rules enforced at repository level |
| T4 | API key read from disk in plain text | Local attacker reads secrets | AES-256-CBC encryption with machine-bound key derivation | `EncryptionService` encrypts `keys.enc`; key derived from machine identity |
| T5 | Brute-force decryption of `keys.enc` | Key compromise | PBKDF2 with 100,000 iterations | `Rfc2898DeriveBytes` with `HashAlgorithmName.SHA256` |
| T6 | API key shown in UI | Shoulder surfing, screenshot leakage | UI masking (last 4 characters only) | UI displays `••••••••XXXX` pattern |
| T7 | Prompt or response body logged | Privacy violation, intellectual property leak | Logger records metadata only (request ID, provider, status code) | `AppLogger.LogRequest()` formats metadata-only messages |
| T8 | Sensitive file content in exports | Unintended data sharing | File content excluded from exports; only metadata index included | `ExportService.ExportAllToFolder()` copies `index.json` but not file content |
| T9 | Invalid provider injection | Unexpected data in key store | Provider whitelist validation | `KeyStore.ValidateProvider()` checks against `{"chatgpt", "gemini", "grok"}` |
| T10 | Oversized file import causing DoS | Memory exhaustion, storage abuse | Configurable file size limit (default 10 MB) | `FileStoreService` validates file size before import |

---

## 3. Encryption at Rest

### 3.1 Algorithm Selection

The application uses **AES-256-CBC** with **PKCS7 padding** for symmetric encryption. This was chosen for the following reasons:

- AES-256 is the industry standard for data-at-rest encryption, approved by NIST (FIPS 197).
- CBC mode provides confidentiality and is widely supported across all .NET platforms.
- PKCS7 padding handles arbitrary-length plaintext.

**Why not DPAPI?** The project requires cross-platform development. Pong develops on macOS while the production target is Windows. DPAPI (`ProtectedData`) is Windows-only (`System.Security.Cryptography.ProtectedData` requires `net8.0-windows`). AES-256-CBC with PBKDF2 provides equivalent security in a cross-platform implementation using only `net8.0` APIs.

### 3.2 Key Derivation

Keys are derived using **PBKDF2** (Password-Based Key Derivation Function 2) via `Rfc2898DeriveBytes`:

| Parameter | Value | Rationale |
|-----------|-------|-----------|
| Password | `Environment.MachineName + Environment.UserName` | Machine-bound; keys encrypted on one machine cannot be decrypted on another |
| Salt | 16 random bytes (per encryption) | Prevents rainbow table attacks; ensures identical plaintext produces different ciphertext |
| Iterations | 100,000 | OWASP recommended minimum for PBKDF2-HMAC-SHA256 (2023 guidance) |
| Hash Algorithm | SHA-256 | Secure, widely supported, no known practical attacks |
| Key Size | 32 bytes (256 bits) | Full AES-256 key length |

### 3.3 Ciphertext Format

The encrypted output uses a simple concatenated binary format:

```
Byte offset:  [0..15]     [16..31]    [32..N]
Content:      Salt         IV          Ciphertext
Size:         16 bytes     16 bytes    Variable
```

- **Salt:** Random 16 bytes generated via `RandomNumberGenerator.GetBytes()` (CSPRNG).
- **IV (Initialisation Vector):** Random 16 bytes generated via `RandomNumberGenerator.GetBytes()`.
- **Ciphertext:** AES-256-CBC encrypted data with PKCS7 padding.

This format is self-contained — the salt and IV are stored alongside the ciphertext, enabling decryption without external metadata.

### 3.4 What Is Encrypted

| Data | File | Encryption |
|------|------|------------|
| LLM provider API keys (ChatGPT, Gemini, Grok) | `keys.enc` | AES-256-CBC via `KeyStore` |
| Product key (licensing) | `keys.enc` | AES-256-CBC via `ProductKeyService` |
| Project metadata | `project.json` | Not encrypted (no secrets) |
| Imported files | `files/` | Not encrypted (user content, not secrets) |
| Memory notes | `memory/items.json` | Not encrypted (no secrets) |
| Request/response logs | `requests/` | Not encrypted; redacted before writing |
| Application log | `logs/app.log` | Not encrypted; redacted before writing |

### 3.5 Encryption Implementation

The `EncryptionService` class (`Services/EncryptionService.cs`) provides two constructors:

- **Default constructor:** Uses `MachineName + UserName` as passphrase (production use).
- **Parameterised constructor:** Accepts a custom passphrase string (testing use — enables unit tests with predictable keys).

Decryption validates data integrity implicitly: if the passphrase is wrong or the ciphertext is corrupted, `Aes.CreateDecryptor().TransformFinalBlock()` throws a `CryptographicException` due to invalid PKCS7 padding. This provides tamper detection without requiring a separate HMAC.

---

## 4. Secret Redaction

### 4.1 RedactionFilter

The `RedactionFilter` is a static class that strips API key patterns from any string. It is applied automatically at two critical points: logging and export.

**Detected patterns:**

| Provider | Regex Pattern | Example Match |
|----------|---------------|---------------|
| OpenAI | `sk-[a-zA-Z0-9_-]{20,}` | `sk-abc123def456ghi789jkl` |
| Google Gemini | `AIza[a-zA-Z0-9_-]{35}` | `AIzaSyA1B2C3D4E5F6G7H8I9J0K1L2M3N4O5P6Q` |
| xAI Grok | `xai-[a-zA-Z0-9_-]{20,}` | `xai-abc123def456ghi789jkl` |
| Bearer Token | `Bearer\s+[A-Za-z0-9_-]{20,}` | `Bearer eyJhbGciOiJIUzI1NiIs...` |

All patterns use `RegexOptions.Compiled` for optimal performance in high-frequency scenarios (e.g., redacting entire log files during export).

**Behaviour:**
- Matching strings are replaced with `[REDACTED]`.
- Multiple patterns in a single string are all replaced.
- `null` input returns `""` (empty string) — fail-safe.
- Patterns require minimum length thresholds (20+ characters) to avoid false positives on short strings.

### 4.2 Points of Redaction

The following diagram shows where redaction is applied in the data flow:

```
User Input
    │
    ▼
  AppLogger.Log()  ──→  RedactionFilter.Redact()  ──→  logs/app.log  (safe)
    │
    ▼
  ExportService.ExportProjectSummary()  ──→  RedactionFilter.Redact()  ──→  exported project.json  (safe)
    │
  ExportService.ExportRequestLog()      ──→  RedactionFilter.Redact()  ──→  exported request JSON  (safe)
    │
  ExportService.ExportAllToFolder()     ──→  RedactionFilter.Redact()  ──→  all exported files  (safe)
                                             keys.enc  ──→  NEVER COPIED
```

### 4.3 Defence-in-Depth for Secrets

A secret (API key) must pass through **four independent barriers** before it could appear in any output:

1. **Storage:** Encrypted in `keys.enc` (AES-256-CBC). Raw binary, not readable.
2. **Logging:** `AppLogger` calls `RedactionFilter.Redact()` on every message before writing to `app.log`. Prompt/response body content is never logged — only metadata (request ID, provider, status code).
3. **Export:** `ExportService` applies `RedactionFilter.Redact()` to every file it copies. `keys.enc` is explicitly excluded (`// keys.enc is NEVER copied`).
4. **Version Control:** `.gitignore` excludes `*.enc`, `ProjectData/`, and `logs/` from the repository.

---

## 5. Key Management

### 5.1 Provider API Keys

Provider API keys are managed by `KeyStore` (`Services/KeyStore.cs`):

- **Storage format:** All keys for a project are stored as a `Dictionary<string, string>` (provider → API key), serialised to JSON, then encrypted as a single blob in `keys.enc`.
- **Provider validation:** Only `"chatgpt"`, `"gemini"`, and `"grok"` are accepted. Any other provider name throws `ArgumentException`.
- **Empty key validation:** Blank or whitespace-only API keys are rejected.
- **Atomic writes:** The entire dictionary is re-encrypted and written on every save, ensuring consistency.

### 5.2 Product Key (Licensing)

The `ProductKeyService` (`Services/ProductKeyService.cs`) implements a simulated licensing system as required by the Project Brief (Section 4.1):

- Product keys are stored alongside provider keys in `keys.enc` under a reserved entry (`__productKey`).
- Two key types are recognised: `EDU-TRIAL-2025` (educational trial) and `PRO-2025-*` (professional).
- Feature gating: `export` and `file_import` are always enabled; `send_request` and `memory_injection` require a valid product key.
- Invalid or missing product keys result in a graceful `ProductKeyStatus` response — no exceptions, no data leakage.

### 5.3 Key Lifecycle

```
Add Key:  UI → KeyStore.SaveKey() → Serialize to JSON → EncryptionService.Encrypt() → Write keys.enc
Read Key: UI → KeyStore.GetKey()  → Read keys.enc → EncryptionService.Decrypt() → Deserialize JSON → Return key
Delete:   UI → KeyStore.DeleteKey() → Load → Remove entry → Re-encrypt → Write keys.enc
```

At no point in this lifecycle does a plaintext API key exist in a file on disk (other than transiently in process memory during the encrypt/decrypt operation).

---

## 6. Logging Security

### 6.1 AppLogger Design

The `AppLogger` (`Services/AppLogger.cs`) enforces security through its design:

- **Mandatory redaction:** Every call to `Log()` passes the message through `RedactionFilter.Redact()` before writing. There is no bypass path.
- **Metadata-only request logging:** `LogRequest()` logs only the request ID, provider name, and HTTP status code — never the prompt text, response body, or API key.
- **Thread safety:** A `lock` object serialises concurrent writes to prevent interleaved or corrupted log entries.

### 6.2 Log Format

```
[2026-03-27 14:30:00 UTC] [INFO] Project created: my-project
[2026-03-27 14:30:05 UTC] [INFO] [REQ-001] Request REQ-001 to chatgpt returned 200
[2026-03-27 14:30:10 UTC] [ERROR] [REQ-002] Request REQ-002 to gemini returned 429
```

Secrets that might accidentally appear in log messages are replaced:

```
Input:   "Using key sk-abc123def456ghi789jklmno for request"
Output:  "Using key [REDACTED] for request"
```

---

## 7. File Integrity

### 7.1 SHA-256 Hashing

Every imported file is hashed with SHA-256 (`SHA256.HashData()`) and the hash is stored in `files/index.json` as the `sha256Hash` field. This provides:

- **Integrity verification:** The hash can detect if a file has been modified after import.
- **Duplicate detection:** Two files with the same content will produce the same hash, regardless of filename.

SHA-256 was chosen over MD5 (which was used in the original UI prototype) because:

- MD5 is cryptographically broken (practical collision attacks exist since 2004).
- SHA-256 is the current industry standard for file integrity, with no known practical attacks.
- The Project Brief's Design Report explicitly requires SHA-256.

### 7.2 File Import Controls

| Control | Implementation |
|---------|----------------|
| Maximum file size | 10 MB (configurable) — prevents storage abuse |
| Extension whitelist | `.cs`, `.md`, `.txt`, `.json`, `.xml`, `.html`, `.css`, `.js`, `.py`, `.java`, `.cpp`, `.h`, `.log`, `.csv`, `.yaml`, `.yml` |
| Duplicate filename handling | Automatic suffix (`_2`, `_3`) to prevent overwrites |
| Sequential file IDs | `F001`, `F002`, ... — predictable, human-readable, not guessable |

---

## 8. Export Security

### 8.1 ExportService Controls

The `ExportService` (`Services/ExportService.cs`) sanitises all exported data:

- **`ExportProjectSummary()`:** Reads `project.json` through `RedactionFilter.Redact()` before extracting fields. Returns only metadata (name, description, creation date, file count, memory count, request count).
- **`ExportRequestLog()`:** Reads request, response, and trace JSON files, all passed through `RedactionFilter.Redact()`.
- **`ExportAllToFolder()`:** Copies sanitised versions of `project.json`, `files/index.json`, all request/response JSON, trace files, and `app.log`. **The `keys.enc` file is explicitly excluded** — this is enforced in code with a comment marker.

### 8.2 What Is Never Exported

| Item | Reason |
|------|--------|
| `keys.enc` | Contains encrypted API keys — no legitimate reason to include in exports |
| Raw API keys | Already encrypted in `keys.enc`; redacted from all other files |
| Imported file content | Only `index.json` (metadata) is exported, not the actual files |

---

## 9. Version Control Security

### 9.1 .gitignore Rules

The repository `.gitignore` prevents sensitive files from being committed:

```
*.enc            # Encrypted key files (keys.enc)
ProjectData/     # All project data (keys, files, logs, memory)
logs/            # Application log files
bin/             # Build output
obj/             # Build intermediary files
.vs/             # Visual Studio settings
*.user           # User-specific settings
.DS_Store        # macOS metadata
```

### 9.2 Source Code Audit

The following checks were performed on the Core source code (verified at build time):

- `grep -r "sk-" src/MultiLLMProjectAssistant.Core/`: No matches outside `RedactionFilter.cs` regex patterns.
- `grep -r "AIza" src/MultiLLMProjectAssistant.Core/`: No matches outside `RedactionFilter.cs` regex patterns.
- `grep -r "xai-" src/MultiLLMProjectAssistant.Core/`: No matches outside `RedactionFilter.cs` regex patterns.
- No hardcoded API keys, passwords, or connection strings in any source file.
- No external NuGet dependencies in the Core project — reduces supply chain risk.

---

## 10. Privacy Considerations

### 10.1 Data Classification

| Data Category | Sensitivity | Storage | Protection |
|---------------|-------------|---------|------------|
| API keys | **High** | `keys.enc` (encrypted binary) | AES-256-CBC + PBKDF2, redaction, .gitignore |
| Product key | **Medium** | `keys.enc` (encrypted binary) | Same as API keys |
| User prompts | **Medium** | `requests/` (JSON files) | Not encrypted; redacted on export; excluded from git via `ProjectData/` |
| LLM responses | **Medium** | `requests/` (JSON files) | Same as user prompts |
| Imported files | **Low–Medium** | `files/` (copies) | Not encrypted; excluded from git via `ProjectData/` |
| Memory notes | **Low** | `memory/items.json` | Not encrypted; excluded from git |
| Application log | **Low** | `logs/app.log` | Redacted; no prompt/response content; excluded from git |

### 10.2 Privacy Risk Mitigations

| Risk | Mitigation |
|------|------------|
| User sends personal data to LLM provider | Application responsibility; out of scope for storage layer. UI should display a warning per Project Brief Section 11. |
| Exported artefacts contain personal data from prompts | `ExportService` redacts secrets but cannot identify personal data in free-text fields. Users must review exports before sharing. |
| Log files accumulate sensitive metadata | `AppLogger` records only request metadata (ID, provider, status code), not prompt or response content. Log files are excluded from git. |
| Imported files contain confidential content | Files are stored locally in `ProjectData/` which is excluded from git. Files are not transmitted to LLMs unless explicitly attached by the user. |
| Machine-bound encryption prevents data portability | By design — API keys encrypted on one machine cannot be decrypted on another. Users must re-enter keys when moving to a new machine. This is a deliberate trade-off favouring security over convenience. |

### 10.3 Data Retention

- Project data persists until the user explicitly calls `ProjectService.DeleteProject()`, which recursively deletes the entire project folder including `keys.enc`, all files, logs, and memory.
- There is no automatic data expiration or cleanup mechanism. This is appropriate for a desktop application where the user has full control over their data.
- Temporary files created during unit testing are placed in OS temp directories and deleted immediately after test completion.

---

## 11. Security Testing Evidence

### 11.1 Unit Test Coverage

| Test File | Tests | Focus |
|-----------|-------|-------|
| `EncryptionServiceTests.cs` | 7 | Roundtrip, empty string, unicode, long string, different ciphertext per call, wrong passphrase, corrupted data |
| `RedactionFilterTests.cs` | 8 | Each provider pattern, multiple keys in one string, no keys unchanged, null input, partial key not matched |
| `KeyStoreTests.cs` | 6 | Save/get roundtrip, nonexistent key, delete, list providers, overwrite, binary verification of `keys.enc` |
| `AppLoggerTests.cs` | 5 | File creation, append mode, API key redaction in messages, request formatting, request ID inclusion |
| `ExportServiceTests.cs` | 4+ | Summary generation, request log redaction, full export without `keys.enc`, redaction verification |
| `IntegrationTests.cs` | 2 | Full workflow (8-step end-to-end), Security audit (binary scan of `keys.enc`, log scan, export scan) |
| `ProductKeyServiceTests.cs` | 6+ | Save/validate roundtrip, feature gating, invalid keys, empty keys |

### 11.2 Security-Specific Test Cases

- **`test_different_ciphertext`:** Encrypting the same plaintext twice produces different ciphertext (verifies random salt/IV).
- **`test_wrong_passphrase`:** Decryption with wrong passphrase throws `CryptographicException` (verifies fail-secure).
- **`test_corrupted_data`:** Decryption of tampered ciphertext throws `CryptographicException` (verifies integrity).
- **`test_keys_enc_is_binary`:** Raw bytes of `keys.enc` do not contain `"sk-"`, `"AIza"`, or `"xai-"` patterns (verifies encryption).
- **`test_log_redacts_api_keys`:** Writing a message containing a fake API key results in `[REDACTED]` in `app.log`.
- **`test_security_audit`:** End-to-end integration test that creates a project with API keys, then scans all output files for leaked secrets.

### 11.3 Build Verification

```
dotnet build src/MultiLLMProjectAssistant.Core  →  0 errors, 0 warnings
dotnet test tests/MultiLLMProjectAssistant.Tests  →  70+ passed, 0 failed
grep -r "sk-proj" src/  →  No matches (outside regex definitions)
grep -r "AIzaSy" src/   →  No matches (outside regex definitions)
```

---

## 12. Known Limitations and Future Improvements

| Limitation | Impact | Potential Improvement |
|------------|--------|----------------------|
| CBC mode without HMAC | Ciphertext integrity relies on PKCS7 padding validation, which is susceptible to padding oracle attacks in network scenarios. Desktop-only use mitigates this risk. | Migrate to AES-256-GCM (authenticated encryption) in a future version. |
| Machine-bound passphrase | Changing username or hostname breaks decryption. | Add a recovery mechanism or user-supplied master password option. |
| No key rotation | API keys are re-encrypted only when modified. | Implement periodic re-encryption with fresh salt/IV. |
| RedactionFilter patterns are static | New LLM providers may use different key formats. | Make patterns configurable via a JSON file. |
| No file content encryption | Imported files are stored in plaintext. | Add optional per-file encryption for sensitive documents. |
| Single-user model | No access control or multi-user permissions. | Appropriate for desktop app; not applicable. |

---

## 13. Compliance Checklist

This checklist maps the storage layer's security controls to the Project Brief requirements.

| Requirement (Project Brief) | Status | Evidence |
|------------------------------|--------|----------|
| "Key storage must be protected (encrypted-at-rest)" (Section 4.1) | ✅ Done | `EncryptionService` + `KeyStore` → `keys.enc` |
| "Secrets never appear in UI screenshots, logs, or exported artefacts" (Section 6) | ✅ Done | `RedactionFilter` applied in `AppLogger` and `ExportService` |
| "Use redaction and encrypted storage" (Section 6) | ✅ Done | See Sections 3 and 4 of this document |
| "Capture request/response logs with secret redaction" (Section 4.2) | ✅ Done | `AppLogger.LogRequest()` with `RedactionFilter` |
| "Remove secrets from logs and screenshots" (Section 11) | ✅ Done | All log writes pass through `RedactionFilter`; UI masks keys |
| "File list with metadata (name, type, size, import time, hash)" (Section 4.4) | ✅ Done | `FileStoreService` with SHA-256 hash in `index.json` |
| "Per-request traceability" (Section 4.4) | ✅ Done | `TraceService` writes `REQ-xxx_trace.json` per request |
| ".gitignore" security (Development best practice) | ✅ Done | `*.enc`, `ProjectData/`, `logs/` excluded |

---

*Document generated for NIT6150 Assessment 3 submission.*
*Last updated: 2026-04-02*
