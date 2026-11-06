# Open Tangerine (Windows Native Edition)

A high-performance, native Windows clone of **[Open Tangerine](https://github.com/sintaxerrorpvtltd/Open-Tangerine)**. Runs completely offline in the background, featuring the signature **frosted glass radial wheel** with vivid citrus glowing highlights, multi-file Explorer integration, and a companion batch drop window.

---

## 🍊 Key Highlights

- **Instant In-Explorer Conversion**: Select one or multiple files in Windows File Explorer or Desktop, hold `Shift`, and **Right-Click**. The Tangerine frosted glass wheel appears centered on your cursor. Release on a format slice to instantly convert all selected items in-place.
- **Iconic Frosted Glass Radial Wheel**:
  - Pearly white gradient rim with soft 3D shadow.
  - Floating rounded glass segments that illuminate with a vivid Tangerine citrus glow (`#FF6619` / `#FFB36B` / `#EB4F0C`).
  - Center hub with pill capsule displaying active format or `DROP`/`CANCEL`.
  - Floating status pill underneath with dynamic conversion descriptions.
- **Rich Format Engine**:
  - **Images**: PNG, JPEG, WEBP, BMP, ICO.
  - **Image → PDF**: Converts single or batches of images into high-resolution PDF documents.
  - **Subtitles**: Bidirectional SRT ↔ WebVTT conversion.
  - **Audio/Video**: Seamless conversion via installed FFmpeg (MP4, WEBM, MP3, WAV, FLAC, OGG).
- **Desktop Companion Window**:
  - Drag-and-drop batch drop area with Tangerine orange glow on file hover.
  - Embedded interactive glass wheel synced with format selector.
  - File queue with per-file status and output folder options.
- **Ultra Lightweight & Fast**:
  - Native .NET 9 + SkiaSharp GPU/CPU engine (~25–35MB memory footprint).
  - Instant 15ms activation via low-level Win32 / Shell COM automation (no slow PowerShell sub-processes).
  - Single-instance system tray daemon with "Start with Windows" support and toast notifications.

---

## ⚡ How It Works

### In File Explorer:
```
[Select File(s)] ──> [Hold Shift] + [Hold Right-Click] ──> [Frosted Glass Wheel Appears]
                                                                     │
                                                                     ▼
                                                     [Hover format (e.g. PNG, PDF)]
                                                                     │
                                                                     ▼
                                                           [Release Right-Click]
                                                                     │
                                                                     ▼
                                             [Converted file(s) saved in same folder]
```

### In Desktop Companion Window:
- Double-click the orange citrus icon in your system tray or run with `--window`.
- Drag and drop files directly onto the window, pick your desired output on the wheel or dropdown, and click **Convert files**.

---

## 🛠 Project Structure

```
├── Core/
│   ├── Models/
│   │   └── ConversionOption.cs     # Format definitions and Tangerine styling
│   └── Services/
│       ├── IFileConverter.cs       # Extensible single & batch converter contract
│       ├── ImageConverter.cs       # SkiaSharp image transcode, PDF export & FFmpeg bridge
│       ├── ExplorerService.cs      # Multi-selection File Explorer & Desktop resolver
│       └── GlobalInputHook.cs      # Low-level WH_MOUSE_LL hook for Shift+Right-Click
├── UI/
│   ├── RadialGeometryHelper.cs     # Sector geometry math matching Open Tangerine
│   ├── RadialMenuWindow.xaml       # Translucent frosted glass wheel window
│   ├── MainWindow.xaml             # Tangerine companion desktop batch window
│   └── TrayIconManager.cs          # Native notification area tray manager
├── RadialConvert.Tests/            # Comprehensive unit test suite (9 passing tests)
├── App.xaml / App.xaml.cs          # Application startup & lifecycle
└── RadialConvert.sln
```

---
<img width="889" height="720" alt="2026-11-06 14_04_37-Open Tangerine" src="https://github.com/user-attachments/assets/9b435379-53c4-46e4-9158-6f22f3b9fa79" />

## 🧪 Testing & Execution

### Run Automated Tests:
```powershell
dotnet test
```

### Run the App in Background:
```powershell
dotnet run
```

### Open the Desktop Companion Window:
```powershell
dotnet run -- --window
```
