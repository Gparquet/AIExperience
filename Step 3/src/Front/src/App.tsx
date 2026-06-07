import { NavLink, Route, BrowserRouter as Router, Routes } from 'react-router-dom';
import ChatPage from './pages/ChatPage';
import DocumentsPage from './pages/DocumentsPage';
import './index.css';

export default function App() {
  return (
    <Router>
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
            <NavLink to="/chat" className={({ isActive }) => isActive ? 'nav-link active' : 'nav-link'}>
              Chat
            </NavLink>
          </nav>
        </header>
        <main className="main-content">
          <Routes>
            <Route path="/" element={<DocumentsPage />} />
            <Route path="/chat" element={<ChatPage />} />
          </Routes>
        </main>
      </div>
    </Router>
  );
}
