<div align="center">

<img src="wwwroot/images/steppilot-logo.png" alt="StepPilot Logo" width="150" />

# StepPilot

### Workforce Management, Schichtplanung und Zeiterfassung für Unternehmen

**Planen · Importieren · Prüfen · Erfassen · Auswerten**

![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-Application-512BD4?style=flat-square&logo=csharp)
![EF Core](https://img.shields.io/badge/Entity%20Framework-Core-512BD4?style=flat-square)
![SQL Server](https://img.shields.io/badge/SQL-Server-CC2927?style=flat-square&logo=microsoftsqlserver)
![MudBlazor](https://img.shields.io/badge/UI-MudBlazor-594AE2?style=flat-square)
![CI](https://img.shields.io/badge/CI-Release%20Build-success?style=flat-square)

</div>

---

## Über StepPilot

**StepPilot** ist eine mandantenfähige Webanwendung für Mitarbeiterverwaltung, Schichtplanung, Arbeitszeit-Compliance und Zeiterfassung. Die Anwendung bildet typische Abläufe von kleinen Teams bis zu größeren Unternehmensstrukturen ab und verbindet Stammdaten, Standorte, Abteilungen, Qualifikationen, Dienstplanung, Self-Service, Zeitbuchungen und Management-Auswertungen in einer Oberfläche.

## Kernfunktionen

| Bereich | Funktionen |
| --- | --- |
| **Organisation** | Unternehmen, Standorte, Abteilungen, Mitarbeiter, Qualifikationen und Bulk-Aktionen |
| **Datenimport** | CSV/TXT/XLSX, Spaltenzuordnung, Validierung, Duplikaterkennung, Vorschau, Batch-Import und Import-Historie |
| **Dienstplanung** | Wochenplanung, Schichtvorlagen, Standort-/Abteilungsbezug, Qualifikationen, automatische Planung und Konfliktprüfung |
| **Bedarfsplanung** | Personalbedarf nach Wochentag, Standort, Abteilung und Qualifikation |
| **Compliance** | Zentrale Regel-Engine für Arbeitszeit, Pausen, Ruhezeiten sowie Sonn-/Feiertagsprüfungen mit konfigurierbarem Unternehmensprofil |
| **Zeiterfassung** | Arbeitsbeginn/-ende, Pausen, Soll-/Ist-Vergleich, Zeitkonto, Korrekturen mit Begründung und Freigabeworkflow |
| **Self-Service** | Eigene Schichten, Verfügbarkeiten, offene Schichten, Schichttausch und eigene Zeiterfassung |
| **Management** | Handlungszentrum, Planqualität, Audit-Log, Unternehmensprofil und Auswertungen |
| **Security** | ASP.NET Core Identity, Rollenmodell, Mandantentrennung, TenantGuard, Antiforgery, HTTPS und Sicherheitsheader |

## Rollen

| Rolle | Aufgabe |
| --- | --- |
| **SuperAdmin** | Unternehmen und Plattformadministration |
| **Owner** | Unternehmensverantwortung und vollständige Unternehmensverwaltung |
| **Admin** | Stammdaten, Benutzer, Compliance und operative Verwaltung |
| **Planner** | Dienst-, Bedarfs- und Personalplanung sowie operative Zeitprüfung |
| **Employee** | Eigene Schichten, Verfügbarkeiten, Schichtanfragen und Zeiterfassung |

## Arbeitszeit & Compliance

StepPilot unterstützt bei der Prüfung von Arbeitszeit- und Planungsregeln. Die Deutschland-orientierte Regelbasis berücksichtigt unter anderem tägliche Arbeitszeit, Pausen, Ruhezeiten sowie Sonn- und Feiertagsarbeit. Unternehmensspezifische Ausnahmegrundlagen können dokumentiert werden.

**Wichtig:** StepPilot ist keine Rechtsberatung und gibt keine pauschale Garantie für Rechtskonformität. Tarifverträge, Betriebsvereinbarungen, Branchenregeln, besondere Beschäftigtengruppen und gesetzliche Ausnahmen können zu abweichenden Anforderungen führen. Vor einem produktiven Einsatz muss das konkrete Regelprofil für Unternehmen, Branche und Einsatzland geprüft werden.

Siehe [`docs/LEGAL_READINESS.md`](docs/LEGAL_READINESS.md).

## Datenschutz & Sicherheit

StepPilot verarbeitet personenbezogene Mitarbeiter-, Planungs- und Arbeitszeitdaten. Für einen produktiven Betrieb müssen Betreiber insbesondere Rechtsgrundlagen, Informationspflichten, Löschfristen, Auftragsverarbeitung, TOMs, Backup/Restore, Incident Response und Berechtigungskonzepte für ihre konkrete Umgebung festlegen.

Technische Sicherheits- und Meldehinweise stehen in [`SECURITY.md`](SECURITY.md).

## Qualitätssicherung

Das Repository enthält eine GitHub-Actions-CI. Bei Pushes und Pull Requests auf `master` wird die Anwendung mit .NET 10 wiederhergestellt und im **Release-Modus mit Warnungen als Fehler** gebaut. Dadurch werden Compilerwarnungen nicht stillschweigend als produktionsreif akzeptiert.

Vor einem Release gehören zusätzlich ein Migrationstest und ein manueller End-to-End-Test zum Release-Gate.

## Technologie

`ASP.NET Core 10` · `Blazor` · `C#` · `Entity Framework Core` · `SQL Server` · `ASP.NET Core Identity` · `MudBlazor`

## Projektstruktur

```text
StepPilot/
├── .github/workflows/  # CI
├── Components/         # Razor-Komponenten, Layout und Seiten
├── Data/               # DbContext, Identity und Datenzugriff
├── Models/             # Domänenmodelle
├── Services/           # Geschäftslogik, Planung, Compliance und Zeiterfassung
├── docs/               # Betriebs- und Compliance-Dokumentation
├── wwwroot/            # Styles, Assets und Branding
├── Program.cs          # Anwendungskonfiguration
├── SECURITY.md         # Security Policy
└── README.md
```

## Lokal starten

Voraussetzungen: .NET 10 SDK, SQL Server/LocalDB und Visual Studio oder eine andere .NET-IDE.

```bash
git clone https://github.com/Pexiz96/StepPilot.git
cd StepPilot
dotnet restore
dotnet build
```

Datenbankmigration in der Visual-Studio-Paket-Manager-Konsole:

```powershell
Update-Database
```

Anschließend:

```bash
dotnet run
```

## Produktions-Release-Gate

Ein produktiver Stand ist erst erreicht, wenn CI und lokaler Release-Build fehlerfrei laufen, alle Migrationen auf produktionsnahen Daten getestet wurden, Mandantentrennung und Rollen geprüft sind, Backup/Restore getestet wurde und die kunden-/betreiberspezifischen Datenschutz-, Vertrags- und Compliance-Dokumente vollständig sind. Die vollständige Checkliste steht in [`docs/LEGAL_READINESS.md`](docs/LEGAL_READINESS.md).

---

<div align="center">

**StepPilot – Workforce Management mit Überblick.**

</div>
