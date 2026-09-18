# ADR 0005 — SkiaSharp for image work

**Status:** accepted

## Context

Two jobs need a graphics library: rendering the CAPTCHA and downscaling uploads to 320×240. The
idiomatic .NET choice, SixLabors ImageSharp, moved its 3.x line (and ImageSharp.Drawing) to a
commercial licence — the build emits "No Six Labors license found".

## Decision

SkiaSharp (MIT) with `SkiaSharp.NativeAssets.Linux.NoDependencies`, and an embedded OFL font for the
CAPTCHA.

## Consequences

- No licence liability; one library covers both jobs.
- The native asset package needs no fontconfig or system fonts, so the slim ASP.NET image works as is.
- The embedded font means CAPTCHA rendering is identical on Windows and in the container.
