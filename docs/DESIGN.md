# Design System — Database Migrator

## Tema e font

- **Framework**: Avalonia con `FluentTheme` (App.axaml:3)
- **Temi**: Chiaro / Scuro / Sistema, selezionabili dal menu "Modifica → Tema"
- **Font**: SF Pro Display (Windows), Segoe UI (fallback Windows), Helvetica Neue (macOS), Ubuntu (Linux)

## Colori

Definiti in `App.axaml` con `ResourceDictionary.ThemeDictionaries`. Tutti i componenti usano `DynamicResource`.

| Chiave | Scuro | Chiaro | Uso |
|---|---|---|---|
| `AppPageBackground` | `#0d1117` | `#ffffff` | Sfondo pagina principale |
| `AppSurfaceBackground` | `#161b22` | `#f6f8fa` | Menu bar, pannelli elevati |
| `AppSunkenBackground` | `#1c2128` | `#eaeef2` | ListBox, TextBox sfondo |
| `AppSecondaryBackground` | `#21262d` | `#f6f8fa` | Card background |
| `AppHoverBackground` | `#30363d` | `#eaeef2` | Hover elementi |
| `AppBorderBrush` | `#30363d` | `#d0d7de` | Bordi card, input, liste |
| `AppTextPrimary` | `#c9d1d9` | `#1f2328` | Testo principale |
| `AppTextSecondary` | `#8b949e` | `#59636e` | Etichette, metadata |
| `AppTextTertiary` | `#6e7681` | `#59636e` | Testo de-emphasis (Chiaro = Secondary) |
| `AppAccent` | `#58a6ff` | `#0969da` | Link, schema target, contatori |
| `AppPrimaryButton` | `#1f6feb` | `#0969da` | Bottone primario (Connect) |
| `AppPrimaryHover` | `#388bfd` | `#0860ca` | Hover bottone primario |
| `AppPrimaryPressed` | `#1158c7` | `#054da3` | Pressed bottone primario |
| `AppSuccess` | `#3fb950` | `#1a7f37` | Schema target, progresso, righe totali |
| `AppDanger` | `#f85149` | `#d1242c` | Errori, allarmi |
| `AppDangerBackground` | `#3a1015` | `#ffebe9` | Sfondo badge pericolo |
| `AppDangerText` | `#ffa198` | `#d1242c` | Testo pericolo |
| `AppDangerBadge` | `#da3633` | `#cf222e` | Badge pericolo |
| `AppSelectionOverlay` | `#1f6feb33` | `#0969da26` | Selezione riga lista |
| `AppLogInfo` | `#e0e0e0` | `#1f2328` | Voce log info |
| `AppLogWarning` | `#ff9800` | `#9a6700` | Voce log warning |
| `AppLogError` | `#ef5350` | `#cf222e` | Voce log errore |

## Tipografia

| Classe / Uso | Dimensione | Peso | File |
|---|---|---|---|
| Titolo card (`cardTitle`) | 14px | SemiBold | MainWindow.axaml:30 |
| Etichetta campo (`fieldLabel`) | 12px | SemiBold | MainWindow.axaml:36 |
| Testo corpo (`body`, `muted`) | 11-12px | normale | MainWindow.axaml:41,45 |
| Status / contatori | 12px | SemiBold | inline (es. riga 221) |
| Titolo About Window | 20px | Bold | AboutWindow.axaml:12 |
| Info assistenza | 11px | normale | AboutWindow.axaml:19 |

## Spaziature e raggi

- **Scale spaziatura**: `Spacing="4"` (titoli), `Spacing="6"` (campi form), `Spacing="8"` (toolbar), `Spacing="10"` (pannelli card), `Spacing="12"` (sezioni)
- **Margin card**: `16` lato, `8` tra card affiancate
- **Padding card**: `12` interno
- **Padding form fields**: `10,0` orizzontale; `16,0` verticale
- **CornerRadius**: `8` (card), `6` (input, ListBox, tabella)
- **BorderThickness**: `1` ovunque
- **ProgressBar**: altezza `24`, raggio `4`

## Componenti

| Componente | Dove | Note |
|---|---|---|
| Card (`Classes="card"`) | MainWindow.axaml:217,359,379 | Background AppSecondaryBackground, bordo 1px, raggio 8 |
| TextBox / PasswordBox | MainWindow.axaml:235-257 | Raggio 6, focus accent border (stili riga 50-64) |
| ComboBox | MainWindow.axaml:226,283 | Stile proprio (riga 65), raggio 6 |
| CheckBox | MainWindow.axaml:264,267 | Stile proprio (riga 80) |
| Button primary/secondary | MainWindow.axaml:332,351-354 | Primary = AppPrimaryButton, Secondary = bordo accent |
| ProgressBar | MainWindow.axaml:471 | Altezza 24, raggio 4, colore AppSuccess |
| ListBox (dati) | MainWindow.axaml:362,382 | Raggio 6, sfondo AppSunkenBackground, selezione overlay |
| TabControl | MainWindow.axaml:207,156-169 | 2 tab: "Connetti" + "Genera Script" |
| MenuBar | MainWindow.axaml:187-204 | File / Modifica (tema) / Aiuto |
| StatusBar | MainWindow.axaml:503 | Barra inferiore con stato |
| AboutWindow | AboutWindow.axaml | Finestra modale fissa, senza ridimensionamento |

## Regole

1. Usare solo le chiavi `App*` definite in `App.axaml` per colori; mai valori hardcoded.
2. Tutte le risorse colore usano `DynamicResource` (non `StaticResource`) per supportare il cambio tema live.
3. Spaziature verticali dei campi form: `Spacing="6"` tra label e input, `Spacing="10"` tra sezioni.
4. Card sempre con `Classes="card"`, bordi con `{DynamicResource AppBorderBrush}`, raggio 8px.
5. Tabella sorgente usa `AppAccent` per lo schema; tabella destinazione usa `AppSuccess` per il nome tabella.
6. ProgressBar e contatori usano `AppSuccess`; errori di connessione usano `AppDanger`.
7. Bottone primario solo per azioni irreversibili o principali (Connect, Start Migration); tutto il resto è `secondary`.
