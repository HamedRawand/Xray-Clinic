# Diagnostic Clinic Manager — server deployment runbook

One server PC per clinic runs PostgreSQL and the API as a Windows service; every other PC (and the server itself) runs the client.

## 0. Before you start

| Need | Detail |
|---|---|
| Server PC | Windows 10 / 11 (64-bit) or Windows Server 2016+. Always on during clinic hours, wired to the router. |
| IP address | Give the server a **fixed address**: reserve it in the router's DHCP settings (or set a static IP). Clients are pointed at its name or address. |
| Internet on the server PC | Only for the first install (PostgreSQL download, ~370 MB). To work offline, put `postgresql-17.x-windows-x64.exe` in `deploy\prereq` before building the installer, or pass `-PostgresInstallerPath`. |
| Time | Windows time zone set correctly and clock synchronised (the app's day boundaries use Asia/Kabul unless changed). |

## 1. Install the server (about 10 minutes)

1. Copy `DiagnosticClinicManager_Server_Setup_v<version>.exe` to the server PC and run it (it asks for administrator rights).
2. When the file copy finishes a console window opens and the setup script does, in order:
   - installs PostgreSQL 17 silently if it is not installed (verifies EnterpriseDB's code signature first),
   - creates the `clinic` database and its own login with a generated password,
   - generates the JWT signing secret and the **backup encryption password**,
   - issues a 5-year self-signed HTTPS certificate for the PC's name and addresses,
   - registers the **Diagnostic Clinic Manager Server** Windows service (starts automatically with Windows, after PostgreSQL, restarts itself after a crash, runs as the low-privilege *Local Service* account; on a PC where Windows refuses that account access to the HTTPS certificate, setup **switches to the Windows System account by itself** and says so),
   - opens inbound TCP **5443** to the local subnet only,
   - starts the service, waits until it answers, and prints the address, the certificate fingerprint and the first sign-in. **The very first start can take a few minutes** (antivirus scans the newly installed files) — wait for "Server is running".
3. **Save the secrets.** The window and `C:\ProgramData\DiagnosticClinicManager\INSTALL-SECRETS.txt` show the first admin password and the backup encryption password. Copy them somewhere safe that is *not* this PC (printed copy in the safe, password manager), then delete the file.
   **If the backup password is lost, encrypted backups can never be restored.**

Where things live (all under `C:\ProgramData\DiagnosticClinicManager`, readable by administrators and the service only):

| Item | Location |
|---|---|
| Settings and secrets | `server.settings.json` |
| HTTPS certificate | `certs\server.pfx` |
| Backups | `Backups\` |
| Logs | `Logs\api_<date>.log` |
| Program files | `C:\Program Files\DiagnosticClinicManager\Server` |

## 2. First sign-in and clinic set-up (on the server PC)

1. Install the client (`DiagnosticClinicManager_Setup_v<version>.exe`) on the server PC too if it will be used as a workstation.
2. On the login screen open the server address settings ("Clinic Server Address") → enter `https://<server-name>:5443/` → **Test Connection**, then **Trust This Certificate**. Check the fingerprint equals the one the setup printed.
3. Sign in as `admin` with the printed password; you must choose a new one.
4. **Clinic Settings** → enter the clinic's name, Dari name, tagline, contact details and logo.
5. Create the real users (Users), and adjust Custom Roles if needed.
6. **Backup & Restore → Create Backup Now**, and check it appears in the list. Then test a **restore on a copy** (or at least confirm the file opens) before relying on it.

## 3. Each clinic PC

1. Install the client installer.
2. Clinic Server Address → the same `https://<server-name>:5443/` address → **Test Connection** → **Trust This Certificate** (compare the fingerprint with the one on the server screen/printout) → Save.
3. Sign in with the user created for that person.

The one-time "Trust This Certificate" step is by design: the PC remembers *this exact certificate* and refuses any other, so someone on the network cannot impersonate the server.

## 4. Remote access for support (router VPN)

Not automated — depends on the clinic's router:
- Check the router supports a VPN server (WireGuard / OpenVPN). If not, use Windows' built-in VPN server role on a spare PC, or a remote-support tool.
- Remote administration is Windows Remote Desktop **over the VPN only**. Never forward the app's port 5443 or RDP to the internet.
- VPN clients are on a different subnet. If a VPN user must run the client app directly, re-run the setup script with `-FirewallRemoteAddress "LocalSubnet","10.8.0.0/24"` (use your VPN's range).

## 5. Upgrades

Run the newer **server** installer over the old one. The script detects the existing install, keeps the database/secrets/certificate, takes a safety backup (`Backups\pre-upgrade-<time>.dump`) and starts the new version; the database schema upgrades itself on the first start. Then update the clients (they offer the update automatically from GitHub, or run the new client installer).
Upgrade the **server first, then the clients** — a newer client may need the newer server.

## 6. Day-to-day

| Task | How |
|---|---|
| Is the server running? | Services → *Diagnostic Clinic Manager Server* → Running. Or open `https://<server-name>:5443/api/system/clinic` in a browser (shows the clinic name; the certificate warning is expected). |
| Restart it | Services → right-click → Restart. |
| Something is wrong | Read `C:\ProgramData\DiagnosticClinicManager\Logs\api_<date>.log` and Event Viewer → Windows Logs → Application. |
| Setup says the service "cannot be started" (server older than 1.1.2) | Event Viewer showed `CryptographicException: Access denied` while loading the certificate. Run `sc.exe config DiagnosticClinicManagerServer obj= LocalSystem` then `Start-Service DiagnosticClinicManagerServer`, and install server 1.1.2 or newer (it does this by itself). |
| Lost the first-run passwords | They stay in `server.settings.json` (`Seed` → `InitialAdminPassword`, `Backup` → `EncryptionPassword`) until the first successful start; setup 1.1.2+ also re-creates `INSTALL-SECRETS.txt` from them. |
| Nightly backup | Runs automatically at about 02:00 local time and keeps the newest 14 files. Copy `Backups\` to an external disk regularly — a backup on the same disk does not protect against disk failure. |
| Certificate expires | After 5 years. Re-run the setup script with `-ReissueCertificate`, then every PC must **Trust This Certificate** again. |
| Server's name or address changed | Clients pinned to the certificate keep working if they reach the server; update their address in Server Settings. Only re-issue the certificate if you want the new name inside it. |
| Remove the program | Add/Remove Programs. The database, backups and settings are deliberately **not** deleted. |

## 7. Command-line options of `scripts\Install-Server.ps1`

Run from an administrator PowerShell: `& "C:\Program Files\DiagnosticClinicManager\Server\scripts\Install-Server.ps1" [options]`

| Option | Meaning |
|---|---|
| `-PostgresInstallerPath <file>` | Use this PostgreSQL installer instead of downloading one. |
| `-PostgresSuperPassword <SecureString>` | Existing PostgreSQL's `postgres` password (otherwise you are asked). |
| `-ExtraHostNames a,b` | Additional DNS names to put in the certificate. |
| `-FirewallRemoteAddress` | Who may connect to port 5443 (default `LocalSubnet`). |
| `-NoFirewallRule` | Do not touch the firewall. |
| `-ReissueCertificate` | Create a new HTTPS certificate. |
| `-SkipPreUpgradeBackup` | Upgrade without the safety dump (not recommended). |
| `-HttpsPort`, `-HttpPort`, `-DatabaseName`, `-TimeZoneId` | Override the defaults (5443, 5080, `clinic`, `Asia/Kabul`). |

## 8. Final acceptance checklist

- [ ] Server PC has a fixed address; sleep/hibernate disabled (Power options → never).
- [ ] Setup finished with "Server is running"; secrets saved off-PC and `INSTALL-SECRETS.txt` deleted.
- [ ] Admin password changed; Clinic Settings filled in; real users created.
- [ ] Every clinic PC connects (Trust This Certificate done) and can sign in.
- [ ] A manual backup was created and a copy placed on an external disk.
- [ ] Restart the server PC once: the service comes back by itself and the clients reconnect.
- [ ] VPN / remote access agreed and tested by the support person.
