# InfoPanel.EasyBT

Plugin for **InfoPanel** that displays Battery and Status from mouses using [Easy Bluetooth](https://easybluetooth.net/index.html) Unified Standard Data API:

Easy Bluetooth have a "VIP" version, the "Free" version will just show one device, for more devices you will need to buy the "VIP" version!


---

## ✨ Features

* Multiple device support
* Device name as title
* Battery level (%)
* Brand and device type
* Connection status
* Charging / sleeping state
* Configurable via `.ini`

---

## 📦 Installation

1. Download EasyBluetooth: [Download](https://easybluetooth.net/download.html)
2. Follow the initial configurations
3. Enable Unified Standard Data API in Settings > Advanced Features
4. Download the latest release `.zip` of the plugin
5. Open **InfoPanel**
6. Import the plugin
7. Done ✅
---

## ⚙️ Configuration

The plugin automatically creates:

```
InfoPanel.EasyBT.ini
```

Example:

```ini
[General]
ApiUrl=http://localhost:18080/api/v1/status
UpdateIntervalSeconds=2
MaxDevices=10
```

## 📄 License

This project is licensed under the MIT License — see the LICENSE file for details.