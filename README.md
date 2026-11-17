# Vemas.MailRelay

SMTP-Relay für **SQL Server Database Mail** und **SSRS**, die sich nicht modern an Exchange Online
anmelden können. Beide liefern Mails per SMTP mit Basic Auth an den Dienst; der Dienst versendet
sie über **Microsoft Graph** mit OAuth (Client Credentials, Client Secret).

```
SQL Server (Database Mail) ─┐
                            ├─ SMTP 587 + AUTH LOGIN ─►  Vemas.MailRelay  ─► Microsoft Graph ─► Exchange Online
SSRS (Abo-Versand) ─────────┘                            │
                                                         ▼
                                          NextGen-DB, Schema [mailrelay]
                                          Queue · Protokoll · Neu senden
```

- .NET 10, Windows-Dienst, eine self-contained EXE (keine .NET-Installation auf dem Server nötig)
- Queue und Protokoll in der NextGen-Datenbank (Schema `mailrelay`)
- Weboberfläche (Port 8587, Windows-Anmeldung) für Protokoll, Neu senden, Profile, SMTP-Benutzer, Testmail
- Meldungen in der Windows-Ereignisanzeige (Quelle `Vemas.MailRelay`)

---

## Inhalt

1. [Entra ID: App-Registrierung](#1-entra-id-app-registrierung)
2. [Exchange Online: Berechtigungen und Absender einschränken](#2-exchange-online-berechtigungen-und-absender-einschränken)
3. [Datenbank installieren](#3-datenbank-installieren)
4. [EXE erstellen](#4-exe-erstellen)
5. [Dienst installieren](#5-dienst-installieren)
6. [Profil und SMTP-Benutzer anlegen](#6-profil-und-smtp-benutzer-anlegen)
7. [SQL Server Database Mail umstellen](#7-sql-server-database-mail-umstellen)
8. [SSRS umstellen](#8-ssrs-umstellen)
9. [Betrieb](#9-betrieb)
10. [Fehlerbilder](#10-fehlerbilder)
11. [Kundenzugriff aus NextGen](#11-kundenzugriff-aus-nextgen)

---

## 1. Entra ID: App-Registrierung

Pro Tenant ein Profil, pro Profil eine App-Registrierung.

1. Entra Admin Center → **Anwendungen** → **App-Registrierungen** → **Neue Registrierung**:

   | Feld | Wert |
   |---|---|
   | Name | `Vemas.MailRelay` (frei wählbar) |
   | Unterstützte Kontotypen | Nur Konten in diesem Organisationsverzeichnis (ein Mandant) |
   | Umleitungs-URI | leer lassen (für Client Credentials nicht nötig) |

2. **Zertifikate & Geheimnisse** → **Neuer geheimer Clientschlüssel** (max. 24 Monate). Den **Wert** (nicht die ID)
   sofort kopieren und das Ablaufdatum notieren.
3. **Übersicht**: Anwendungs-ID (Client-ID) und Verzeichnis-ID (Tenant-ID) notieren.
4. Berechtigungen: siehe Abschnitt 2 – entweder Exchange-RBAC (empfohlen) **oder** `Mail.Send` und `Mail.ReadWrite`
   (Anwendung) in Entra.

Zuordnung zum Profil in der Weboberfläche:

| Feld im Profil | Herkunft in Entra |
|---|---|
| Tenant-ID | Übersicht → Verzeichnis-ID (Mandant) |
| Client-ID | Übersicht → Anwendungs-ID (Client) |
| Client Secret | Wert des geheimen Clientschlüssels |
| Secret gültig bis | Ablaufdatum des geheimen Clientschlüssels |
| Standard-Postfach | Postfach, das sich anmeldet und versendet (Konto A) – muss im Exchange-Bereich aus Abschnitt 2 liegen |

## 2. Exchange Online: Berechtigungen und Absender einschränken

Die App braucht zwei Rechte auf die Absender-Postfächer:

| Recht | Wofür |
|---|---|
| `Mail.Send` | Versand aller Mails |
| `Mail.ReadWrite` | Mails über ca. 3 MB: Der Dienst legt sie als Entwurf an, lädt die Anhänge hoch und sendet den Entwurf. Ohne dieses Recht schlagen genau diese Mails mit `403 ErrorAccessDenied` fehl. |

> **Wichtig:** Beide Anwendungsberechtigungen gelten ohne Einschränkung für **jedes** Postfach des Tenants,
> `Mail.ReadWrite` erlaubt zusätzlich das Lesen. Deshalb beide über Exchange auf die benötigten Postfächer begrenzen.

### Empfohlen: RBAC for Applications

Bei dieser Variante wird in Entra **keine** `Mail.Send`- oder `Mail.ReadWrite`-Berechtigung erteilt (Entra- und
Exchange-Rechte addieren sich – eine Entra-Zustimmung würde die Einschränkung aushebeln).

```powershell
Connect-ExchangeOnline

# Objekt-ID der Unternehmensanwendung (Entra > Unternehmensanwendungen > Vemas.MailRelay), NICHT die der App-Registrierung
New-ServicePrincipal -AppId "<Client-ID>" -ObjectId "<Objekt-ID Unternehmensanwendung>" -DisplayName "Vemas.MailRelay"

# Bereich: nur das Standard-Postfach des Profils (Konto A)
New-ManagementScope -Name "MailRelay-Postfach" `
    -RecipientRestrictionFilter "PrimarySmtpAddress -eq 'relay@firma.de'"

New-ManagementRoleAssignment -App "<Client-ID>" -Role "Application Mail.Send"      -CustomResourceScope "MailRelay-Postfach"
New-ManagementRoleAssignment -App "<Client-ID>" -Role "Application Mail.ReadWrite" -CustomResourceScope "MailRelay-Postfach"

# Prüfen
Test-ServicePrincipalAuthorization -Identity "<Client-ID>" -Resource "relay@firma.de"
```

Die App braucht nur Rechte auf das **Standard-Postfach** (Konto A), auch im Modus „Senden als“.
Arbeiten mehrere Profile im selben Tenant, statt des Filters eine Gruppe mit allen Standard-Postfächern verwenden:
`-RecipientRestrictionFilter "MemberOfGroup -eq '<DN der Gruppe>'"`. Änderungen können bis zu 2 Stunden dauern, bis sie wirken.

### Modus „Senden als“: Absender freigeben

Im Modus „Senden als“ meldet sich das Standard-Postfach (A) an, als Absender steht das Postfach aus der Mail (B) im
Kopf. Exchange verschickt die Mail nur, wenn A das Recht „Senden als“ auf B hat:

```powershell
Add-RecipientPermission -Identity "b@firma.de" -Trustee "relay@firma.de" -AccessRights SendAs -Confirm:$false

# Optional: Kopie zusätzlich in „Gesendete Elemente“ von B (sonst nur bei A)
Set-Mailbox -Identity "b@firma.de" -MessageCopyForSentAsEnabled $true
```

Fehlt das Recht, steht im Verlauf der Mail `ErrorSendAsDenied`.

### Alternative: Berechtigungen in Entra + Application Access Policy

`Mail.Send` und `Mail.ReadWrite` (Anwendung) in Entra erteilen, Administratorzustimmung geben, dann:

```powershell
New-ApplicationAccessPolicy -AppId "<Client-ID>" -PolicyScopeGroupId "relay@firma.de" `
    -AccessRight RestrictAccess -Description "Vemas.MailRelay"
```

Microsoft ersetzt Application Access Policies schrittweise durch RBAC for Applications.

## 3. Datenbank installieren

`install/Install-Database.sql` in SSMS ausführen (db_owner, für `CREATE LOGIN` zusätzlich securityadmin):

- **Einzelinstallation beim Kunden:** in der NextGen-Datenbank
- **Cloud (viele Kunden auf einem SQL Server):** in einer eigenen Relay-Datenbank

Vorher in Abschnitt 9 des Skripts das Dienstkonto eintragen:

```sql
DECLARE @ServiceUser SYSNAME = N'CONTOSO\svc-mailrelay';
```

Das Skript ist wiederholbar und legt an:

| Objekt | Zweck |
|---|---|
| `mailrelay.Profile` | Versandprofile (Tenant, Client-ID, verschlüsseltes Secret, Absender-Modus) |
| `mailrelay.SmtpUser` | SMTP-Logins mit Passwort-Hash und IP-Freigabe |
| `mailrelay.AllowedSender` | erlaubte Absender je Profil (`adresse@firma.de` oder `*@firma.de`) |
| `mailrelay.Message` | Queue und Protokoll inkl. MIME-Inhalt |
| `mailrelay.Attempt` | alle Versuche und manuellen Aktionen |
| `mailrelay.Resend` / `Cancel` / `Cleanup` | Neu senden, Abbrechen, Aufräumen |
| `mailrelay.Customer` / `CustomerLogin` | Kunden und die SQL-Server-Logins ihrer NextGen-Instanz (Kundenzugriff aus NextGen, Abschnitt 11) |
| `mailrelay.TestMailRequest` | Testmail-Aufträge aus NextGen |
| Row-Level Security `mailrelay.CustomerPolicy` | Datentrennung je Kunde |
| Schema `mailrelay_kunde` | Views und Prozeduren für den Kundenzugriff |

Das Dienstkonto erhält nur `SELECT, INSERT, UPDATE, DELETE, EXECUTE` auf das Schema – keine DDL-Rechte – und wird
Mitglied der Rolle `mailrelay_service` (sieht trotz Row-Level Security alle Kunden).

### Update einer bestehenden Installation

Wurde die Datenbank mit der **ersten Version** eingerichtet (ohne Kundenzugriff), genügt
`install/Update-Database-1.1.sql`. Es enthält nur die Neuerungen (Kunden, neue Spalten, Testmail-Aufträge,
Row-Level Security, Kunden-Schnittstelle) und lässt vorhandene Daten unverändert. Am Ende des Skripts das
Dienstkonto eintragen – dasselbe wie bei der Erstinstallation:

```sql
DECLARE @ServiceUser SYSNAME = N'CONTOSO\svc-mailrelay';
```

> **Wichtig:** Ohne die Rolle `mailrelay_service` sieht der Dienst nach dem Update wegen der Row-Level Security
> keine Daten mehr. Das Skript bricht deshalb ab, solange der Platzhalter nicht ersetzt ist.

Alternativ kann auch `Install-Database.sql` erneut ausgeführt werden; es ist ebenfalls wiederholbar und führt zum
selben Ergebnis.

## 4. EXE erstellen

```powershell
dotnet publish src/Vemas.MailRelay -c Release
```

Das Ergebnis landet direkt im Ordner `install`, neben den Skripten: `Vemas.MailRelay.exe` (self-contained,
win-x64), `appsettings.json`, `Install-Service.ps1`, `Uninstall-Service.ps1` und `Install-Database.sql`.
Diesen Ordner komplett auf den Server kopieren, z.B. nach `C:\VemasDienste\VemasMailRelay`.
EXE und `appsettings.json` im Ordner `install` sind von Git ausgenommen.

## 5. Dienst installieren

### Voraussetzungen für das Dienstkonto

- Recht **„Als Dienst anmelden“** (Lokale Sicherheitsrichtlinie oder GPO → Zuweisen von Benutzerrechten).
  Ohne dieses Recht startet der Dienst mit Fehler 1069.
- SQL-Zugriff (vergibt `Install-Database.sql`).

### appsettings.json

Die `appsettings.json` neben der EXE im Dienstordner anpassen:

| Schlüssel | Bedeutung |
|---|---|
| `ConnectionStrings:MailRelay` | NextGen-Datenbank, Windows-Authentifizierung |
| `Web:AdminGroup` | AD-Gruppe mit Zugriff auf die Weboberfläche, z.B. `CONTOSO\MailRelay-Admins` |
| `Smtp:Port` / `Web:Port` | Standard 587 / 8587 |
| `Smtp:CertificateThumbprint` | optional: Zertifikat (LocalMachine\My) für STARTTLS |
| `Smtp:RequireTls` | optional: AUTH nur nach STARTTLS |
| `Web:CertificateThumbprint` | optional: Zertifikat für HTTPS |
| `Sending:RetryDelaysMinutes` | Wartezeiten vor den Wiederholungen, Standard `[1, 5, 15, 60, 240]` |
| `Sending:MaxParallel` | parallele Versendungen, Standard 4 |
| `Maintenance:MimeRetentionDays` | MIME-Inhalt löschen nach, Standard 30 Tage |
| `Maintenance:LogRetentionDays` | Protokoll löschen nach, Standard 365 Tage |
| `Maintenance:SecretExpiryWarningDays` | Warnung vor Secret-Ablauf, Standard 30 Tage |
| `DatabaseMail:Enabled` | Fehler an Database Mail zurückmelden (Abschnitt 7), Standard `true` |
| `DatabaseMail:ConnectionString` | Instanz mit Database Mail; leer = Instanz der Relay-Datenbank, Datenbank `msdb` |

Bei Updates die vorhandene `appsettings.json` auf dem Server nicht mit der aus dem Ordner `install` überschreiben.

### Installation

PowerShell **als Administrator**, im Dienstordner auf dem Server:

```powershell
cd C:\VemasDienste\VemasMailRelay
.\Install-Service.ps1 -ServiceAccount "CONTOSO\svc-mailrelay" -SmtpAllowedRemoteAddress "10.0.0.5","10.0.0.6"
```

Liegt die EXE neben dem Skript, läuft der Dienst direkt aus diesem Ordner – es wird nichts kopiert.
Die ersten Ausgaben zeigen, welche Ordner verwendet werden:

```
Quelle: C:\VemasDienste\VemasMailRelay
Dienst läuft aus: C:\VemasDienste\VemasMailRelay
```

Andere Ordner lassen sich ausdrücklich angeben; dann kopiert das Skript die EXE (und beim ersten Mal die
`appsettings.json`) von `-SourcePath` nach `-InstallPath`:

```powershell
.\Install-Service.ps1 -ServiceAccount "CONTOSO\svc-mailrelay" -SmtpAllowedRemoteAddress "10.0.0.5" `
    -SourcePath "D:\Download\MailRelay" -InstallPath "C:\VemasDienste\VemasMailRelay"
```

Das Skript registriert die Ereignisquelle, legt den Dienst an (Autostart, Neustart nach Absturz), erstellt
Firewall-Regeln und startet den Dienst. Für ein gMSA das Konto mit `$` angeben
(`CONTOSO\gmsa-mailrelay$`) – dann wird kein Passwort abgefragt.

Update: Dienst stoppen (`Stop-Service Vemas.MailRelay` – die laufende EXE lässt sich nicht überschreiben),
neue EXE in den Dienstordner kopieren (die `appsettings.json` behalten) und das Skript erneut ausführen;
es startet den Dienst wieder.
Deinstallation: `.\Uninstall-Service.ps1 [-RemoveFiles]`.

### Kerberos für die Weboberfläche (optional)

Läuft der Dienst unter einem Domänenkonto, für Kerberos einen SPN setzen (sonst wird NTLM verwendet, was ebenfalls funktioniert):

```powershell
setspn -S HTTP/mailrelay01 CONTOSO\svc-mailrelay
setspn -S HTTP/mailrelay01.contoso.local CONTOSO\svc-mailrelay
```

## 6. Profil und SMTP-Benutzer anlegen

Weboberfläche: `http://<server>:8587/`

1. **Profile → Neues Profil**: Tenant-ID, Client-ID, Client Secret, Ablaufdatum, Modus, Standard-Postfach.
   - **Fester Absender**: Jede Mail geht über das Standard-Postfach und mit ihm als Absender. Der ursprüngliche
     Absender wird zur Antwortadresse (Reply-To).
   - **Senden als**: Jede Mail geht über das Standard-Postfach (A), als Absender bleibt die Adresse aus der Mail (B)
     stehen. A braucht in Exchange „Senden als“ auf B (Abschnitt 2). Die Liste *Erlaubte Absender* filtert zusätzlich
     schon beim SMTP-Eingang: Andere Absender werden mit `5.7.1 Absender nicht erlaubt` abgewiesen.
2. **Testmail senden** auf der Profilseite – prüft Anmeldung und Exchange-Berechtigung. Ergebnis im Verlauf.
3. **SMTP-Benutzer → Neuer Benutzer**: Login, Passwort (mind. 12 Zeichen), Profil, erlaubte IPs der SQL-/SSRS-Server.
   Ohne IP-Eintrag ist keine Anmeldung möglich.

Secrets werden per DPAPI (Maschinenschlüssel) verschlüsselt, Passwörter als PBKDF2-Hash gespeichert.
Nach einem **Serverumzug** müssen alle Client Secrets neu eingetragen werden.

## 7. SQL Server Database Mail umstellen

SSMS → Verwaltung → Datenbank-E-Mail → Konto bearbeiten, oder per T-SQL:

```sql
EXEC msdb.dbo.sysmail_update_account_sp
    @account_name            = N'<vorhandenes Konto>',
    @email_address           = N'reports@firma.de',      -- muss zum Profil passen
    @mailserver_name         = N'mailrelay01',
    @port                    = 587,
    @enable_ssl              = 0,                        -- 1 nur mit Smtp:CertificateThumbprint
    @username                = N'sqlmail',               -- SMTP-Benutzer aus der Weboberfläche
    @password                = N'<Passwort>',
    @use_default_credentials = 0;
```

Test: `EXEC msdb.dbo.sp_send_dbmail @recipients = 'ich@firma.de', @subject = 'Test', @body = 'Test';`

Bcc-Empfänger (`@blind_copy_recipients`) werden korrekt übernommen.

### Fehler an Database Mail zurückmelden

Database Mail hält eine Mail für gesendet, sobald das Relay sie angenommen hat. Schlägt der Versand an Exchange
Online danach **endgültig** fehl, meldet der Dienst das zurück:

- die Mail erscheint in `msdb.dbo.sysmail_faileditems` statt in `sysmail_sentitems`,
- der Fehlertext steht im Database-Mail-Protokoll (`sysmail_event_log`, SSMS: Datenbank-E-Mail-Protokoll anzeigen),
  z.B. `Vemas.MailRelay [Relay-Nr. 815]: Zustellung über Exchange Online endgültig fehlgeschlagen. …`,
- wird die Mail später erfolgreich neu gesendet, steht sie wieder auf „gesendet“,
- im Verlauf der Mail im Relay erscheint die Aktion „Database Mail“ mit der Nr. der Database-Mail.

Einrichtung: `install/Install-DatabaseMail-Rueckmeldung.sql` auf der SQL-Server-Instanz mit Database Mail ausführen
(sysadmin, `@ServiceUser` = Dienstkonto). Das Skript legt `msdb.dbo.mailrelay_StatusMelden` an; das Dienstkonto darf
nur diese Prozedur ausführen, nicht direkt in die Tabellen von msdb schreiben. Liegt die Relay-Datenbank auf einer
anderen Instanz, `DatabaseMail:ConnectionString` setzen. Ist die Prozedur nicht eingerichtet, läuft der Versand
normal weiter; der Dienst schreibt höchstens einmal pro Stunde einen Hinweis ins Ereignisprotokoll.

Zuordnung: Database Mail schreibt keine eigene Kennung in die Mail. Zugeordnet wird über Betreff, Empfänger (alle
Empfänger der Database-Mail müssen Empfänger der Relay-Mail sein) und Zeit (höchstens 30 Sekunden zwischen Eingang im
Relay und Versandzeit in msdb). Bei mehreren gleichen Mails kurz hintereinander gilt die zeitlich nächste, wenn sie
eindeutig näher liegt – sonst wird nichts geändert. Mails aus SSRS oder anderen Anwendungen haben keinen Eintrag in
msdb und bleiben unberührt. Abgebrochene Mails werden nicht zurückgemeldet.

## 8. SSRS umstellen

Report Server Configuration Manager → **E-Mail-Einstellungen**:

| Feld | Wert |
|---|---|
| Absenderadresse | `reports@firma.de` (muss zum Profil passen) |
| Verwendeter SMTP-Server | `mailrelay01` |
| Authentifizierung | Benutzername und Kennwort (Basic) |
| Benutzername / Kennwort | SMTP-Benutzer aus der Weboberfläche |

Port und SSL in `rsreportserver.config` (unter `<RSEmailDPConfiguration>`), danach den SSRS-Dienst neu starten:

```xml
<SMTPServerPort>587</SMTPServerPort>
<SMTPUseSSL>False</SMTPUseSSL>
<SendUsing>2</SendUsing>
<SMTPAuthenticate>1</SMTPAuthenticate>
```

## 9. Betrieb

### Ablauf einer Mail

| Status | Bedeutung |
|---|---|
| Wartend (`Queued`) | angenommen, wird sofort versendet |
| Wird gesendet (`Sending`) | in Bearbeitung (Sperre 15 Minuten; nach Absturz wird sie wieder aufgenommen) |
| Wiederholung (`Retry`) | vorübergehender Fehler, nächster Versuch geplant |
| Gesendet (`Sent`) | von Graph angenommen, Kopie in „Gesendete Elemente“ |
| Fehlgeschlagen (`Failed`) | dauerhafter Fehler oder alle Wiederholungen erschöpft |
| Abgebrochen (`Cancelled`) | manuell abgebrochen |

- **Wiederholungen**: erster Versuch sofort, danach bis zu 5 Wiederholungen nach 1, 5, 15, 60 und 240 Minuten.
- **Ohne Wiederholung** (sofort `Failed`): ungültiges Secret, App ohne Berechtigung, unbekanntes Postfach, Absender nicht erlaubt.
- **Neu senden**: In Protokoll oder Detailansicht, auch für mehrere Mails. Setzt Status und Versuchszähler zurück.
  Möglich, solange der MIME-Inhalt gespeichert ist (Standard 30 Tage). Per SQL: `EXEC mailrelay.Resend @MessageId = 4711;`
- **Große Mails** (über ca. 2,9 MB MIME): Der Dienst legt einen Entwurf an, lädt die Anhänge einzeln bzw. per Upload-Session hoch
  (bis 150 MB) und sendet ihn dann.
- **Wichtig**: SQL Server und SSRS melden „gesendet“, sobald der Dienst die Mail angenommen hat.
  Fehler beim Versand an Exchange stehen **nur im Protokoll des Dienstes**, nicht in `sysmail_event_log` oder im SSRS-Abostatus.

### Secret/Passwort per SQL setzen

Für Automatisierung oder Notfälle können Klartextwerte direkt eingetragen werden. Der Dienst übernimmt sie
innerhalb einer Minute, verschlüsselt bzw. hasht sie und leert das Feld:

```sql
UPDATE mailrelay.Profile  SET SecretPlain   = N'<neues Secret>', SecretExpiresAt = '2028-11-15' WHERE Name  = N'Firma';
UPDATE mailrelay.SmtpUser SET PasswordPlain = N'<neues Passwort>'                                WHERE Login = N'sqlmail';
```

### Konsole statt Dienst (Fehlersuche)

`Vemas.MailRelay.exe` direkt in einer Konsole starten – alle Meldungen erscheinen im Fenster. Vorher den Dienst stoppen (Ports).

## 10. Fehlerbilder

| Meldung | Ursache / Lösung |
|---|---|
| `AADSTS7000215: Invalid client secret` | Secret falsch oder abgelaufen → neues Secret in Entra erstellen und im Profil eintragen |
| `AADSTS700016: Application ... not found` | Client-ID oder Tenant-ID falsch |
| `Graph 403 ErrorAccessDenied` / `Access is denied` | App hat keine Berechtigung für dieses Postfach → Abschnitt 2 prüfen (Gruppe, Wartezeit). Nur große Mails betroffen: `Mail.ReadWrite` fehlt |
| `Graph 404 ErrorInvalidUser` / `ResourceNotFound` | Absenderpostfach existiert nicht |
| `Client Secret kann auf diesem Server nicht entschlüsselt werden` | DB von anderem Server übernommen → Secret neu eintragen |
| `MIME-Inhalt nicht mehr vorhanden` | Aufbewahrungsfrist abgelaufen, Neu senden nicht mehr möglich |
| SMTP `535 Authentication failed` | Ereignisanzeige zeigt den Grund: Benutzer unbekannt/deaktiviert, IP nicht freigegeben, Passwort falsch |
| SMTP `5.7.1 Absender nicht erlaubt` | Absender passt nicht zum Profil „Senden als“ → Liste *Erlaubte Absender* ergänzen |
| `Graph 403 ErrorSendAsDenied` | Standard-Postfach hat kein „Senden als“-Recht auf den Absender → `Add-RecipientPermission` (Abschnitt 2) |
| Dienst startet nicht, Fehler 1069 | Dienstkonto fehlt das Recht „Als Dienst anmelden“ |
| Weboberfläche: 403 | Benutzer ist nicht in `Web:AdminGroup` (nach Gruppenänderung neu anmelden) |

## 11. Kundenzugriff aus NextGen

Zusatz zum bisherigen Verfahren: Kunden sehen in ihrer NextGen-Instanz das Protokoll ihrer Mails, senden neu,
brechen ab, fordern Testmails an und bearbeiten ihre Profile – jeweils **nur ihre eigenen Daten**.
Profile und SMTP-Benutzer ohne Kunde sind weiterhin nur in der Relay-Weboberfläche sichtbar.

### Prinzip

- Ein Kunde wird am **SQL-Server-Login** erkannt, mit dem seine NextGen-Instanz auf die Datenbank zugreift (Tabelle `mailrelay.CustomerLogin`).
- **Row-Level Security** in der Relay-Datenbank trennt die Daten. Sichtbar ist eine Zeile, wenn der Login zum Kunden
  der Zeile gehört, Mitglied von `mailrelay_service` ist (Relay-Dienst) oder db_owner der Relay-Datenbank ist.
- Kunden-Logins erhalten nur `SELECT`/`EXECUTE` auf das Schema `mailrelay_kunde`, nie auf die Tabellen.
- Die Kunden-Schnittstelle `mailrelay_kunde` filtert zusätzlich **explizit** auf den Kunden des angemeldeten Logins.
  Auch ein Login, der als db_owner alle Tabellen sehen dürfte (Einzelinstallation), sieht in NextGen nur die Daten
  seines Kunden – Mails von Profilen ohne Kunde (z.B. des Hosters) erscheinen dort nicht.
- In der NextGen-Datenbank legt `Add-Customer.sql` das Schema `mailrelay_ng` mit Synonymen (`rk_*`) auf die
  Kunden-Schnittstelle an; das NextGen-Paket nutzt nur diese Synonyme.

| | Cloud | Einzelinstallation |
|---|---|---|
| Schema `mailrelay` | eigene Relay-Datenbank | in der NextGen-Datenbank |
| Synonyme `mailrelay_ng.rk_*` | zeigen auf die Relay-Datenbank | zeigen auf dieselbe Datenbank |
| Kunden-Login | darf in der Relay-DB **nicht** db_owner/sysadmin sein, sobald es mehrere Kunden gibt (Skript prüft) | ist meist db_owner; NextGen zeigt trotzdem nur die Daten des Kunden |
| NextGen-Paket | identisch | identisch |

### Welcher Login?

Einzutragen ist der Login, mit dem **NextGen** auf seine Datenbank zugreift – nicht der IIS-Benutzer an sich:

| NextGen verbindet sich … | Login |
|---|---|
| mit Windows-Auth, App-Pool unter eigenem Konto (Cloud) | `DOMAENE\svc-nextgen-kunde` |
| mit Windows-Auth, App-Pool-Identität, SQL auf demselben Server | `IIS APPPOOL\<Poolname>` |
| mit Windows-Auth, App-Pool-Identität, SQL auf anderem Server | `DOMAENE\<Webserver>$` (Computerkonto) |
| mit SQL-Authentifizierung (Benutzer/Passwort im Connection String) | Name des SQL-Logins, z.B. `vemas` |

Welcher Login es ist, zeigt diese Abfrage, während NextGen läuft:

```sql
SELECT DISTINCT login_name, program_name
  FROM sys.dm_exec_sessions
 WHERE database_id = DB_ID(N'<NextGen-Datenbank>');
```

Bei einer **Einzelinstallation** ist der NextGen-Login in der Regel db_owner seiner Datenbank. Direkt auf die Tabellen
sieht er damit alle Relay-Daten, in NextGen aber nur die seines Kunden. `Add-Customer.sql` ist deshalb Pflicht – es
legt die Login-Zuordnung, die Synonyme und die Rechte an. Fehlende Windows-Logins legt das Skript an; ein SQL-Login muss bereits existieren.

### Einrichtung je Kunde

1. `Install-Database.sql` in der aktuellen Version ausführen (einmalig, siehe Abschnitt 3).
2. `Add-Customer.sql` **in der Datenbank des Relays** ausführen. Oben vier Werte eintragen:
   ```sql
   DECLARE @CustomerName NVARCHAR(200) = N'Musterkunde GmbH';
   DECLARE @ShortName    NVARCHAR(50)  = N'MUSTER';                    -- erscheint im Verlauf
   DECLARE @Login        SYSNAME       = N'CONTOSO\svc-nextgen-muster'; -- Login der NextGen-Instanz (siehe unten)
   DECLARE @NextGenDb    SYSNAME       = N'NextGen_Muster';
   ```
   Das Skript legt Kunde und Login-Zuordnung an, vergibt die Rechte und erzeugt die Synonyme in der NextGen-Datenbank.
3. `NextGen-Paket.sql` **in der NextGen-Datenbank** ausführen. Navigator und Position stehen oben im Abschnitt 3
   des Skripts (`@ZielNavigator`, Standard `Default`).
4. In der Relay-Weboberfläche unter **Profile** dem Profil den Kunden zuordnen. SMTP-Benutzer gehören automatisch
   zum Kunden ihres Profils; Mails erhalten den Kunden beim Eingang.

Kunden und Logins lassen sich auch in der Weboberfläche unter **Kunden** pflegen; Datenbank-Benutzer, Rechte und
Synonyme legt aber nur `Add-Customer.sql` an.

### Was der Kunde in NextGen sieht

FunctionFolder **Mail-Relay** (nur für NextGen-Administratoren, OpenPermission `AdministrateNextGen`):

| Eintrag | Funktionen |
|---|---|
| Übersicht | Dashboard: Kacheln (heute gesendet, Warteschlange, fehlgeschlagen in 7 Tagen, Profile mit fehlendem/ablaufendem Client Secret), Diagramm der letzten 14 Tage, Liste offener und fehlgeschlagener Mails mit denselben Aktionen wie das Protokoll |
| Protokoll | Mails der letzten 30 Tage (Zeitraum einstellbar); Mail anzeigen (Kopfdaten, Text, Anhangsnamen), Verlauf, Neu senden, Versand abbrechen |
| Testmails | angeforderte Testmails mit Ergebnis |
| Profile | Profil bearbeiten (Name, Tenant, Client-ID, Standard-Postfach, „Senden als“, erlaubte Absender), Client Secret setzen, Testmail senden |
| SMTP-Benutzer | nur lesend |

Aktionen aus NextGen erscheinen im Verlauf als `<NextGen-Benutzer> (<Kurzname>) [<Login>]`.
Ein neues Client Secret landet im Klartextfeld und wird vom Dienst innerhalb einer Minute verschlüsselt.
Profile anlegen, sperren und SMTP-Benutzer pflegen bleibt dem Hoster vorbehalten.

### Hinweise

- Die Aktionen laufen über den NextGen-Standardaufruf `formapicall/GenericExecuteSP`. Fehlt einem Administrator
  dafür eine Berechtigung, meldet NextGen `MISSING_PERMISSION`.
- Kunden entfernen: in der Weboberfläche sperren (sieht sofort nichts mehr) bzw. Login-Zuordnung löschen;
  NextGen-Metadaten löschen: Einträge mit `MAILRELAY_` / `MAILRELAY.` / `MailRelay` in `vngSys` sowie das Schema `mailrelay_ng`.