import { useId, useRef, useState, type FormEvent } from 'react'
import {
  importCompanies,
  previewCompanyImport,
  type ImportPreview,
  type ImportResult,
  type ImportRowStatus,
} from '../api'
import { useAction } from '../hooks'
import { ProblemAlert } from './ProblemAlert'

const STATUS_LABEL: Record<ImportRowStatus, string> = {
  new: 'New company',
  existing_employer: 'Existing company',
  already_in_list: 'Already in this list',
  duplicate_in_input: 'Duplicate in pasted text',
  invalid: 'Invalid',
}

const rowsText = (n: number): string => (n === 1 ? '1 row' : `${n} rows`)

function summaryLine(preview: ImportPreview): string {
  const s = preview.summary
  return `${rowsText(s.total)}: ${s.new} new, ${s.existingEmployer} existing, ${s.alreadyInList} already in this list, ${s.duplicateInInput} duplicate in pasted text, ${s.invalid} invalid.`
}

function resultLine(result: ImportResult): string {
  return `Import saved: ${result.created} created, ${result.reused} reused, ${result.skipped} skipped (${result.addedToList} added to this list).`
}

interface Props {
  listId: string
  /** Called after a save, and when the user asks to reload after an error. */
  onChanged: () => void
}

/**
 * Multiline paste, preview and confirm-save for one company list. The preview is for the
 * exact text that was pasted: any edit discards it, and the save sends that same text.
 */
export function CompanyImport({ listId, onChanged }: Props) {
  const action = useAction()
  const errorId = useId()
  const textRef = useRef<HTMLTextAreaElement>(null)
  const [text, setText] = useState('')
  const [preview, setPreview] = useState<{ text: string; data: ImportPreview } | null>(null)
  const [textError, setTextError] = useState<string | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)

  function onTextChange(value: string) {
    setText(value)
    setPreview(null)
    setTextError(null)
    setResult(null)
    action.clearError()
  }

  async function onPreview(event: FormEvent) {
    event.preventDefault()
    setResult(null)
    if (text.trim() === '') {
      setPreview(null)
      setTextError('Paste at least one company name, one per line.')
      textRef.current?.focus()
      return
    }
    setTextError(null)
    const sent = text
    const reply = await action.run(() => previewCompanyImport(listId, sent))
    setPreview(reply.ok ? { text: sent, data: reply.value } : null)
  }

  async function onConfirm() {
    if (!preview) return
    const sent = preview.text
    const reply = await action.run(() => importCompanies(listId, sent))
    if (!reply.ok) return
    setResult(reply.value)
    setPreview(null)
    setText('')
    onChanged()
  }

  const invalidCount = preview?.data.summary.invalid ?? 0

  return (
    <div className="import">
      <h4>Import companies</h4>
      <form
        aria-label="Import companies"
        onSubmit={(e) => void onPreview(e)}
        noValidate
        className="stack"
      >
        <label>
          Companies to import (one per line)
          <textarea
            ref={textRef}
            rows={8}
            value={text}
            onChange={(e) => onTextChange(e.target.value)}
            aria-invalid={textError !== null}
            aria-describedby={textError ? errorId : undefined}
            disabled={action.busy}
          />
        </label>
        <p className="hint">
          Each line is one company. Commas are part of the name, so “Acme, Inc.” is a single
          company. Blank lines are ignored. At most 1,000 companies per import.
        </p>
        {textError && (
          <p id={errorId} className="field-error">
            {textError}
          </p>
        )}
        <button type="submit" disabled={action.busy}>
          Preview import
        </button>
      </form>

      {action.error && (
        <ProblemAlert error={action.error} subject="company list" onReload={onChanged} />
      )}
      {/* Two persistent polite live regions, so a screen reader announces each new text. */}
      <p role="status" aria-label="Preview summary">
        {preview ? summaryLine(preview.data) : ''}
      </p>
      <p role="status" aria-label="Import result">
        {result ? resultLine(result) : ''}
      </p>

      {preview && (
        <section aria-label="Import preview" className="stack">
          <h5>Preview</h5>
          <ul className="rows cards" aria-label="Preview rows">
            {preview.data.rows.map((row) => (
              <li key={row.rowNumber} className={row.status === 'invalid' ? 'invalid' : undefined}>
                <span className="row-name">
                  Row {row.rowNumber}: {row.name ?? row.input}
                </span>
                <span className="meta">
                  {STATUS_LABEL[row.status]}. {row.message}
                </span>
              </li>
            ))}
          </ul>
          {invalidCount > 0 ? (
            <p className="field-error">
              {invalidCount === 1 ? '1 row is' : `${invalidCount} rows are`} invalid, so nothing
              can be saved. Fix the text and preview again.
            </p>
          ) : (
            <p className="hint">
              Nothing is saved until you confirm. Skipped rows change nothing; the save checks
              each row again against the current companies.
            </p>
          )}
          <div className="buttons">
            <button
              type="button"
              onClick={() => void onConfirm()}
              disabled={action.busy || invalidCount > 0}
            >
              Confirm import
            </button>
          </div>
        </section>
      )}
    </div>
  )
}
