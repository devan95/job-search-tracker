import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import type { WorkMode } from '../api'
import { WorkModeChecks, WorkModeField } from './WorkModeFields'

function Harness({ initial }: { initial: WorkMode[] | null }) {
  const [value, setValue] = useState<WorkMode[] | null>(initial)
  return (
    <>
      <WorkModeField value={value} onChange={setValue} />
      <output data-testid="value">{JSON.stringify(value)}</output>
    </>
  )
}

describe('WorkModeField', () => {
  it('shows inherit (null) without any mode checkboxes', () => {
    render(<Harness initial={null} />)
    expect(screen.getByLabelText('Inherit from the selected lists')).toBeChecked()
    expect(screen.queryByLabelText('Remote')).not.toBeInTheDocument()
    expect(screen.getByTestId('value')).toHaveTextContent('null')
  })

  it('switches to an explicit empty set, which is not the same as inherit', async () => {
    const user = userEvent.setup()
    render(<Harness initial={null} />)
    await user.click(screen.getByLabelText('Choose work modes for this profile'))
    expect(screen.getByTestId('value')).toHaveTextContent('[]')
    expect(screen.getByTestId('explicit-empty-note')).toBeInTheDocument()
    expect(screen.getByLabelText('Remote')).not.toBeChecked()
  })

  it('stores checked modes in a stable order and drops the empty-set note', async () => {
    const user = userEvent.setup()
    render(<Harness initial={[]} />)
    await user.click(screen.getByLabelText('Hybrid'))
    await user.click(screen.getByLabelText('Remote'))
    expect(screen.getByTestId('value')).toHaveTextContent('["remote","hybrid"]')
    expect(screen.queryByTestId('explicit-empty-note')).not.toBeInTheDocument()
  })

  it('returns to inherit (null) from an explicit set', async () => {
    const user = userEvent.setup()
    render(<Harness initial={['remote']} />)
    expect(screen.getByLabelText('Remote')).toBeChecked()
    await user.click(screen.getByLabelText('Inherit from the selected lists'))
    expect(screen.getByTestId('value')).toHaveTextContent('null')
    expect(screen.queryByLabelText('Remote')).not.toBeInTheDocument()
  })

  it('shows an error message inside the work-mode group and links it to the group', () => {
    render(<WorkModeField value={['remote']} onChange={() => {}} errors={['Unknown work mode.']} />)
    const group = screen.getByRole('group', { name: 'Work modes' })
    expect(group).toHaveAttribute('aria-invalid', 'true')
    const message = screen.getByText('Unknown work mode.')
    expect(group).toContainElement(message)
    expect(group).toHaveAttribute('aria-describedby', message.id)
  })

  it('links an error message to a plain work-mode checkbox group', () => {
    render(
      <WorkModeChecks legend="Default work modes" value={[]} onChange={() => {}} errors={['Too many.']} />,
    )
    const group = screen.getByRole('group', { name: 'Default work modes' })
    expect(group).toHaveAttribute('aria-describedby', screen.getByText('Too many.').id)
  })

  it('has no error attributes without errors', () => {
    render(<WorkModeChecks legend="Default work modes" value={[]} onChange={() => {}} />)
    const group = screen.getByRole('group', { name: 'Default work modes' })
    expect(group).not.toHaveAttribute('aria-invalid')
    expect(group).not.toHaveAttribute('aria-describedby')
  })
})
