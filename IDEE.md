# IDEE — Proposte di nuove funzionalità

Raccolta di funzionalità utili, rare in altri file manager o tipicamente riservate a versioni premium (TeraCopy Pro, Beyond Compare, DirectoryOpus, GoodSync, ecc.). Ogni voce indica il valore per l'utente e una stima di complessità (B = bassa, M = media, A = alta).

Legenda stato: `[ ]` proposta · `[~]` in lavorazione · `[x]` implementata

---

## Copia & trasferimento

1. `[x]` **Verifica post-copia automatica (copy + verify)** — dopo ogni copia, ricalcolo del checksum sorgente/destinazione e report degli eventuali mismatch. Nei tool commerciali è quasi sempre feature Pro (TeraCopy). Il `ChecksumService` esiste già: si tratta di integrarlo nel flusso di copia con opzione nelle Impostazioni. *(M)*

2. `[x]` **Coda di copia persistente con ripresa** — journal su disco delle operazioni in corso; dopo crash, chiusura o riavvio l'app propone di riprendere la coda dal punto esatto (file parziali ripresi via offset). Rarissimo fuori da robocopy/rsync a riga di comando. *(A)*

3. `[x]` **Copia multi-destinazione (1 lettura → N scritture)** — copiare lo stesso set di file verso più destinazioni contemporaneamente leggendo la sorgente una sola volta. Ottimo per backup su due dischi. Feature premium di Ultracopier/Supercopier. *(M)*

4. `[x]` **Throttling I/O configurabile** — limite di banda (MB/s) per la copia, per non saturare dischi/rete mentre si lavora. Slider in Impostazioni + toggle rapido durante la copia. *(B)*

5. `[ ]` **Delta-copy stile rsync** — se il file di destinazione esiste, copiare solo i blocchi cambiati (rolling checksum). Enorme risparmio su file grandi modificati poco (VM, database, video in editing). *(A)*

6. `[x]` **Dry-run / simulazione operazioni** — anteprima completa di cosa verrebbe copiato/sovrascritto/eliminato, con verifica spazio disponibile per destinazione, prima di lanciare il batch. Beyond Compare lo fa solo in versione Pro. *(B)*

7. `[x]` **Profili di copia salvabili** — preset nominati (es. "Backup foto", "Sync progetti") che memorizzano coppie sorgente/destinazione, filtri, opzioni di verifica e parallelismo. Un click per rieseguire. *(M)* *(filtri non esistenti nell'app; opzioni di verifica/parallelismo restano globali in Impostazioni)*

8. `[x]` **Copia programmata / watch-folder** — monitor di una cartella (FileSystemWatcher) con sincronizzazione automatica verso la destinazione al cambiamento, o a intervallo di minuti (l'orario fisso resta da fare). È il cuore di GoodSync/SyncBackPro (a pagamento). *(A)*

## Confronto & sincronizzazione

9. `[x]` **Report di confronto esportabile** — esito del confronto directory esportabile in HTML/CSV/JSON con riepilogo (solo-a-sinistra, solo-a-destra, diversi, identici). Utile per audit e documentazione. Feature Pro di Beyond Compare. *(B)*

10. `[ ]` **Sync bidirezionale con rilevamento conflitti** — merge a due vie tra i pannelli: rileva file modificati da entrambi i lati dall'ultima sync (stato salvato) e chiede risoluzione per i conflitti invece di sovrascrivere alla cieca. *(A)*

11. `[x]` **Confronto byte-range di due file** — selezione di un file per pannello e confronto binario con indicazione degli intervalli di byte differenti (primo offset diverso, % identica). Più leggero di un diff visuale completo, quasi mai presente nei file manager. *(M)*

12. `[ ]` **Database integrità / rilevamento bit-rot** — snapshot degli hash di una cartella salvato su disco; una verifica successiva segnala file corrotti silenziosamente (bit-rot) o modificati senza cambio di data. Nessun file manager mainstream lo offre; esiste solo in tool dedicati (SnapRAID, chkbit). *(M)*

## Organizzazione & pulizia

13. `[x]` **Ricerca duplicati con azioni sicure** — scansione per dimensione + hash parziale + hash completo (a cascata, veloce), raggruppamento duplicati e azioni: elimina, sposta, o sostituisci con hardlink per recuperare spazio senza perdere nulla. I dedup-finder decenti sono tutti a pagamento. *(M)*

14. `[ ]` **Rinomina batch con regex e anteprima** — rinomina multipla con pattern regex/contatori/metadati data, anteprima live del risultato e undo completo dell'operazione. Feature storica premium di Directory Opus/Total Commander plugin. *(M)*

15. `[x]` **Treemap occupazione disco integrata** — vista tipo WizTree/SpaceSniffer nel pannello: rettangoli proporzionali alla dimensione per capire subito cosa occupa spazio, con drill-down e azioni dirette (apri, elimina). *(A)*

16. `[ ]` **Cestino di sicurezza interno con undo batch** — le operazioni distruttive (sovrascrittura, eliminazione) spostano prima l'originale in un'area di staging interna; ogni batch è ripristinabile in un click per N giorni. Più affidabile del cestino di sistema per operazioni massive. *(M)*

## Robustezza & diagnostica

17. `[ ]` **Gestione file bloccati con retry intelligente** — quando un file è in uso, mostrare quale processo lo blocca (lsof su Linux / Restart Manager su Windows) e offrire: riprova, riprova a fine coda, salta. TeraCopy Pro-style. *(M)*

18. `[ ]` **Benchmark dischi integrato** — test rapido sequenziale/random su sorgente e destinazione per tarare automaticamente il parallelismo di copia adattivo già presente (suggerisce il valore ottimale invece di lasciarlo all'utente). *(M)*

19. `[x]` **Grafico velocità in tempo reale** — durante la copia: sparkline MB/s, ETA per file e totale, velocità media/di picco, file/s per le copie di molti file piccoli. Dati già disponibili dal motore parallelo, manca solo la visualizzazione. *(B)*

20. `[ ]` **Sanificazione nomi cross-filesystem** — in copia verso FAT/exFAT/NTFS/SMB, rilevamento preventivo di nomi illegali, path troppo lunghi, caratteri riservati e collisioni case-insensitive, con rinomina automatica suggerita e report. Causa classica di copie fallite a metà che quasi nessun tool previene. *(M)*

## Interfaccia & personalizzazione

21. `[ ]` **Selettore lingua (localizzazione UI)** — picker della lingua nelle Impostazioni (es. Italiano/English) con stringhe estratte in file di risorse (`.resx` o JSON) e cambio a caldo senza riavvio dove possibile. Oggi la UI è monolingua; prerequisito per distribuire l'app a un pubblico più ampio. *(M)*

22. `[x]` **Menu hamburger laterale (navigazione a sinistra)** — sostituire la barra tab orizzontale in alto con un menu hamburger a sinistra: pannello laterale collassabile (icone sole ↔ icone + etichette) con le voci di navigazione (Copia, Esplora, Sync auto, ecc.), stato espanso/collassato persistito nelle Impostazioni. Libera spazio verticale e scala meglio all'aumentare delle sezioni. *(M)* *(implementata: TabControl verticale con etichette collassabili, stato in Impostazioni/settings.json)*

23. `[ ]` **Visualizzazione gerarchica alternativa per l'occupazione disco** — alternativa al treemap (punto 15). Vista tipo tree-file-size / du-browser: albero espandibile verticale che mostra path e dimensioni relative senza riempire lo schermo di blocchi. **Scelta: lista gerarchica con barre inline** (ogni riga = folder, barra % occupazione, byte totali). Utile per directory con migliaia di file dove i blocchi diventano invisibili. *(M)*

---

## Note di priorità

Punti di ingresso rapidi (valore alto, complessità bassa): **4, 6, 9, 19**.
Sinergie con il codice esistente: **1** e **12** riusano `ChecksumService`; **18** e **19** si appoggiano al motore di copia parallela adattiva già implementato.
