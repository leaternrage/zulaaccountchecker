## Zula Account Checker (GUI)

A modern Windows GUI tool for checking Zula accounts using combos and optional HTTP proxies.  
The app is designed to be **simple to use**, **proxy-friendly**, and show **clear live logs** while checking.

---

## Features

- **Modern dark UI**
  - Clean layout with config panel, live stats, and live log.
- **Combo support**
  - `email:password` format.
  - Skips empty/invalid lines automatically.
- **Proxy support (optional)**
  - Supports:
    - `ip:port`
    - `ip:port:user:pass`
  - Multi-threaded proxy testing.
  - Detects your **real IP** and only accepts proxies that change it.
  - Saves working proxies to a `.txt` file on your Desktop.
- **Live statistics**
  - Total checked
  - Valid
  - Invalid
  - Speed (checks per second)
- **Logging**
  - Timestamped, colored log for all important actions.
  - Shows IP detection, proxy results, errors, and hits.

---

🎥 Demo Video ( I changed my IP address :().. )
 

[![Demo Video](https://img.youtube.com/vi/S3IsUFGSw3E/0.jpg)](https://www.youtube.com/watch?v=S3IsUFGSw3E)

## Requirements

- **OS**: Windows 10 / 11  
- **Runtime**:
  - .NET 8.0 Desktop Runtime (if you build as framework-dependent), or
  - No runtime needed if you publish as self-contained.

If you don’t trust prebuilt binaries, you should **build from source**.

---

## Project Structure

Main files in this repo:

- `CheckerGUI.sln` – Solution file  
- `CheckerGUI.csproj` – Project file  
- `Program.cs` – Main form and all application logic  
- `FodyWeavers.xml` / `FodyWeavers.xsd` – Fody configuration (if used)

Build outputs like `bin/` and `obj/` are **not** included.

---

## How to Build

1. Clone the repository:
  
   git clone https://github.com/leaternrage/zulaaccountchecker.git
   cd <your-repo-name>
   2. Open `CheckerGUI.sln` in **Visual Studio** (recommended) or another C# IDE.

3. Make sure the target framework is **.NET 8.0 (Windows)**.

4. Build the project:
   - In Visual Studio: `Build → Build Solution`, or
   - From CLI:
    
     dotnet build
     5. The executable will be in:
   - `bin/Debug/net8.0-windows/` or  
   - `bin/Release/net8.0-windows/`

---

## How to Use

### 1. API URL

- Default:
  `https://api.zulaoyun.com/zula/login/LogOn`

The app checks if the URL is reachable and shows status with an icon.  
In most cases you **don’t need to change this**.

---

### 2. Combo File

Prepare a `.txt` file with combos in this format:

email1@example.com:password1
email2@example.com:password2
...Click the **📁** button next to **Combo File** and select your file.

---

### 3. Proxy File (Optional but Recommended)

Prepare a `.txt` file with one proxy per line.

**Supported formats:**

ip:port
ip:port:user:passExamples:

123.45.67.89:8080
98.76.54.32:3128:user123:pass123Steps:

1. Click the **📁** button next to **Proxy File** and select your proxy list.
2. The app loads and deduplicates all proxies.
3. Click **“✓ Test”**:
   - Detects your real IP (without proxy).
   - Tests each proxy against `https://api.ipify.org?format=json`.
   - Keeps only proxies that:
     - return a valid IP, and  
     - are **different** from your real IP.
   - Runs a **second round** on the first-round working proxies.
4. Working proxies are:
   - Saved to: `working_proxies_yyyyMMdd_HHmmss.txt` on your Desktop.
   - Used for checking (if **Use Proxy** is enabled).

You can toggle proxy usage with the **Use Proxy** checkbox.

---

### 4. Settings

- **Check Threads**  
  Number of concurrent checking operations.

- **Proxy Threads**  
  Number of concurrent proxy test tasks.

- **Check Interval (Min)**  
  Delay **between combo checks**, in minutes.
  - Higher value = safer, but slower.
  - Lower value = faster, but may hit rate limits.

If you want a faster checker, keep this low (e.g. `1`) and adjust threads carefully.

---

### 5. Running the Checker

1. Make sure:
   - Combo file is selected.
   - (Optional) Proxy file is loaded and tested.
   - Settings are adjusted (threads, interval, etc.).

2. Click **“▶ Start Checking”**:
   - If proxies are enabled and available, the app:
     - Verifies a few proxies.
     - Asks if you want to continue without proxy if they seem dead.
   - Loads all combos and starts checking them.

3. Live statistics update:
   - `CHECKED`, `VALID`, `INVALID`, `SPEED`

4. Live log shows:
   - Loaded combo count
   - Proxy info
   - Proxy errors
   - Extra debug for the first few combos

5. Click **“⬛ Stop”** to cancel early.

---

## Output

- **Hits (valid accounts)**  
  Saved to:
  `zula_hits_yyyyMMdd_HHmmss.txt` on your Desktop.

- **Working proxies**  
  Saved to:
  `working_proxies_yyyyMMdd_HHmmss.txt` on your Desktop.

---

## Notes & Disclaimer

- This project is for **educational and personal testing purposes only**.
- You are responsible for:
  - Respecting Zula’s Terms of Service.
  - Following the laws in your country.
- Too many requests may cause:
  - Temporary bans
  - Rate limiting (the app tries to handle some cases like response `"6"`, but nothing is guaranteed).

Contributions, bug reports, and improvements are welcome via **Issues** or **Pull Requests**.
