const MAX_ALIASES = 50

/** One alias per line; blank lines are ignored. */
export function parseAliases(text: string): string[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line !== '')
}

/** Mirrors the server's name normalization: NFKC, whitespace runs become one space, lower case. */
export function normalizeText(value: string): string {
  return value.normalize('NFKC').split(/\s+/).filter(Boolean).join(' ').toLowerCase()
}

/** Distinct aliases by normalized form; the server keeps a repeated alias once. */
function distinctCount(aliases: readonly string[]): number {
  return new Set(aliases.map(normalizeText)).size
}

/** Mirrors the API rules for a title and its aliases; the API stays the authority. */
export function validateTitle(
  title: string,
  aliases: readonly string[],
): { title: string | null; aliases: string | null } {
  const titleProblem = title.trim() === '' ? 'A title is required.' : null
  let aliasProblem: string | null = null
  if (distinctCount(aliases) > MAX_ALIASES) {
    aliasProblem = `A title can have at most ${MAX_ALIASES} aliases.`
  } else if (titleProblem === null && aliases.some((a) => normalizeText(a) === normalizeText(title))) {
    aliasProblem = 'An alias cannot be the same as its own title.'
  }
  return { title: titleProblem, aliases: aliasProblem }
}
