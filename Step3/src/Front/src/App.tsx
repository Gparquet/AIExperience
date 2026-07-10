import { NavLink, Route, BrowserRouter as Router, Routes } from 'react-router-dom';
import { IngestionNotificationsProvider } from './context/IngestionNotificationsContext';
import ChatPage from './pages/ChatPage';
import DocumentDetailPage from './pages/DocumentDetailPage';
import DocumentsPage from './pages/DocumentsPage';
import VideoPage from './pages/VideoPage';
import './index.css';

export default function App() {
  return (
    <Router>
      {/* Englobe les routes (et non l'inverse) pour que le toast de fin de traitement reste
          visible même si l'utilisateur a navigué ailleurs pendant que le document s'ingérait. */}
      <IngestionNotificationsProvider>
        <div className="app">
          <header className="topbar">
            <div className="topbar-brand">
              <span className="brand-icon">🤖</span>
              <span className="brand-name">RAG Document Chat</span>
            </div>
            <nav className="topbar-nav">
              <NavLink to="/" end className={({ isActive }) => isActive ? 'nav-link active' : 'nav-link'}>
                Documents
              </NavLink>
              <NavLink to="/video" className={({ isActive }) => isActive ? 'nav-link active' : 'nav-link'}>
                Vidéo
              </NavLink>
              <NavLink to="/chat" className={({ isActive }) => isActive ? 'nav-link active' : 'nav-link'}>
                Chat
              </NavLink>
            </nav>
          </header>
          <main className="main-content">
            <Routes>
              <Route path="/" element={<DocumentsPage />} />
              <Route path="/documents/:id" element={<DocumentDetailPage />} />
              <Route path="/video" element={<VideoPage />} />
              <Route path="/chat" element={<ChatPage />} />
            </Routes>
          </main>
        </div>
      </IngestionNotificationsProvider>
    </Router>
  );
}
