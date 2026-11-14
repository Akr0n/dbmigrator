# Audit Repository DatabaseMigrator — Report Completo

**Data:** 2026-11-13  
**Strumento:** codegraph_explore (knowledge graph) + lettura sorgenti verificate  
**Scope:** Core services, UI, credential handling, SQL generation, escaping, test coverage

---

## MUST FIX (vulnerabilità critiche o rischio perdita dati)

### 1. Credenziali loggate in chiaro nel debug output
**File:** `ConnectionInfo.cs` + `LoggerService.cs`  
**Gravità:** Alta — esposizione password nei log di sistema

`ConnectionInfo.ToString()` e il logging interno scrivono username e password (o parte della password per Oracle) nel debug output. `LoggerService.Log()` scrive tutto su file disco in `%LOCALAPPDATA%\DatabaseMigrator\debug.log`. Le credenziali sono leggibili da chiunque abbia accesso all'account utente locale.

**Fix:** Sanitize le stringhe di connessione prima del logging; sostituire password con `[REDACTED]` o `***`.

---

### 2. Escape SQL basato su replace (vulnerabilità injection negli script)
**File:** `DatabaseService.cs` — `EscapeSqlString()` (linea 1813-1816)  
**Gravità:** Alta — rischio SQL injection nella generazione script

```csharp
private string EscapeSqlString(string value) => value.Replace("'", "''");
```

L'escape è replace-based, non parameterizzato. Funziona per valori letterali in INSERT generati dinamicamente, ma:
- Non gestisce sequenze di escape complesse (es. `'` già raddoppiato → `''` → `'''` = bug)
- L'escape è applicato a valori stringa ma la query SQL viene costruita tramite concatenazione

**Fix:** Usare un approccio più robusto per l'escaping; validare che i dati sorgente non contengano già escape sequence.

---

### 3. Oracle DROP TABLE senza quoting degli identificatori
**File:** `SchemaMigrationService.cs` + `ScriptGenerationService.cs`  
**Gravità:** Alta — drop silenzioso della tabella sbagliata per nomi case-sensitive

Oracle mantiene il case degli identificatori quotati (`"Tabella"` ≠ `TABELLA`). Se il DDL usa `"Tabella"` ma il DROP usa `TABELLA` (o viceversa), il DROP fallisce o colpisce la tabella sbagliata. La consistency dipende da come viene formattato il nome in entrambe le posizioni.

**Fix:** Verificare che FormatTableName sia usato consistentemente per CREATE e DROP; aggiungere test unitari per nomi con case misto.

---

### 4. DPAPI fallback su Windows non gestito (non-Windows → decrypt sempre falso)
**File:** `CredentialProtectionService.cs`  
**Gravità:** Media-Alta — password non decrittate su sistemi non-Windows

Su piattaforme non-Windows, `ProtectedData.Unprotect()` lancia `PlatformNotSupportedException`. Il fallback restituisce `false` per Decrypt, rendendo le password illeggibili. L'opzione `AllowPlaintextConfigFallback` può aggirare il problema ma è disabilitata di default.

**Fix:** Gestire esplicitamente il caso non-Windows con un messaggio d'errore chiaro; considerare un provider di crittografia cross-platform (es. AES+PBKDF2 con key derivata da machine-id).

---

### 5. `TrustServerCertificateByDefault` configurabile via env var
**File:** `RuntimeOptions.cs` — linea 124  
**Gravità:** Media — può disabilitare la validazione TLS senza consapevolezza

L'env var `DBMIGRATOR_TRUST_SERVER_CERTIFICATE` può sovrascrivere il default `false`, abilitando certificate validation bypass su connessioni in produzione. Non c'è alcun warning UI quando questa opzione è attiva.

**Fix:** Mostrare un banner di avvertenza nella UI quando TrustServerCertificate è attivo; loggare esplicitamente quando la connessione viene stabilita con cert bypass.

---

## SHOULD FIX (miglioramenti rilevanti)

### 6. `SelectVisible` accumula selezioni tra filtri successivi
**File:** `TableSelection.cs` — linea 30-34  
**Problema:** "Seleziona tutto" seleziona solo le tabelle visibili con il filtro corrente, ma non deseleziona quelle nascoste. Filtrare→selezionare→cambiare filtro→selezionare di nuovo accumula selezioni in modo non intuitivo.

**Fix:** Aggiungere un "Deseleziona tutto prima di selezionare" esplicito o documentare il comportamento come "accumulativo".

---

### 7. Table selection carry-over durante reload
**File:** `TableSelection.cs` — linea 61-71  
**Problema:** Il CarryOver usa `(Schema, TableName)` come chiave di matching, ma i nomi sono case-sensitive per il matching. Se la sorgente cambia case (es. PostgreSQL lowercase → SQL Server preserved), le tabelle selezionate non vengono carry-over.

**Fix:** Usare un matching case-insensitive per il carry-over, coerente con `TableDependencyOrderer.Key()` che usa `ToUpperInvariant()`.

---

### 8. Oracle line breaks in string literals: CHR(13)/CHR(10) può spezzare SQL
**File:** `DatabaseService.cs` — linea 1728-1743  
**Problema:** Per Oracle, i caratteri di newline sono convertiti in `||CHR(13)||` e `||CHR(10)||`. Se il valore contiene già il testo letterale `CHR(13)` o `CHR(10)`, questo viene interpretato come codice ASCII invece che come testo.

**Fix:** Usare un approccio più robusto (es. concatenazione carattere per carattere con CASE/DECODE, o escape completo prima della conversione).

---

### 9. Log rotation: rename atomic su file lock
**File:** `LoggerService.cs` — linea 134  
**Problema:** `File.Move()` può fallire se il file di log è aperto da un altro processo (es. editor di testo, antivirus). Il catch-all silenzia l'errore senza retry.

**Fix:** Implementare un breve retry (2-3 tentativi) prima di fallback a rename con timestamp diverso.

---

### 10. Nessun controllo su dimensione massima INSERT
**File:** `ScriptGenerationService.cs` — linea 658  
**Problema:** Gli INSERT multi-riga per SQL Server hanno batchSize limitato da `RowsPerInsertBatch`, ma non c'è un limite alla lunghezza totale della stringa SQL. Una tabella con colonne molto larghe (es. VARCHAR(MAX), NVARCHAR(4000)) può generare una riga INSERT che supera il limite del server (tipicamente 65536 byte per riga in alcuni driver).

**Fix:** Calcolare la lunghezza stimata di ogni batch e ridurre se si avvicina al limite; aggiungere un warning nel report.

---

### 11. Oracle `SET DEFINE OFF` non sempre applicato
**File:** E2E script (`run-e2e-matrix.ps1`) + init Oracle  
**Problema:** Gli script SQL Oracle che contengono il carattere `&` (ampersand) vengono interpretati come variabili di substitution da SQL*Plus/SQLcl se `SET DEFINE OFF` non è attivo. L'init richiede `SET DEFINE OFF`, ma gli script generati potrebbero contenere `&` nei dati.

**Fix:** Verificare che la generazione script Oracle escape i caratteri `&` o che SQL*Plus sia invocato con `SET DEFINE OFF`.

---

### 12. Cyclic foreign key: fallback a disable non verificato
**File:** `TableDependencyOrderer.cs` — linea 67-68  
**Problema:** Quando vengono rilevati cicli FK, `HadCycles=true` viene impostato ma il caller (`MainWindowViewModel`) deve decidere se disabilitare i vincoli. Se il callback non è configurato o l'utente rifiuta, la migrazione fallisce senza un fallback automatico.

**Fix:** Implementare un fallback automatico: disabilita i vincoli e continua con log warning.

---

## NICE TO HAVE (miglioramenti minori)

### 13. Log path hardcoded in `LocalApplicationData`
**File:** `LoggerService.cs` — linea 14-16  
**Suggerimento:** Il percorso di log è hardcoded. Sarebbe meglio esporlo come opzione configurabile (`DBMIGRATOR_LOG_PATH`) per ambienti con policy di storage restrittive.

---

### 14. Nessun rate limiting su retry
**File:** `DatabaseServiceBase.cs`  
**Suggerimento:** Il retry usa delay esponenziale ma senza un cap globale di tempo. Su connessioni instabili, il totale può superare il timeout del command (300s default). Aggiungere un budget temporale al retry policy.

---

### 15. Test coverage UI limitata
**File:** `tests/DatabaseMigrator.UiTests`  
**Suggerimento:** Solo alcuni test UI (ConnectionSecurity, LogFollow) sono implementati. La maggior parte della logica ViewModel non ha copertura UI test. Aggiungere test per SelectAll/DeselectAll con filtri attivi.

---

### 16. Oracle catalog list hardcoded
**File:** `DatabaseService.cs` — linea 95-105  
**Suggerimento:** La lista degli owner Oracle esclusi è hardcoded e non mantiene il passo con le nuove versioni Oracle (es. APEX 23.x). Considerare un approccio basato su `IS_PUBLICLY_ACCESSIBLE` o flag di sistema.

---

### 17. Nessuna validazione input per nome database/server
**File:** `ConnectionInfo.cs`  
**Suggerimento:** I campi server e database non sono validati prima dell'uso nella connection string. Un valore malformato può generare errori criptici dal driver.

---

### 18. Memory: tutte le tabelle caricate in memoria
**File:** `MainWindowViewModel.cs`  
**Suggerimento:** Per cataloghi con migliaia di tabelle, il caricamento completo in `ObservableCollection<TableInfo>` può consumare memoria significativa. Valutare lazy loading o virtualizzazione avanzata.

---

## Riepilogo

| Categoria | Count | Priorità |
|-----------|-------|----------|
| Must Fix | 5 | Critico — correggere prima del prossimo rilascio |
| Should Fix | 7 | Importante — pianificare per sprint successivo |
| Nice to Have | 6 | Miglioramento continuo — backlog normale |

**Totale findings:** 18 (sotto il limite di 20)

---

## Metodi di verifica

1. **Credenziali in log:** Cercare `Debug.WriteLine` con password nel codice; verificare che i file di log non contengano credenziali dopo il fix
2. **Escape SQL:** Test unitari con stringhe contenenti `'`, `''`, `'''`, Unicode, e caratteri speciali
3. **Oracle DROP/CREATE consistency:** E2E test con tabelle create via DDL quotato e verify che DROP le rimuova correttamente
4. **DPAPI cross-platform:** Test su Linux/macOS (Wine o CI) per verificare il comportamento di fallback
5. **TrustServerCertificate banner:** UI test per verificare che il banner appaia quando l'opzione è attiva
