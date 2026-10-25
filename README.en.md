<p align="center">
  <a href="README.md">
    <img alt="Русский" src="https://img.shields.io/badge/Русский-21262d?style=for-the-badge">
  </a>
</p>

<h1 align="center">GlassCast</h1>

<p align="center">
  A phone or PC screen on Rokid glasses.<br>
  One Wi‑Fi, Motion JPEG, and black bars that stay dark on the panel.
</p>

<p align="center">
  <a href="https://github.com/ikkiteo/Rokid-Glasscast/releases/tag/v2.1.0"><img alt="v2.1.0" src="https://img.shields.io/badge/release-v2.1.0-1f6feb?style=flat-square"></a>
  <a href="PROTOCOL.md"><img alt="protocol" src="https://img.shields.io/badge/protocol-GCS1-6e7681?style=flat-square"></a>
</p>

This is the English page. The Russian page and the Russian builds are one click away.

## Download

Same three apps. Only the interface language changes. Installing the other language replaces the one already on the glasses or the phone.

| | Russian | English |
|---|---|---|
| Glasses | [GlassCast-glasses-ru.apk](https://github.com/ikkiteo/Rokid-Glasscast/releases/download/v2.1.0/GlassCast-glasses-ru.apk) | [GlassCast-glasses-en.apk](https://github.com/ikkiteo/Rokid-Glasscast/releases/download/v2.1.0/GlassCast-glasses-en.apk) |
| Phone | [GlassCast-phone-ru.apk](https://github.com/ikkiteo/Rokid-Glasscast/releases/download/v2.1.0/GlassCast-phone-ru.apk) | [GlassCast-phone-en.apk](https://github.com/ikkiteo/Rokid-Glasscast/releases/download/v2.1.0/GlassCast-phone-en.apk) |
| PC | [GlassCast-PC-ru.zip](https://github.com/ikkiteo/Rokid-Glasscast/releases/download/v2.1.0/GlassCast-PC-ru.zip) | [GlassCast-PC-en.zip](https://github.com/ikkiteo/Rokid-Glasscast/releases/download/v2.1.0/GlassCast-PC-en.zip) |

The PC archive already contains `GlassCast.exe`, `VddHost.exe`, and the driver folder. Unpack it and run the exe from that folder. It uses .NET Framework 4.8, which is already on Windows 10.

The virtual-monitor driver is **USB Mobile Monitor** by Amyuni Technologies. It is not embedded in the exe. Its license is `driver/usbmmidd/usbmmidd_v2/License.txt`.

## How it fits together

The glasses listen. The phone and the PC only connect.

```text
phone ──JPEG──┐
              ├── TCP 47810 ── Rokid glasses
PC ───────────┘
       discover over UDP 47811
```

| app | runs on | job |
|---|---|---|
| `receiver` | glasses | receives JPEG and draws it in the center of the panel |
| `sender` | phone | captures the screen and sends JPEG |
| `windows` | PC | captures a monitor, a window, a region, or a virtual display |

The wire format is in [PROTOCOL.md](PROTOCOL.md).

## Start here

1. Install the glasses app and open it. The panel shows an IP address.
2. Keep the phone or the PC on the same Wi‑Fi.
3. On the phone: **GlassCast Phone** → find glasses → allow screen capture.
4. On the PC: unpack the archive → `GlassCast.exe` → find glasses → **stream**.

On the glasses, one tap rotates the picture by 90°. A double tap asks for a fresh frame. The volume keys change the stream quality.

## On the PC

**Monitor, region, window.** The glasses receive that part of the desktop. Capture goes through DXGI, so the hardware cursor on the main screen does not blink.

**Virtual display.** Display driver installs the Amyuni driver once. The 1920×1080 monitor turns on only while that source is streaming and turns off when the stream stops. The first launch asks for administrator rights; after that a small service toggles the monitor. Drag windows onto it and stream it.

The virtual display does nothing until `driver\usbmmidd` is installed. Windows sometimes turns on test signing after that install. An administrator command prompt turns it back off:

```bat
bcdedit -set TESTSIGNING OFF
bcdedit -set NOINTEGRITYCHECKS OFF
```

**Canvas.** A spare window when you do not want a driver. F11 fills the current monitor. It is not a separate Windows display.

Bars above and below the picture are pure black, so those pixels do not light up on the glasses.

## Build

```bat
gradlew.bat :receiver:assembleDebug :sender:assembleDebug
windows\build.bat
```

Android flavors are `ru` and `en`. The APKs land in `receiver/build/outputs/apk/` and `sender/build/outputs/apk/`.

Windows output is `windows\out\ru` and `windows\out\en`. Each folder has `GlassCast.exe` and `VddHost.exe`. The English build is compiled with `/define:EN`.
