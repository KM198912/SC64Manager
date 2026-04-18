# 🎮 SC64 Gui Manager v1.0

![Platform](https://img.shields.io/badge/Platform-Windows%2011%20%7C%2010-blue)
![Framework](https://img.shields.io/badge/Framework-.NET%2010%20MAUI-blueviolet)
![Hardware](https://img.shields.io/badge/Hardware-SummerCart64-green)

A high-performance, modern desktop application for managing your **SummerCart64** (SC64) SD card filesystem and firmware. Built with .NET 10 MAUI, this tool provides a native Windows 11 Fluent experience with hardware-optimized transfer speeds.

---

## ✨ Key Features

### 🚀 High-Speed I/O (1Mbps)
The communication engine is optimized for high-speed serial transfers at **1,000,000 baud**. 
- **Chunked Sector Writing**: Leverages a multi-sector packing algorithm that reduces packet overhead by up to 256x.
- **Progress-Aware Streaming**: Real-time throughput monitoring for all uploads and downloads.

### 🛰️ Automated Firmware Management
Stay up to date with the latest SummerCart64 features without leaving the app.
- **Auto-Check**: Automatically compares your cartridge's firmware against the latest official releases on GitHub.
- **One-Click Flashing**: If an update is available, a dedicated banner allows you to download and flash the new firmware directly to the cartridge SDRAM.

### 🍱 Fluent Windows 11 UI
- **Native Visuals**: Uses `Segoe MDL2 Assets` glyphs for a crisp, system-native look.
- **Smart Filtering**: Automatically hides low-level system files (e.g., `sc64menu.n64`, `found.000`) to keep your SD view clean.
- **Autoscrolling Diagnostics**: A hardened hardware log pins the most recent activity to the bottom for real-time monitoring.

### 🛠 Reliability & Robustness
- **DTR/DSR Handshaking**: Hardware-level synchronization ensures stable connections and prevents race conditions.
- **Thread-Safe Serial Access**: Instrumented with `SemaphoreSlim` to guarantee 100% stable communication between background tasks and the UI.
- **Recursive Navigation**: Effortlessly traverse nested directory structures on your SD card.

---

## 🛠 Installation & Build

### Prerequisites
- Windows 10/11
- .NET 10 SDK
- SummerCart64 with USB connection

### Build from Source
```powershell
# Clone the repository
git clone https://github.com/km198912/SC64Manager.git

# Navigate to the project folder
cd SC64Manager/NetGui

# Build and run
dotnet run
```

---

## 📖 How to Use

1.  **Connect**: Plug in your SummerCart64 via USB.
2.  **Identify**: Select the correct COM port from the dropdown and hit **Connect**.
3.  **Manage**:
    - **Upload**: Select local files and click "Upload" to send them to the current SD folder.
    - **Download**: Click the download arrow (⬇) on any remote file to choose a local save path.
    - **Delete**: Check the files you want to remove and use "Delete Selected".
    - **Update**: If a gold banner appears, click "Flash Firmware" to perform an automated update.
    - **Navigate**: Use the ".." entry to go back or tap folders to dive deeper.

---

## 🏗 Technical Specifications
- **Framework**: .NET 10 MAUI (Windows App SDK)
- **Library**: `CommunityToolkit.Maui`, `CommunityToolkit.Mvvm`, `LTRData.DiscUtils.Fat`
- **Protocol**: SummerCart64 V2 Communication Protocol
- **Baud Rate**: 1,000,000 (1Mbps)

---

## 🤝 Contributing
Contributions are welcome! Please open an issue or submit a pull request for any bugs or feature requests.

## 🤝 Credits
- **SummerCart64**: [https://github.com/Polprzewodnikowy/SummerCart64](https://github.com/Polprzewodnikowy/SummerCart64)

## 📄 License
MIT License - Copyright (c) 2026

---

*Enjoy your N64 gaming experience! 🍄🌟*
