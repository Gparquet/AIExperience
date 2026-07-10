# ADR-004 — Suivi de progression d'upload via XMLHttpRequest plutôt que fetch

**Date :** 2026-07-08  
**Statut :** Accepté  
**Auteur :** Geoffrey

---

## Contexte

Le front-end Step 3 envoie des fichiers potentiellement volumineux : PDF lourds, mais surtout
**vidéos et fichiers audio** (plusieurs dizaines, voire centaines de Mo) vers
`POST /api/documents` et `POST /api/video/transcribe`.

Sur un transfert de cette taille, laisser l'utilisateur devant un bouton figé sans retour
visuel est une mauvaise expérience : il ne sait pas si l'envoi progresse, s'il est bloqué, ni
combien de temps il reste. Il faut afficher une **barre de progression réelle** du transfert.

Or tout le reste du client HTTP (`client.ts`) est bâti sur **`fetch`**, l'API moderne standard.
Le problème : **`fetch` n'expose pas la progression d'upload**.

---

## Décision

Utiliser **`XMLHttpRequest` (XHR)** pour les seuls appels d'upload de fichier nécessitant une
barre de progression, tout en conservant **`fetch`** pour tout le reste de l'API.

### Pourquoi pas `fetch` ?

`fetch` ne fournit aucun événement de progression **côté requête (upload)**. La propriété
`ReadableStream` de la réponse permet de suivre le *téléchargement* (download), mais **pas
l'envoi** du corps de la requête. Il n'existe, à ce jour, aucun moyen standard et largement
supporté d'obtenir `bytes envoyés / bytes totaux` pendant un upload avec `fetch`.

`XMLHttpRequest`, plus ancien, expose au contraire nativement **`xhr.upload.onprogress`**, avec
`event.loaded` / `event.total` — exactement l'information recherchée.

### Périmètre volontairement restreint

XHR n'est utilisé **que** pour les deux uploads multipart avec progression. Tous les autres
appels (JSON, streaming SSE, GET…) restent en `fetch`. On isole la seule primitive « legacy »
là où elle apporte une capacité que le standard moderne n'offre pas, sans contaminer le reste.

---

## Architecture de la solution

### Un helper `uploadWithProgress` encapsulant XHR dans une `Promise`

```typescript
/**
 * Envoie un formulaire multipart via XMLHttpRequest plutôt que fetch, pour pouvoir suivre la
 * progression réelle du transfert (fetch ne l'expose pas côté requête). Traite tout code 2xx
 * (y compris 202 Accepted) comme un succès, symétriquement à request().
 */
function uploadWithProgress<T>(
  path: string,
  formData: FormData,
  onProgress?: (percent: number) => void,
): Promise<T> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', `${BASE_URL}${path}`);

    // Seul XHR expose la progression d'envoi : loaded / total pendant le transfert.
    xhr.upload.onprogress = event => {
      if (onProgress && event.lengthComputable)
        onProgress(Math.round((event.loaded / event.total) * 100));
    };

    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(xhr.responseText ? (JSON.parse(xhr.responseText) as T) : (undefined as T));
        return;
      }
      // On rattache le code HTTP à l'erreur, comme request(), pour distinguer un 409 (doublon).
      const error = new Error(xhr.responseText || `HTTP ${xhr.status}`) as Error & { status?: number };
      error.status = xhr.status;
      reject(error);
    };

    xhr.onerror = () => reject(new Error("Échec réseau pendant l'envoi du fichier."));
    xhr.send(formData);
  });
}
```

Deux exigences de cohérence avec le helper `request` (fetch) :

1. **Tout code 2xx est un succès**, y compris `202 Accepted` désormais renvoyé par les uploads
   traités en arrière-plan (cf. [ADR-002](002-ingestion-asynchrone-outbox.md)).
2. **Le code HTTP est rattaché à l'erreur** (`error.status`) pour que l'appelant distingue un
   `409` (doublon détecté, cf. [ADR-007](007-detection-doublons-upload.md)) d'une autre erreur.

### Enveloppement en `Promise`

XHR étant basé sur des callbacks, on l'enveloppe dans une `Promise` pour l'exposer avec la même
signature `async` que le reste de `client.ts`. Le code appelant (`api.documents.upload`,
`api.video.transcribe`) reçoit une simple fonction `onProgress?: (percent) => void` et ignore
totalement qu'un XHR se cache derrière.

---

## Conséquences

### Positives

- Barre de progression **réelle** (basée sur les octets transférés), pas une animation factice.
- Détail d'implémentation **encapsulé** : l'API publique du client reste homogène et typée.
- Comportement d'erreur **symétrique** à `fetch` (codes 2xx, `error.status`), donc gestion
  d'erreurs unifiée côté composants (notamment le `409` doublon).

### Négatives / points d'attention

- Coexistence de **deux primitives HTTP** (`fetch` + XHR) dans le même client — cloisonnée au
  seul cas de l'upload, à documenter pour éviter la confusion.
- XHR ne supporte pas nativement `AbortController` ; une annulation d'upload passerait par
  `xhr.abort()` (non exposé aujourd'hui, à ajouter si le besoin d'annulation apparaît).
- API « legacy » : acceptable tant qu'aucun standard ne comble le manque de `fetch`.

---

## Alternatives rejetées

| Alternative | Raison du rejet |
|-------------|-----------------|
| `fetch` seul | N'expose aucune progression d'**upload** ; impossible d'afficher une barre réelle. |
| Barre de progression factice (animation temporisée) | Trompe l'utilisateur, ne reflète ni un blocage ni la taille réelle du fichier. |
| Découper le fichier et streamer les chunks avec `fetch` + `ReadableStream` en requête | Support navigateur/serveur immature (`duplex: 'half'`), complexité disproportionnée. |
| Bibliothèque tierce (axios, tus-js-client) | Dépendance supplémentaire pour un besoin que ~30 lignes de XHR couvrent exactement. |
