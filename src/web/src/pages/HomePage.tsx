import {
  ArrowRight,
  Camera,
  Images,
  Layout,
  MonitorPlay,
  SquaresFour,
} from '@phosphor-icons/react'
import type { Icon } from '@phosphor-icons/react'
import { Link } from 'react-router-dom'
import { BrandMark, CropFrame, DemoMediaNotice, SimulatedPhoto } from '../components'

const surfaces: Array<{
  path: string
  title: string
  description: string
  cue: string
  icon: Icon
}> = [
  {
    path: '/kiosk',
    title: 'Guest kiosk',
    description: 'Run a complete free photo session on a tablet-sized surface.',
    cue: 'Touch-first flow',
    icon: Camera,
  },
  {
    path: '/portal',
    title: 'Operations overview',
    description: 'See the event, booth health, sessions, and recovery truth.',
    cue: 'Tenant portal',
    icon: MonitorPlay,
  },
  {
    path: '/templates/editor',
    title: 'Template Studio',
    description: 'Edit a local demo draft on a real Fabric canvas and preview publishing.',
    cue: 'Fabric 7.4.0',
    icon: Layout,
  },
  {
    path: '/g/demo',
    title: 'Private gallery',
    description: 'Review the mobile delivery experience and retention notice.',
    cue: 'Guest delivery',
    icon: Images,
  },
]

export function HomePage() {
  return (
    <div className="demo-home">
      <header className="demo-home__header">
        <BrandMark />
        <span className="demo-home__context">
          <SquaresFour aria-hidden="true" size={18} />
          Interactive product slice
        </span>
      </header>

      <main id="main-content" className="demo-home__main">
        <section className="demo-home__intro" aria-labelledby="demo-heading">
          <div className="demo-home__copy">
            <h1 id="demo-heading">A cozy photo moment, handled with studio precision.</h1>
            <p>
              Follow Machi Studio from the first pose to a printed keepsake and private phone gallery.
            </p>
            <Link className="button button--primary button--large" to="/kiosk">
              Start guest demo
              <ArrowRight aria-hidden="true" size={22} />
            </Link>
          </div>

          <CropFrame className="demo-home__viewfinder">
            <SimulatedPhoto />
            <DemoMediaNotice compact />
            <span className="viewfinder-readout viewfinder-readout--left">MACHI STUDIO</span>
            <span className="viewfinder-readout viewfinder-readout--right">READY</span>
          </CropFrame>
        </section>

        <section className="surface-index" aria-labelledby="surface-heading">
          <div className="surface-index__heading">
            <h2 id="surface-heading">Choose a product surface</h2>
            <p>Each route uses the same cozy Machi identity and dependable Shutter Rail.</p>
          </div>
          <div className="surface-index__rows">
            {surfaces.map(({ path, title, description, cue, icon: SurfaceIcon }, index) => (
              <Link className="surface-row" to={path} key={path}>
                <span className="surface-row__index">{String(index + 1).padStart(2, '0')}</span>
                <span className="surface-row__icon">
                  <SurfaceIcon aria-hidden="true" size={24} />
                </span>
                <span className="surface-row__content">
                  <strong>{title}</strong>
                  <small>{description}</small>
                </span>
                <span className="surface-row__cue">{cue}</span>
                <ArrowRight className="surface-row__arrow" aria-hidden="true" size={22} />
              </Link>
            ))}
          </div>
        </section>
      </main>
    </div>
  )
}
