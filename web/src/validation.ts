export const MAX_NAME_LENGTH = 200

/** Client-side check that mirrors the API rule; the API stays the authority. */
export function validateName(value: string): string | null {
  const trimmed = value.trim()
  if (trimmed.length === 0) return 'A name is required.'
  if (trimmed.length > MAX_NAME_LENGTH) {
    return `A name can be at most ${MAX_NAME_LENGTH} characters.`
  }
  return null
}
