# ADR 0004 — A hand-written sanitiser rather than a library

**Status:** accepted

## Context

The assignment allows exactly four tags, requires unbalanced tags to be caught, and requires the
result to be valid XHTML. General-purpose sanitisers (HtmlSanitizer, AngleSharp-based cleaners) aim
to keep as much of arbitrary HTML as is safe, and they *repair* broken markup rather than rejecting it.

## Decision

A small tokenising sanitiser with a four-tag allowlist, attribute rebuilding, a scheme allowlist on
`href`, a balance stack (demoted tags carry their closing tag with them), and a final XML parse with
DTD processing disabled as verification.

## Consequences

- Behaviour matches the assignment exactly: unbalanced input is a field error, not a silent fix.
- The security boundary is ~300 lines that can be read in one sitting, with a payload corpus asserted
  on the parsed tree.
- Angular's sanitiser runs on the output as an independent second layer.
