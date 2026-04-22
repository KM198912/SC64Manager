# 🎮 SC64 Gui Manager v2.2.0

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
- **Memory Viewer**: View the memory of the SC64 in real-time.
- **RTC Sync**: Sync the RTC of the SC64 to the current time.
- **Set Menu Background Music**: Set the background music of the SC64 menu.
- **Boxart and Description Scraper**: Scrapes boxart and descriptions for games from the internet, and saves them to the SD card in the correct format for the SC64 Menu.
- **Pretty Titles**: Gui Generates a titles.txt file, which is used by the SC64 Menu to display the title of the game instead of the filename. (Needs Modded sc64menu.n64) [KM198912/SC64Menu](https://github.com/KM198912/SC64Menu)
- **SD Card Management**: Decoupled SD management from Connection Status
- **Hardware Page**: Added a page to view the hardware information of the SC64, like Voltage, Temperature, CIC Handshake

### Menu Compatibility

This GUI works with both the original SC64Menu and my modified version.

- Original SC64Menu:
  - Fully supported
  - Recommended if you prefer the stock experience

- Modified SC64Menu (optional):
  - Enables additional features:
    - Pretty Titles (titles.txt)
    - Game Descriptions (description.txt)
    - Background Music (apparently was planned in the original SC64Menu but never implemented, i couldnt find it atleast)
    - Carousel Mode (requires Expansion Pak)

These features are purely optional. If you use the original menu, everything else in the GUI works as expected.


### 🛰️ Automated Firmware Management
Stay up to date with the latest SummerCart64 features without leaving the app.
- **Auto-Check**: Automatically compares your cartridge's firmware against the latest official releases on GitHub.
- **One-Click Flashing**: If an update is available, a dedicated banner allows you to download and flash the new firmware directly to the cartridge SDRAM using the sc64deployer.exe downloaded at runtime, and downloading the latest firmware from GitHub.

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
- .NET 10 Maui Workload
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
    - **Mount**: Click "Mount" to mount the SD card.
    - **Unmount**: Click "Unmount" to unmount the SD card.
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
GPLv2 License. See: [GNU GPL v2.0](https://www.gnu.org/licenses/old-licenses/gpl-2.0.html)
Since the original project is licensed under GPLv2, this project is also licensed under GPLv2.
And since i use their Logo in the project, i have to Use the same License.
However if it went by my choice, i would have used the WTFPL License but it is what it is :P 

---

*Enjoy your N64 gaming experience! 🍄🌟*
