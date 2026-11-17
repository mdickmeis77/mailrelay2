-- ============================================================
-- Vemas.MailRelay - Rückmeldung an SQL Server Database Mail
--
-- Database Mail hält eine Mail für "gesendet", sobald das Relay sie angenommen hat. Schlägt der
-- Versand an Exchange Online danach endgültig fehl, meldet der Relay-Dienst das über die Prozedur
-- msdb.dbo.mailrelay_StatusMelden zurück:
--   * sysmail_mailitems.sent_status = 2 -> die Mail erscheint in sysmail_faileditems
--   * Eintrag mit dem Fehlertext in sysmail_log -> sichtbar in sysmail_event_log / Datenbank-E-Mail-Protokoll
-- Wird die Mail später erfolgreich neu gesendet, setzt der Dienst den Status wieder auf "sent".
--
-- Zuordnung (Database Mail schreibt keine eigene Kennung in die Mail):
--   gleicher Betreff, alle Empfänger (An, Cc, Bcc) der Database-Mail sind auch Empfänger der
--   Relay-Mail, Status "sent", Sendezeit höchstens 30 Sekunden vom Eingang im Relay entfernt.
--   Mehrere gleiche Mails kurz hintereinander: es gilt die zeitlich nächste, wenn sie eindeutig
--   (mind. 2 Sekunden) näher liegt - sonst wird nichts geändert. Mails aus SSRS oder anderen
--   Anwendungen haben keinen Eintrag in msdb und werden einfach übergangen.
--
-- Ausführen: in SSMS auf der SQL-Server-Instanz, deren Database Mail das Relay nutzt
--            (Datenbank egal, das Skript wechselt nach msdb). Benötigt: sysadmin.
-- Liegt die Relay-Datenbank auf einer anderen Instanz: im Dienst unter "DatabaseMail:ConnectionString"
-- die Verbindung zu dieser Instanz eintragen (siehe README).
-- Das Skript ist wiederholbar.
--
-- >>> VOR DEM AUSFÜHREN @ServiceUser ANPASSEN (Windows-Konto des Relay-Dienstes) <<<
-- ============================================================
SET NOCOUNT ON;
GO
USE msdb;
GO

CREATE OR ALTER PROCEDURE dbo.mailrelay_StatusMelden
    @RelayMessageId BIGINT,                  -- Nr. der Mail im Relay (für den Protokolltext)
    @Failed         BIT,                     -- 1 = endgültig fehlgeschlagen, 0 = nach Neu senden zugestellt
    @Subject        NVARCHAR(1000) = NULL,   -- nur für die Zuordnung bei @Failed = 1
    @Recipients     NVARCHAR(MAX)  = NULL,   -- alle SMTP-Empfänger der Relay-Mail, mit ";" getrennt
    @ReceivedUtc    DATETIME2(3)   = NULL,   -- Eingang im Relay (UTC)
    @ErrorText      NVARCHAR(3000) = NULL,
    @MailItemId     INT            = NULL OUTPUT  -- bekannt: direkt verwenden; sonst gefundene Nr. (0 = keine)
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @prefix NVARCHAR(100) = N'Vemas.MailRelay [Relay-Nr. ' + CAST(@RelayMessageId AS NVARCHAR(20)) + N']: ';
    DECLARE @caller SYSNAME = ORIGINAL_LOGIN();

    IF @Failed = 1 AND @MailItemId IS NULL
    BEGIN
        -- Database Mail speichert Ortszeit des Servers
        DECLARE @local DATETIME = DATEADD(MINUTE, DATEDIFF(MINUTE, GETUTCDATE(), GETDATE()), CAST(@ReceivedUtc AS DATETIME));

        DECLARE @relay TABLE (Address NVARCHAR(400) PRIMARY KEY);
        INSERT INTO @relay (Address)
        SELECT DISTINCT LOWER(LTRIM(RTRIM(value))) FROM STRING_SPLIT(ISNULL(@Recipients, N''), N';')
         WHERE LTRIM(RTRIM(value)) <> N'';

        DECLARE @found TABLE (MailItemId INT, DeltaMs INT);
        INSERT INTO @found (MailItemId, DeltaMs)
        SELECT TOP (2) m.mailitem_id, ABS(DATEDIFF(MILLISECOND, m.sent_date, @local))
          FROM dbo.sysmail_mailitems m
         WHERE m.sent_status = 1
           AND m.sent_date BETWEEN DATEADD(MINUTE, -2, @local) AND DATEADD(MINUTE, 2, @local)
           AND ISNULL(m.subject, N'') = ISNULL(@Subject, N'')
           AND NOT EXISTS (
                SELECT 1
                  FROM STRING_SPLIT(CONCAT(m.recipients, N';', m.copy_recipients, N';', m.blind_copy_recipients), N';') r
                 WHERE LTRIM(RTRIM(r.value)) <> N''
                   AND LOWER(LTRIM(RTRIM(r.value))) NOT IN (SELECT Address FROM @relay))
         ORDER BY ABS(DATEDIFF(MILLISECOND, m.sent_date, @local));

        -- Database Mail vermerkt den Versand Sekundenbruchteile nach dem Eingang im Relay.
        -- Gleiche Mails kurz hintereinander: der zeitlich nächste Treffer gilt, wenn er höchstens
        -- 30 Sekunden entfernt und mindestens 2 Sekunden näher als der zweite ist.
        DECLARE @first INT, @firstDelta INT, @secondDelta INT;
        SELECT TOP (1) @first = MailItemId, @firstDelta = DeltaMs FROM @found ORDER BY DeltaMs;
        SELECT @secondDelta = MAX(DeltaMs) FROM @found HAVING COUNT(*) = 2;

        IF @first IS NULL OR @firstDelta > 30000 OR @secondDelta - @firstDelta < 2000
        BEGIN
            SET @MailItemId = 0;   -- keine oder keine eindeutige Database-Mail
            RETURN 0;
        END
        SET @MailItemId = @first;
    END

    IF ISNULL(@MailItemId, 0) = 0
        RETURN 0;

    BEGIN TRANSACTION;

    IF @Failed = 1
    BEGIN
        UPDATE dbo.sysmail_mailitems SET sent_status = 2 WHERE mailitem_id = @MailItemId AND sent_status = 1;
        IF @@ROWCOUNT = 1
            INSERT INTO dbo.sysmail_log (event_type, log_date, description, mailitem_id, account_id, last_mod_date, last_mod_user)
            SELECT 3, GETDATE(),
                   LEFT(@prefix + N'Zustellung über Exchange Online endgültig fehlgeschlagen. ' + ISNULL(@ErrorText, N''), 4000),
                   mailitem_id, sent_account_id, GETDATE(), @caller
              FROM dbo.sysmail_mailitems WHERE mailitem_id = @MailItemId;
    END
    ELSE
    BEGIN
        UPDATE dbo.sysmail_mailitems SET sent_status = 1, sent_date = GETDATE() WHERE mailitem_id = @MailItemId AND sent_status = 2;
        IF @@ROWCOUNT = 1
            INSERT INTO dbo.sysmail_log (event_type, log_date, description, mailitem_id, account_id, last_mod_date, last_mod_user)
            SELECT 1, GETDATE(), @prefix + N'Nach erneutem Senden über Exchange Online zugestellt.',
                   mailitem_id, sent_account_id, GETDATE(), @caller
              FROM dbo.sysmail_mailitems WHERE mailitem_id = @MailItemId;
    END

    COMMIT TRANSACTION;
    RETURN 1;
END
GO

-- ------------------------------------------------------------
-- Recht für den Relay-Dienst: nur diese Prozedur, kein Zugriff auf die Tabellen
-- ------------------------------------------------------------
DECLARE @ServiceUser SYSNAME = N'DOMAENE\svc-mailrelay';   -- <<< ANPASSEN

IF @ServiceUser LIKE N'DOMAENE\%'
BEGIN
    RAISERROR(N'Prozedur angelegt. Bitte @ServiceUser anpassen und das Skript erneut ausführen - der Dienst darf sie sonst nicht aufrufen.', 16, 1);
    RETURN;
END
IF SUSER_ID(@ServiceUser) IS NULL
BEGIN
    RAISERROR(N'Der Login %s existiert auf diesem Server nicht.', 16, 1, @ServiceUser);
    RETURN;
END

DECLARE @UserName SYSNAME = (SELECT name FROM sys.database_principals WHERE sid = SUSER_SID(@ServiceUser));
IF @UserName IS NULL
BEGIN
    SET @UserName = @ServiceUser;
    EXEC (N'CREATE USER [' + @UserName + N'] FOR LOGIN [' + @ServiceUser + N']');
END
EXEC (N'GRANT EXECUTE ON dbo.mailrelay_StatusMelden TO [' + @UserName + N']');

PRINT N'Vemas.MailRelay: Rückmeldung an Database Mail eingerichtet, ' + @ServiceUser + N' darf msdb.dbo.mailrelay_StatusMelden ausführen.';
GO
