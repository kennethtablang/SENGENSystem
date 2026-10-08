import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import '@fontsource/ibm-plex-sans/400.css'
import '@fontsource/ibm-plex-sans/500.css'
import '@fontsource/ibm-plex-sans/600.css'
import '@fontsource/ibm-plex-sans/700.css'
import '@fontsource/ibm-plex-mono/500.css'
import './index.css'
import App from './App.jsx'
import { AuthProvider } from './features/auth/AuthContext.jsx'
import ErrorBoundary from './features/shell/ErrorBoundary.jsx'
import { applyPrefs } from './features/settings/prefs.js'

// Stamp saved display preferences (density, motion) on <html> before first paint.
applyPrefs()

/* The boundary wraps the router and the auth provider rather than sitting inside App, so a crash
   in either of those — not just in a feature page — still lands on the recovery screen instead of
   a white page. It is outside StrictMode's concern entirely: StrictMode double-invokes renders in
   development, which surfaces crashes earlier but never catches them. */
createRoot(document.getElementById('root')).render(
  <StrictMode>
    <ErrorBoundary>
      <BrowserRouter>
        <AuthProvider>
          <App />
        </AuthProvider>
      </BrowserRouter>
    </ErrorBoundary>
  </StrictMode>,
)
