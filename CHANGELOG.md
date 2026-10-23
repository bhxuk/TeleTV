# Changelog

## 1.0.2

- Fixed the arrow keys, Enter and the other navigation keys inside Android; holding an arrow repeats like a remote
- Installs now look inside the file instead of trusting its name: app bundles named .apk install properly, and parts built only for ARM are skipped
- The Downloader spots when a site hands over a web page instead of the app, and says so instead of failing with a "parse" error
- Downloads that are apps but saved without an .apk name are recognised and can be installed

## 1.0.1

- Fixed the keyboard inside Android: letters, Backspace and the arrow keys now type and move correctly (they were landing on the wrong keys, such as Backspace typing 7)
- Media and volume keys now go to Android as play, pause, skip and volume

## 1.0.0

- Android TV in a normal Windows window, with full screen on F11
- First launch downloads Google TV (with the Play Store) or Android TV straight from Google, checks virtualisation and switches on Windows Hypervisor Platform if needed
- Import APK: drag and drop or browse, with .apks and .xapk bundles supported
- Built-in Downloader: browse, download, and install APKs with one click
- Apps: open, stop, clear data and uninstall
- Double-click .apk files in File Explorer to install them
- Keyboard, mouse, wheel, media keys and TV remotes control Android
- Quick start saves Android's state so it opens in seconds
- Settings for memory, processor cores, resolution and graphics
- In-app updates for TeleTV and for Android
