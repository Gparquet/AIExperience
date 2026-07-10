import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { api } from '../api/client';
import { createIngestionHubConnection } from '../realtime/ingestionHub';
import type { DocumentProgressChangedEvent, DocumentStatusChangedEvent } from '../types';

type StatusListener = (event: DocumentStatusChangedEvent) => void;

interface IngestionNotificationsContextValue {
  /**
   * À appeler juste après la création d'un document (upload ou transcription vidéo), pour que sa
   * fin de traitement déclenche un toast nommé et, si la connexion temps réel est indisponible,
   * soit suivie par le repli en polling.
   */
  registerPendingDocument: (documentId: string, fileName: string) => void;
  /** S'abonne à tous les changements de statut, pour patcher l'état local d'une page (liste de documents...). */
  subscribe: (listener: StatusListener) => () => void;
  /**
   * S'abonne à l'avancement fin d'UN document (filtré par id). Réservé à la page de détail :
   * les ticks haute fréquence ne concernent pas la liste, qui ne s'abonne qu'au statut grossier.
   */
  subscribeProgress: (documentId: string, listener: (event: DocumentProgressChangedEvent) => void) => () => void;
}

const IngestionNotificationsContext = createContext<IngestionNotificationsContextValue | null>(null);

/** Fréquence du repli en polling — uniquement actif tant que la connexion temps réel n'est pas établie. */
const POLL_INTERVAL_MS = 4000;

export function IngestionNotificationsProvider({ children }: { children: ReactNode }) {
  const [toast, setToast] = useState<{ message: string; kind: 'success' | 'error' } | null>(null);
  const listenersRef = useRef(new Set<StatusListener>());
  // Listeners d'avancement fin, indexés par documentId (un seul document suivi à la fois en pratique).
  const progressListenersRef = useRef(new Map<string, Set<(e: DocumentProgressChangedEvent) => void>>());
  // Associe chaque document en cours de traitement à son nom, pour donner un libellé au toast
  // et savoir quels documents suivre par polling si la connexion temps réel est indisponible.
  const pendingNamesRef = useRef(new Map<string, string>());
  const connectedRef = useRef(false);

  const showToast = useCallback((message: string, kind: 'success' | 'error') => {
    setToast({ message, kind });
    setTimeout(() => setToast(null), 3500);
  }, []);

  const handleStatusChanged = useCallback((event: DocumentStatusChangedEvent) => {
    listenersRef.current.forEach(listener => listener(event));

    if (event.status !== 'Completed' && event.status !== 'Failed') return;

    const fileName = pendingNamesRef.current.get(event.documentId);
    pendingNamesRef.current.delete(event.documentId);
    if (!fileName) return; // pas un document que cette session a créé (ex. autre onglet) — pas de toast

    if (event.status === 'Completed') showToast(`« ${fileName} » est prêt — vous pouvez l'interroger.`, 'success');
    else showToast(`Échec du traitement de « ${fileName} ».`, 'error');
  }, [showToast]);

  useEffect(() => {
    const connection = createIngestionHubConnection();
    connection.on('documentStatusChanged', handleStatusChanged);
    // Dispatch de l'avancement fin uniquement aux abonnés du document concerné.
    connection.on('documentProgressChanged', (event: DocumentProgressChangedEvent) => {
      progressListenersRef.current.get(event.documentId)?.forEach(listener => listener(event));
    });
    connection.onreconnected(() => { connectedRef.current = true; });
    connection.onreconnecting(() => { connectedRef.current = false; });
    connection.onclose(() => { connectedRef.current = false; });

    connection.start()
      .then(() => { connectedRef.current = true; })
      .catch(() => { connectedRef.current = false; }); // le repli en polling ci-dessous prend le relais

    return () => { void connection.stop(); };
  }, [handleStatusChanged]);

  useEffect(() => {
    // Le statut en base reste la source de vérité ; ce polling n'est qu'un filet de sécurité
    // quand la connexion temps réel est indisponible (WebSocket et long-polling tous deux hors service).
    const interval = setInterval(() => {
      if (connectedRef.current || pendingNamesRef.current.size === 0) return;

      for (const documentId of [...pendingNamesRef.current.keys()]) {
        api.documents.get(documentId)
          .then(document => {
            if (document.status === 'Completed' || document.status === 'Failed') {
              handleStatusChanged({ documentId, status: document.status, errorMessage: document.errorMessage ?? null });
            }
          })
          .catch(() => {
            // Document supprimé entre-temps ou API momentanément indisponible : nouvelle tentative au tour suivant.
          });
      }
    }, POLL_INTERVAL_MS);

    return () => clearInterval(interval);
  }, [handleStatusChanged]);

  const registerPendingDocument = useCallback((documentId: string, fileName: string) => {
    pendingNamesRef.current.set(documentId, fileName);
  }, []);

  const subscribe = useCallback((listener: StatusListener) => {
    listenersRef.current.add(listener);
    return () => { listenersRef.current.delete(listener); };
  }, []);

  const subscribeProgress = useCallback((documentId: string, listener: (event: DocumentProgressChangedEvent) => void) => {
    const map = progressListenersRef.current;
    if (!map.has(documentId)) map.set(documentId, new Set());
    map.get(documentId)!.add(listener);
    return () => {
      const set = map.get(documentId);
      set?.delete(listener);
      if (set && set.size === 0) map.delete(documentId);
    };
  }, []);

  return (
    <IngestionNotificationsContext.Provider value={{ registerPendingDocument, subscribe, subscribeProgress }}>
      {children}
      {toast && (
        <div className={`toast toast-${toast.kind}`}>
          {toast.kind === 'success' ? '✓' : '✕'} {toast.message}
        </div>
      )}
    </IngestionNotificationsContext.Provider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components -- hook indissociable de son contexte, pattern standard
export function useIngestionNotifications(): IngestionNotificationsContextValue {
  const context = useContext(IngestionNotificationsContext);
  if (!context) throw new Error('useIngestionNotifications doit être utilisé sous IngestionNotificationsProvider.');
  return context;
}
