# GitTray

**GitTray** is a lightweight Windows tray utility that continuously monitors all Git repositories under selected directories and displays a small colored icon in the Windows system tray:

- 🟢 **Green** – all repositories clean and up to date.
- 🟡 **Yellow** – repositories with unpushed commits or without a remote.
- 🔴 **Red** – repositories with uncommitted changes or that are behind/diverged from remote.

It’s ideal for developers managing multiple repositories across drives, ensuring that nothing remains uncommitted or unpushed.

## ✨ Features

- Scans multiple root directories for Git repositories.
- Detects and categorizes repositories as **Clean**, **Dirty**, **Ahead**, **Behind**, **Diverged**, or **No Upstream**.
- Updates every N seconds (default 60).
- Tray icon color reflects overall state.
- Tooltip shows quick summary.
- Left-click opens a list of problematic repos.
- Double-click a repo to open it in **Explorer**.
- Right-click tray icon → manual rescan, edit configuration, or exit.

## 🚀 Installation

### 1. Prerequisites
- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- `git` in PATH

### 2. Clone and Build
```bash
git clone https://github.com/ajlakanen/GitTray.git
cd GitTray
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
Output executable will be at:
```
./bin/Release/net8.0-windows/win-x64/publish/GitTray.exe
```

### 3. Run
Launch `GitTray.exe`. It will appear as a small circle icon in the Windows system tray.

### 4. Configure
On first launch, a default config file is created at:
```
%LocalAppData%\GitTray\config.json
```

Example:
```json
{
  "roots": [
    "C:\\Users\\YOURNAME\\source",
    "D:\\repos"
  ],
  "ignorePatterns": ["node_modules", "bin\\*", "obj\\*"],
  "intervalSeconds": 60
}
```
- **roots** – directories to recursively scan.
- **ignorePatterns** – substrings or simple wildcards to skip.
- **intervalSeconds** – refresh interval.

You can open this file anytime via right-click → **Open config.json**.

## 🧠 How it Works
GitTray runs a background timer that executes:
- `git status --porcelain` → checks for uncommitted changes.
- `git status -sb --porcelain=2 -b` → inspects ahead/behind counts and upstream presence.

It aggregates all results and updates the tray icon color:
- **Green:** all repos clean and synced.
- **Yellow:** clean but ahead of remote or no remote.
- **Red:** dirty, behind, or diverged.

## ⚙️ Autostart

To start GitTray automatically when you log in:
1. Press `Win + R` → type `shell:startup` → press Enter.
2. Copy a shortcut to `GitTray.exe` into that folder.

## 🧪 Development Notes

- Built using **WinForms** on .NET 8 (WindowsDesktop SDK).
- Uses only standard libraries — no dependencies.
- Thread-safe async git status checks via `Task.WhenAll`.
- Designed for minimal memory footprint.

## Full disclosure about AI usage

The majority of the code and documentation for this project was generated with the assistance of AI tools, specifically OpenAI's GPT-5 and GitHub Copilot. The AI was utilized to help draft initial versions of the code, suggest improvements, and create documentation. All generated content was reviewed, tested, and modified by me to ensure functionality and accuracy. The AI was utilized to help draft initial versions of the code, suggest improvements, and create documentation. 

## 💡 Future Ideas

- Optional notifications when repos become dirty.
- Support for `git fetch` to detect remote changes.
- Integration with GitHub Desktop or Visual Studio.
- Dark/light tray icon themes.