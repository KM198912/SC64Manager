# SC64 Manager Help & Documentation

Welcome to the **SC64 Manager**, a comprehensive developer and user suite for the SummerCart64!

## Getting Started

1. **Connect**: Select the COM port corresponding to your SC64 and click **Connect**.
2. **N64 Power State**: Some operations (like writing to the SD card) are restricted while the N64 is powered ON to prevent data corruption. This is the "SD Guardian" safety feature.

## Features

### File Manager
- **Local Files (Left)**: Your PC's filesystem.
- **Remote Files (Right)**: The SC64's SD card filesystem.
- **Actions**:
    - **Upload**: Select a local file and click the arrow pointing right.
    - **Download**: Select a remote file and click the arrow pointing left.
    - **Rename**: Click the pencil icon on a remote item.
    - **Delete**: Click the trash icon to remove items from the SD card.

### Hardware Health
- **Voltage**: Monitor the internal 3.3V power rail of the cartridge.
- **Temperature**: Monitor the FPGA's internal temperature sensor.
- **RTC Sync**: Ensure your cartridge's Real-Time Clock matches your PC for accurate file timestamps and game events.

## Troubleshooting

- **No COM Ports**: Ensure you have the USB-C cable is connected to the 'USB' port on the cartridge, it should show up in Device managers as COM<x> Port.
- **Access Denied**: Close any other programs (like sc64deployer or serial terminals) that might be using the COM port.
- **SD Mount Failed**: Ensure a FAT32 or exFAT formatted SD card is inserted.

## Links

- **GitHub Repository**: [KM198912/SC64Manager](https://github.com/KM198912/SC64Manager)
- **Official SC64 Project**: [SummerCart64](https://github.com/Polprzewodnikowy/SummerCart64)
