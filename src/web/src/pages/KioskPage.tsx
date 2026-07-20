import {
  ArrowRight,
  Camera,
  CaretDown,
  Check,
  CheckCircle,
  CloudCheck,
  ClockCounterClockwise,
  Heart,
  Images,
  LockKey,
  Printer,
  QrCode,
  ShieldCheck,
  Timer,
  UserFocus,
  UsersThree,
  WifiHigh,
} from '@phosphor-icons/react'
import {
  Album2Line,
  Camera2Line,
  CheckLine as CozyCheck,
  PrintLine,
  QrcodeLine,
  SafeShield2Line,
  Sparkles2Line,
} from '@mingcute/react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { simulatorImages } from '../assets'
import {
  BrandMark,
  CapybaraLoader,
  CropFrame,
  SimulatedPhoto,
  StatusLabel,
} from '../components'
import {
  demoPackageSnapshots,
  loadDemoEventPaymentSettings,
  type DemoPackageId,
  type DemoPackageSnapshot,
} from '../demoEventSettings'

type KioskStep =
  | 'attract'
  | 'package'
  | 'privacy'
  | 'payment'
  | 'preview'
  | 'countdown'
  | 'review'
  | 'edit'
  | 'processing'
  | 'complete'

type MinorAnswer = 'yes' | 'no' | ''
type PhotoFilter = 'original' | 'black-and-white' | 'warm' | 'cool' | 'film'
type PhotoAdjustments = {
  brightness: number
  contrast: number
  exposure: number
  filter: PhotoFilter
}
type EditedCapture = {
  source: string
  adjustments: PhotoAdjustments
}

const defaultPhotoAdjustments: PhotoAdjustments = {
  brightness: 0,
  contrast: 0,
  exposure: 0,
  filter: 'original',
}

const processTimeSeconds = 120
const highFiveEvent = 'digiphoto:high-five'
const freeWorkflow = ['Choose', 'Privacy', 'Capture', 'Process', 'Print', 'Complete'] as const
const paidWorkflow = ['Choose', 'Privacy', 'Payment', 'Capture', 'Process', 'Print', 'Complete'] as const
const freeWorkflowIndex: Record<KioskStep, number> = {
  attract: -1,
  package: 0,
  privacy: 1,
  payment: 1,
  preview: 2,
  countdown: 2,
  review: 2,
  edit: 3,
  processing: 4,
  complete: 5,
}
const paidWorkflowIndex: Record<KioskStep, number> = {
  attract: -1,
  package: 0,
  privacy: 1,
  payment: 2,
  preview: 3,
  countdown: 3,
  review: 3,
  edit: 4,
  processing: 5,
  complete: 6,
}

const currentEventId = '11111111-1111-4111-8111-111111111112'
const phpCurrency = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' })

const packageIcons = { strip: Images, classic: UserFocus } as const
const packages = demoPackageSnapshots.map((snapshot) => ({
  ...snapshot,
  icon: packageIcons[snapshot.id],
}))

const processingSteps = [
  { label: 'Session captures ready', detail: 'Three selected photos are staged locally' },
  { label: 'Print queue simulated', detail: 'No physical printer is connected' },
  { label: 'Gallery demo ready', detail: 'A private demo route is prepared' },
]

const workflowIcons = {
  Choose: Album2Line,
  Privacy: SafeShield2Line,
  Payment: QrcodeLine,
  Capture: Camera2Line,
  Process: Sparkles2Line,
  Print: PrintLine,
  Complete: CozyCheck,
} as const

const photoFilters: ReadonlyArray<{ value: PhotoFilter; label: string }> = [
  { value: 'original', label: 'Original' },
  { value: 'black-and-white', label: 'B&W' },
  { value: 'warm', label: 'Warm' },
  { value: 'cool', label: 'Cool' },
  { value: 'film', label: 'Film' },
]

const photoFilterEffects: Record<PhotoFilter, string> = {
  original: '',
  'black-and-white': 'grayscale(1)',
  warm: 'sepia(0.18) saturate(1.18)',
  cool: 'saturate(0.92) hue-rotate(8deg)',
  film: 'sepia(0.28) saturate(0.82)',
}

function photoFilterStyle(adjustments: PhotoAdjustments) {
  const outputBrightness = (1 + adjustments.brightness / 100) * (2 ** adjustments.exposure)
  const outputContrast = 1 + adjustments.contrast / 100
  const effect = photoFilterEffects[adjustments.filter]

  return { filter: `${effect ? `${effect} ` : ''}brightness(${outputBrightness.toFixed(3)}) contrast(${outputContrast.toFixed(3)})` }
}

function signedValue(value: number, suffix: string, digits = 0) {
  return `${value > 0 ? '+' : ''}${value.toFixed(digits)}${suffix}`
}

function formatTimer(seconds: number) {
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`
}

function PhotoAdjustmentControl({
  id,
  label,
  value,
  min,
  max,
  step,
  output,
  onChange,
}: {
  id: string
  label: string
  value: number
  min: number
  max: number
  step: number
  output: string
  onChange: (value: number) => void
}) {
  return (
    <label className="photo-adjustment" htmlFor={id}>
      <span>{label}</span>
      <input
        id={id}
        aria-label={label}
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={(event) => onChange(Number(event.target.value))}
      />
      <output htmlFor={id}>{output}</output>
    </label>
  )
}

function WorkflowRail({ step, paymentEnabled }: { step: KioskStep; paymentEnabled: boolean }) {
  const workflow = paymentEnabled ? paidWorkflow : freeWorkflow
  const current = paymentEnabled ? paidWorkflowIndex[step] : freeWorkflowIndex[step]

  return (
    <nav className="workflow-rail" aria-label="Session progress">
      <ol className={paymentEnabled ? 'workflow-rail__list workflow-rail__list--paid' : 'workflow-rail__list'}>
        {workflow.map((label, index) => {
          const state = index < current ? 'complete' : index === current ? 'current' : 'upcoming'
          const StepIcon = workflowIcons[label]
          return (
            <li className={`workflow-rail__item workflow-rail__item--${state}`} key={label}>
              <span className="workflow-rail__icon" aria-hidden="true">
                <StepIcon size={26} />
              </span>
              <span className="workflow-rail__marker" aria-hidden="true">
                {state === 'complete' ? <CozyCheck size={16} /> : index + 1}
              </span>
              <span className="workflow-rail__label">{label}</span>
              <small>{state}</small>
              {state === 'current' ? <span className="visually-hidden" aria-current="step">Current step</span> : null}
            </li>
          )
        })}
      </ol>
    </nav>
  )
}

function ShotRail({
  captures,
  current,
  onSelect,
}: {
  captures: ReadonlyArray<EditedCapture>
  current: number
  onSelect?: (index: number) => void
}) {
  return (
    <section className="shot-rail" aria-labelledby="shot-rail-heading">
      <div className="shot-rail__title">
        <span id="shot-rail-heading">Shutter Rail</span>
        <strong>{onSelect ? 'Editing' : 'Photo'} {Math.min(current + 1, 3)} of 3</strong>
      </div>
      <ol>
        {[0, 1, 2].map((index) => {
          const capture = captures[index]
          const state = index === current ? 'current' : capture ? 'complete' : 'upcoming'
          return (
            <li className={`shot-rail__frame shot-rail__frame--${state}`} key={index}>
              <span className="shot-rail__number">{capture ? <Check size={14} weight="bold" /> : index + 1}</span>
              {capture ? (
                <SimulatedPhoto
                  source={capture.source}
                  style={photoFilterStyle(capture.adjustments)}
                  alt={`Synthetic simulator capture ${index + 1}`}
                />
              ) : (
                <span className="shot-rail__empty">Photo {index + 1}</span>
              )}
              <span className="visually-hidden">{state}</span>
              {capture && onSelect ? (
                <button
                  className="shot-rail__select"
                  type="button"
                  aria-label={`Edit photo ${index + 1}`}
                  aria-pressed={index === current}
                  onClick={() => onSelect(index)}
                />
              ) : null}
            </li>
          )
        })}
      </ol>
    </section>
  )
}

function KeepsakeStage({ captures, selectedPackage }: { captures: ReadonlyArray<EditedCapture>; selectedPackage: DemoPackageSnapshot }) {
  const stageCapture = captures[captures.length - 1] ?? {
    source: simulatorImages[0],
    adjustments: defaultPhotoAdjustments,
  }
  return (
    <figure className="keepsake-stage">
      <CropFrame className="keepsake-stage__viewfinder">
        {selectedPackage.id === 'strip' ? (
          <SimulatedPhoto
            source={stageCapture.source}
            style={photoFilterStyle(stageCapture.adjustments)}
            alt="Synthetic selected photo from this demo session"
          />
        ) : (
          <div className="keepsake-portrait-proof" role="img" aria-label="Simulated 4x6 portrait sheet using the three selected photos">
            <div className="keepsake-portrait-proof__sheet">
              <div className="keepsake-portrait-proof__photos">
                {captures.slice(0, 3).map((capture, index) => (
                  <SimulatedPhoto
                    source={capture.source}
                    style={photoFilterStyle(capture.adjustments)}
                    alt={`Synthetic output photo ${index + 1}`}
                    key={`${capture.source}-${index}`}
                  />
                ))}
              </div>
              <strong>Mara &amp; Nico</strong>
              <span>July 19, 2026</span>
            </div>
          </div>
        )}
      </CropFrame>
      <ShotRail captures={captures} current={Math.max(0, captures.length - 1)} />
      <figcaption className="visually-hidden">
        <span>Session viewfinder with the three selected captures. Print package queued: {selectedPackage.outputLabel}.</span>
        <strong>{selectedPackage.copies} simulated {selectedPackage.copies === 1 ? 'copy' : 'copies'}</strong>
      </figcaption>
    </figure>
  )
}

export function KioskPage() {
  const [step, setStep] = useState<KioskStep>('attract')
  const [packageId, setPackageId] = useState<DemoPackageId>('strip')
  const [minorAnswer, setMinorAnswer] = useState<MinorAnswer>('')
  const [noticeConfirmed, setNoticeConfirmed] = useState(false)
  const [guardianConfirmed, setGuardianConfirmed] = useState(false)
  const [promotionConsent, setPromotionConsent] = useState(false)
  const [privacyError, setPrivacyError] = useState('')
  const [captures, setCaptures] = useState<EditedCapture[]>([])
  const [selectedEditIndex, setSelectedEditIndex] = useState(0)
  const [replacingCaptureIndex, setReplacingCaptureIndex] = useState<number | null>(null)
  const [previewSourceIndex, setPreviewSourceIndex] = useState(0)
  const [captureTrigger, setCaptureTrigger] = useState<'button' | 'gesture'>('button')
  const [countdown, setCountdown] = useState(3)
  const [processSeconds, setProcessSeconds] = useState(processTimeSeconds)
  const [filterMenuOpen, setFilterMenuOpen] = useState(false)
  const [processIndex, setProcessIndex] = useState(0)
  const [completionSeconds, setCompletionSeconds] = useState(45)
  const [paymentSettings] = useState(() => loadDemoEventPaymentSettings(currentEventId))
  const [paymentStatus, setPaymentStatus] = useState<'awaiting' | 'verifying' | 'verified'>('awaiting')

  const currentCapture = simulatorImages[previewSourceIndex]
  const selectedEditCapture = captures[selectedEditIndex]
  const selectedPackage = packages.find((item) => item.id === packageId) ?? packages[0]
  const paymentRequired = paymentSettings.paymentQrEnabled && selectedPackage.priceMinor > 0
  const selectedFilterLabel = photoFilters.find((filter) => filter.value === selectedEditCapture?.adjustments.filter)?.label ?? 'Original'
  const filterButtonRef = useRef<HTMLButtonElement>(null)

  const resetSession = useCallback(() => {
    setStep('attract')
    setPackageId('strip')
    setMinorAnswer('')
    setNoticeConfirmed(false)
    setGuardianConfirmed(false)
    setPromotionConsent(false)
    setPrivacyError('')
    setCaptures([])
    setSelectedEditIndex(0)
    setReplacingCaptureIndex(null)
    setPreviewSourceIndex(0)
    setCaptureTrigger('button')
    setCountdown(3)
    setProcessSeconds(processTimeSeconds)
    setFilterMenuOpen(false)
    setProcessIndex(0)
    setCompletionSeconds(45)
    setPaymentStatus('awaiting')
    window.scrollTo({ top: 0, behavior: 'auto' })
  }, [])

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
    if (step !== 'preview') {
      return
    }

    const handleHighFive = () => {
      setCaptureTrigger('gesture')
      setCountdown(3)
      setStep('countdown')
    }

    window.addEventListener(highFiveEvent, handleHighFive)
    return () => window.removeEventListener(highFiveEvent, handleHighFive)
  }, [step])

  useEffect(() => {
    if (step !== 'edit') {
      return
    }

    const timer = window.setInterval(() => {
      setProcessSeconds((seconds) => Math.max(0, seconds - 1))
    }, 1000)

    return () => window.clearInterval(timer)
  }, [step])

  useEffect(() => {
    if (step === 'edit' && processSeconds === 0) {
      setProcessIndex(0)
      setStep('processing')
    }
  }, [processSeconds, step])

  useEffect(() => {
    if (!filterMenuOpen) {
      return
    }

    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setFilterMenuOpen(false)
        filterButtonRef.current?.focus()
      }
    }

    window.addEventListener('keydown', closeOnEscape)
    return () => window.removeEventListener('keydown', closeOnEscape)
  }, [filterMenuOpen])

  useEffect(() => {
    if (step !== 'payment' || paymentStatus === 'awaiting') {
      return
    }

    const timer = window.setTimeout(() => {
      if (paymentStatus === 'verifying') {
        setPaymentStatus('verified')
        return
      }
      setStep('preview')
    }, paymentStatus === 'verifying' ? 800 : 420)

    return () => window.clearTimeout(timer)
  }, [paymentStatus, step])

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
  }, [completionSeconds, resetSession, step])

  function confirmPrivacy() {
    if (!noticeConfirmed) {
      setPrivacyError('Confirm the privacy notice before continuing.')
      return
    }
    if (minorAnswer === '') {
      setPrivacyError('Answer whether anyone in the session is under 18 before continuing.')
      return
    }
    if (minorAnswer === 'yes' && !guardianConfirmed) {
      setPrivacyError('A parent or guardian must confirm before a minor joins the session.')
      return
    }
    setPrivacyError('')
    setStep(paymentRequired ? 'payment' : 'preview')
  }

  function takePhoto() {
    setCaptureTrigger('button')
    setCountdown(3)
    setStep('countdown')
  }

  function keepPhoto() {
    if (replacingCaptureIndex !== null) {
      setCaptures((current) => current.map((capture, index) => (
        index === replacingCaptureIndex
          ? { source: currentCapture, adjustments: { ...defaultPhotoAdjustments } }
          : capture
      )))
      setSelectedEditIndex(replacingCaptureIndex)
      setReplacingCaptureIndex(null)
      setPreviewSourceIndex((index) => (index + 1) % simulatorImages.length)
      setStep('edit')
      return
    }

    const nextCaptures = [...captures, { source: currentCapture, adjustments: { ...defaultPhotoAdjustments } }]
    setCaptures(nextCaptures)
    setPreviewSourceIndex((index) => (index + 1) % simulatorImages.length)
    if (nextCaptures.length === 3) {
      setSelectedEditIndex(0)
      setProcessSeconds(processTimeSeconds)
      setStep('edit')
      return
    }
    setStep('preview')
  }

  function retakePhoto() {
    setPreviewSourceIndex((index) => (index + 1) % simulatorImages.length)
    setStep('preview')
  }

  function replaceSelectedPhoto() {
    const sourceIndex = simulatorImages.findIndex((source) => source === captures[selectedEditIndex].source)
    setPreviewSourceIndex((sourceIndex + 1) % simulatorImages.length)
    setReplacingCaptureIndex(selectedEditIndex)
    setStep('preview')
  }

  function updateSelectedAdjustments(update: Partial<PhotoAdjustments>) {
    setCaptures((current) => current.map((capture, index) => (
      index === selectedEditIndex
        ? { ...capture, adjustments: { ...capture.adjustments, ...update } }
        : capture
    )))
  }

  function selectFilter(filter: PhotoFilter) {
    updateSelectedAdjustments({ filter })
    setFilterMenuOpen(false)
    window.requestAnimationFrame(() => filterButtonRef.current?.focus())
  }

  function finishPhotoEdits() {
    setProcessIndex(0)
    setStep('processing')
  }

  return (
    <div className={`kiosk kiosk--${step}`}>
      <header className={`kiosk__topbar${step === 'attract' ? ' kiosk__topbar--attract' : ' kiosk__topbar--session'}`}>
        <div className="kiosk__identity">
          <BrandMark linked={false} />
        </div>
        {step === 'attract' ? null : <WorkflowRail step={step} paymentEnabled={paymentRequired} />}
      </header>

      <main id="main-content" className="kiosk__main">
        {step === 'attract' ? (
          <section className="attract-screen kiosk-step" aria-labelledby="attract-heading">
            <CropFrame className="attract-screen__media">
              <SimulatedPhoto />
            </CropFrame>
            <div className="attract-screen__content">
              <CapybaraLoader animated={false} compact label="A little keepsake, made with care" />
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
          <section className="decision-screen kiosk-step" aria-labelledby="package-heading">
            <div className="decision-screen__copy">
              <h1 id="package-heading">Choose your print</h1>
              <p>
                {paymentRequired
                  ? `${phpCurrency.format(selectedPackage.priceMinor / 100)} for the selected published package. The payment demo appears after privacy and accepts no money.`
                  : paymentSettings.paymentQrEnabled
                    ? 'This published package has no charge, so the payment step is skipped.'
                  : 'This event is free. Payment is not part of this session.'}
              </p>
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
                    <small>{detail}{paymentSettings.paymentQrEnabled ? ` · ${phpCurrency.format((packages.find((item) => item.id === id)?.priceMinor ?? 0) / 100)}` : ''}</small>
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
          <section className="privacy-screen kiosk-step" aria-labelledby="privacy-heading">
            <div className="privacy-screen__notice">
              <div className="privacy-screen__icon">
                <ShieldCheck aria-hidden="true" size={34} />
              </div>
              <h1 id="privacy-heading">Your photos stay private</h1>
              <p>
                Machi Studio uses your photos to create this output, print it, and deliver a private gallery. Media is deleted after 30 days.
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
                  {paymentRequired ? 'Continue to payment' : 'Continue to camera'}
                  <ArrowRight aria-hidden="true" size={24} />
                </button>
              </div>
            </form>
          </section>
        ) : null}

        {step === 'payment' ? (
          <section className="payment-screen kiosk-step" aria-labelledby="payment-heading">
            <div className="payment-screen__summary">
              <span className="payment-screen__icon"><QrCode aria-hidden="true" size={36} /></span>
              <h1 id="payment-heading">Payment verification demo</h1>
              <p>
                Amount due: <strong>{phpCurrency.format(selectedPackage.priceMinor / 100)}</strong> for {selectedPackage.title}. This marker is intentionally non-scannable and never accepts money.
              </p>
              <div className="demo-payment-qr" role="img" aria-label="Non-scannable payment demo marker">
                <QrCode aria-hidden="true" size={120} weight="thin" />
                <strong>DEMO ONLY</strong>
              </div>
              <small>Production will create a dynamic PayMongo QR for this exact session and amount.</small>
            </div>

            <aside className="payment-screen__actions">
              <CloudCheck aria-hidden="true" size={48} />
              <h2>Wait for verified payment</h2>
              <p>Scanning a QR, showing a screenshot, or returning from a payment page cannot unlock the booth.</p>
              <div className={`payment-verification payment-verification--${paymentStatus}`} role="status" aria-live="polite">
                <CheckCircle aria-hidden="true" size={24} />
                <span>
                  <strong>{paymentStatus === 'awaiting' ? 'Awaiting demo verification' : paymentStatus === 'verifying' ? 'Checking simulated cloud state' : 'Demo payment verified'}</strong>
                  <small>{paymentStatus === 'verified' ? 'Continuing to the camera.' : 'No provider request or money transfer occurs.'}</small>
                </span>
              </div>
              <button
                className="button button--secondary button--kiosk"
                type="button"
                disabled={paymentStatus !== 'awaiting'}
                onClick={() => setStep('privacy')}
              >
                Back to privacy
              </button>
              <button
                className="button button--primary button--kiosk"
                type="button"
                disabled={paymentStatus !== 'awaiting'}
                onClick={() => setPaymentStatus('verifying')}
              >
                <CloudCheck aria-hidden="true" size={24} />
                {paymentStatus === 'awaiting' ? 'Simulate cloud-verified payment' : paymentStatus === 'verifying' ? 'Verifying demo payment' : 'Payment verified'}
              </button>
            </aside>
          </section>
        ) : null}

        {step === 'preview' || step === 'countdown' ? (
          <section className="capture-screen kiosk-step" aria-labelledby="capture-heading">
            <div className="capture-screen__stage-column">
              <CropFrame className="viewfinder-stage">
                <SimulatedPhoto className="viewfinder-stage__mirrored" source={currentCapture} />
                <span className="viewfinder-stage__focus" aria-hidden="true" />
                <span className="viewfinder-stage__mirror">Mirrored preview</span>
                {step === 'countdown' ? (
                  <div className="countdown-overlay" aria-live="assertive" aria-label={`Photo in ${countdown}`}>
                    <strong>{countdown}</strong>
                    <span>Hold that pose</span>
                  </div>
                ) : null}
              </CropFrame>
              <ShotRail captures={captures} current={replacingCaptureIndex ?? captures.length} />
            </div>

            <aside className="capture-screen__actions">
              <div className="capture-guide" aria-hidden="true">
                <CapybaraLoader animated label="" />
              </div>
              <h1 id="capture-heading">{replacingCaptureIndex === null ? 'Look at the camera' : `Replace photo ${replacingCaptureIndex + 1}`}</h1>
              <p>{step === 'countdown' ? (captureTrigger === 'gesture' ? 'High five detected. The photo will save automatically.' : 'The photo will save automatically.') : 'High-five detection is ready. Raise your palm or use the shutter button.'}</p>
              <button className="button button--primary button--capture" type="button" onClick={takePhoto} disabled={step === 'countdown'}>
                <Camera aria-hidden="true" size={32} />
                {step === 'countdown' ? `Photo in ${countdown}` : 'Take photo'}
              </button>
              <button className="button button--secondary button--kiosk" type="button" onClick={resetSession} disabled={step === 'countdown'}>
                End session
              </button>
              <div className="capture-screen__health">
                <StatusLabel label="High-five ready" icon={WifiHigh} />
                <StatusLabel label="Printer ready" icon={Printer} />
              </div>
            </aside>
          </section>
        ) : null}

        {step === 'review' ? (
          <section className="review-screen kiosk-step" aria-labelledby="review-heading">
            <div className="review-screen__stage-column">
              <CropFrame className="review-stage">
                <SimulatedPhoto source={currentCapture} />
              </CropFrame>
              <ShotRail captures={captures} current={replacingCaptureIndex ?? captures.length} />
            </div>
            <aside className="review-screen__actions">
              <div className="capture-guide" aria-hidden="true">
                <CapybaraLoader animated label="" />
              </div>
              <h1 id="review-heading">Keep this photo?</h1>
              <p>{replacingCaptureIndex === null ? 'Review this capture now. You can edit all three photos after the set is complete.' : 'Use this replacement or take another shot. Your current photo stays until you confirm.'}</p>
              <button className="button button--secondary button--kiosk" type="button" onClick={retakePhoto}>
                <ClockCounterClockwise aria-hidden="true" size={22} />
                Retake photo
              </button>
              <button className="button button--primary button--kiosk" type="button" onClick={keepPhoto}>
                Use this photo
                <ArrowRight aria-hidden="true" size={24} />
              </button>
            </aside>
          </section>
        ) : null}

        {step === 'edit' && selectedEditCapture ? (
          <section className="review-screen review-screen--editor kiosk-step" aria-labelledby="edit-heading">
            <div className="review-screen__stage-column">
              <CropFrame className="review-stage">
                <SimulatedPhoto
                  source={selectedEditCapture.source}
                  style={photoFilterStyle(selectedEditCapture.adjustments)}
                  alt={`Photo ${selectedEditIndex + 1} selected for editing`}
                />
              </CropFrame>
              <ShotRail captures={captures} current={selectedEditIndex} onSelect={(index) => {
                setSelectedEditIndex(index)
                setFilterMenuOpen(false)
              }} />
            </div>
            <aside className="review-screen__actions">
              <div className="capture-guide" aria-hidden="true">
                <CapybaraLoader animated label="" />
              </div>
              <h1 id="edit-heading">Process your photos</h1>
              <p>Choose a photo to edit or replace before your session moves to print.</p>
              <div className="process-timer" role="timer" aria-label={`${formatTimer(processSeconds)} remaining in photo processing`}>
                <Timer aria-hidden="true" size={28} />
                <span><strong>{formatTimer(processSeconds)}</strong><small>left to edit or replace</small></span>
                <button type="button" onClick={() => setProcessSeconds((seconds) => seconds + 30)}>+30 sec</button>
              </div>
              <fieldset className="photo-adjustments">
                <legend>Adjust photo {selectedEditIndex + 1}</legend>
                <PhotoAdjustmentControl
                  id="photo-brightness"
                  label="Brightness"
                  value={selectedEditCapture.adjustments.brightness}
                  min={-25}
                  max={25}
                  step={1}
                  output={signedValue(selectedEditCapture.adjustments.brightness, '%')}
                  onChange={(brightness) => updateSelectedAdjustments({ brightness })}
                />
                <PhotoAdjustmentControl
                  id="photo-contrast"
                  label="Contrast"
                  value={selectedEditCapture.adjustments.contrast}
                  min={-25}
                  max={25}
                  step={1}
                  output={signedValue(selectedEditCapture.adjustments.contrast, '%')}
                  onChange={(contrast) => updateSelectedAdjustments({ contrast })}
                />
                <PhotoAdjustmentControl
                  id="photo-exposure"
                  label="Exposure"
                  value={selectedEditCapture.adjustments.exposure}
                  min={-0.5}
                  max={0.5}
                  step={0.1}
                  output={signedValue(selectedEditCapture.adjustments.exposure, ' EV', 1)}
                  onChange={(exposure) => updateSelectedAdjustments({ exposure })}
                />
              </fieldset>
              <div className="filter-picker">
                <span className="filter-picker__label">Photo filter</span>
                <button
                  ref={filterButtonRef}
                  className="filter-picker__trigger"
                  type="button"
                  aria-label={`Photo filter: ${selectedFilterLabel}`}
                  aria-expanded={filterMenuOpen}
                  aria-controls="photo-filter-menu"
                  onClick={() => setFilterMenuOpen((open) => !open)}
                >
                  <span>Filter</span>
                  <strong>{selectedFilterLabel}</strong>
                  <CaretDown aria-hidden="true" size={20} />
                </button>
                {filterMenuOpen ? (
                  <div id="photo-filter-menu" className="filter-picker__menu" role="group" aria-label="Choose a photo filter">
                    {photoFilters.map((filter) => (
                      <button
                        type="button"
                        aria-label={`Preview and apply ${filter.label} filter`}
                        aria-pressed={selectedEditCapture.adjustments.filter === filter.value}
                        onClick={() => selectFilter(filter.value)}
                        key={filter.value}
                      >
                        <span className="filter-picker__preview">
                          <SimulatedPhoto
                            source={selectedEditCapture.source}
                            style={photoFilterStyle({ ...selectedEditCapture.adjustments, filter: filter.value })}
                            alt=""
                          />
                        </span>
                        <span>{filter.label}</span>
                      </button>
                    ))}
                  </div>
                ) : null}
              </div>
              <div className="process-actions">
                <button className="button button--secondary button--kiosk" type="button" onClick={replaceSelectedPhoto}>
                  <ClockCounterClockwise aria-hidden="true" size={22} />
                  Replace photo {selectedEditIndex + 1}
                </button>
                <button className="button button--primary button--kiosk" type="button" onClick={finishPhotoEdits}>
                  Finish and print
                  <ArrowRight aria-hidden="true" size={24} />
                </button>
              </div>
            </aside>
          </section>
        ) : null}

        {step === 'processing' ? (
          <section className="keepsake-screen keepsake-screen--processing kiosk-step" aria-labelledby="processing-heading">
            <KeepsakeStage captures={captures} selectedPackage={selectedPackage} />
            <div className="keepsake-screen__panel">
              <header className="keepsake-screen__header">
                <h1 id="processing-heading">Preparing your keepsake</h1>
                <span className="keepsake-screen__rule" aria-hidden="true">
                  <span />
                  <Heart size={20} weight="fill" />
                  <span />
                </span>
                <p>Machi is getting everything ready</p>
              </header>
              <CapybaraLoader label="Machi is preparing your keepsake" />
              <div className="keepsake-progress" role="status" aria-live="polite">
                <span aria-hidden="true">0{Math.min(processIndex + 1, processingSteps.length)}</span>
                <div>
                  <strong>{processingSteps[processIndex]?.label ?? 'Finishing up'}</strong>
                  <small>{processingSteps[processIndex]?.detail ?? 'Your keepsake is ready'}</small>
                </div>
              </div>
            </div>
          </section>
        ) : null}

        {step === 'complete' ? (
          <section className="keepsake-screen keepsake-screen--complete kiosk-step" aria-labelledby="completion-heading">
            <KeepsakeStage captures={captures} selectedPackage={selectedPackage} />
            <div className="keepsake-screen__panel">
              <header className="keepsake-screen__header">
                <span className="keepsake-screen__eyebrow">Preparing your keepsake</span>
                <span className="keepsake-screen__rule" aria-hidden="true">
                  <span />
                  <Heart size={20} weight="fill" />
                  <span />
                </span>
                <h1 id="completion-heading">Your print is on the way</h1>
              </header>
              <img
                className="keepsake-screen__art"
                src="/brand/capybara-printer-scene-trimmed.png"
                width="1162"
                height="764"
                alt="Watercolor Machi capybaras beside a pink photo printer"
              />
              <p className="keepsake-screen__delivery-note">{selectedPackage.title} · {selectedPackage.copies} {selectedPackage.copies === 1 ? 'copy' : 'copies'} · private demo gallery ready on this device.</p>
              <div className="keepsake-screen__actions">
                <Link className="button button--secondary button--kiosk" to="/g/demo">
                  <Images aria-hidden="true" size={28} />
                  Open private gallery
                </Link>
                <button className="button button--primary button--kiosk completion-screen__done" type="button" onClick={resetSession}>
                  <CheckCircle aria-hidden="true" size={34} />
                  Done
                </button>
                <div className="completion-screen__timer">
                  <LockKey aria-hidden="true" size={20} />
                  <span>Session resets in {completionSeconds} seconds</span>
                  <button type="button" onClick={() => setCompletionSeconds(90)}>Add more time</button>
                </div>
              </div>
            </div>
          </section>
        ) : null}
      </main>
    </div>
  )
}
