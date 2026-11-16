# Sicurezza

## Regole

1. **Mai inserire credenziali nel repository.** Le password sono cifrate con DPAPI (Windows-only) e salvate in `appsettings.json` locale o `%LOCALAPPDATA%\DatabaseMigrator\`.
2. **Non condividere file `appsettings.json` contenenti password protette.** La protezione DPAPI è legata all'account Windows dell'autore; un altro utente non può decifrarle.
3. **Abilitare `AllowPlaintextConfigFallback` solo quando strettamente necessario** (es. esecuzione su Linux/macOS senza DPAPI). Le password saranno salvate in chiaro nel file JSON.
4. **Non abilitare `TrustServerCertificate` in produzione** se non per debug. Disabilita la verifica del certificato TLS del server, esponendo a attacchi man-in-the-middle.
5. **Non abilitare `TrustServerCertificateByDefault` via env var** (`DBMIGRATOR_TRUST_SERVER_CERTIFICATE=true`). Il valore di default è `false`; l'override logga un avviso ma disabilita la verifica TLS per tutte le connessioni.
6. **Le password non sono mai esposte nei log.** Il servizio di logging registra server, porta e database, ma non le stringhe di connessione complete né i contenuti protetti.
7. **I file di log (`debug.log`, `error.log`) contengono stack trace ed errori grezzi.** Non archiviare log su repository condivisi o sistemi di logging remoto senza sanitizzazione.
8. **Gli identificatori SQL (nomi tabella, colonna) sono "escaped" per dialetto**, ma il codice usa costruzione stringa per alcune query DDL/DML. Non alimentare nomi di schema/tabella con input non fidati.
9. **I dati migrati possono contenere informazioni sensibili.** Lo strumento copia i dati così come esistono nel sorgente; nessuna sanitizzazione o masking è applicato.
10. **Le connessioni SQL Server usano `Encrypt=True` di default.** PostgreSQL e Oracle richiedono opzione esplicita `RequireEncryption`. Verificare che il flag sia attivo per DB non locali.
11. **Il tool gira solo su Windows Desktop** (Avalonia, DPAPI). Su altri OS le password vengono salvate in chiaro e la protezione credenziali è disabilitata (log di debug).

## Aggiornamenti audit 2026-11 — Credenziali e log

12. **Sanitizzare ogni output di debug prima della scrittura.** `Debug.WriteLine` non scrive su file ma può essere catturato da debugger esterni; nessuna password, token o stringa di connessione deve apparire in forma leggibile (#1).
13. **Implementare retry con backoff nel servizio di log rotation.** `File.Move` senza retry fallisce silenziosamente se il file è bloccato da antivirus/editor (#9).

## Aggiornamenti audit 2026-11 — SQL e quoting

14. **Validare l'escaping di stringhe per ogni dialetto.** `EscapeSqlString` deve gestire sequenze pre-escape (`''` → `'''`); Oracle `CHR(13)/CHR(10)` non deve interpretare testo letterale come codice ASCII (#2, #8).
15. **Limitare la lunghezza totale di ogni INSERT batch** calcolando la dimensione stimata della stringa SQL; ridurre il batch se si avvicina al limite del driver (65 KB riga SQL Server) (#10).

## Aggiornamenti audit 2026-11 — Connessioni TLS e certificati

16. **Mostrare un banner UI quando `TrustServerCertificateByDefault` è attivo** (env var `DBMIGRATOR_TRUST_SERVER_CERTIFICATE=true`) per avvisare l'utente del rischio MITM (#5).
17. **Aggiungere un cap globale al retry esponenziale delle connessioni.** Il tempo totale non deve superare il command timeout (300 s default) (#14).

## Aggiornamenti audit 2026-11 — File e crittografia locale

18. **Rendere configurabile la lista degli owner Oracle da escludere** (attualmente hardcoded in `ScriptGenerationService.cs`, l.20-26) per mantenere compatibilità con APEX 23.x e nuove versioni (#16).
19. **Coprire con UI test i flussi di connessione e selezione tabelle.** `ConnectionSecurity` e `LogFollow` esistono ma manca coverage su SelectAll/DeselectAll, filtri, e carry-over (#15).
