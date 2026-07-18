import {
  ArrowRight,
  Camera,
  Check,
  CheckCircle,
  ClockCounterClockwise,
  DownloadSimple,
  Images,
  LockKey,
  Printer,
  ShieldCheck,
  Sparkle,
  UserFocus,
  UsersThree,
  WifiHigh,
} from '@phosphor-icons/react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  CropFrame,
  DemoMediaNotice,
  PageBackLink,
  SimulatedPhoto,
  StatusLabel,
  simulatorImages,
} from '../components'

type KioskStep =
  | 'attract'
  | 'package'
  | 'privacy'
  | 'preview'
  | 'countdown'
  | 'review'
  | 'processing'
  | 'complete'

type PackageId = 'strip' | 'classic'
type MinorAnswer = 'yes' | 'no' | ''
type PhotoFilter = 'original' | 'black-and-white'

const workflow = ['Choose', 'Privacy', 'Capture', 'Print', 'Gallery'] as const

const workflowIndex: Record<KioskStep, number> = {
  attract: -1,
  package: 0,
  privacy: 1,
  preview: 2,
  countdown: 2,
  review: 2,
  processing: 3,
  complete: 4,
}

const packages = [
  {
    id: 'strip' as const,
    title: 'Classic photo strip',
    detail: 'Three photos, paired 2x6 strips, 2 copies',
    icon: Images,
  },
  {
    id: 'classic' as const,
    title: '4x6 portrait',
    detail: 'Three photos in one 4x6 layout, 1 copy',
    icon: UserFocus,
  },
]

const processingSteps = [
  { label: 'Output saved', detail: 'Final PNG persisted locally' },
  { label: 'Print queued', detail: 'Simulated printer accepted one job' },
  { label: 'Gallery prepared', detail: 'Private demo link is ready' },
]

function WorkflowRail({ step }: { step: KioskStep }) {
  const current = workflowIndex[step]

  return (
    <nav className="workflow-rail" aria-label="Session progress">
      <ol>
        {workflow.map((label, index) => {
          const state = index < current ? 'complete' : index === current ? 'current' : 'upcoming'
          return (
            <li className={`workflow-rail__item workflow-rail__item--${state}`} key={label}>
              <span className="workflow-rail__marker" aria-hidden="true">
                {state === 'complete' ? <Check size={16} weight="bold" /> : index + 1}
              </span>
              <span>{label}</span>
              <small>{state}</small>
              {state === 'current' ? <span className="visually-hidden" aria-current="step">Current step</span> : null}
            </li>
          )
        })}
      </ol>
    </nav>
  )
}

function ShotRail({ captures, current }: { captures: ReadonlyArray<string>; current: number }) {
  return (
    <section className="shot-rail" aria-labelledby="shot-rail-heading">
      <div className="shot-rail__title">
        <span id="shot-rail-heading">Shutter Rail</span>
        <strong>Photo {Math.min(current + 1, 3)} of 3</strong>
      </div>
      <ol>
        {[0, 1, 2].map((index) => {
          const source = captures[index]
          const state = source ? 'complete' : index === current ? 'current' : 'upcoming'
          return (
            <li className={`shot-rail__frame shot-rail__frame--${state}`} key={index}>
              <span className="shot-rail__number">{source ? <Check size={14} weight="bold" /> : index + 1}</span>
              {source ? (
                <SimulatedPhoto source={source} alt={`Synthetic simulator capture ${index + 1}`} />
              ) : (
                <span className="shot-rail__empty">Photo {index + 1}</span>
              )}
              <span className="visually-hidden">{state}</span>
            </li>
          )
        })}
      </ol>
    </section>
  )
}

function PrintSheet({ packageId, captures }: { packageId: PackageId; captures: ReadonlyArray<string> }) {
  const sheetImages = captures.length === 0 ? simulatorImages : captures

  return (
    <div className={`print-sheet print-sheet--${packageId}`} aria-label="Simulated final print output">
      <div className="print-sheet__photos">
        {sheetImages.slice(0, 3).map((source, index) => (
          <SimulatedPhoto source={source} alt={`Synthetic output photo ${index + 1}`} key={`${source}-${index}`} />
        ))}
      </div>
      <div className="print-sheet__footer">
        <strong>Mara &amp; Nico</strong>
        <span>July 19, 2026</span>
      </div>
    </div>
  )
}

export function KioskPage() {
  const [step, setStep] = useState<KioskStep>('attract')
  const [packageId, setPackageId] = useState<PackageId>('strip')
  const [minorAnswer, setMinorAnswer] = useState<MinorAnswer>('')
  const [noticeConfirmed, setNoticeConfirmed] = useState(false)
  const [guardianConfirmed, setGuardianConfirmed] = useState(false)
  const [promotionConsent, setPromotionConsent] = useState(false)
  const [privacyError, setPrivacyError] = useState('')
  const [captures, setCaptures] = useState<string[]>([])
  const [filter, setFilter] = useState<PhotoFilter>('original')
  const [countdown, setCountdown] = useState(3)
  const [processIndex, setProcessIndex] = useState(0)
  const [completionSeconds, setCompletionSeconds] = useState(45)

  const currentCapture = simulatorImages[Math.min(captures.length, simulatorImages.length - 1)]
  const selectedPackage = packages.find((item) => item.id === packageId) ?? packages[0]

  useEffect(() => {
    if (step !== 'countdown') {
      return
    }

    const timer = window.setTimeout(() => {
      if (countdown > 1) {
        setCountdown((value) => value - 1)
      } else {
        setStep('review')
      }
    }, 450)

    return () => window.clearTimeout(timer)
  }, [countdown, step])

  useEffect(() => {
    if (step !== 'processing') {
      return
    }

    const timer = window.setTimeout(() => {
      if (processIndex < processingSteps.length) {
        setProcessIndex((value) => value + 1)
      } else {
        setStep('complete')
      }
    }, 520)

    return () => window.clearTimeout(timer)
  }, [processIndex, step])

  useEffect(() => {
    if (step !== 'complete') {
      return
    }

    const timer = window.setInterval(() => {
      setCompletionSeconds((seconds) => {
        if (seconds <= 1) {
          window.clearInterval(timer)
          return 0
        }
        return seconds - 1
      })
    }, 1000)

    return () => window.clearInterval(timer)
  }, [step])

  useEffect(() => {
    if (step === 'complete' && completionSeconds === 0) {
      resetSession()
    }
  })

  function resetSession() {
    setStep('attract')
    setPackageId('strip')
    setMinorAnswer('')
    setNoticeConfirmed(false)
    setGuardianConfirmed(false)
    setPromotionConsent(false)
    setPrivacyError('')
    setCaptures([])
    setFilter('original')
    setCountdown(3)
    setProcessIndex(0)
    setCompletionSeconds(45)
    window.scrollTo({ top: 0, behavior: 'instant' })
  }

  function confirmPrivacy() {
    if (!noticeConfirmed || minorAnswer === '' || (minorAnswer === 'yes' && !guardianConfirmed)) {
      setPrivacyError('Confirm the privacy notice and answer the participant question before continuing.')
      return
    }
    setPrivacyError('')
    setStep('preview')
  }

  function takePhoto() {
    setCountdown(3)
    setStep('countdown')
  }

  function keepPhoto() {
    const nextCaptures = [...captures, currentCapture]
    setCaptures(nextCaptures)
    setFilter('original')
    if (nextCaptures.length === 3) {
      setProcessIndex(0)
      setStep('processing')
      return
    }
    setStep('preview')
  }

  return (
    <div className={`kiosk kiosk--${step}`}>
      <header className="kiosk__topbar">
        <div>
          <PageBackLink />
          <strong>Mara &amp; Nico</strong>
        </div>
        {step === 'attract' ? <DemoMediaNotice compact /> : <span className="kiosk__session-code">FREE DEMO SESSION</span>}
      </header>

      {step === 'attract' ? null : <WorkflowRail step={step} />}

      <main id="main-content" className="kiosk__main">
        {step === 'attract' ? (
          <section className="attract-screen" aria-labelledby="attract-heading">
            <CropFrame className="attract-screen__media">
              <SimulatedPhoto />
              <DemoMediaNotice compact />
            </CropFrame>
            <div className="attract-screen__content">
              <span className="event-date">July 19, 2026</span>
              <h1 id="attract-heading">Ready for your photo?</h1>
              <p>Three quick shots, a printed keepsake, and a private gallery for your phone.</p>
              <button className="button button--primary button--kiosk" type="button" onClick={() => setStep('package')}>
                Start session
                <ArrowRight aria-hidden="true" size={26} />
              </button>
              <StatusLabel label="Booth ready" detail="Camera and printer simulator connected" />
            </div>
          </section>
        ) : null}

        {step === 'package' ? (
          <section className="decision-screen" aria-labelledby="package-heading">
            <div className="decision-screen__copy">
              <h1 id="package-heading">Choose your print</h1>
              <p>This event is free. Payment is not part of this session.</p>
            </div>
            <fieldset className="selection-list">
              <legend className="visually-hidden">Print package</legend>
              {packages.map(({ id, title, detail, icon: PackageIcon }) => (
                <label className="selection-option" key={id}>
                  <input
                    type="radio"
                    name="package"
                    value={id}
                    checked={packageId === id}
                    onChange={() => setPackageId(id)}
                  />
                  <span className="selection-option__icon">
                    <PackageIcon aria-hidden="true" size={30} />
                  </span>
                  <span className="selection-option__copy">
                    <strong>{title}</strong>
                    <small>{detail}</small>
                  </span>
                  <span className="selection-option__check" aria-hidden="true">
                    <Check size={18} weight="bold" />
                  </span>
                </label>
              ))}
            </fieldset>
            <div className="decision-screen__actions">
              <button className="button button--secondary button--kiosk" type="button" onClick={resetSession}>
                Start over
              </button>
              <button className="button button--primary button--kiosk" type="button" onClick={() => setStep('privacy')}>
                Continue with {selectedPackage.title}
                <ArrowRight aria-hidden="true" size={24} />
              </button>
            </div>
          </section>
        ) : null}

        {step === 'privacy' ? (
          <section className="privacy-screen" aria-labelledby="privacy-heading">
            <div className="privacy-screen__notice">
              <div className="privacy-screen__icon">
                <ShieldCheck aria-hidden="true" size={34} />
              </div>
              <h1 id="privacy-heading">Your photos stay private</h1>
              <p>
                DigiPhoto uses your photos to create this output, print it, and deliver a private gallery. Media is deleted after 30 days.
              </p>
              <ul>
                <li>Anyone with the private link can view the gallery.</li>
                <li>We do not use face recognition, age detection, or media training.</li>
                <li>You can request access or deletion from the gallery.</li>
              </ul>
            </div>

            <form
              className="privacy-screen__form"
              onSubmit={(event) => {
                event.preventDefault()
                confirmPrivacy()
              }}
            >
              {privacyError ? (
                <div className="error-summary" role="alert">
                  <strong>More information is needed</strong>
                  <span>{privacyError}</span>
                </div>
              ) : null}

              <label className="check-control">
                <input
                  type="checkbox"
                  checked={noticeConfirmed}
                  onChange={(event) => setNoticeConfirmed(event.target.checked)}
                />
                <span>I have read the privacy notice and agree to create this session.</span>
              </label>

              <fieldset className="minor-question">
                <legend>Is anyone in this session under 18?</legend>
                <p>We only need this answer. Do not enter a birthdate or show an ID.</p>
                <div className="segmented-choice segmented-choice--two">
                  <label>
                    <input
                      type="radio"
                      name="minor"
                      value="no"
                      checked={minorAnswer === 'no'}
                      onChange={() => setMinorAnswer('no')}
                    />
                    <span>No, everyone is 18 or older</span>
                  </label>
                  <label>
                    <input
                      type="radio"
                      name="minor"
                      value="yes"
                      checked={minorAnswer === 'yes'}
                      onChange={() => setMinorAnswer('yes')}
                    />
                    <span>Yes, a minor is included</span>
                  </label>
                </div>
              </fieldset>

              {minorAnswer === 'yes' ? (
                <div className="guardian-confirmation">
                  <UsersThree aria-hidden="true" size={28} />
                  <label className="check-control">
                    <input
                      type="checkbox"
                      checked={guardianConfirmed}
                      onChange={(event) => setGuardianConfirmed(event.target.checked)}
                    />
                    <span>I am the parent or guardian and confirm this child may join the photo.</span>
                  </label>
                  <p>For younger guests: We use the photo only to make your print and private gallery.</p>
                </div>
              ) : null}

              <label className="check-control check-control--optional">
                <input
                  type="checkbox"
                  checked={promotionConsent}
                  onChange={(event) => setPromotionConsent(event.target.checked)}
                />
                <span>
                  Allow Mara &amp; Nico Photography to share this output in its portfolio.
                  <small>Optional. Leave unchecked to keep it private.</small>
                </span>
              </label>

              <div className="decision-screen__actions">
                <button className="button button--secondary button--kiosk" type="button" onClick={() => setStep('package')}>
                  Back to packages
                </button>
                <button className="button button--primary button--kiosk" type="submit">
                  Continue to camera
                  <ArrowRight aria-hidden="true" size={24} />
                </button>
              </div>
            </form>
          </section>
        ) : null}

        {step === 'preview' || step === 'countdown' ? (
          <section className="capture-screen" aria-labelledby="capture-heading">
            <div className="capture-screen__stage-column">
              <CropFrame className="viewfinder-stage">
                <SimulatedPhoto className="viewfinder-stage__mirrored" source={currentCapture} />
                <span className="viewfinder-stage__focus" aria-hidden="true" />
                <DemoMediaNotice compact />
                <span className="viewfinder-stage__mirror">Mirrored preview</span>
                {step === 'countdown' ? (
                  <div className="countdown-overlay" aria-live="assertive" aria-label={`Photo in ${countdown}`}>
                    <strong>{countdown}</strong>
                    <span>Hold that pose</span>
                  </div>
                ) : null}
              </CropFrame>
              <ShotRail captures={captures} current={captures.length} />
            </div>

            <aside className="capture-screen__actions">
              <Camera aria-hidden="true" className="capture-screen__camera-icon" size={48} />
              <h1 id="capture-heading">Look at the camera</h1>
              <p>{step === 'countdown' ? 'The photo will save automatically.' : 'The camera simulator is ready.'}</p>
              <button
                className="button button--primary button--capture"
                type="button"
                onClick={takePhoto}
                disabled={step === 'countdown'}
              >
                <Camera aria-hidden="true" size={32} />
                {step === 'countdown' ? `Photo in ${countdown}` : 'Take photo'}
              </button>
              <button className="button button--secondary button--kiosk" type="button" onClick={resetSession} disabled={step === 'countdown'}>
                End session
              </button>
              <div className="capture-screen__health">
                <StatusLabel label="Camera ready" icon={WifiHigh} />
                <StatusLabel label="Printer ready" icon={Printer} />
              </div>
            </aside>
          </section>
        ) : null}

        {step === 'review' ? (
          <section className="review-screen" aria-labelledby="review-heading">
            <div className="review-screen__stage-column">
              <CropFrame className={`review-stage${filter === 'black-and-white' ? ' review-stage--bw' : ''}`}>
                <SimulatedPhoto source={currentCapture} />
                <DemoMediaNotice compact />
              </CropFrame>
              <ShotRail captures={captures} current={captures.length} />
            </div>
            <aside className="review-screen__actions">
              <CheckCircle aria-hidden="true" size={46} />
              <h1 id="review-heading">Keep this photo?</h1>
              <p>Your original capture stays unchanged. The filter only affects this output.</p>
              <fieldset className="filter-choice">
                <legend>Photo filter</legend>
                <div className="segmented-choice segmented-choice--two">
                  <label>
                    <input
                      type="radio"
                      name="filter"
                      value="original"
                      checked={filter === 'original'}
                      onChange={() => setFilter('original')}
                    />
                    <span>Original</span>
                  </label>
                  <label>
                    <input
                      type="radio"
                      name="filter"
                      value="black-and-white"
                      checked={filter === 'black-and-white'}
                      onChange={() => setFilter('black-and-white')}
                    />
                    <span>Black and white</span>
                  </label>
                </div>
              </fieldset>
              <button className="button button--primary button--kiosk" type="button" onClick={keepPhoto}>
                Use this photo
                <ArrowRight aria-hidden="true" size={24} />
              </button>
              <button className="button button--secondary button--kiosk" type="button" onClick={() => setStep('preview')}>
                <ClockCounterClockwise aria-hidden="true" size={22} />
                Retake photo
              </button>
            </aside>
          </section>
        ) : null}

        {step === 'processing' ? (
          <section className="processing-screen" aria-labelledby="processing-heading">
            <div className="processing-screen__preview">
              <PrintSheet packageId={packageId} captures={captures} />
            </div>
            <div className="processing-screen__status">
              <Printer aria-hidden="true" size={48} />
              <h1 id="processing-heading">Preparing your print</h1>
              <p>Each stage completes only after the simulator persists its result.</p>
              <ol aria-live="polite">
                {processingSteps.map((item, index) => {
                  const state = index < processIndex ? 'complete' : index === processIndex ? 'current' : 'upcoming'
                  return (
                    <li className={`process-step process-step--${state}`} key={item.label}>
                      <span aria-hidden="true">{state === 'complete' ? <Check size={18} weight="bold" /> : index + 1}</span>
                      <div>
                        <strong>{item.label}</strong>
                        <small>{item.detail}</small>
                      </div>
                      <em>{state}</em>
                    </li>
                  )
                })}
              </ol>
              <span className="visually-hidden" role="status">
                {processIndex < processingSteps.length ? processingSteps[processIndex].label : 'All processing steps complete'}
              </span>
            </div>
          </section>
        ) : null}

        {step === 'complete' ? (
          <section className="completion-screen" aria-labelledby="completion-heading">
            <div className="completion-screen__output">
              <PrintSheet packageId={packageId} captures={captures} />
            </div>
            <div className="completion-screen__content">
              <Sparkle aria-hidden="true" className="completion-screen__spark" size={50} />
              <h1 id="completion-heading">Your print is on the way</h1>
              <p>Open your private gallery now, or scan the demo marker on another screen.</p>
              <div className="demo-qr" role="img" aria-label="Simulated QR marker. Use Open private gallery to continue.">
                <span>DEMO</span>
              </div>
              <Link className="button button--primary button--kiosk" to="/g/demo">
                <DownloadSimple aria-hidden="true" size={24} />
                Open private gallery
              </Link>
              <button className="button button--secondary button--kiosk" type="button" onClick={resetSession}>
                Finish and reset booth
              </button>
              <div className="completion-screen__timer" role="status">
                <LockKey aria-hidden="true" size={20} />
                <span>Session resets in {completionSeconds} seconds</span>
                <button type="button" onClick={() => setCompletionSeconds(90)}>Add more time</button>
              </div>
            </div>
          </section>
        ) : null}
      </main>
    </div>
  )
}
