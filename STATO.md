# Stato Audit — Finding APERTI

## MUST FIX (critico)

| # | Titolo | Azione |
|---|--------|--------|
| 1 | Credenziali in output debug | Sanitizzare `Debug.WriteLine` in `ConnectionInfo.cs` (#53-68) |
| 2 | Escape SQL sequenza pre-escape | Estendere `EscapeSqlString` per gestire `''` → `'''` (l.1802) |
| 5 | Banner TrustServerCertificate mancante | Aggiungere banner UI quando env var è attiva (#132) |

## SHOULD FIX (importante)

| # | Titolo | Azione |
|---|--------|--------|
| 8 | Oracle CHR(13)/CHR(10) testo letterale | Sanitize prima della conversione in `FormatSqlValue` (l.1720) |
| 9 | Log rotation senza retry | Aggiungere 2-3 tentativi in `RotateIfNeeded` (LoggerService.cs) |
| 10 | INSERT batch senza limite lunghezza | Calcolare dimensione stimata stringa SQL per batch |

## NICE TO HAVE (backlog)

| # | Titolo | Azione |
|---|--------|--------|
| 14 | Retry esponenziale senza cap temporale | Budget globale = command timeout - overhead |
| 15 | Test coverage UI limitata | Aggiungere test per SelectAll/DeselectAll con filtri |
| 16 | Owner Oracle hardcoded | Spostare lista in `appsettings.json` / env var |
