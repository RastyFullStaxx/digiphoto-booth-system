import {
  ArrowRight,
  Bell,
  CalendarBlank,
  Camera,
  CheckCircle,
  CreditCard,
  House,
  ImagesSquare,
  Layout,
  Monitor,
  Printer,
  SignOut,
  UserCircle,
  Users,
  Warning,
  WifiHigh,
} from '@phosphor-icons/react'
import type { Icon } from '@phosphor-icons/react'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { BrandMark, DemoMediaNotice, PageBackLink, StatusLabel } from '../components'
import {
  demoPackageSnapshots,
  loadDemoEventPaymentSettings,
  saveDemoEventPaymentSettings,
  type DemoEventPaymentSettings,
} from '../demoEventSettings'

const currentEventId = '11111111-1111-4111-8111-111111111112'
const phpCurrency = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' })

const navigation: Array<{ label: string; icon: Icon; path?: string }> = [
  { label: 'Overview', icon: House, path: '/portal' },
  { label: 'Events', icon: CalendarBlank },
  { label: 'Templates', icon: Layout, path: '/templates/editor' },
  { label: 'Booths', icon: Camera },
  { label: 'Sessions', icon: ImagesSquare },
  { label: 'Payments', icon: CreditCard },
  { label: 'Team', icon: Users },
]

const sessions = [
  { time: '7:42 PM', id: 'S-88392', guests: 3, photos: 2, prints: 0, status: 'Active', duration: '00:02:18' },
  { time: '7:28 PM', id: 'S-88391', guests: 4, photos: 3, prints: 2, status: 'Completed', duration: '00:04:52' },
  { time: '7:05 PM', id: 'S-88390', guests: 2, photos: 3, prints: 2, status: 'Completed', duration: '00:03:37' },
  { time: '6:41 PM', id: 'S-88389', guests: 3, photos: 3, prints: 2, status: 'Completed', duration: '00:04:11' },
]

function PortalNavigation() {
  return (
    <nav className="portal-nav" aria-label="Tenant portal">
      <BrandMark />
      <ul>
        {navigation.map(({ label, icon: NavigationIcon, path }) => (
          <li key={label}>
            {path ? (
              <Link className={label === 'Overview' ? 'portal-nav__link portal-nav__link--active' : 'portal-nav__link'} to={path}>
                <NavigationIcon aria-hidden="true" size={22} />
                <span>{label}</span>
              </Link>
            ) : (
              <span className="portal-nav__link portal-nav__link--disabled" aria-disabled="true" title="Connect the cloud API to enable this section">
                <NavigationIcon aria-hidden="true" size={22} />
                <span>{label}</span>
              </span>
            )}
          </li>
        ))}
      </ul>
      <div className="portal-nav__account">
        <span className="portal-nav__avatar">AA</span>
        <span>
          <strong>Ana Admin</strong>
          <small>Owner</small>
        </span>
        <SignOut aria-hidden="true" size={18} />
      </div>
    </nav>
  )
}

export function PortalPage() {
  const [eventOpen, setEventOpen] = useState(false)
  const [printWarning, setPrintWarning] = useState(false)
  const [paymentSettings, setPaymentSettings] = useState(() => loadDemoEventPaymentSettings(currentEventId))
  const [paymentSaveStatus, setPaymentSaveStatus] = useState('Saved locally in this browser.')

  const updatePaymentSettings = (next: DemoEventPaymentSettings) => {
    setPaymentSettings(next)
    setPaymentSaveStatus(
      saveDemoEventPaymentSettings(next)
        ? 'Saved locally in this browser.'
        : 'Could not save locally. The payment gate remains off after reload.',
    )
  }

  return (
    <div className="portal">
      <PortalNavigation />
      <div className="portal__workspace">
        <header className="portal-topbar">
          <div>
            <PageBackLink />
            <h1>Good evening, Ana</h1>
          </div>
          <div className="portal-topbar__actions">
            <DemoMediaNotice compact />
            <span className="icon-button portal-topbar__status" title="No unread notifications">
              <Bell aria-hidden="true" size={22} />
              <span className="visually-hidden">No unread notifications</span>
            </span>
            <span className="icon-button portal-topbar__status" title="Ana Admin, owner">
              <UserCircle aria-hidden="true" size={24} />
              <span className="visually-hidden">Ana Admin, owner</span>
            </span>
          </div>
        </header>

        <main id="main-content" className="portal-main">
          <section className="current-event" aria-labelledby="current-event-heading">
            <span className="current-event__icon">
              <CalendarBlank aria-hidden="true" size={26} />
            </span>
            <div>
              <span>Current event</span>
              <h2 id="current-event-heading">Mara &amp; Nico</h2>
            </div>
            <button
              className="button button--primary"
              type="button"
              aria-expanded={eventOpen}
              aria-controls="event-details"
              onClick={() => setEventOpen((open) => !open)}
            >
              {eventOpen ? 'Close event details' : 'Open event'}
              <ArrowRight aria-hidden="true" size={20} />
            </button>
          </section>

          {eventOpen ? (
            <section className="event-detail-strip" id="event-details" aria-label="Current event details">
              <dl>
                <div><dt>Package</dt><dd>Classic photo strip</dd></div>
                <div><dt>Retention</dt><dd>30 days</dd></div>
                <div>
                  <dt>Payment</dt>
                  <dd>{paymentSettings.paymentQrEnabled ? 'Demo setting on · published package prices' : 'Off for this event'}</dd>
                </div>
                <div><dt>Template</dt><dd>Modern Strip v3</dd></div>
              </dl>
              <Link className="text-action" to="/templates/editor">Edit draft template <ArrowRight aria-hidden="true" size={18} /></Link>

              <section className="event-payment-demo" aria-labelledby="event-payment-demo-heading">
                <div className="event-payment-demo__header">
                  <div>
                    <span className="event-payment-demo__eyebrow">Simulation only</span>
                    <h3 id="event-payment-demo-heading">Unsupervised payment QR</h3>
                  </div>
                  <label className="event-payment-demo__toggle">
                    <input
                      type="checkbox"
                      role="switch"
                      aria-label="Enable unsupervised payment QR demo"
                      checked={paymentSettings.paymentQrEnabled}
                      aria-describedby="event-payment-demo-description event-payment-demo-safety"
                      onChange={(event) => updatePaymentSettings({
                        ...paymentSettings,
                        paymentQrEnabled: event.currentTarget.checked,
                      })}
                    />
                    <span>{paymentSettings.paymentQrEnabled ? 'Demo setting on' : 'Off'}</span>
                  </label>
                </div>

                <p id="event-payment-demo-description" className="event-payment-demo__notice">
                  This turns the kiosk's non-scannable payment gate demo on. It accepts no money and never calls PayMongo.
                </p>

                {paymentSettings.paymentQrEnabled ? (
                  <div className="event-payment-demo__amount" aria-label="Published package snapshot prices">
                    <span>Published package snapshot prices</span>
                    {demoPackageSnapshots.map((item) => (
                      <small key={item.versionId}>{item.title} · {item.priceMinor.toLocaleString('en-PH')} minor units · {phpCurrency.format(item.priceMinor / 100)}</small>
                    ))}
                  </div>
                ) : null}

                <p id="event-payment-demo-safety" className="event-payment-demo__safety">
                  A production gate must fail closed: continue only after the cloud verifies a terminal payment for the correct event, amount, currency, and tenant.
                </p>
                <p className="event-payment-demo__status" role="status">{paymentSaveStatus}</p>
              </section>
            </section>
          ) : null}

          <section className="booth-health" aria-labelledby="booth-health-heading">
            <div className="booth-health__name">
              <Monitor aria-hidden="true" size={28} />
              <div>
                <h2 id="booth-health-heading">Booth 01</h2>
                <small>Simulator device</small>
              </div>
            </div>
            <StatusLabel label="Camera ready" detail="File-backed adapter" icon={Camera} />
            <StatusLabel
              label={printWarning ? 'Print needs review' : 'Printer ready'}
              detail={printWarning ? 'Outcome is ambiguous' : 'DNP simulator'}
              icon={Printer}
              tone={printWarning ? 'warning' : 'ready'}
            />
            <StatusLabel label="Online" detail="Cloud API pending" icon={WifiHigh} tone="info" />
            <dl className="booth-health__paper">
              <dt>Paper</dt>
              <dd>256 / 400</dd>
              <small>64% remaining</small>
            </dl>
            <dl className="booth-health__sync">
              <dt>Last synced</dt>
              <dd>12 seconds ago</dd>
              <small>Outbox empty</small>
            </dl>
          </section>

          <section className="sessions-panel" aria-labelledby="sessions-heading">
            <div className="section-heading-row">
              <div>
                <h2 id="sessions-heading">Recent sessions</h2>
                <p>Simulator records for the current event.</p>
              </div>
              <Link className="text-action" to="/kiosk">Run guest session <ArrowRight aria-hidden="true" size={18} /></Link>
            </div>
            <div className="responsive-table">
              <table>
                <caption className="visually-hidden">Recent simulator sessions for Mara and Nico</caption>
                <thead>
                  <tr>
                    <th scope="col">Time</th>
                    <th scope="col">Session ID</th>
                    <th scope="col">Guests</th>
                    <th scope="col">Photos</th>
                    <th scope="col">Prints</th>
                    <th scope="col">Status</th>
                    <th scope="col">Duration</th>
                  </tr>
                </thead>
                <tbody>
                  {sessions.map((session) => (
                    <tr key={session.id}>
                      <td data-label="Time">{session.time}</td>
                      <td data-label="Session ID" className="technical-text">{session.id}</td>
                      <td data-label="Guests">{session.guests}</td>
                      <td data-label="Photos">{session.photos}</td>
                      <td data-label="Prints">{session.prints}</td>
                      <td data-label="Status">
                        <span className={`table-status table-status--${session.status.toLowerCase()}`}>
                          {session.status === 'Active' ? <Camera aria-hidden="true" size={16} /> : <CheckCircle aria-hidden="true" size={16} />}
                          {session.status}
                        </span>
                      </td>
                      <td data-label="Duration" className="technical-text">{session.duration}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section className={`attention-strip${printWarning ? ' attention-strip--warning' : ''}`} aria-labelledby="attention-heading">
            <span className="attention-strip__icon">
              {printWarning ? <Warning aria-hidden="true" size={30} /> : <CheckCircle aria-hidden="true" size={30} />}
            </span>
            <div>
              <h2 id="attention-heading">Needs attention</h2>
              <strong>{printWarning ? 'Print outcome is unknown' : 'No sessions need recovery'}</strong>
              <p>
                {printWarning
                  ? 'The simulator cannot confirm whether the print left the spooler. It will not reprint automatically.'
                  : 'All completed simulator sessions are healthy and fully synced.'}
              </p>
            </div>
            <button className="button button--secondary" type="button" onClick={() => setPrintWarning((warning) => !warning)}>
              {printWarning ? 'Mark simulator healthy' : 'Simulate print warning'}
            </button>
          </section>
        </main>
      </div>

      <nav className="portal-mobile-nav" aria-label="Tenant portal mobile navigation">
        <Link className="portal-mobile-nav__active" to="/portal"><House aria-hidden="true" size={22} /><span>Overview</span></Link>
        <Link to="/templates/editor"><Layout aria-hidden="true" size={22} /><span>Templates</span></Link>
        <Link to="/kiosk"><Camera aria-hidden="true" size={22} /><span>Kiosk</span></Link>
      </nav>
    </div>
  )
}
