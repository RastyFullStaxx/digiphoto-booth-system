import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { AppRoutes } from './App'
import { hasSupportedArtworkSignature } from './templateArtwork'

function renderRoute(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AppRoutes />
    </MemoryRouter>,
  )
}

async function reachPrivacy(user: ReturnType<typeof userEvent.setup>) {
  renderRoute('/kiosk')
  await user.click(screen.getByRole('button', { name: 'Start session' }))
  await user.click(screen.getByRole('button', { name: /Continue with Classic photo strip/i }))
}

describe('DigiPhoto demo routes', () => {
  beforeEach(() => {
    window.localStorage.clear()
  })

  it('introduces each product surface from the demo home', () => {
    renderRoute('/')

    expect(screen.getByRole('heading', { name: /A cozy photo moment, handled with studio precision/i })).toBeInTheDocument()
    expect(screen.getByText('まちスタジオ')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Guest kiosk/i })).toHaveAttribute('href', '/kiosk')
    expect(screen.getByRole('link', { name: /Operations overview/i })).toHaveAttribute('href', '/portal')
    expect(screen.getByRole('link', { name: /Template Studio/i })).toHaveAttribute('href', '/templates/editor')
    expect(screen.getByRole('link', { name: /Private gallery/i })).toHaveAttribute('href', '/g/demo')
  })

  it('keeps kiosk sound effects under guest control', async () => {
    const user = userEvent.setup()
    renderRoute('/kiosk')

    await user.click(screen.getByRole('button', { name: 'Mute sound effects' }))
    expect(screen.getByRole('button', { name: 'Turn sound effects on' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('requires the immutable privacy notice and age answer before camera access', async () => {
    const user = userEvent.setup()
    await reachPrivacy(user)

    await user.click(screen.getByRole('button', { name: /Continue to camera/i }))
    expect(screen.getByRole('alert')).toHaveTextContent('Confirm the privacy notice')

    await user.click(screen.getByRole('checkbox', { name: /I have read the privacy notice/i }))
    await user.click(screen.getByRole('radio', { name: /No, everyone is 18 or older/i }))
    await user.click(screen.getByRole('button', { name: /Continue to camera/i }))

    expect(screen.getByRole('heading', { name: 'Look at the camera' })).toBeInTheDocument()
  })

  it('requires guardian confirmation when a minor is included', async () => {
    const user = userEvent.setup()
    await reachPrivacy(user)

    await user.click(screen.getByRole('checkbox', { name: /I have read the privacy notice/i }))
    await user.click(screen.getByRole('radio', { name: /Yes, a minor is included/i }))
    await user.click(screen.getByRole('button', { name: /Continue to camera/i }))
    expect(screen.getByRole('alert')).toHaveTextContent('parent or guardian')

    await user.click(screen.getByRole('checkbox', { name: /I am the parent or guardian/i }))
    await user.click(screen.getByRole('button', { name: /Continue to camera/i }))
    expect(screen.getByRole('heading', { name: 'Look at the camera' })).toBeInTheDocument()
  })

  it('moves from capture countdown into photo review', async () => {
    const user = userEvent.setup()
    await reachPrivacy(user)
    await user.click(screen.getByRole('checkbox', { name: /I have read the privacy notice/i }))
    await user.click(screen.getByRole('radio', { name: /No, everyone is 18 or older/i }))
    await user.click(screen.getByRole('button', { name: /Continue to camera/i }))
    await user.click(screen.getByRole('button', { name: 'Take photo' }))

    expect(await screen.findByRole('heading', { name: 'Keep this photo?' }, { timeout: 2_500 })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Use this photo/i })).toBeInTheDocument()
    expect(screen.queryByRole('slider', { name: 'Brightness' })).not.toBeInTheDocument()
  })

  it('accepts the simulator high-five trigger through the normal capture countdown', async () => {
    const user = userEvent.setup()
    await reachPrivacy(user)
    await user.click(screen.getByRole('checkbox', { name: /I have read the privacy notice/i }))
    await user.click(screen.getByRole('radio', { name: /No, everyone is 18 or older/i }))
    await user.click(screen.getByRole('button', { name: /Continue to camera/i }))
    fireEvent(window, new Event('digiphoto:high-five'))

    expect(screen.getByText(/High five detected/i)).toBeInTheDocument()
    expect(await screen.findByRole('heading', { name: 'Keep this photo?' }, { timeout: 2_500 })).toBeInTheDocument()
  })

  it('edits each output only after all three captures are accepted', async () => {
    const user = userEvent.setup()
    await reachPrivacy(user)
    await user.click(screen.getByRole('checkbox', { name: /I have read the privacy notice/i }))
    await user.click(screen.getByRole('radio', { name: /No, everyone is 18 or older/i }))
    await user.click(screen.getByRole('button', { name: /Continue to camera/i }))

    for (let shot = 0; shot < 3; shot += 1) {
      await user.click(screen.getByRole('button', { name: 'Take photo' }))
      await screen.findByRole('heading', { name: 'Keep this photo?' }, { timeout: 2_500 })
      expect(screen.queryByRole('slider', { name: 'Brightness' })).not.toBeInTheDocument()
      await user.click(screen.getByRole('button', { name: /Use this photo/i }))
    }

    expect(screen.getByRole('heading', { name: 'Process your photos' })).toBeInTheDocument()
    expect(screen.getByRole('timer', { name: /2:00 remaining/i })).toBeInTheDocument()

    fireEvent.change(screen.getByRole('slider', { name: 'Brightness' }), { target: { value: '20' } })
    fireEvent.change(screen.getByRole('slider', { name: 'Contrast' }), { target: { value: '-10' } })
    fireEvent.change(screen.getByRole('slider', { name: 'Exposure' }), { target: { value: '0.3' } })
    await user.click(screen.getByRole('button', { name: 'Photo filter: Original' }))
    expect(screen.getAllByRole('button', { name: /Preview and apply .* filter/i })).toHaveLength(5)
    await user.click(screen.getByRole('button', { name: 'Preview and apply Warm filter' }))

    expect(screen.getByAltText('Photo 1 selected for editing')).toHaveStyle({
      filter: 'sepia(0.18) saturate(1.18) brightness(1.477) contrast(0.900)',
    })

    await user.click(screen.getByRole('button', { name: 'Edit photo 2' }))
    expect(screen.getByRole('slider', { name: 'Brightness' })).toHaveValue('0')
    expect(screen.getByRole('slider', { name: 'Contrast' })).toHaveValue('0')
    expect(screen.getByRole('slider', { name: 'Exposure' })).toHaveValue('0')
    expect(screen.getByRole('button', { name: 'Photo filter: Original' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Edit photo 1' }))
    expect(screen.getByRole('slider', { name: 'Brightness' })).toHaveValue('20')
    expect(screen.getByRole('button', { name: 'Photo filter: Warm' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Replace photo 1' }))
    expect(screen.getByRole('heading', { name: 'Replace photo 1' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Take photo' }))
    await screen.findByRole('heading', { name: 'Keep this photo?' }, { timeout: 2_500 })
    await user.click(screen.getByRole('button', { name: /Use this photo/i }))
    expect(screen.getByRole('heading', { name: 'Process your photos' })).toBeInTheDocument()
    expect(screen.getByRole('slider', { name: 'Brightness' })).toHaveValue('0')
  }, 15_000)

  it('keeps the optional payment demo fail-closed until explicit simulated verification', async () => {
    const user = userEvent.setup()
    const portal = renderRoute('/portal')

    await user.click(screen.getByRole('button', { name: 'Open event' }))
    await user.click(screen.getByRole('switch', { name: 'Enable unsupervised payment QR demo' }))
    expect(screen.getByRole('switch', { name: 'Enable unsupervised payment QR demo' })).toBeChecked()
    portal.unmount()

    await reachPrivacy(user)
    await user.click(screen.getByRole('checkbox', { name: /I have read the privacy notice/i }))
    await user.click(screen.getByRole('radio', { name: /No, everyone is 18 or older/i }))
    await user.click(screen.getByRole('button', { name: /Continue to payment/i }))

    expect(screen.getByRole('heading', { name: 'Payment verification demo' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Look at the camera' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Simulate cloud-verified payment' }))
    expect(await screen.findByRole('heading', { name: 'Look at the camera' }, { timeout: 2_500 })).toBeInTheDocument()
  })

  it('takes the demo payment amount from the selected published package snapshot', async () => {
    const user = userEvent.setup()
    const portal = renderRoute('/portal')

    await user.click(screen.getByRole('button', { name: 'Open event' }))
    await user.click(screen.getByRole('switch', { name: 'Enable unsupervised payment QR demo' }))
    portal.unmount()

    renderRoute('/kiosk')
    await user.click(screen.getByRole('button', { name: 'Start session' }))
    expect(screen.getByText(/₱150\.00 for the selected published package/i)).toBeInTheDocument()

    await user.click(screen.getByRole('radio', { name: /4x6 portrait/i }))
    expect(screen.getByText(/₱200\.00 for the selected published package/i)).toBeInTheDocument()
  })

  it('finishes a three-photo demo with the capybara process and Done reset', async () => {
    const user = userEvent.setup()
    await reachPrivacy(user)
    await user.click(screen.getByRole('checkbox', { name: /I have read the privacy notice/i }))
    await user.click(screen.getByRole('radio', { name: /No, everyone is 18 or older/i }))
    await user.click(screen.getByRole('button', { name: /Continue to camera/i }))

    for (let shot = 0; shot < 3; shot += 1) {
      await user.click(screen.getByRole('button', { name: 'Take photo' }))
      expect(await screen.findByRole('heading', { name: 'Keep this photo?' }, { timeout: 2_500 })).toBeInTheDocument()
      await user.click(screen.getByRole('button', { name: /Use this photo/i }))
    }

    await user.click(screen.getByRole('button', { name: 'Finish and print' }))
    expect(screen.getByText('Machi is preparing your keepsake')).toBeInTheDocument()
    expect(await screen.findByRole('heading', { name: 'Your print is on the way' }, { timeout: 4_000 })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Done' }))
    expect(screen.getByRole('heading', { name: 'Ready for your photo?' })).toBeInTheDocument()
  }, 15_000)

  it('accepts only real PNG, JPEG, or WebP artwork signatures', async () => {
    const png = new File(
      [new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])],
      'strip.png',
      { type: 'image/png' },
    )
    const renamedSvg = new File(['<svg></svg>'], 'strip.png', { type: 'image/png' })

    expect(await hasSupportedArtworkSignature(png)).toBe(true)
    expect(await hasSupportedArtworkSignature(renamedSvg)).toBe(false)
  })

  it('exposes recoverable print ambiguity without automatic reprint', async () => {
    const user = userEvent.setup()
    renderRoute('/portal')

    expect(screen.getByRole('heading', { name: 'Recent sessions' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Simulate print warning' }))
    expect(screen.getByText('Print outcome is unknown')).toBeInTheDocument()
    expect(screen.getByText(/It will not reprint automatically/i)).toBeInTheDocument()
  })

  it('keeps gallery privacy controls visible and records a local-only request', async () => {
    const user = userEvent.setup()
    renderRoute('/g/demo')

    expect(screen.getByText('Anyone with this link can view the gallery')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'GIF' }))
    expect(screen.getByRole('link', { name: 'Download gif' })).toHaveAttribute('href', '/simulator/capture-02.png')

    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValueOnce(new Error('Clipboard unavailable'))
    await user.click(screen.getByRole('button', { name: 'Copy private link' }))
    expect(screen.getByText(/Could not copy the private link/i)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /Request access or deletion/i }))
    await user.type(screen.getByLabelText('Contact email'), 'guest@example.com')
    await user.click(screen.getByRole('button', { name: 'Record demo request' }))
    expect(screen.getByText(/No information was sent/i)).toBeInTheDocument()
  })
})
