import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import type { WorkMode } from '../api'
import { WorkModeField } from './WorkModeFields'

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
})
