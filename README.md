# StepPilot

StepPilot ist eine mandantenfähige Webanwendung zur digitalen Mitarbeiter- und Schichtplanung. Das Projekt wurde als praxisnahes Portfolio-Projekt für moderne Personalplanung entwickelt und bildet typische Abläufe aus Unternehmen ab.

## Funktionsumfang

- Mitarbeiterverwaltung
- Standorte und Abteilungen
- Qualifikationen
- Schichtvorlagen und Schichtpräferenzen
- Wochenbasierte Dienstplanung
- Automatische Schichtplanung mit Konfliktprüfung
- Veröffentlichung von Dienstplänen
- Persönliche Ansicht „Meine Schichten“
- Verfügbarkeiten
- Urlaubs- und Abwesenheitsverwaltung
- Offene Schichten und freiwillige Übernahmeanfragen
- Schichttausch zwischen Mitarbeitern
- Genehmigungsworkflows für Planung und Verwaltung
- In-App-Benachrichtigungen
- Handlungszentrum für offene Personalentscheidungen
- Planqualitätsprüfung
- Auswertungen zu Soll-/Ist-Stunden, Auslastung und Besetzungsgrad
- CSV-Export für Auswertungen
- Dark Mode und Light Mode
- Rollen- und mandantenbasierte Zugriffskontrolle

## Rollen

StepPilot verwendet folgende Rollen:

- **SuperAdmin** – verwaltet Unternehmen und den privaten Administrations-/Testbereich
- **Owner** – Unternehmensverantwortlicher mit umfangreichen Verwaltungsrechten
- **Admin** – Verwaltung von Mitarbeitern und Benutzerkonten
- **Planner** – operative Schicht- und Personalplanung
- **Employee** – persönlicher Mitarbeiterbereich für eigene Schichten, Verfügbarkeiten, offene Schichten und Tauschanfragen

Unternehmen können sich nicht selbst registrieren. Neue Unternehmen werden ausschließlich durch den SuperAdmin angelegt.

## Sicherheit und Mandantentrennung

Unternehmensdaten werden über `CompanyId` voneinander getrennt. Geschützte Bereiche verwenden Rollenprüfungen und den `TenantGuard`, damit normale Benutzer ausschließlich Daten ihres eigenen Unternehmens aufrufen können.

Zusätzlich prüft die zentrale Autorisierung, ob Benutzerkonto und Unternehmen aktiv sind. Für Nicht-Entwicklungsumgebungen werden detaillierte Blazor-Fehler deaktiviert und grundlegende HTTP-Sicherheitsheader gesetzt.

## Technologie

- ASP.NET Core / Blazor Web App
- C#
- Entity Framework Core
- SQL Server / LocalDB
- ASP.NET Core Identity
- MudBlazor

## Lokale Entwicklung

Voraussetzungen:

- aktuelle .NET-SDK-Version passend zum Projekt
- SQL Server LocalDB oder eine kompatible SQL-Server-Instanz
- Visual Studio oder eine andere .NET-IDE

Nach dem Klonen des Repositories:

```bash
dotnet restore
```

Datenbankmigrationen anwenden:

```powershell
Update-Database
```

Alternativ über die .NET CLI, wenn das EF-Tool installiert ist:

```bash
dotnet ef database update
```

Anschließend kann das Projekt gestartet werden.

## Projektstatus

Der aktuelle Stand ist als **Portfolio-/Version-1.0-Stand** gedacht. Die Kernprozesse der Mitarbeiter- und Schichtplanung sind umgesetzt. Für einen produktiven SaaS-Einsatz wären zusätzlich unter anderem Deployment-Konfiguration, echtes E-Mail-Versenden, automatisierte Tests, Monitoring, Backup-Konzept und weitere Betriebs-/Datenschutzmaßnahmen erforderlich.

## Ziel des Projekts

StepPilot zeigt den Aufbau einer mehrbenutzerfähigen Business-Anwendung mit Authentifizierung, Rollen, Mandantentrennung, Datenbankzugriff und realistischen Personalplanungsprozessen.
