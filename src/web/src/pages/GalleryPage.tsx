import {
  ArrowLeft,
  Check,
  Clock,
  Copy,
  DotsThree,
  DownloadSimple,
  FilmStrip,
  Images,
  LockSimple,
  Pause,
  Play,
  ShieldWarning,
  UserCircle,
  VideoCamera,
} from '@phosphor-icons/react'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { simulatorImages } from '../assets'
import { BrandMark, SimulatedPhoto } from '../components'

type GalleryMedia = 'photo' | 'gif' | 'video'
type CopyStatus = 'idle' | 'copied' | 'failed'

export function GalleryPage() {
  const [media, setMedia] = useState<GalleryMedia>('photo')
  const [playing, setPlaying] = useState(false)
  const [copyStatus, setCopyStatus] = useState<CopyStatus>('idle')
  const [detailsOpen, setDetailsOpen] = useState(false)
  const [requestOpen, setRequestOpen] = useState(false)
  const [requestSent, setRequestSent] = useState(false)
  const selectedSource = media === 'photo' ? simulatorImages[0] : media === 'gif' ? simulatorImages[1] : simulatorImages[2]

  async function copyPrivateLink() {
    try {
      await navigator.clipboard.writeText(window.location.href)
      setCopyStatus('copied')
    } catch {
      setCopyStatus('failed')
    }
  }

  return (
    <div className="gallery-page">
      <header className="gallery-header">
        <Link className="icon-button" to="/" aria-label="Back to demo surfaces">
          <ArrowLeft aria-hidden="true" size={24} />
        </Link>
        <BrandMark compact />
        <button
          className="icon-button"
          type="button"
          aria-label="Gallery details"
          aria-expanded={detailsOpen}
          aria-controls="gallery-details"
          onClick={() => setDetailsOpen((open) => !open)}
        >
          <DotsThree aria-hidden="true" size={28} weight="bold" />
        </button>
      </header>

      <main id="main-content" className="gallery-main">
        <section className="gallery-title" aria-labelledby="gallery-heading">
          <img className="gallery-title__mascot" src="/brand/capybara-loading-still.png" alt="" aria-hidden="true" />
          <div>
            <h1 id="gallery-heading">Your photo keepsake</h1>
            <p><LockSimple aria-hidden="true" size={24} />Private gallery</p>
          </div>
        </section>

        {detailsOpen ? (
          <section className="gallery-detail-panel" id="gallery-details" aria-label="Gallery details">
            <strong>Only people with this link can view the gallery.</strong>
            <p>The link can be revoked. Media is scheduled for deletion on August 18, 2026.</p>
          </section>
        ) : null}

        <section className="gallery-media" id="gallery-output" aria-label={`${media} output`}>
          <SimulatedPhoto
            className={media !== 'photo' && playing ? 'gallery-media__image gallery-media__image--motion' : 'gallery-media__image'}
            source={selectedSource}
            alt={`Synthetic simulator ${media} output featuring three fictional adult guests`}
          />
          {media === 'photo' ? null : (
            <button className="gallery-media__play" type="button" onClick={() => setPlaying((value) => !value)}>
              {playing ? <Pause aria-hidden="true" size={24} weight="fill" /> : <Play aria-hidden="true" size={24} weight="fill" />}
              {playing ? `Pause simulated ${media}` : `Play simulated ${media}`}
            </button>
          )}
        </section>

        <div className="gallery-tabs" role="group" aria-label="Output type">
          <button type="button" aria-pressed={media === 'photo'} aria-controls="gallery-output" onClick={() => { setMedia('photo'); setPlaying(false) }}>
            <SimulatedPhoto className="gallery-tabs__thumb" source={simulatorImages[0]} alt="" aria-hidden="true" />
            <span><Images aria-hidden="true" size={20} />Photo</span>
          </button>
          <button type="button" aria-pressed={media === 'gif'} aria-controls="gallery-output" onClick={() => { setMedia('gif'); setPlaying(false) }}>
            <SimulatedPhoto className="gallery-tabs__thumb" source={simulatorImages[1]} alt="" aria-hidden="true" />
            <span><FilmStrip aria-hidden="true" size={20} />GIF</span>
          </button>
          <button type="button" aria-pressed={media === 'video'} aria-controls="gallery-output" onClick={() => { setMedia('video'); setPlaying(false) }}>
            <SimulatedPhoto className="gallery-tabs__thumb" source={simulatorImages[2]} alt="" aria-hidden="true" />
            <span><VideoCamera aria-hidden="true" size={20} />Video</span>
          </button>
        </div>

        <div className="gallery-actions">
          <a className="button button--primary button--kiosk" href={selectedSource} download={`machi-studio-demo-${media}.png`}>
            <DownloadSimple aria-hidden="true" size={24} />
            Download {media}
          </a>
          <button className="button button--secondary button--kiosk" type="button" onClick={copyPrivateLink}>
            {copyStatus === 'copied' ? <Check aria-hidden="true" size={24} weight="bold" /> : <Copy aria-hidden="true" size={24} />}
            {copyStatus === 'copied' ? 'Private link copied' : 'Copy private link'}
          </button>
          <span className={copyStatus === 'failed' ? 'gallery-copy-feedback' : 'visually-hidden'} role="status" aria-live="polite">
            {copyStatus === 'copied' ? 'Private link copied to clipboard.' : copyStatus === 'failed' ? 'Could not copy the private link. Copy it from the browser address bar instead.' : ''}
          </span>
        </div>

        <section className="gallery-privacy" aria-labelledby="gallery-privacy-heading">
          <h2 id="gallery-privacy-heading" className="visually-hidden">Privacy and retention</h2>
          <div>
            <span className="gallery-privacy__icon gallery-privacy__icon--lime"><Clock aria-hidden="true" size={24} /></span>
            <p><strong>Available for 30 days</strong><span>Scheduled for deletion on August 18, 2026.</span></p>
          </div>
          <div>
            <span className="gallery-privacy__icon gallery-privacy__icon--coral"><ShieldWarning aria-hidden="true" size={24} /></span>
            <p><strong>Anyone with this link can view the gallery</strong><span>Keep it private and share only with people you trust.</span></p>
          </div>
          <div>
            <span className="gallery-privacy__icon"><LockSimple aria-hidden="true" size={24} /></span>
            <p><strong>No public event album</strong><span>This session is not indexed or listed with other guests.</span></p>
          </div>
        </section>

        <section className="privacy-request" aria-labelledby="privacy-request-heading">
          <button
            className="privacy-request__toggle"
            type="button"
            aria-expanded={requestOpen}
            aria-controls="privacy-request-form"
            onClick={() => setRequestOpen((open) => !open)}
          >
            <UserCircle aria-hidden="true" size={24} />
            <span id="privacy-request-heading">Request access or deletion</span>
          </button>
          {requestOpen ? (
            <form
              id="privacy-request-form"
              onSubmit={(event) => {
                event.preventDefault()
                setRequestSent(true)
              }}
            >
              {requestSent ? (
                <div className="success-message" role="status">
                  <Check aria-hidden="true" size={22} weight="bold" />
                  <span>Demo request recorded. No information was sent.</span>
                </div>
              ) : (
                <>
                  <label htmlFor="privacy-request-type">What do you need?</label>
                  <select id="privacy-request-type" name="requestType" defaultValue="delete">
                    <option value="delete">Delete this gallery</option>
                    <option value="access">Request a copy of my data</option>
                    <option value="revoke">Revoke this private link</option>
                  </select>
                  <label htmlFor="privacy-request-email">Contact email</label>
                  <input id="privacy-request-email" name="email" type="email" autoComplete="email" required />
                  <p>This local demo does not send or store the address.</p>
                  <button className="button button--secondary" type="submit">Record demo request</button>
                </>
              )}
            </form>
          ) : null}
        </section>
      </main>
    </div>
  )
}
