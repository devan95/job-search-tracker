const MAX_ALIASES = 50

/** One alias per line; blank lines are ignored. */
export function parseAliases(text: string): string[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line !== '')
}

const sameText = (a: string, b: string) => a.trim().toLowerCase() === b.trim().toLowerCase()

/** Mirrors the API rules for a title and its aliases; the API stays the authority. */
export function validateTitle(
  title: string,
  aliases: readonly string[],
): { title: string | null; aliases: string | null } {
  const titleProblem = title.trim() === '' ? 'A title is required.' : null
  let aliasProblem: string | null = null
  if (aliases.length > MAX_ALIASES) {
    aliasProblem = `A title can have at most ${MAX_ALIASES} aliases.`
  } else if (titleProblem === null && aliases.some((a) => sameText(a, title))) {
    aliasProblem = 'An alias cannot be the same as its own title.'
  }
  return { title: titleProblem, aliases: aliasProblem }
}
