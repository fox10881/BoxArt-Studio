🌍 **Language:** <strong>English</strong> ∙ [العربية](README.ar.md)

# 🎮 BoxArt Studio
<img width="3804" height="2007" alt="image" src="https://github.com/user-attachments/assets/417e0a97-eb08-442b-947e-71a824b85596" />
<img width="3798" height="1995" alt="image" src="https://github.com/user-attachments/assets/325f457a-5029-4fc7-9a3e-c1f6284fcc41" />


<img width="3810" height="2016" alt="Screenshot 2026-10-18 034840" src="https://github.com/user-attachments/assets/25d5e901-7117-42b7-be8b-b60d323a838c" />


**Turn your boring Windows folders and game shortcuts into a beautiful, physical-looking game shelf.**

BoxArt Studio is a lightweight, modern Windows utility that automatically scans your game shortcuts and folders, matches them with high-quality cover art from SteamGridDB, and applies customizable overlays to make them look like real physical game cases.

## 🌟 The Transformation

### 📂 Folders: Before & After
*Transform generic yellow Windows directories into stunning vertical posters.*
 <img width="3357" height="1470" alt="Screenshot 2026-10-17 125535" src="https://github.com/user-attachments/assets/151818d0-1d9d-45f8-82ff-42825d1590a1" />
⬇️
<img width="3363" height="1374" alt="Screenshot 2026-10-17 125153" src="https://github.com/user-attachments/assets/89a7950d-af73-464e-864d-42c2a3aa1f38" />


### 🔗 Shortcuts: Before & After
*Turn tiny, pixelated shortcut icons into highly detailed physical box arts.*
<img width="3363" height="1002" alt="Screenshot 2026-10-17 125045" src="https://github.com/user-attachments/assets/265a2fe2-b6eb-42ae-84ea-1c9ebb4e08af" />


⬇️
<img width="3366" height="1296" alt="Screenshot 2026-10-17 125014" src="https://github.com/user-attachments/assets/915adb43-87c2-4802-947f-15c522845fad" />


---

## ✨ Key Features

* **🤖 Smart Auto-Matching:** Powered by the SteamGridDB API, the app automatically reads your shortcut/folder names and fetches the best 2:3 grid artwork.
* **📦 Physical Box Art Overlays:** Make your digital collection look tangible. Apply transparent stickers to the bottom of the covers, including:
  * Platform Logos: `PC CD-ROM`, `DVD`
  * Age Ratings: `ESRB (T)`, `PEGI 18` (Smartly stacked to prevent overlapping)
  * Quality Seals
* **🎨 Advanced Rendering Engine:** Built with Win2D. The app handles perfect aspect-fitting without awkward stretching or squishing, preserving the original art while injecting transparent backgrounds for a sleek desktop look.
* **🔄 Full Un-do Support:** Don't like a cover? The "Remove Custom Icon" feature instantly deletes the `desktop.ini` or resets the `.lnk` file to restore the default Windows look.
* **🌍 Bilingual & RTL Ready:** Beautifully designed WinUI 3 interface with full support for English and Arabic (Right-to-Left layout flipping).
* **🐛 Built-in Debug Console:** A live in-app log viewer allows you to monitor API requests, file permission issues, and rendering steps without hunting down hidden text files.
* **⚡ Lightweight Installer:** Framework-dependent architecture keeps the installer ultra-small (~20MB) while automatically fetching the required .NET 8 and Windows App SDK runtimes if needed.

## 🚀 How to Use

1. Get a free API Key from [SteamGridDB](https://www.steamgriddb.com/profile/preferences/api).
2. Open **BoxArt Studio** and paste your key.
3. Select your Games folder.
4. Let the app scan your games, or manually pick alternative covers from the built-in studio interface.
5. Toggle your preferred overlays (Stickers) and hit **Apply**!

6. ### ⚠️ System Requirements
This is a lightweight framework-dependent build. To run this application, please ensure your PC has the following official Microsoft runtimes installed:
1. [.NET 8.0 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
2. [Windows App SDK 1.8 Runtime (x64)](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)

## 🛠️ Tech Stack

* **Framework:** WinUI 3 / Windows App SDK (C#)
* **Graphics:** Win2D for hardware-accelerated image compositing and alpha-blending.
* **Deployment:** Inno Setup for a fully automated, user-friendly installation.

---
*Developed with a passion for clean desktops and beautiful game libraries.*
