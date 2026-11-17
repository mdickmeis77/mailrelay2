-- ============================================================
-- Vemas.MailRelay - Datenbank-Update auf Version 1.1 (Kundenzugriff aus NextGen)
--
-- Für BESTEHENDE Installationen, die mit der ersten Version von Install-Database.sql
-- eingerichtet wurden. Enthält nur die Neuerungen:
--   * Kunden und Login-Zuordnung (mailrelay.Customer, mailrelay.CustomerLogin)
--   * neue Spalten in mailrelay.Profile und mailrelay.Message
--   * Testmail-Aufträge (mailrelay.TestMailRequest)
--   * Rolle mailrelay_service für das Dienstkonto
--   * Row-Level Security (Datentrennung je Kunde)
--   * Kunden-Schnittstelle (Schema mailrelay_kunde)
--
-- Ausführen: in SSMS, verbunden mit der Datenbank, in der das Schema mailrelay liegt
--            (NextGen-Datenbank bzw. Relay-Datenbank), mit db_owner-Rechten.
-- Vorhandene Daten bleiben erhalten. Das Skript ist wiederholbar.
-- Neuinstallationen verwenden weiterhin Install-Database.sql.
--
-- >>> VOR DEM AUSFÜHREN: Dienst-Benutzer am Ende des Skripts eintragen <<<
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Ohne bestehende Installation werden alle folgenden Batches nicht ausgeführt.
IF OBJECT_ID(N'mailrelay.Profile', N'U') IS NULL OR OBJECT_ID(N'mailrelay.Message', N'U') IS NULL
BEGIN
    RAISERROR(N'Keine bestehende Vemas.MailRelay-Installation in dieser Datenbank gefunden (Schema mailrelay fehlt). Für eine Neuinstallation Install-Database.sql verwenden.', 16, 1);
    SET NOEXEC ON;
END
GO
-- ============================================================
-- 6. Kunden (Kundenzugriff aus NextGen)
-- ============================================================
IF OBJECT_ID(N'mailrelay.Customer', N'U') IS NULL
CREATE TABLE mailrelay.Customer
(
    CustomerId  INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_mailrelay_Customer PRIMARY KEY,
    Name        NVARCHAR(200)  NOT NULL,
    ShortName   NVARCHAR(50)   NOT NULL,
    IsActive    BIT            NOT NULL CONSTRAINT DF_mailrelay_Customer_IsActive DEFAULT (1),
    CreatedAt   DATETIME2(0)   NOT NULL CONSTRAINT DF_mailrelay_Customer_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_mailrelay_Customer_ShortName UNIQUE (ShortName)
);
GO

-- Windows-Login bzw. Windows-Gruppe, über den ein Kunde erkannt wird (z.B. App-Pool-Konto der NextGen-Instanz)
IF OBJECT_ID(N'mailrelay.CustomerLogin', N'U') IS NULL
CREATE TABLE mailrelay.CustomerLogin
(
    CustomerLoginId INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_mailrelay_CustomerLogin PRIMARY KEY,
    CustomerId      INT           NOT NULL CONSTRAINT FK_mailrelay_CustomerLogin_Customer REFERENCES mailrelay.Customer (CustomerId) ON DELETE CASCADE,
    LoginName       NVARCHAR(256) NOT NULL,
    CONSTRAINT UQ_mailrelay_CustomerLogin_LoginName UNIQUE (LoginName)
);
GO

IF COL_LENGTH(N'mailrelay.Profile', N'CustomerId') IS NULL
    ALTER TABLE mailrelay.Profile ADD CustomerId INT NULL
        CONSTRAINT FK_mailrelay_Profile_Customer REFERENCES mailrelay.Customer (CustomerId);
GO
IF COL_LENGTH(N'mailrelay.Message', N'CustomerId') IS NULL
    ALTER TABLE mailrelay.Message ADD CustomerId INT NULL
        CONSTRAINT FK_mailrelay_Message_Customer REFERENCES mailrelay.Customer (CustomerId);
GO
-- Kopfdaten und Text der Mail für die Anzeige in NextGen (vom Dienst beim Eingang befüllt)
IF COL_LENGTH(N'mailrelay.Message', N'HeaderCc') IS NULL
    ALTER TABLE mailrelay.Message ADD HeaderCc NVARCHAR(MAX) NULL;
GO
IF COL_LENGTH(N'mailrelay.Message', N'ReplyTo') IS NULL
    ALTER TABLE mailrelay.Message ADD ReplyTo NVARCHAR(320) NULL;
GO
IF COL_LENGTH(N'mailrelay.Message', N'BodyText') IS NULL
    ALTER TABLE mailrelay.Message ADD BodyText NVARCHAR(MAX) NULL;
GO
IF COL_LENGTH(N'mailrelay.Message', N'AttachmentNames') IS NULL
    ALTER TABLE mailrelay.Message ADD AttachmentNames NVARCHAR(MAX) NULL;
GO
-- Rückmeldung an Database Mail: Nr. der zugeordneten Mail in msdb.dbo.sysmail_mailitems
IF COL_LENGTH(N'mailrelay.Message', N'DbMailItemId') IS NULL
    ALTER TABLE mailrelay.Message ADD DbMailItemId INT NULL;
GO
-- Verlaufsart "DbMail" (Rückmeldung an Database Mail) zulassen
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_mailrelay_Attempt_Type' AND definition LIKE N'%DbMail%')
BEGIN
    IF OBJECT_ID(N'mailrelay.CK_mailrelay_Attempt_Type', N'C') IS NOT NULL
        ALTER TABLE mailrelay.Attempt DROP CONSTRAINT CK_mailrelay_Attempt_Type;
    ALTER TABLE mailrelay.Attempt ADD CONSTRAINT CK_mailrelay_Attempt_Type CHECK (AttemptType IN ('Send', 'Resend', 'Cancel', 'DbMail'));
END
GO
-- Mails und Testaufträge ohne Kunde übernehmen den Kunden ihres Profils
-- (Profil erst nach dem Eingang der Mails einem Kunden zugeordnet)
UPDATE m SET CustomerId = p.CustomerId
  FROM mailrelay.Message m JOIN mailrelay.Profile p ON p.ProfileId = m.ProfileId
 WHERE m.CustomerId IS NULL AND p.CustomerId IS NOT NULL;
UPDATE t SET CustomerId = p.CustomerId
  FROM mailrelay.TestMailRequest t JOIN mailrelay.Profile p ON p.ProfileId = t.ProfileId
 WHERE t.CustomerId IS NULL AND p.CustomerId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'mailrelay.Message') AND name = N'IX_mailrelay_Message_Customer')
    CREATE INDEX IX_mailrelay_Message_Customer ON mailrelay.Message (CustomerId, MessageId DESC);
GO

-- Testmail-Aufträge aus NextGen; der Dienst baut die Mail und stellt sie in die Queue
IF OBJECT_ID(N'mailrelay.TestMailRequest', N'U') IS NULL
CREATE TABLE mailrelay.TestMailRequest
(
    TestMailRequestId INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_mailrelay_TestMailRequest PRIMARY KEY,
    CustomerId        INT            NULL CONSTRAINT FK_mailrelay_TestMailRequest_Customer REFERENCES mailrelay.Customer (CustomerId),
    ProfileId         INT            NOT NULL CONSTRAINT FK_mailrelay_TestMailRequest_Profile REFERENCES mailrelay.Profile (ProfileId),
    Recipient         NVARCHAR(320)  NOT NULL,
    RequestedBy       NVARCHAR(256)  NULL,
    RequestedAt       DATETIME2(0)   NOT NULL CONSTRAINT DF_mailrelay_TestMailRequest_RequestedAt DEFAULT (SYSUTCDATETIME()),
    ProcessedAt       DATETIME2(0)   NULL,
    MessageId         BIGINT         NULL,
    ErrorText         NVARCHAR(MAX)  NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'mailrelay.TestMailRequest') AND name = N'IX_mailrelay_TestMailRequest_Pending')
    CREATE INDEX IX_mailrelay_TestMailRequest_Pending ON mailrelay.TestMailRequest (ProcessedAt);
GO

-- Rolle für das Dienstkonto: sieht trotz Row-Level Security alle Daten
IF DATABASE_PRINCIPAL_ID(N'mailrelay_service') IS NULL
    CREATE ROLE mailrelay_service AUTHORIZATION dbo;
GO

-- ============================================================
-- 7. Row-Level Security (Datentrennung je Kunde)
-- ============================================================
-- Sichtbar ist eine Zeile, wenn
--   * der Login zum Kunden der Zeile gehört (CustomerLogin, Login oder Windows-Gruppe), oder
--   * der Login Mitglied der Rolle mailrelay_service ist (Relay-Dienst, Hoster-Oberfläche), oder
--   * der Login db_owner dieser Datenbank ist (Hoster-Admins; Einzelinstallation: NextGen-Login).
-- Zum Ändern der Funktionen muss die Policy kurz entfernt werden.
IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'CustomerPolicy' AND schema_id = SCHEMA_ID(N'mailrelay'))
    DROP SECURITY POLICY mailrelay.CustomerPolicy;
GO
-- Schemagebundene Funktionen verweisen aufeinander: in umgekehrter Reihenfolge entfernen
DROP FUNCTION IF EXISTS mailrelay.fn_AccessMessage;
DROP FUNCTION IF EXISTS mailrelay.fn_AccessProfile;
DROP FUNCTION IF EXISTS mailrelay.fn_AccessCustomer;
GO

CREATE OR ALTER FUNCTION mailrelay.fn_AccessCustomer (@CustomerId INT)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS Allowed
     WHERE IS_MEMBER(N'mailrelay_service') = 1
        OR IS_MEMBER(N'db_owner') = 1
        OR (@CustomerId IS NOT NULL AND EXISTS (
                SELECT 1
                  FROM mailrelay.CustomerLogin cl
                  JOIN mailrelay.Customer c ON c.CustomerId = cl.CustomerId
                 WHERE cl.CustomerId = @CustomerId
                   AND c.IsActive = 1
                   AND (cl.LoginName = ORIGINAL_LOGIN() OR IS_MEMBER(cl.LoginName) = 1)));
GO

CREATE OR ALTER FUNCTION mailrelay.fn_AccessProfile (@ProfileId INT)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS Allowed
      FROM mailrelay.Profile p
     CROSS APPLY mailrelay.fn_AccessCustomer(p.CustomerId) a
     WHERE p.ProfileId = @ProfileId;
GO

CREATE OR ALTER FUNCTION mailrelay.fn_AccessMessage (@MessageId BIGINT)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS Allowed
      FROM mailrelay.Message m
     CROSS APPLY mailrelay.fn_AccessCustomer(m.CustomerId) a
     WHERE m.MessageId = @MessageId;
GO

CREATE SECURITY POLICY mailrelay.CustomerPolicy
    ADD FILTER PREDICATE mailrelay.fn_AccessCustomer(CustomerId) ON mailrelay.Profile,
    ADD BLOCK  PREDICATE mailrelay.fn_AccessCustomer(CustomerId) ON mailrelay.Profile AFTER INSERT,
    ADD BLOCK  PREDICATE mailrelay.fn_AccessCustomer(CustomerId) ON mailrelay.Profile AFTER UPDATE,
    ADD FILTER PREDICATE mailrelay.fn_AccessProfile(ProfileId)   ON mailrelay.AllowedSender,
    ADD BLOCK  PREDICATE mailrelay.fn_AccessProfile(ProfileId)   ON mailrelay.AllowedSender AFTER INSERT,
    ADD FILTER PREDICATE mailrelay.fn_AccessProfile(ProfileId)   ON mailrelay.SmtpUser,
    ADD FILTER PREDICATE mailrelay.fn_AccessCustomer(CustomerId) ON mailrelay.Message,
    ADD BLOCK  PREDICATE mailrelay.fn_AccessCustomer(CustomerId) ON mailrelay.Message AFTER UPDATE,
    ADD FILTER PREDICATE mailrelay.fn_AccessMessage(MessageId)   ON mailrelay.Attempt,
    ADD BLOCK  PREDICATE mailrelay.fn_AccessMessage(MessageId)   ON mailrelay.Attempt AFTER INSERT,
    ADD FILTER PREDICATE mailrelay.fn_AccessCustomer(CustomerId) ON mailrelay.TestMailRequest,
    ADD BLOCK  PREDICATE mailrelay.fn_AccessCustomer(CustomerId) ON mailrelay.TestMailRequest AFTER INSERT
WITH (STATE = ON, SCHEMABINDING = ON);
GO

-- ============================================================
-- 8. Kunden-Schnittstelle (Schema mailrelay_kunde)
-- Kunden-Logins erhalten nur SELECT/EXECUTE auf dieses Schema (siehe Add-Customer.sql).
-- Zeiten werden in deutscher Zeit (W. Europe Standard Time) ausgegeben.
-- ============================================================
IF SCHEMA_ID(N'mailrelay_kunde') IS NULL
    EXEC (N'CREATE SCHEMA [mailrelay_kunde] AUTHORIZATION [dbo]');
GO

CREATE OR ALTER FUNCTION mailrelay_kunde.fn_Lokal (@Utc DATETIME2(3))
RETURNS DATETIME2(0)
AS
BEGIN
    RETURN CAST(CAST(@Utc AS DATETIMEOFFSET(3)) AT TIME ZONE N'W. Europe Standard Time' AS DATETIME2(0));
END
GO

CREATE OR ALTER FUNCTION mailrelay_kunde.fn_StatusText (@Status VARCHAR(16))
RETURNS NVARCHAR(30)
AS
BEGIN
    RETURN CASE @Status
        WHEN 'Queued'    THEN N'Wartend'
        WHEN 'Sending'   THEN N'Wird gesendet'
        WHEN 'Retry'     THEN N'Wiederholung'
        WHEN 'Sent'      THEN N'Gesendet'
        WHEN 'Failed'    THEN N'Fehlgeschlagen'
        WHEN 'Cancelled' THEN N'Abgebrochen'
        ELSE @Status END;
END
GO

-- Kunden des angemeldeten Logins (Login oder Windows-Gruppe, nur aktive Kunden).
-- Die Schnittstelle filtert zusätzlich zur Row-Level Security explizit darauf: Auch ein Login,
-- der als db_owner alle Daten sehen dürfte (Einzelinstallation), sieht hier nur seinen Kunden.
CREATE OR ALTER FUNCTION mailrelay_kunde.fn_MeineKunden ()
RETURNS TABLE
AS
RETURN
    SELECT c.CustomerId
      FROM mailrelay.Customer c
     WHERE c.IsActive = 1
       AND EXISTS (SELECT 1 FROM mailrelay.CustomerLogin cl
                    WHERE cl.CustomerId = c.CustomerId
                      AND (cl.LoginName = ORIGINAL_LOGIN() OR IS_MEMBER(cl.LoginName) = 1));
GO

-- Anzeigename für den Verlauf: "<NextGen-Benutzer> (<Kunde>) [<Windows-Login>]"
CREATE OR ALTER FUNCTION mailrelay_kunde.fn_Benutzer (@Benutzer NVARCHAR(100))
RETURNS NVARCHAR(256)
AS
BEGIN
    DECLARE @kunde NVARCHAR(50) =
        (SELECT TOP 1 c.ShortName
           FROM mailrelay.CustomerLogin cl
           JOIN mailrelay.Customer c ON c.CustomerId = cl.CustomerId
          WHERE cl.LoginName = ORIGINAL_LOGIN() OR IS_MEMBER(cl.LoginName) = 1);
    RETURN LEFT(ISNULL(NULLIF(@Benutzer, N''), N'?')
              + ISNULL(N' (' + @kunde + N')', N'')
              + N' [' + ORIGINAL_LOGIN() + N']', 256);
END
GO

CREATE OR ALTER VIEW mailrelay_kunde.Protokoll
AS
SELECT m.MessageId,
       mailrelay_kunde.fn_Lokal(m.ReceivedAt)               AS Eingang,
       m.Status,
       mailrelay_kunde.fn_StatusText(m.Status)              AS StatusText,
       m.HeaderFrom                                         AS Absender,
       m.EnvelopeTo                                         AS Empfaenger,
       m.Subject                                            AS Betreff,
       m.SizeBytes                                          AS GroesseBytes,
       m.AttemptCount                                       AS Versuche,
       mailrelay_kunde.fn_Lokal(m.SentAt)                   AS Gesendet,
       CASE WHEN m.Status = 'Retry' THEN mailrelay_kunde.fn_Lokal(m.NextAttemptAt) END AS NaechsterVersuch,
       m.LastError                                          AS LetzterFehler,
       m.ProfileId,
       p.Name                                               AS Profil,
       u.Login                                              AS SmtpBenutzer,
       CAST(CASE WHEN m.Mime IS NULL THEN 0 ELSE 1 END AS BIT) AS InhaltVorhanden,
       m.ReceivedAt                                         AS EingangUtc
  FROM mailrelay.Message m
  LEFT JOIN mailrelay.Profile  p ON p.ProfileId  = m.ProfileId
  LEFT JOIN mailrelay.SmtpUser u ON u.SmtpUserId = m.SmtpUserId
 WHERE m.CustomerId IN (SELECT CustomerId FROM mailrelay_kunde.fn_MeineKunden());
GO

CREATE OR ALTER VIEW mailrelay_kunde.Verlauf
AS
SELECT a.AttemptId,
       a.MessageId,
       mailrelay_kunde.fn_Lokal(a.AttemptAt) AS Zeitpunkt,
       a.AttemptType                         AS Art,
       CASE a.AttemptType WHEN 'Send' THEN N'Versand' WHEN 'Resend' THEN N'Neu senden' WHEN 'Cancel' THEN N'Abbrechen' WHEN 'DbMail' THEN N'Database Mail' ELSE a.AttemptType END AS ArtText,
       a.Success                             AS Erfolg,
       a.HttpStatus,
       a.ErrorText                           AS Fehler,
       a.GraphRequestId                      AS RequestId,
       a.UserName                            AS Benutzer
  FROM mailrelay.Attempt a
  JOIN mailrelay.Message m ON m.MessageId = a.MessageId
 WHERE m.CustomerId IN (SELECT CustomerId FROM mailrelay_kunde.fn_MeineKunden());
GO

CREATE OR ALTER VIEW mailrelay_kunde.Mail
AS
SELECT m.MessageId,
       mailrelay_kunde.fn_Lokal(m.ReceivedAt) AS Eingang,
       m.HeaderFrom      AS Absender,
       m.HeaderTo        AS An,
       m.HeaderCc        AS Cc,
       m.EnvelopeTo      AS EmpfaengerSmtp,
       m.ReplyTo         AS AntwortAn,
       m.Subject         AS Betreff,
       m.BodyText        AS Text,
       m.AttachmentNames AS Anhaenge,
       m.Status,
       mailrelay_kunde.fn_StatusText(m.Status) AS StatusText
  FROM mailrelay.Message m
 WHERE m.CustomerId IN (SELECT CustomerId FROM mailrelay_kunde.fn_MeineKunden());
GO

CREATE OR ALTER VIEW mailrelay_kunde.Profile
AS
SELECT p.ProfileId,
       p.Name,
       p.TenantId,
       p.ClientId,
       CAST(CASE WHEN p.SecretEncrypted IS NULL THEN 0 ELSE 1 END AS BIT) AS SecretHinterlegt,
       CAST(CASE WHEN p.SecretPlain IS NULL THEN 0 ELSE 1 END AS BIT)     AS SecretWirdVerarbeitet,
       p.SecretExpiresAt AS SecretGueltigBis,
       p.SenderMode      AS Modus,
       CASE p.SenderMode WHEN 'SendAs' THEN N'Senden als' ELSE N'Fester Absender' END AS ModusText,
       CAST(CASE WHEN p.SenderMode = 'SendAs' THEN 1 ELSE 0 END AS BIT) AS SendenAls,
       p.DefaultSender   AS StandardPostfach,
       p.IsActive        AS Aktiv,
       (SELECT STRING_AGG(s.Pattern, NCHAR(10)) WITHIN GROUP (ORDER BY s.Pattern)
          FROM mailrelay.AllowedSender s WHERE s.ProfileId = p.ProfileId) AS ErlaubteAbsender
  FROM mailrelay.Profile p
 WHERE p.CustomerId IN (SELECT CustomerId FROM mailrelay_kunde.fn_MeineKunden());
GO

CREATE OR ALTER VIEW mailrelay_kunde.ErlaubteAbsender
AS
SELECT s.AllowedSenderId, s.ProfileId, s.Pattern AS Muster
  FROM mailrelay.AllowedSender s
  JOIN mailrelay.Profile p ON p.ProfileId = s.ProfileId
 WHERE p.CustomerId IN (SELECT CustomerId FROM mailrelay_kunde.fn_MeineKunden());
GO

CREATE OR ALTER VIEW mailrelay_kunde.SmtpBenutzer
AS
SELECT u.SmtpUserId,
       u.Login,
       u.IsActive AS Aktiv,
       mailrelay_kunde.fn_Lokal(u.LastLoginAt) AS LetzteAnmeldung,
       u.ProfileId,
       p.Name AS Profil
  FROM mailrelay.SmtpUser u
  JOIN mailrelay.Profile p ON p.ProfileId = u.ProfileId
 WHERE p.CustomerId IN (SELECT CustomerId FROM mailrelay_kunde.fn_MeineKunden());
GO

CREATE OR ALTER VIEW mailrelay_kunde.Testauftraege
AS
SELECT t.TestMailRequestId,
       t.ProfileId,
       p.Name AS Profil,
       t.Recipient AS Empfaenger,
       t.RequestedBy AS AngefordertVon,
       mailrelay_kunde.fn_Lokal(t.RequestedAt) AS Angefordert,
       mailrelay_kunde.fn_Lokal(t.ProcessedAt) AS Verarbeitet,
       t.MessageId,
       m.Status,
       mailrelay_kunde.fn_StatusText(m.Status) AS StatusText,
       COALESCE(t.ErrorText, m.LastError) AS Fehler
  FROM mailrelay.TestMailRequest t
  LEFT JOIN mailrelay.Profile p ON p.ProfileId = t.ProfileId
  LEFT JOIN mailrelay.Message m ON m.MessageId = t.MessageId
 WHERE t.CustomerId IN (SELECT CustomerId FROM mailrelay_kunde.fn_MeineKunden());
GO

CREATE OR ALTER PROCEDURE mailrelay_kunde.NeuSenden
    @MessageId BIGINT,
    @Benutzer  NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM mailrelay_kunde.Protokoll WHERE MessageId = @MessageId)
        THROW 50003, N'Mail nicht gefunden.', 1;
    DECLARE @user NVARCHAR(256) = mailrelay_kunde.fn_Benutzer(@Benutzer);
    DECLARE @ok INT;
    EXEC @ok = mailrelay.Resend @MessageId = @MessageId, @UserName = @user;
    IF @ok = 0
        THROW 50001, N'Die Mail kann nicht neu gesendet werden (nicht gefunden, falscher Status oder Inhalt bereits gelöscht).', 1;
END
GO

CREATE OR ALTER PROCEDURE mailrelay_kunde.Abbrechen
    @MessageId BIGINT,
    @Benutzer  NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM mailrelay_kunde.Protokoll WHERE MessageId = @MessageId)
        THROW 50003, N'Mail nicht gefunden.', 1;
    DECLARE @user NVARCHAR(256) = mailrelay_kunde.fn_Benutzer(@Benutzer);
    DECLARE @ok INT;
    EXEC @ok = mailrelay.Cancel @MessageId = @MessageId, @UserName = @user;
    IF @ok = 0
        THROW 50002, N'Der Versand kann nicht mehr abgebrochen werden.', 1;
END
GO

-- Vorhandenes Profil bearbeiten. Anlegen und Sperren bleiben dem Hoster vorbehalten.
CREATE OR ALTER PROCEDURE mailrelay_kunde.ProfilSpeichern
    @ProfileId        INT,
    @Name             NVARCHAR(100),
    @TenantId         NVARCHAR(100),
    @ClientId         NVARCHAR(100),
    @SendenAls        BIT,
    @StandardPostfach NVARCHAR(320),
    @ErlaubteAbsender NVARCHAR(MAX) = NULL,
    @Benutzer         NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM mailrelay_kunde.Profile WHERE ProfileId = @ProfileId)
        THROW 50014, N'Profil nicht gefunden.', 1;
    IF NULLIF(LTRIM(RTRIM(@Name)), N'') IS NULL OR NULLIF(LTRIM(RTRIM(@TenantId)), N'') IS NULL
        THROW 50010, N'Name und Tenant-ID sind Pflichtfelder.', 1;
    IF TRY_CONVERT(UNIQUEIDENTIFIER, @ClientId) IS NULL
        THROW 50011, N'Die Client-ID muss eine GUID sein.', 1;
    IF @StandardPostfach NOT LIKE N'%_@_%._%' OR @StandardPostfach LIKE N'% %'
        THROW 50012, N'Das Standard-Postfach ist keine gültige Adresse.', 1;

    DECLARE @senders TABLE (Pattern NVARCHAR(320) PRIMARY KEY);
    INSERT INTO @senders (Pattern)
    SELECT DISTINCT LTRIM(RTRIM(REPLACE(value, NCHAR(13), N'')))
      FROM STRING_SPLIT(REPLACE(ISNULL(@ErlaubteAbsender, N''), N';', NCHAR(10)), NCHAR(10))
     WHERE LTRIM(RTRIM(REPLACE(value, NCHAR(13), N''))) <> N'';

    IF EXISTS (SELECT 1 FROM @senders WHERE Pattern NOT LIKE N'%_@_%._%' AND Pattern NOT LIKE N'*@_%._%')
        THROW 50013, N'Mindestens ein erlaubter Absender ist ungültig (erwartet adresse@domain.de oder *@domain.de).', 1;

    BEGIN TRANSACTION;

    UPDATE mailrelay.Profile
       SET Name = LTRIM(RTRIM(@Name)),
           TenantId = LTRIM(RTRIM(@TenantId)),
           ClientId = LTRIM(RTRIM(@ClientId)),
           SenderMode = CASE WHEN @SendenAls = 1 THEN 'SendAs' ELSE 'FixedSender' END,
           DefaultSender = LTRIM(RTRIM(@StandardPostfach)),
           ModifiedAt = SYSUTCDATETIME()
     WHERE ProfileId = @ProfileId;

    IF @@ROWCOUNT = 0
        THROW 50014, N'Profil nicht gefunden.', 1;

    DELETE FROM mailrelay.AllowedSender WHERE ProfileId = @ProfileId;
    INSERT INTO mailrelay.AllowedSender (ProfileId, Pattern)
    SELECT @ProfileId, Pattern FROM @senders;

    COMMIT TRANSACTION;
END
GO

-- Neues Client Secret: landet im Klartextfeld, der Dienst verschlüsselt es innerhalb einer Minute.
CREATE OR ALTER PROCEDURE mailrelay_kunde.SecretSetzen
    @ProfileId  INT,
    @Secret     NVARCHAR(500),
    @GueltigBis DATE = NULL,
    @Benutzer   NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM mailrelay_kunde.Profile WHERE ProfileId = @ProfileId)
        THROW 50021, N'Profil nicht gefunden.', 1;
    IF NULLIF(LTRIM(RTRIM(@Secret)), N'') IS NULL
        THROW 50020, N'Bitte ein Client Secret eingeben.', 1;

    UPDATE mailrelay.Profile
       SET SecretPlain = LTRIM(RTRIM(@Secret)),
           SecretExpiresAt = ISNULL(@GueltigBis, SecretExpiresAt),
           ModifiedAt = SYSUTCDATETIME()
     WHERE ProfileId = @ProfileId;

    IF @@ROWCOUNT = 0
        THROW 50021, N'Profil nicht gefunden.', 1;
END
GO

CREATE OR ALTER PROCEDURE mailrelay_kunde.TestmailAnfordern
    @ProfileId  INT,
    @Empfaenger NVARCHAR(320),
    @Benutzer   NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Empfaenger NOT LIKE N'%_@_%._%' OR @Empfaenger LIKE N'% %'
        THROW 50030, N'Bitte eine gültige Empfängeradresse angeben.', 1;

    DECLARE @customerId INT, @found BIT = 0;
    SELECT @customerId = CustomerId, @found = 1 FROM mailrelay.Profile
     WHERE ProfileId = @ProfileId AND CustomerId IN (SELECT CustomerId FROM mailrelay_kunde.fn_MeineKunden());
    IF @found = 0
        THROW 50031, N'Profil nicht gefunden.', 1;

    INSERT INTO mailrelay.TestMailRequest (CustomerId, ProfileId, Recipient, RequestedBy)
    VALUES (@customerId, @ProfileId, LTRIM(RTRIM(@Empfaenger)), mailrelay_kunde.fn_Benutzer(@Benutzer));
END
GO

-- ============================================================
-- 9. Dienst-Benutzer in die Rolle mailrelay_service aufnehmen
--    (sieht trotz Row-Level Security alle Kunden)
-- ============================================================
DECLARE @ServiceUser SYSNAME = N'DOMAENE\svc-mailrelay';   -- <<< ANPASSEN (dasselbe Konto wie bei der Erstinstallation)

IF @ServiceUser LIKE N'DOMAENE\%'
BEGIN
    RAISERROR(N'Bitte @ServiceUser anpassen. Die Rolle mailrelay_service wurde NICHT zugewiesen - ohne sie sieht der Dienst nach dem Update keine Daten mehr.', 16, 1);
    RETURN;
END

DECLARE @UserName SYSNAME = (SELECT name FROM sys.database_principals WHERE sid = SUSER_SID(@ServiceUser));
IF @UserName IS NULL
BEGIN
    RAISERROR(N'Der Dienst-Benutzer hat in dieser Datenbank keinen Benutzer. Bitte das bei der Erstinstallation verwendete Konto eintragen.', 16, 1);
    RETURN;
END

IF IS_ROLEMEMBER(N'mailrelay_service', @UserName) = 0
    EXEC (N'ALTER ROLE [mailrelay_service] ADD MEMBER [' + @UserName + N']');

PRINT N'Vemas.MailRelay: Datenbank auf Version 1.1 aktualisiert, ' + @UserName + N' ist Mitglied von mailrelay_service.';
GO

SET NOEXEC OFF;
GO