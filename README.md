# QuickConvert (Radial File Converter)

A lightweight background Windows application that allows you to instantly convert files from File Explorer via a gesture-driven **Radial Wheel**.

---

## ⚡ How It Works

1. **Hold `Shift` and Right-Click** on any supported file (e.g., `.webp`, `.png`, `.jpg`) in Windows File Explorer or Desktop.
2. **Hold down the Right Mouse Button**: A radial wheel smoothly pops up centered on your cursor, displaying conversion options tailored to that file type.
   - For example, Shift+Right-Clicking a `.webp` file displays **PNG** and **JPEG** slices.
3. **Hover over your desired format slice** (the slice highlights in real time with haptic visual glow).
4. **Release Right-Click**: A new copy of that file is immediately created in the exact same directory in the selected format!
5. **To Cancel**: Simply release inside the center circle ("Cancel" hub).

---

## 🚀 Key Features

- **Lightweight Background Process**: Sits quietly in the Windows System Tray with ~30MB memory footprint.
- **Zero Explorer Intrusions**: Does not modify Windows Shell registry keys or pollute the standard Windows context menu.
- **Hardware-Accelerated Radial UI**: Modern WPF Fluent dark-mode translucent wheel with responsive mouse tracking.
- **Fast, High-Fidelity Image Engine**: Powered by Google's open-source `SkiaSharp` engine (native WebP, PNG, JPEG, BMP, ICO support with automatic transparency handling).
- **Collision Avoidance**: If `photo.png` already exists, automatically saves as `photo (1).png`.
- **System Tray Management**:
  - Right-click tray icon to toggle "Start with Windows" (auto-run on boot).
  - "Test Conversion with File..." option to test the wheel anytime without Explorer.
  - Toast / balloon notifications upon successful conversion.

---

## 🛠 Project Structure

```
├── Core/
│   ├── Models/
│   │   └── ConversionOption.cs     # Conversion target metadata & color accents
│   └── Services/
│       ├── IFileConverter.cs       # Extensible converter interface
│       ├── ImageConverter.cs       # SkiaSharp image transcode (WebP, PNG, JPG, etc.)
│       ├── ExplorerService.cs      # File Explorer & Desktop selection detection
│       └── GlobalInputHook.cs      # Low-level WH_MOUSE_LL hook for Shift+Right-Click
├── UI/
│   ├── RadialGeometryHelper.cs     # Dynamic pie-slice vector geometry calculation
│   ├── RadialMenuWindow.xaml       # Translucent WPF radial wheel UI
│   └── TrayIconManager.cs          # Native Windows notification area (System Tray)
├── RadialConvert.Tests/            # Comprehensive unit test suite
├── App.xaml / App.xaml.cs          # Single-instance lifecycle management
└── RadialConvert.sln
```

---

## 🧪 Running & Testing

### Run Tests:
```powershell
dotnet test
```

### Run Application:
```powershell
dotnet run
```

### Build Release Executable:
```powershell
dotnet publish -c Release -r win-x64 --no-self-contained -o ./publish
```
