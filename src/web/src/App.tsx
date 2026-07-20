import { lazy, Suspense } from 'react'
import { Route, Routes } from 'react-router-dom'
import { GalleryPage } from './pages/GalleryPage'
import { HomePage } from './pages/HomePage'
import { KioskPage } from './pages/KioskPage'
import { PortalPage } from './pages/PortalPage'
import { CapybaraLoader } from './components'

const TemplateEditorPage = lazy(async () => {
  const module = await import('./pages/TemplateEditorPage')
  return { default: module.TemplateEditorPage }
})

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
      <Route path="/kiosk" element={<KioskPage />} />
      <Route path="/portal" element={<PortalPage />} />
      <Route
        path="/templates/editor"
        element={(
          <Suspense fallback={<main id="main-content" className="route-loading" aria-live="polite"><CapybaraLoader compact label="Opening Template Studio" /></main>}>
            <TemplateEditorPage />
          </Suspense>
        )}
      />
      <Route path="/g/demo" element={<GalleryPage />} />
      <Route path="*" element={<HomePage />} />
    </Routes>
  )
}

export function App() {
  return (
    <>
      <a className="skip-link" href="#main-content">
        Skip to main content
      </a>
      <AppRoutes />
    </>
  )
}
