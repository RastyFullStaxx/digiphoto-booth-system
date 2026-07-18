import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { AppRoutes } from './App'

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
  it('introduces each product surface from the demo home', () => {
    renderRoute('/')

    expect(screen.getByRole('heading', { name: /Follow the session from shutter to private gallery/i })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Guest kiosk/i })).toHaveAttribute('href', '/kiosk')
    expect(screen.getByRole('link', { name: /Operations overview/i })).toHaveAttribute('href', '/portal')
    expect(screen.getByRole('link', { name: /Template Studio/i })).toHaveAttribute('href', '/templates/editor')
    expect(screen.getByRole('link', { name: /Private gallery/i })).toHaveAttribute('href', '/g/demo')
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
