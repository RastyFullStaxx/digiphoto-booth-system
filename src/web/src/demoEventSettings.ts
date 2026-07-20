export type DemoEventPaymentSettings = {
  schemaVersion: 2
  eventId: string
  paymentQrEnabled: boolean
}

export type DemoPackageId = 'strip' | 'classic'

export type DemoPackageSnapshot = {
  id: DemoPackageId
  versionId: string
  title: string
  detail: string
  outputLabel: string
  copies: number
  currency: 'PHP'
  priceMinor: number
}

export const demoPackageSnapshots: ReadonlyArray<DemoPackageSnapshot> = [
  {
    id: 'strip',
    versionId: 'classic-strip-v3',
    title: 'Classic photo strip',
    detail: 'Three photos, paired 2x6 strips, 2 copies',
    outputLabel: 'Paired 2x6 strips on one 4x6 sheet',
    copies: 2,
    currency: 'PHP',
    priceMinor: 15_000,
  },
  {
    id: 'classic',
    versionId: 'portrait-4x6-v1',
    title: '4x6 portrait',
    detail: 'Three photos in one 4x6 layout, 1 copy',
    outputLabel: 'Single 4x6 portrait sheet',
    copies: 1,
    currency: 'PHP',
    priceMinor: 20_000,
  },
]

const storageKey = (eventId: string) => `digiphoto.demo-event-payment.v2:${eventId}`

export function defaultDemoEventPaymentSettings(eventId: string): DemoEventPaymentSettings {
  return {
    schemaVersion: 2,
    eventId,
    paymentQrEnabled: false,
  }
}

function isValidSettings(value: unknown, eventId: string): value is DemoEventPaymentSettings {
  if (typeof value !== 'object' || value === null) return false

  const settings = value as Record<string, unknown>
  return settings.schemaVersion === 2
    && settings.eventId === eventId
    && typeof settings.paymentQrEnabled === 'boolean'
}

export function loadDemoEventPaymentSettings(eventId: string): DemoEventPaymentSettings {
  const fallback = defaultDemoEventPaymentSettings(eventId)
  if (typeof window === 'undefined') return fallback

  try {
    const stored = window.localStorage.getItem(storageKey(eventId))
    if (!stored) return fallback

    const parsed: unknown = JSON.parse(stored)
    return isValidSettings(parsed, eventId) ? parsed : fallback
  } catch {
    return fallback
  }
}

export function saveDemoEventPaymentSettings(settings: DemoEventPaymentSettings): boolean {
  if (!isValidSettings(settings, settings.eventId) || typeof window === 'undefined') return false

  try {
    window.localStorage.setItem(storageKey(settings.eventId), JSON.stringify(settings))
    return true
  } catch {
    return false
  }
}
