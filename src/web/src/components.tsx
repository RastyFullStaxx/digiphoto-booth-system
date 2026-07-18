import type { Icon } from '@phosphor-icons/react'
import { ArrowLeft, CheckCircle, ImageSquare, LockSimple } from '@phosphor-icons/react'
import type { ImgHTMLAttributes, ReactNode } from 'react'
import { Link } from 'react-router-dom'

import { simulatorImages } from './assets'

export function BrandMark({ compact = false }: { compact?: boolean }) {
  return (
    <Link className="brand-mark" to="/" aria-label="DigiPhoto demo surfaces">
      <span className="brand-mark__name">DigiPhoto</span>
      {compact ? null : <span className="brand-mark__product">Booth System</span>}
    </Link>
  )
}

export function PageBackLink({ to = '/', label = 'All demo surfaces' }: { to?: string; label?: string }) {
  return (
    <Link className="icon-text-link" to={to}>
      <ArrowLeft aria-hidden="true" size={20} />
      <span>{label}</span>
    </Link>
  )
}

type SimulatedPhotoProps = ImgHTMLAttributes<HTMLImageElement> & {
  source?: string
  fallbackLabel?: string
}

export function SimulatedPhoto({
  source = simulatorImages[0],
  fallbackLabel = 'Simulated camera fixture unavailable',
  alt = 'Synthetic simulator photo of three fictional adult event guests',
  ...props
}: SimulatedPhotoProps) {
  return (
    <img
      {...props}
      alt={alt}
      src={source}
      onError={(event) => {
        const image = event.currentTarget
        if (!image.src.endsWith(simulatorImages[0])) {
          image.src = simulatorImages[0]
          return
        }
        image.hidden = true
        image.parentElement?.setAttribute('data-fallback-label', fallbackLabel)
      }}
    />
  )
}

export function CropFrame({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <div className={`crop-frame ${className}`.trim()}>
      <i className="crop-frame__corner crop-frame__corner--tl" aria-hidden="true" />
      <i className="crop-frame__corner crop-frame__corner--tr" aria-hidden="true" />
      <i className="crop-frame__corner crop-frame__corner--bl" aria-hidden="true" />
      <i className="crop-frame__corner crop-frame__corner--br" aria-hidden="true" />
      {children}
    </div>
  )
}

type StatusTone = 'ready' | 'info' | 'warning' | 'error' | 'muted'

export function StatusLabel({
  label,
  detail,
  tone = 'ready',
  icon: StatusIcon = CheckCircle,
}: {
  label: string
  detail?: string
  tone?: StatusTone
  icon?: Icon
}) {
  return (
    <span className={`status-label status-label--${tone}`}>
      <StatusIcon aria-hidden="true" size={20} weight="regular" />
      <span>
        <strong>{label}</strong>
        {detail ? <small>{detail}</small> : null}
      </span>
    </span>
  )
}

export function DemoMediaNotice({ compact = false }: { compact?: boolean }) {
  return (
    <span className={`demo-media-notice${compact ? ' demo-media-notice--compact' : ''}`}>
      <ImageSquare aria-hidden="true" size={18} />
      Synthetic simulator media
    </span>
  )
}

export function PrivacyBadge() {
  return (
    <span className="privacy-badge">
      <LockSimple aria-hidden="true" size={20} />
      Private gallery
    </span>
  )
}
