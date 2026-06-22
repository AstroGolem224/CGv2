# CGv2 — Windows Activity Tracker — Design

- **Datum:** 2026-06-22
- **Repo:** https://github.com/AstroGolem224/CGv2 (public, leer, greenfield)
- **Status:** Design approved, Spec-Review ausstehend

## 1. Zweck

Zeigt für die **letzten 10 Tage** auf einem Windows-PC:
- wann der PC **angeschaltet** wurde (erster Boot des Tages),
- wann er **ausgeschaltet** wurde (letztes Shutdown des Tages),
- wann er im Fenster **11:00–13:00 gesperrt** war (Workstation Lock).

Ausgabe als **kopierbare Tabelle**, damit Matthias die Zeiten leicht in eine
Zeitmanagement-App überträgt. App mit ansprechendem ("coolem") Design.

## 2. Harte Constraints

| Constraint | Konsequenz |
|---|---|
| **Kein Admin** auf Zielsystem | Security-Log (4800/4801) nicht lesbar → Lock-Events müssen selbst mitgeschnitten werden. System-Log (Boot/Shutdown) ist ohne Admin lesbar. |
| **Keine Installation** | Eine portable EXE, einfach starten. Kein MSI, kein Setup. Daten neben der EXE. |
| **Build per Cross-Compile von Linux** (CachyOS) | Stack muss `GOOS`/`dotnet publish -r win-x64` von Linux unterstützen. Windows-Glue auf Linux nicht lauffähig → dort nur Build + Logik-Tests + HTML-Preview. |
| **Tray-Agent + Autostart** | Hintergrundprozess nötig, damit Sperren während 11–13h erfasst werden. Autostart ohne Admin/Install. |

## 3. Stack-Entscheidung

**Gewählt: .NET 10 — WinForms-Shell + WebView2-UI.**

Begründung:
- **Lock-Erfassung ohne Admin** = `Microsoft.Win32.SystemEvents.SessionSwitch`
  (`SessionSwitchReason.SessionLock` / `SessionUnlock`) — wenige Zeilen, kein
  Admin. Das ist der kritische Pfad; in Go wären es ~100 Zeilen WTS-Syscalls.
- **Boot/Shutdown** = `System.Diagnostics.Eventing.Reader.EventLogReader` auf dem
  System-Log — ohne Admin lesbar, native API, kein Shell-out.
- **Cooles Design blind treffbar** = HTML/CSS/JS in WebView2; HTML lässt sich auf
  Linux im Browser vorab prüfen.
- **Cross-Compile von Linux** = `dotnet publish -c Release -r win-x64
  --self-contained -p:PublishSingleFile=true` erzeugt **eine portable EXE** ohne
  .NET-Abhängigkeit auf dem Ziel.

Verworfen:
- **Go + go-webview2 + systray**: winziges Binary, aber Lock-Detection via
  WTS-Syscalls deutlich mehr/riskanter Code am kritischen Punkt.
- **Tauri (Rust+Web)**: kleinste/hübscheste Variante, aber Cross-Build von Linux
  ist Krampf + Rust-Session-Plumbing.

## 4. Architektur

Eine portable EXE, läuft ohne Admin/Install, Daten als JSON **neben der EXE**
(Fallback `%APPDATA%\CGv2\` wenn EXE-Ordner nicht schreibbar).

```
┌────────────────────────── CGv2.exe (single-file, self-contained) ──────────────────────────┐
│                                                                                              │
│  TrayAgent (WinForms NotifyIcon, kein Taskbar-Fenster)                                        │
│   ├─ Menü: Anzeigen · Autostart [an/aus] · Beenden                                            │
│   ├─ startet LockLogger beim App-Start                                                        │
│   └─ öffnet bei "Anzeigen" das MainWindow                                                     │
│                                                                                              │
│  LockLogger  ──SessionSwitch──▶  hängt {ts, type:"lock|unlock"} an  lock-log.json            │
│                                                                                              │
│  EventLogReader  ──System-Log──▶  Boot/Shutdown-Events der letzten 10 Tage (live bei Anzeige) │
│                                                                                              │
│  Aggregator (pure, ohne Windows-API)                                                          │
│   Input: List<RawEvent> (Boot/Shutdown/Lock/Unlock)                                           │
│   Output: List<DayRow> (Datum, An, Aus, LockIntervalle∩[11,13))                               │
│   ⇒ auf Linux unit-testbar                                                                     │
│                                                                                              │
│  MainWindow (WebView2)                                                                        │
│   lädt eingebettetes HTML/CSS/JS, bekommt DayRows als JSON,                                    │
│   rendert dunkle Tabelle + "Kopieren als TSV" (Zeile + ganze Tabelle)                          │
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```

## 5. Datenquellen (exakt)

### 5.1 Boot / Shutdown — System-Log (kein Admin)
- **Boot:** Provider `Microsoft-Windows-Kernel-General`, **Event ID 12** (System
  gestartet, enthält präzisen Startzeitpunkt).
- **Shutdown (sauber):** `Microsoft-Windows-Kernel-General`, **Event ID 13**.
- **Shutdown (unerwartet):** `EventLog`, **Event ID 6008** (letzter Shutdown war
  unerwartet) — als Fallback/Marker.
- **User-initiierter Shutdown/Restart:** `User32`, **Event ID 1074** (optional, für
  Kontext).
- Query: XPath/Zeitfilter auf die letzten 10 Tage, Log `"System"`.

> Primär 12/13. 6008/1074 nur ergänzend, falls 13 fehlt (harter Power-Loss).

### 5.2 Lock / Unlock — eigener Mitschnitt (kein Admin)
- `Microsoft.Win32.SystemEvents.SessionSwitch` Event.
- `SessionSwitchReason.SessionLock` → `{ts, "lock"}`
- `SessionSwitchReason.SessionUnlock` → `{ts, "unlock"}`
- Persistiert in `lock-log.json` (append, dedupe identischer ts).
- **Auto-Save pro Event:** jeder Lock/Unlock wird **sofort** geschrieben
  (`File.WriteAllText`), nicht im RAM gepuffert → App-Schließen/Absturz/Reboot
  verliert nichts. JSON statt CSV gewählt, weil es **Paare** mit Zeitstempel sind
  (strukturiert, `.bak`-Fallback bei Korruption). CSV gibt es separat als Export
  (§7), nicht als internen Speicher.
- **Historie erst ab erstem App-Start** — kein rückwirkender Zugriff ohne Admin.

## 6. Aggregations-Regeln (Aggregator, pure logic)

Pro Kalendertag der letzten 10 Tage (lokale Zeit):
- **An** = frühester Boot-Zeitpunkt (ID 12) des Tages. Kein Boot → "–" (PC lief
  schon / aus).
- **Aus** = spätester Shutdown (ID 13, sonst 6008/1074) des Tages. Keiner → "–"
  (lief durch / unbekannt).
- **Gesperrt 11–13h** = alle Lock→Unlock-Intervalle, geschnitten mit `[11:00,
  13:00)`. Pro Intervall Anzeige `HH:MM–HH:MM`; zusätzlich Summe der Minuten im
  Fenster. Offener Lock ohne Unlock → bis 13:00 (bzw. Tagesende) clampen, als
  "offen" markieren. Keine Lock-Daten (vor Install) → "—" (keine Daten), klar
  unterschieden von "00:00" (lief, nie gesperrt).
- **Arbeitszeit (h)** = `(Aus − An) − gesperrte Zeit des ganzen Tages`, in
  Dezimalstunden. „Gesperrte Zeit" hier = Summe **aller** Lock→Unlock-Dauern des
  Tages (nicht nur 11–13h), geclippt auf `[An, Aus]`. Laufender Tag: Ende = jetzt
  (Arbeitszeit bisher). Nur berechenbar bei vorhandenem An **und** (Aus oder
  laufend) **und** Lock-Daten → sonst „—". Nie negativ (clamp ≥ 0). Caveat:
  korrekt nur an Tagen, an denen der Tray-Agent durchlief (sonst Sperrzeit
  untergezählt → Arbeitszeit zu hoch).

Edge Cases:
- Lock vor 11:00, Unlock nach 13:00 → Intervall = ganzes Fenster (11:00–13:00).
- Mehrere Lock/Unlock-Paare im Fenster → alle gelistet.
- Unlock ohne vorausgehenden Lock (App-Neustart mitten in Lock) → ignorieren,
  defensiv.

## 7. UI-Spezifikation (WebView2)

> **Freigegebenes Mockup:** `ui-mockup.html` (neben dieser Datei) — Flat-Dark
> "Instrument-Console", Fonts Bricolage Grotesque + IBM Plex Mono, Akzent amber.
> Dieses HTML ist die Basis des WebView2-Frontends, nicht nur Skizze.

- **Echtes App-Fenster** (kein Browser-Tab), dunkles, modernes Design.
- Tabelle, 10 Zeilen (neueste oben):
  `Datum │ An │ Aus │ Arbeit (h) │ Gesperrt 11–13h │ Σ Min`.
- Leere/„keine Daten"-Zellen visuell dezent (grau, "—"), echte Nullzeiten normal.
- Arbeitszeit-Spalte grün hervorgehoben (Kernzahl für die Zeit-App).
- **Kopieren:** Button „Kopieren (TSV)" → kompletter Tab-getrennter Block
  (inkl. Arbeit) in die Zwischenablage (klebt sauber in Excel/Zeit-App). Pro
  Zeile ein kleiner Copy-Button.
- **CSV-Export:** Button „CSV" → SaveFileDialog, schreibt die 10-Tage-Tabelle
  (inkl. Arbeitszeit) als `;`-getrennte CSV (Dezimal-Komma, UTF-8-BOM) für
  Excel/LibreOffice/Zeit-App. Im Browser-Fallback (kein WebView2) Blob-Download.
- Refresh-Button (liest System-Log + lock-log neu).
- Statuszeile: „Lock-Mitschnitt aktiv seit &lt;Datum&gt;" + Hinweis falls Tage ohne
  Lock-Historie.

## 8. Autostart ohne Installation

- Opt-in Toggle im Tray-Menü.
- An → legt `CGv2.lnk` in `shell:startup`
  (`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`). Kein Admin, kein
  Installer, nur ein Shortcut-File, das auf die aktuelle EXE zeigt.
- Aus → löscht den Shortcut.
- Zweck: App läuft beim Login, damit Sperren in 11–13h erfasst werden.

## 9. Fehlerbehandlung

| Fall | Verhalten |
|---|---|
| WebView2-Runtime fehlt | Meldung + Fallback: Report als temp-HTML im Default-Browser öffnen. |
| EXE-Ordner nicht schreibbar | Daten nach `%APPDATA%\CGv2\` ausweichen. |
| `lock-log.json` korrupt | Backup `.bak`, frisch starten, Hinweis in Statuszeile. |
| System-Log-Zugriff verweigert (unerwartet) | Boot/Shutdown leer, Tabelle trotzdem mit Lock-Daten zeigen, Warnung. |
| Mehrfachstart der EXE | Single-Instance (Mutex), zweiter Start bringt Fenster nach vorn. |

## 10. Build / Publish

```
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
- Ergebnis: eine `CGv2.exe`, portabel, keine .NET-Installation auf Ziel nötig.
- Cross-Build von Linux: ja (Restore zieht windows-desktop ref packs).
- Einzige Ziel-Abhängigkeit: **WebView2-Runtime** (auf Win10/11 i.d.R.
  vorinstalliert; Fallback siehe §9).

## 11. Verifikation

| Was | Wo | Wie |
|---|---|---|
| Kompiliert | Linux | `dotnet publish` läuft durch |
| Aggregator-Logik | Linux | xUnit: synthetische RawEvents → erwartete DayRows (Edge Cases §6) |
| Design | Linux | HTML/CSS im Browser öffnen, Tabelle mit Dummy-JSON |
| SessionSwitch-Logging | Windows | App starten, Win+L, Unlock → lock-log.json prüfen |
| Event-Log-Read | Windows | Tabelle zeigt korrekte Boot/Shutdown der letzten Tage |
| Autostart | Windows | Toggle → Shortcut in Startup, nach Re-Login läuft Agent |

## 12. Bekannte Grenzen

- **Lock-Historie erst ab Installation** — die ersten ~10 Tage Spalte „Gesperrt"
  teils leer („—"). Boot/Shutdown sofort 10 Tage da.
- Agent muss **während 11–13h laufen** (→ Autostart), sonst Sperren verpasst.
- Wenn der PC pro Tag mehrfach an/aus geht, zeigt die Tabelle nur **erstes An /
  letztes Aus** (Default). Voll-Liste pro Tag = out of scope (siehe §13).
- WebView2-Runtime-Abhängigkeit (Fallback vorhanden).

## 13. Out of Scope (YAGNI)

- Volle An/Aus-Liste pro Tag (mehrere Sessions) — nur erstes/letztes.
- Sperren außerhalb 11–13h.
- Sync/Cloud/Direkt-API zu konkreter Zeit-App — TSV-Copy + CSV-Export reichen.
- Direkter Admin-Backfill aus Security-Log — nur falls Matthias es später will.

## 14. Sicherheit / Privacy

- Repo CGv2 ist **public**. Code committen ok. **Generierte Daten
  (`lock-log.json`, Reports) NICHT committen** → `.gitignore` deckt `*.json`
  (außer config), `bin/`, `obj/`, publish-Output.
- Keine Telemetrie, kein Netzwerk. Alles lokal.
