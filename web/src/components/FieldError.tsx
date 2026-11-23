/** The message to show for a field: the local check first, otherwise what the API reported for it. */
export function errorText(local: string | null, server: readonly string[]): string | null {
  if (local) return local
  return server.length > 0 ? server.join(' ') : null
}

interface Props {
  id: string
  message: string | null
}

/** A field's error text with a stable id, so the field can point at it with aria-describedby. */
export function FieldError({ id, message }: Props) {
  if (message === null) return null
  return (
    <p className="field-error" id={id}>
      {message}
    </p>
  )
}
