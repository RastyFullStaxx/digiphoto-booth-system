import { Route, Routes } from 'react-router-dom'
import { GalleryPage } from './pages/GalleryPage'
import { HomePage } from './pages/HomePage'
import { KioskPage } from './pages/KioskPage'
import { PortalPage } from './pages/PortalPage'
import { TemplateEditorPage } from './pages/TemplateEditorPage'

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
      <Route path="/kiosk" element={<KioskPage />} />
      <Route path="/portal" element={<PortalPage />} />
      <Route path="/templates/editor" element={<TemplateEditorPage />} />
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
