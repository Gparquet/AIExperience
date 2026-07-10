import type { DocumentStatus, IngestionProgress, IngestionStage } from '../types';

// Détermine la séquence d'étapes pertinente selon le type de fichier.
function stagesFor(fileName: string): IngestionStage[] {
  const lower = fileName.toLowerCase();
  const isVideo = /\.(mp4|mkv|webm|avi|mov)$/.test(lower);
  const isAudio = /\.(wav|mp3|m4a|ogg|flac)$/.test(lower);
  if (isVideo) return ['Queued', 'ExtractingAudio', 'Transcribing', 'Chunking', 'Embedding', 'Storing', 'Completed'];
  if (isAudio) return ['Queued', 'Transcribing', 'Chunking', 'Embedding', 'Storing', 'Completed'];
  return ['Queued', 'ExtractingText', 'Chunking', 'Embedding', 'Storing', 'Completed'];
}

// Libellés lisibles par étape.
const STAGE_LABELS: Record<IngestionStage, string> = {
  Queued: 'En file d’attente',
  ExtractingAudio: 'Extraction audio',
  Transcribing: 'Transcription',
  ExtractingText: 'Extraction du texte',
  Chunking: 'Découpage',
  Embedding: 'Vectorisation',
  Storing: 'Stockage',
  Completed: 'Terminé',
  Failed: 'Échec',
};

// Ordre canonique pour comparer l'avancement (les étapes avant l'étape courante sont « faites »).
const STAGE_ORDER: IngestionStage[] = [
  'Queued', 'ExtractingAudio', 'Transcribing', 'ExtractingText', 'Chunking', 'Embedding', 'Storing', 'Completed',
];

type StepState = 'done' | 'active' | 'pending' | 'failed';

/**
 * Stepper vertical du pipeline d'ingestion. Chaque étape est marquée faite/active/à venir/échouée ;
 * l'étape active à pourcentage affiche une barre de progression et ses compteurs.
 */
export default function IngestionStepper(
  { status, fileName, progress }: { status: DocumentStatus; contentType: string; fileName: string; progress: IngestionProgress | null },
) {
  const stages = stagesFor(fileName);
  const currentStage = progress?.stage ?? (status === 'Completed' ? 'Completed' : 'Queued');
  const currentIndex = STAGE_ORDER.indexOf(currentStage);

  // Détermine l'état d'affichage d'une étape donnée.
  function stateOf(stage: IngestionStage): StepState {
    if (status === 'Completed') return 'done';
    if (status === 'Failed') {
      if (stage === currentStage) return 'failed';
      return STAGE_ORDER.indexOf(stage) < currentIndex ? 'done' : 'pending';
    }
    if (stage === currentStage) return 'active';
    return STAGE_ORDER.indexOf(stage) < currentIndex ? 'done' : 'pending';
  }

  // Construit la ligne de détail (compteurs) de l'étape active.
  function detail(stage: IngestionStage): string | null {
    if (!progress || stage !== progress.stage) return null;
    const c = progress.counters;
    if (stage === 'Embedding' && c.batchCount) {
      return `lot ${c.batchIndex}/${c.batchCount}${c.chunksTotal ? ` · ${c.chunksDone}/${c.chunksTotal} chunks` : ''}`;
    }
    if (stage === 'Transcribing') {
      return progress.percent != null ? `${progress.percent} %` : 'en cours…';
    }
    return null;
  }

  return (
    <ol className="ingestion-stepper">
      {stages.map(stage => {
        const st = stateOf(stage);
        const showBar = st === 'active' && progress?.percent != null;
        return (
          <li key={stage} className={`stepper-item stepper-${st}`}>
            <span className="stepper-marker" aria-hidden />
            <div className="stepper-body">
              <span className="stepper-label">{STAGE_LABELS[stage]}</span>
              {detail(stage) && <span className="stepper-detail">{detail(stage)}</span>}
              {showBar && (
                <div className="progress-bar">
                  <div className="progress-bar-fill" style={{ width: `${progress!.percent}%` }} />
                </div>
              )}
            </div>
          </li>
        );
      })}
    </ol>
  );
}
