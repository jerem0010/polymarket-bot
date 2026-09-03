# Rapport final - Polymarket Bot Research

Date: 2026-07-11

Objectif initial: reproduire/adapter une approche de bot quant sur les marches
crypto courts de Polymarket, sans argent reel au depart, pour voir s'il existe
un signal exploitable.

Conclusion courte: on n'a pas encore une strategie prete pour le reel, mais on a
appris beaucoup de choses utiles. Le prochain bot doit demarrer comme un outil
de recherche/backtest, pas comme un bot de trading live.

## 1. Ce qui a ete construit

On a cree un bot C#/.NET terminal-first avec:

- scan des marches crypto court terme Polymarket
- recuperation des order books YES/NO via CLOB
- recuperation du spot BTC via APIs publiques
- estimation simple de probabilite directionnelle
- paper trading avec journal CSV
- shadow trading pour mesurer les signaux rejetes
- stats paper et shadow
- thresholds differents YES/NO
- filtres de prix d'entree ask
- snapshot journal complet
- replay offline depuis snapshots
- analyze/sweep offline de parametres
- garde-fous:
  - `--side-filter`
  - `--max-yes-ask`
  - `--max-no-ask`
  - `--min-yes-probability`
  - `--min-no-probability`
  - `--first-signal-only`
  - `--max-entry-seconds-after-start`

Aucun ordre reel n'a ete place. Aucune cle privee n'a ete utilisee.

## 2. Pourquoi C# au debut

C# a ete choisi car .NET etait deja disponible sur la machine, sans installer
Python/WSL au depart. Pour cette phase, la performance du langage n'etait pas le
facteur limitant: les limites etaient surtout la qualite du signal, le parsing
des marches, la latence reseau, la resolution des marches et le backtest.

Pour le prochain bot, Python peut etre excellent pour la recherche/analyse, mais
le plus important est l'architecture:

- collecte propre
- stockage snapshots
- replay/backtest
- validation hors echantillon
- execution separee de la recherche

Le langage vient apres.

## 3. Chronologie des versions

### v0-v2

Premiers scans et premiers journaux paper. Le bot trouvait peu de signaux.
Problemes principaux:

- peu de candidats detectes
- nombreux `missing ask`
- pas assez de contexte pour comprendre les rejets

### v3-v6

Premiers vrais paper trades.

Resultats:

| Run | Closed | Wins | PnL | Lecture |
| --- | ---: | ---: | ---: | --- |
| v3 | 1 | 1 | +1.17 | encourageant mais trop petit |
| v4 | 1 | 0 | -2.00 | bug/risque de timing repere |
| v5 | 1 | 1 | +2.00 | positif mais trop petit |
| v6 | 1 | 0 | -2.00 | pas de conclusion |

Apprentissage: quelques trades ne veulent rien dire. Il fallait du shadow et
plus de donnees.

### v8

Ajout du shadow mode.

Paper v8:

| Closed | Wins | PnL | ROI |
| ---: | ---: | ---: | ---: |
| 4 | 4 | +7.16 | +89.5% |

Shadow v8:

| Closed | Wins | PnL | ROI |
| ---: | ---: | ---: | ---: |
| 198 | 124 | -3.12 | -0.8% |

Point important: le paper etait tres bon, mais trop petit. Le shadow global etait
deja proche de zero/negatif.

### v9

Test de seuils asymetriques YES/NO.

Paper v9:

| Closed | Wins | PnL | ROI |
| ---: | ---: | ---: | ---: |
| 22 | 15 | -1.52 | -3.4% |

Lecture: plus de volume, mais pas rentable. Le winrate etait bon, mais les
pertes coutaient plus que les gains.

### v10

Ajout de plafonds d'ask pour eviter d'acheter trop cher.

Paper v10:

| Closed | Wins | PnL | ROI |
| ---: | ---: | ---: | ---: |
| 7 | 4 | -1.40 | -10.0% |

Lecture: capper l'ask a aide a diagnostiquer, mais pas assez pour creer une
strategie rentable.

### v11

Ajout des snapshots, replay et first-signal discipline.

Paper v11:

| Closed | Wins | PnL | ROI |
| ---: | ---: | ---: | ---: |
| 13 | 7 | -4.41 | -17.0% |

Replay v11 baseline:

| Trades | Wins | PnL | ROI |
| ---: | ---: | ---: | ---: |
| 13 | 8 | -1.47 | -5.7% |

Lecture: la config v11 a ete rejetee. Mais les snapshots ont permis de faire une
analyse offline serieuse.

### v12

Hypothese testee: NO tres tot, tres pas cher, edge positif.

Resultat: 0 paper trade. La regle etait trop stricte.

Lecture: le bot collectait, mais ne tradetait presque jamais.

### v13

Regle moins stricte:

```text
side NO
probability >= 0.52
edge >= -0.10
ask <= 0.65
max entry age 45s
```

Au premier check, v13 etait mauvais. En laissant tourner, il est devenu positif:

Paper v13 final observe:

| Closed | Wins | Win rate | PnL | ROI |
| ---: | ---: | ---: | ---: | ---: |
| 20 | 13 | 65.0% | +8.72 | +21.8% |

Replay v13:

| Trades | Wins | Win rate | PnL | ROI |
| ---: | ---: | ---: | ---: | ---: |
| 20 | 13 | 65.0% | +8.46 | +21.1% |

Lecture: v13 est le meilleur run paper significatif du projet. Mais 20 trades
restent insuffisants pour passer au reel.

## 4. Ce qu'on a appris sur le signal

### 4.1 Le signal brut n'est pas suffisant

Les stats shadow globales ont souvent ete negatives ou proches de zero. Donc
prendre tous les signaux rejetes aurait ete mauvais.

### 4.2 Le winrate ne suffit pas

On a eu des periodes avec 60-70% de winrate mais PnL negatif. Sur Polymarket,
acheter a 0.70 et perdre coute beaucoup. Gagner a 0.70 rapporte peu.

Donc le bot doit optimiser:

- prix d'entree
- payout attendu
- distribution des pertes
- pas seulement le pourcentage de trades gagnants

### 4.3 Les entrees tardives sont dangereuses

Le marche change tres vite dans les fenetres 5 minutes. Les signaux tardifs ont
souvent donne du bruit. Le parametre `--max-entry-seconds-after-start` est utile.

### 4.4 Les signaux "first actionable" etaient souvent meilleurs

Le premier signal exploitable par marche avait souvent de meilleures stats que
les signaux suivants. Mais il faut le definir proprement dans le moteur, pas via
shadow approximatif.

### 4.5 YES vs NO n'est pas stable

Au debut, NO semblait tres fort. Ensuite YES semblait meilleur. Puis v13 NO a
fini positif. Conclusion: il ne faut pas figer une croyance "YES bon" ou "NO bon"
sur une petite session.

Le prochain bot doit analyser par regime de marche:

- heure de la journee
- volatilite
- distance au prix d'ouverture
- prix du contrat
- temps restant
- liquidite/spread

### 4.6 L'edge calcule n'est pas encore fiable

Notre formule etait heuristique:

```text
probability = f(distance_from_open + momentum)
```

Elle n'est pas calibree. On a parfois gagne avec edge negatif et perdu avec edge
positif. Il faut calibrer le modele avant d'utiliser "edge" comme verite.

## 5. Points positifs du projet

- On n'a pas mis d'argent reel.
- On a evite les cles privees trop tot.
- On a construit paper + shadow avant execution.
- On a identifie que les tweaks manuels etaient dangereux.
- On a ajoute snapshots/replay/analyze.
- On a obtenu un run paper v13 positif avec 20 trades.
- On a compris que la recherche doit preceder l'execution.

## 6. Points negatifs / erreurs

- Trop de changements de parametres au debut.
- Trop d'importance donnee a de petits samples.
- Shadow interprete trop vite comme strategie.
- Pas de settlement officiel Polymarket.
- Pas de backtest au debut.
- Pas assez de separation entre collecte, recherche et execution.
- Pas de notion de regime de marche.
- Pas de vraie calibration probabiliste.
- Paper fill trop optimiste: entree immediate au top ask.

## 7. Ce qu'il faut garder pour le prochain bot

Garder absolument:

- paper trading obligatoire
- snapshot journal complet
- replay offline
- analyze/sweep offline
- stats par side
- stats par ask bucket
- stats par temps depuis ouverture
- stats par probabilite estimee
- aucun argent reel avant validation

Garder comme idee de strategie candidate:

```text
NO early
probability >= 0.52
edge >= -0.10
ask <= 0.65
entry <= 45s after start
```

Mais a retester sur un nouveau dataset.

## 8. Ce qu'il faut supprimer / ne plus faire

Ne plus faire:

- lancer v14/v15 juste parce qu'un check est mauvais
- optimiser a l'oeil depuis les logs console
- confondre shadow et trades executables
- utiliser 5-10 trades comme preuve
- discuter live trading avant settlement officiel
- utiliser flash loans: inutile pour cette strategie et dangereux

## 9. Priorites pour le prochain bot

### Priorite 1 - Settlement officiel

Le plus gros trou actuel: le settlement paper utilise le spot local, pas la
resolution officielle Polymarket.

Le prochain bot doit recuperer/verifier:

- outcome officiel du marche
- prix de resolution
- token gagnant
- timestamp de resolution

Sans ca, les backtests peuvent mentir.

### Priorite 2 - Base de donnees snapshots

CSV suffit pour prototype, mais le prochain bot devrait utiliser SQLite ou
Parquet.

Table minimale:

- timestamp
- slug
- asset
- side
- window start/end
- seconds since start
- seconds to end
- spot
- open price
- distance bps
- momentum bps
- bid/ask/spread
- bid size/ask size
- decision
- reason
- eventual official outcome

### Priorite 3 - Replay propre

Le replay doit etre le coeur du projet.

Il doit permettre:

- comparer plusieurs configs
- train/test chronologique
- walk-forward validation
- frais/slippage simules
- max one trade per market
- first signal only
- cooldowns

### Priorite 4 - Modele probabiliste calibre

Remplacer la formule simple par une calibration:

- features: distance, momentum, volatility, time remaining, ask price, spread
- label: outcome officiel
- calibration: bins de probabilite
- verifier si 60% estime correspond vraiment a 60% gagne

### Priorite 5 - Execution seulement apres

Le module live doit venir a la fin, separe de la recherche.

Conditions minimales avant live:

- 200+ marches frais
- ROI positif en replay et paper
- settlement officiel
- simulation slippage
- pas de profit concentre sur 1 heure
- drawdown acceptable

## 10. Architecture recommandee pour le prochain projet

Structure conseillee:

```text
collector/
  collecte marches, order books, spot, outcomes

storage/
  SQLite/Parquet

research/
  replay, analyze, sweep, reports

strategy/
  signaux purs, sans I/O

paper/
  execution simulee

live/
  plus tard seulement

dashboard/
  optionnel, apres la recherche
```

Regle d'or: aucune strategie ne doit dependre de logs console. Tout doit etre
rejouable.

## 11. Commandes utiles du bot actuel

Stats paper:

```powershell
dotnet run -- stats --journal data/paper_trades_v13.csv
```

Stats shadow:

```powershell
dotnet run -- shadow-stats --shadow-journal data/paper_trades_v13_shadow.csv
```

Replay:

```powershell
dotnet run -- replay --snapshot-journal data/paper_trades_v13_snapshots.csv --side-filter NO --min-no-probability 0.52 --min-no-edge -0.10 --max-no-ask 0.65 --max-entry-seconds-after-start 45 --max-entry-seconds-before-end 15
```

Analyze:

```powershell
dotnet run -- analyze --snapshot-journal data/paper_trades_v13_snapshots.csv
```

## 12. Decision finale

On n'a pas encore un bot pret pour trader en reel.

Mais on a une hypothese interessante:

```text
NO court terme, entree rapide, ask controle, seuil faible mais discipline forte
```

Et surtout on a compris la bonne methode:

```text
collecte -> settlement officiel -> replay -> analyse -> holdout paper -> seulement ensuite live
```

Le prochain projet doit commencer directement par cette architecture. C'est ca
le vrai gain de tout ce travail.
