# ADR-010 — Gestion du schéma PostgreSQL par scripts SQL manuels plutôt que par migrations EF Core

**Date :** 2026-06-01  
**Statut :** Accepté (avec réserves)  
**Auteur :** Geoffrey

---

## Contexte

Le projet utilise **Entity Framework Core** comme ORM, qui fournit un système de **migrations**
(génération et application automatiques des évolutions de schéma à partir des entités C#).

Pourtant, le schéma de la base est ici géré par un **script SQL d'initialisation** (`init.sql`)
exécuté au démarrage du conteneur PostgreSQL, complété par des **scripts de migration manuels**
(`migrate-temporal-chunks.sql`, `migrate-lot2ter-async-ingestion.sql`…).

Deux raisons de fond à ce choix :

1. **pgvector.** Le cœur du système repose sur l'extension `pgvector` : type `vector(768)`, index
   **HNSW** avec paramètres de distance cosinus, extension à activer (`CREATE EXTENSION`). Ces
   objets ne sont pas exprimables proprement par le modèle d'entités EF Core et exigent du SQL
   explicite de toute façon.
2. **Contrôle total du DDL.** Index spécialisés, colonnes typées `double precision`, contraintes,
   ordre d'activation des extensions : on veut maîtriser exactement le DDL produit, pas déléguer
   à un générateur.

---

## Décision

**Ne pas utiliser les migrations EF Core.** Le schéma est la propriété de scripts SQL
versionnés : `init.sql` pour la création complète, et un script `migrate-*.sql` par évolution.
EF Core est utilisé **uniquement** comme ORM d'accès aux données (mapping, requêtes), jamais
comme gestionnaire de schéma.

---

## Architecture de la solution

### Fichiers et responsabilités

| Fichier | Rôle |
|---------|------|
| `scripts/init.sql` | Schéma complet : extension pgvector, tables (`documents`, `document_chunks`, `conversation_sessions`, `chat_messages`, `citations`, `outbox_messages`), index HNSW. |
| `scripts/migrate-temporal-chunks.sql` | Ajout des colonnes vidéo `start_time_seconds` / `end_time_seconds`. |
| `scripts/migrate-lot2ter-async-ingestion.sql` | Évolutions liées à l'ingestion asynchrone / outbox. |

`init.sql` est monté et exécuté par le conteneur PostgreSQL (`docker-compose`). Les migrations
manuelles s'appliquent à une base **déjà existante** que l'on ne veut pas recréer.

### Le mapping EF Core doit rester aligné « à la main »

Comme EF Core ne génère pas le schéma, c'est au développeur de garder les **configurations EF**
(`DocumentChunkConfiguration`, value converters `TimeSpan` ↔ `double`, etc.) **cohérentes** avec
le SQL écrit à la main. Un désalignement ne se voit qu'à l'exécution.

### Dette identifiée et assumée

Ce choix a déjà produit un **incident réel** : les colonnes `start_time_seconds` /
`end_time_seconds`, mappées par EF Core et utilisées dans les requêtes SQL brutes de
`PgVectorStoreService`, **manquaient dans `init.sql`**. Une base recréée à partir de `init.sql`
seul était donc cassée, et il a fallu appliquer `migrate-temporal-chunks.sql` manuellement.

C'est le talon d'Achille de l'approche : **rien ne vérifie automatiquement** que le modèle C# et
le schéma SQL concordent. D'où le statut « Accepté **avec réserves** ».

---

## Conséquences

### Positives

- **Contrôle total du DDL** : extension pgvector, index HNSW et paramètres exacts, impossibles à
  exprimer proprement via les migrations EF Core.
- Schéma **lisible et auditable** dans un unique fichier SQL, indépendant de la version d'EF.
- Initialisation **reproductible** au démarrage du conteneur, sans étape applicative de migration.
- Pas de couplage du démarrage de l'API à un `dotnet ef database update`.

### Négatives / points d'attention

- **Aucune détection automatique de dérive** entre entités C# et schéma SQL → risque d'oubli
  (déjà survenu avec les colonnes vidéo).
- **Application manuelle** des migrations sur les bases existantes : discipline requise, risque
  d'environnements désynchronisés.
- Pas d'historique de migrations structuré (`__EFMigrationsHistory`) ni de rollback outillé.
- Onboarding : un nouveau venu doit savoir quels scripts appliquer et dans quel ordre.

### Piste d'atténuation

Un test d'intégration démarrant une base neuve **à partir de `init.sql` seul** puis exécutant un
`SELECT` sur chaque colonne mappée par EF détecterait immédiatement toute dérive. À considérer
pour transformer la réserve en garantie.

---

## Alternatives rejetées

| Alternative | Raison du rejet |
|-------------|-----------------|
| Migrations EF Core | Gèrent mal l'extension pgvector, le type `vector` et les index HNSW ; DDL généré peu maîtrisable. |
| ORM « code-first » avec `EnsureCreated()` | Ne crée pas les objets pgvector spécialisés ; inadapté au versioning de schéma. |
| Outil de migration SQL dédié (Flyway, DbUp, Liquibase) | Dépendance/outillage supplémentaires ; les scripts SQL + docker-compose suffisent à l'échelle du projet. |
| Générer le schéma puis retoucher le SQL | Boucle de synchronisation fragile ; autant écrire le SQL directement et en faire la source de vérité. |
