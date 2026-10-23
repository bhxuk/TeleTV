# TeleTV user guide

TeleTV runs Android TV inside a normal Windows window. This guide covers setting it up, getting apps in, and fixing things if they go wrong.

- [First launch](#first-launch)
- [Getting around](#getting-around)
- [Adding apps](#adding-apps)
- [The Downloader](#the-downloader)
- [Your apps](#your-apps)
- [Settings](#settings)
- [Updates](#updates)
- [Fixing problems](#fixing-problems)
- [Where TeleTV keeps things](#where-teletv-keeps-things)

## First launch

The first time you open TeleTV it sets up Android TV:

1. **It fetches Google's download list** and shows what it will download: the Android emulator, adb tools and the TV system (Google TV with the Play Store where Google publishes it, otherwise Android TV). It's about 2 GB.
2. **You accept Google's licence.** Android comes straight from Google's servers under the Android SDK licence, so TeleTV asks you to accept it once.
3. **It downloads and unpacks everything.** If the connection drops, press Try again and it carries on from where it stopped.
4. **It checks virtualisation.** Android needs Windows Hypervisor Platform. If it's off, choose **Switch on and restart**. Windows asks for permission and restarts once. If your PC's BIOS has virtualisation switched off, TeleTV tells you; switch on Intel VT-x or AMD-V (sometimes called SVM) in the BIOS or UEFI settings.
5. **Android TV starts.** The very first start takes a minute or two. With Google TV you'll go through Google's setup and sign in with a Google account.

After that, TeleTV opens straight into Android TV.

## Getting around

The bar along the top has **Home** (the TV), **Import APK**, **Downloader**, **Apps** and **Settings**. The dot on the right shows whether Android is running.

| Key or button | Does |
|---|---|
| Arrow keys | Move, like a remote's D-pad |
| Enter | Select |
| Esc, or the mouse's back button | Back |
| Home, or middle click | Android TV home screen |
| F5 | Recent apps |
| F11 or Alt+Enter | Full screen on and off |
| Ctrl+V | Paste text from Windows into Android |
| Ctrl+O | Import an APK |
| Ctrl+1, 2, 3, 4 and Ctrl+, | Home, Import APK, Downloader, Apps, Settings |
| Media keys | Play, pause, skip, as on a remote |
| Mouse | Click and scroll inside apps |

Bluetooth TV remotes and air mice that show up in Windows as a keyboard work the same way.

## Adding apps

There are four ways to get an app in:

- **Drag and drop.** Drop one or more `.apk` files anywhere on the TeleTV window.
- **Import APK.** Open Import APK and press **Browse files**.
- **Double-click.** In File Explorer, double-click an `.apk` file. (Setup adds this unless you untick it.)
- **The Downloader**, below.

Apps install one at a time and, by default, open as soon as they're in. Turn that off with the switch at the bottom of Import APK or in Settings > General.

`.apks` and `.xapk` bundles (apps split into several parts) work too.

If an install fails, TeleTV says why in plain English. The usual reasons:

- **Built for a different processor.** The app only has ARM code. Look for a "universal" or "x86" version.
- **A different version is already installed.** Uninstall it in Apps first.
- **Needs a newer Android.** The app doesn't support this Android version.

## The Downloader

The Downloader is a browser inside TeleTV. Type a web address or a direct link to an APK and press **Go**.

- When an Android app finishes downloading, a card appears with **Install**.
- Everything you download is kept in **Downloads**, where you can install or delete it later.
- Press the star to save a page to **Favourites**.

The Downloader uses Microsoft Edge WebView2, which is part of Windows 11 and most Windows 10 PCs. Without it, direct APK links still download.

## Your apps

**Apps** lists everything you've installed. **Open** starts an app on the TV. The **…** button can force stop it, clear its data (it starts fresh) or uninstall it.

## Settings

**General**

- **Open in full screen** when TeleTV starts.
- **Open apps after installing.**
- **Quick start** saves Android's state when TeleTV closes so it opens in seconds. Turn it off for a full start every time.

**Android TV**

- **Memory** and **Processor cores**: Auto suits most PCs. Give more if heavy apps stutter, but leave enough for Windows.
- **Picture**: 1080p or 720p. 720p is lighter on older PCs.
- **Graphics**: Auto uses the graphics card when it can. Choose **Software** if the picture stays black or Android won't start.
- **Fix virtualisation** checks and switches on Windows Hypervisor Platform.
- **Switch to Android TV / Google TV** downloads the other system. Apps inside Android are erased.
- **Reset Android** erases every app and setting inside Android, like a factory reset.

Changes to memory, cores, picture and graphics apply when Android restarts. TeleTV offers a **Restart now** button.

## Updates

**Settings > Updates** has two parts:

- **TeleTV** checks this project's GitHub releases. **Update now** downloads the new version, closes Android cleanly and reinstalls; TeleTV comes back on its own.
- **Android TV system** checks Google for a newer emulator, adb or system image. Updates download in the background and are applied the next time Android starts.

Choose **Beta** to get new TeleTV features before everyone else.

## Fixing problems

**Android won't start, or stops straight away**
Open Settings > Android TV and press **Fix virtualisation**. If that says it's working, set **Graphics** to **Software** and restart Android.

**"Virtualisation is switched off in this PC's BIOS"**
Restart into the BIOS/UEFI (usually F2, Del or F10 while the PC starts), find Intel Virtualization Technology (VT-x) or SVM Mode (AMD), switch it on, save and exit.

**Another emulator or VirtualBox is fighting with it**
TeleTV uses Windows Hypervisor Platform, which works alongside Hyper-V, WSL 2 and modern VirtualBox. Very old VirtualBox or VMware versions may need updating.

**The picture is black but sound plays**
Set Graphics to Software in Settings > Android TV.

**An app says the device isn't certified, or won't play video**
Apps that require Google's certified-device checks for DRM, such as Netflix, Prime Video and Disney+, don't work in any Android emulator.

**Starting over**
Settings > Android TV > Reset Android erases apps and settings. To remove everything, including the 2 GB download, uninstall TeleTV and answer Yes when it asks.

**Reporting a problem**
Open an issue on the project page and attach `teletv.log` from the `logs` folder (Settings > About > TeleTV's files).

## Where TeleTV keeps things

Everything lives in `%LOCALAPPDATA%\TeleTV`:

| Folder | Holds |
|---|---|
| `sdk` | The Android emulator, adb and the TV system image from Google |
| `avd` | Your Android TV: installed apps, settings and saved state |
| `downloads` | Files from the Downloader |
| `browser` | The Downloader's cookies and history |
| `logs` | `teletv.log`, useful when something goes wrong |
| `settings.json` | TeleTV's settings |

Uninstalling TeleTV asks whether to remove this folder too.
