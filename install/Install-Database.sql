-- ============================================================
-- Vemas.MailRelay - Datenbank-Installation
-- Legt das Schema [mailrelay] in der Relay-Datenbank an.
--
-- Ausführen: in SSMS, verbunden mit der Relay-Datenbank,
--            mit einem Benutzer mit db_owner-Rechten
--            (für CREATE LOGIN zusätzlich securityadmin).
--
-- Das Skript ist wiederholbar (idempotent) und kann auch bei
-- Updates erneut ausgeführt werden.
--
-- >>> VOR DEM AUSFÜHREN ANPASSEN: Dienst-Benutzer in Abschnitt 7 <<<
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ============================================================
-- 1. Schema
-- ============================================================
IF SCHEMA_ID(N'mailrelay') IS NULL
    EXEC (N'CREATE SCHEMA [mailrelay] AUTHORIZATION [dbo]');
GO

-- ============================================================
-- 2. Tabellen
-- ============================================================

-- Versandprofil: eine App-Registrierung in einem Tenant
IF OBJECT_ID(N'mailrelay.Profile', N'U') IS NULL
CREATE TABLE mailrelay.Profile
(
    ProfileId        INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_mailrelay_Profile PRIMARY KEY,
    Name             NVARCHAR(100)  NOT NULL,
    TenantId         NVARCHAR(100)  NOT NULL,
    ClientId         NVARCHAR(100)  NOT NULL,
    SecretEncrypted  VARBINARY(MAX) NULL,      -- per DPAPI verschlüsselt (nur auf dem Dienst-Server lesbar)
    SecretPlain      NVARCHAR(500)  NULL,      -- Eingabefeld: wird vom Dienst verschlüsselt und geleert
    SecretExpiresAt  DATE           NULL,
    SenderMode       VARCHAR(16)    NOT NULL CONSTRAINT DF_mailrelay_Profile_SenderMode DEFAULT ('FixedSender'),
    DefaultSender    NVARCHAR(320)  NOT NULL,
    IsActive         BIT            NOT NULL CONSTRAINT DF_mailrelay_Profile_IsActive DEFAULT (1),
    CreatedAt        DATETIME2(0)   NOT NULL CONSTRAINT DF_mailrelay_Profile_CreatedAt DEFAULT (SYSUTCDATETIME()),
    ModifiedAt       DATETIME2(0)   NOT NULL CONSTRAINT DF_mailrelay_Profile_ModifiedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_mailrelay_Profile_Name UNIQUE (Name),
    CONSTRAINT CK_mailrelay_Profile_SenderMode CHECK (SenderMode IN ('FixedSender', 'SendAs'))
);
GO

-- SMTP-Benutzer: Basic-Auth-Login für SQL Server / SSRS
IF OBJECT_ID(N'mailrelay.SmtpUser', N'U') IS NULL
CREATE TABLE mailrelay.SmtpUser
(
    SmtpUserId     INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_mailrelay_SmtpUser PRIMARY KEY,
    Login          NVARCHAR(100)  NOT NULL,
    PasswordHash   VARCHAR(200)   NULL,        -- PBKDF2-Hash
    PasswordPlain  NVARCHAR(200)  NULL,        -- Eingabefeld: wird vom Dienst gehasht und geleert
    ProfileId      INT            NOT NULL CONSTRAINT FK_mailrelay_SmtpUser_Profile REFERENCES mailrelay.Profile (ProfileId),
    AllowedIps     NVARCHAR(1000) NULL,        -- z.B. "10.0.0.5; 10.0.1.0/24" - leer = kein Zugriff
    IsActive       BIT            NOT NULL CONSTRAINT DF_mailrelay_SmtpUser_IsActive DEFAULT (1),
    LastLoginAt    DATETIME2(0)   NULL,
    CreatedAt      DATETIME2(0)   NOT NULL CONSTRAINT DF_mailrelay_SmtpUser_CreatedAt DEFAULT (SYSUTCDATETIME()),
    ModifiedAt     DATETIME2(0)   NOT NULL CONSTRAINT DF_mailrelay_SmtpUser_ModifiedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_mailrelay_SmtpUser_Login UNIQUE (Login)
);
GO

-- Erlaubte Absender je Profil (Adresse oder *@domain.de)
IF OBJECT_ID(N'mailrelay.AllowedSender', N'U') IS NULL
CREATE TABLE mailrelay.AllowedSender
(
    AllowedSenderId INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_mailrelay_AllowedSender PRIMARY KEY,
    ProfileId       INT           NOT NULL CONSTRAINT FK_mailrelay_AllowedSender_Profile REFERENCES mailrelay.Profile (ProfileId) ON DELETE CASCADE,
    Pattern         NVARCHAR(320) NOT NULL,
    CONSTRAINT UQ_mailrelay_AllowedSender UNIQUE (ProfileId, Pattern)
);
GO

-- Queue + Protokoll
IF OBJECT_ID(N'mailrelay.Message', N'U') IS NULL
CREATE TABLE mailrelay.Message
(
    MessageId      BIGINT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_mailrelay_Message PRIMARY KEY,
    ReceivedAt     DATETIME2(3)    NOT NULL CONSTRAINT DF_mailrelay_Message_ReceivedAt DEFAULT (SYSUTCDATETIME()),
    SmtpUserId     INT             NULL CONSTRAINT FK_mailrelay_Message_SmtpUser REFERENCES mailrelay.SmtpUser (SmtpUserId) ON DELETE SET NULL,
    ProfileId      INT             NOT NULL CONSTRAINT FK_mailrelay_Message_Profile REFERENCES mailrelay.Profile (ProfileId),
    ClientIp       VARCHAR(45)     NULL,
    EnvelopeFrom   NVARCHAR(320)   NULL,
    EnvelopeTo     NVARCHAR(MAX)   NOT NULL,   -- alle RCPT TO, mit ";" getrennt (inkl. Bcc)
    HeaderFrom     NVARCHAR(320)   NULL,
    HeaderTo       NVARCHAR(MAX)   NULL,
    Subject        NVARCHAR(1000)  NULL,
    SizeBytes      INT             NOT NULL,
    Mime           VARBINARY(MAX)  NULL,       -- wird nach Aufbewahrungsfrist geleert
    MimePurgedAt   DATETIME2(0)    NULL,
    Status         VARCHAR(16)     NOT NULL CONSTRAINT DF_mailrelay_Message_Status DEFAULT ('Queued'),
    AttemptCount   INT             NOT NULL CONSTRAINT DF_mailrelay_Message_AttemptCount DEFAULT (0),
    NextAttemptAt  DATETIME2(3)    NOT NULL CONSTRAINT DF_mailrelay_Message_NextAttemptAt DEFAULT (SYSUTCDATETIME()),
    LockedUntil    DATETIME2(3)    NULL,
    SentAt         DATETIME2(3)    NULL,
    LastError      NVARCHAR(MAX)   NULL,
    CONSTRAINT CK_mailrelay_Message_Status CHECK (Status IN ('Queued', 'Sending', 'Retry', 'Sent', 'Failed', 'Cancelled'))
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'mailrelay.Message') AND name = N'IX_mailrelay_Message_Queue')
    CREATE INDEX IX_mailrelay_Message_Queue ON mailrelay.Message (Status, NextAttemptAt);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'mailrelay.Message') AND name = N'IX_mailrelay_Message_ReceivedAt')
    CREATE INDEX IX_mailrelay_Message_ReceivedAt ON mailrelay.Message (ReceivedAt) INCLUDE (Status);
GO

-- Versuche und Aktionen je Mail
IF OBJECT_ID(N'mailrelay.Attempt', N'U') IS NULL
CREATE TABLE mailrelay.Attempt
(
    AttemptId      BIGINT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_mailrelay_Attempt PRIMARY KEY,
    MessageId      BIGINT          NOT NULL CONSTRAINT FK_mailrelay_Attempt_Message REFERENCES mailrelay.Message (MessageId) ON DELETE CASCADE,
    AttemptAt      DATETIME2(3)    NOT NULL CONSTRAINT DF_mailrelay_Attempt_AttemptAt DEFAULT (SYSUTCDATETIME()),
    AttemptType    VARCHAR(16)     NOT NULL,   -- Send | Resend | Cancel | DbMail
    Success        BIT             NULL,
    HttpStatus     INT             NULL,
    ErrorText      NVARCHAR(MAX)   NULL,
    GraphRequestId VARCHAR(100)    NULL,
    UserName       NVARCHAR(256)   NULL,       -- Windows-Benutzer bei manuellen Aktionen
    CONSTRAINT CK_mailrelay_Attempt_Type CHECK (AttemptType IN ('Send', 'Resend', 'Cancel'))
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'mailrelay.Attempt') AND name = N'IX_mailrelay_Attempt_MessageId')
    CREATE INDEX IX_mailrelay_Attempt_MessageId ON mailrelay.Attempt (MessageId);
GO

-- ============================================================
-- 3. Stored Procedure: Neu senden
-- ============================================================
CREATE OR ALTER PROCEDURE mailrelay.Resend
    @MessageId BIGINT,
    @UserName  NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE mailrelay.Message
       SET Status        = 'Queued',
           AttemptCount  = 0,
           NextAttemptAt = SYSUTCDATETIME(),
           LockedUntil   = NULL,
           LastError     = NULL
     WHERE MessageId = @MessageId
       AND Mime IS NOT NULL
       AND Status IN ('Sent', 'Failed', 'Cancelled');

    IF @@ROWCOUNT = 0
        RETURN 0;

    INSERT INTO mailrelay.Attempt (MessageId, AttemptType, UserName)
    VALUES (@MessageId, 'Resend', ISNULL(@UserName, SUSER_SNAME()));

    RETURN 1;
END
GO

-- ============================================================
-- 4. Stored Procedure: Abbrechen
-- ============================================================
CREATE OR ALTER PROCEDURE mailrelay.Cancel
    @MessageId BIGINT,
    @UserName  NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE mailrelay.Message
       SET Status      = 'Cancelled',
           LockedUntil = NULL
     WHERE MessageId = @MessageId
       AND Status IN ('Queued', 'Retry');

    IF @@ROWCOUNT = 0
        RETURN 0;

    INSERT INTO mailrelay.Attempt (MessageId, AttemptType, UserName)
    VALUES (@MessageId, 'Cancel', ISNULL(@UserName, SUSER_SNAME()));

    RETURN 1;
END
GO

-- ============================================================
-- 5. Stored Procedure: Aufräumen (Aufbewahrungsfristen)
-- ============================================================
CREATE OR ALTER PROCEDURE mailrelay.Cleanup
    @MimeRetentionDays INT = 30,
    @LogRetentionDays  INT = 365
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @mimeBefore DATETIME2(3) = DATEADD(DAY, -@MimeRetentionDays, SYSUTCDATETIME());
    DECLARE @logBefore  DATETIME2(3) = DATEADD(DAY, -@LogRetentionDays,  SYSUTCDATETIME());
    DECLARE @rows INT = 1;

    -- MIME-Inhalt (Anhänge) entfernen, Protokollzeile behalten
    WHILE @rows > 0
    BEGIN
        UPDATE TOP (500) mailrelay.Message
           SET Mime = NULL, MimePurgedAt = SYSUTCDATETIME()
         WHERE ReceivedAt < @mimeBefore
           AND Mime IS NOT NULL
           AND Status IN ('Sent', 'Failed', 'Cancelled');
        SET @rows = @@ROWCOUNT;
    END

    -- Protokoll komplett entfernen (Attempt per ON DELETE CASCADE)
    SET @rows = 1;
    WHILE @rows > 0
    BEGIN
        DELETE TOP (2000) FROM mailrelay.Message
         WHERE ReceivedAt < @logBefore
           AND Status IN ('Sent', 'Failed', 'Cancelled');
        SET @rows = @@ROWCOUNT;
    END
END
GO

-- ============================================================
-- 6. Rückmeldung an SQL Server Database Mail
-- ============================================================
-- Nr. der zugeordneten Mail in msdb.dbo.sysmail_mailitems
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

-- ============================================================
-- 7. Berechtigungen für den Dienst-Benutzer
-- ============================================================
DECLARE @ServiceUser SYSNAME = N'DOMAENE\svc-mailrelay';   -- <<< ANPASSEN

IF @ServiceUser LIKE N'DOMAENE\%'
BEGIN
    RAISERROR(N'Bitte @ServiceUser in Abschnitt 7 anpassen. Berechtigungen wurden NICHT vergeben.', 16, 1);
    RETURN;
END

IF SUSER_ID(@ServiceUser) IS NULL
    EXEC (N'CREATE LOGIN ' + N'[' + @ServiceUser + N'] FROM WINDOWS');

IF DATABASE_PRINCIPAL_ID(@ServiceUser) IS NULL
    EXEC (N'CREATE USER [' + @ServiceUser + N'] FOR LOGIN [' + @ServiceUser + N']');

-- Nur Datenrechte auf das eigene Schema, keine DDL-Rechte
EXEC (N'GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::[mailrelay] TO [' + @ServiceUser + N']');

PRINT N'Vemas.MailRelay: Schema installiert, Rechte für ' + @ServiceUser + N' vergeben.';
GO
